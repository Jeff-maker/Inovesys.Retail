using LiteDB;
using Inovesys.Retail.Entities;
using Inovesys.Retail.Models;
using System.Text.RegularExpressions;
using System.Text;
using System.Security.Cryptography;

namespace Inovesys.Retail.Services
{
    public class InvoiceService
    {
        private readonly ILiteCollection<Invoice> _invoices;
        private readonly ILiteCollection<InvoiceNumberControl> _controls;
        private readonly ILiteCollection<InvoiceItem> _invoiceItem;
        private readonly ILiteCollection<InvoiceItemTax> _invoiceTaxes;
        private readonly InvoicePostDto _invoicePostDto;
        private readonly Branche _branche;
        LiteDbService _db;

        public Invoice Invoice { get; private set; }
        public string ErrorMessage { get; private set; }
        public bool Success { get; private set; }
        public InvoiceService(LiteDbService lite, InvoicePostDto invoicePostDto, Branche branche)
        {
            _db = lite;
            _invoices = lite.GetCollection<Invoice>("invoice");
            _controls = lite.GetCollection<InvoiceNumberControl>("invoicenumbercontrols");
            _invoiceItem = lite.GetCollection<InvoiceItem>("invoiceitem");
            _invoiceTaxes = lite.GetCollection<InvoiceItemTax>("invoiceitemtax");
            _branche = branche;
            _invoicePostDto = invoicePostDto;
        }
        
        public async Task<(bool Sucess, Invoice Invoice)> CreateAsync()
        {
            try
            {
                _db.Database.BeginTrans(); // Inicia transação manualmente

                var numberControl = _controls.FindOne(c =>
                    c.ClientId == _branche.ClientId &&
                    c.CompanyId == _branche.CompanyId &&
                    c.BrancheId == _branche.Id &&
                    c.Serie == _invoicePostDto.Serie);

                if (numberControl == null)
                {
                    var col = _db.GetCollection<UserConfig>("user_config");
                    var settings = col.FindById("CURRENT") ?? new UserConfig();
                    if (settings != null)
                    {
                        numberControl = new InvoiceNumberControl
                        {
                            ClientId = _branche.ClientId,
                            CompanyId = _branche.CompanyId,
                            BrancheId = _branche.Id,
                            Serie = settings.SeriePDV,
                            LastNumber = int.Parse(settings.FirstInvoiceNumber),
                        };
                        _controls.Insert(numberControl);
                    }
                    else
                    {
                        ErrorMessage = "Série não configurada e nenhuma nota anterior encontrada para inferir o número inicial.";
                        _db.Database.Rollback();
                        return (false, null);
                    }
                }

                if (numberControl.LastNumber > 999999999)
                {
                    ErrorMessage = "Número máximo atingido para esta série";
                    _db.Database.Rollback();
                    return (false, null);
                }

                var nextNumber = numberControl.LastNumber;
                var numeroFormatado = nextNumber.ToString("D9");
                var nfKey = GerarChaveNFCE(numeroFormatado, _branche.CompanyId, _invoicePostDto.Serie, _branche);

                var items = await ProcessInvoiceItemsAsync(_invoicePostDto, nextNumber);
                if (!items.Success)
                {
                    ErrorMessage = $"Erro ao processar itens: {items.Error}";
                    _db.Database.Rollback();
                    return (false, null);
                }

                Invoice = new Invoice
                {
                    ClientId = _branche.ClientId,
                    InvoiceTypeId = _invoicePostDto.InvoiceTypeId,
                    InvoiceId = nextNumber,
                    CompanyId = _invoicePostDto.CompanyId,
                    BrancheId = _invoicePostDto.BrancheId,
                    IssueDate = DateTime.UtcNow,
                    CustomerId = _invoicePostDto.CustomerId,
                    Nfe = numeroFormatado,
                    NfKey = nfKey,
                    Serie = _invoicePostDto.Serie,
                    AuthorizedXml = string.Empty,
                    Protocol = string.Empty,
                    NfeStatus = "NO_SEND",
                    LastUpdate = DateTime.UtcNow,
                    LastChange = DateTime.UtcNow,
                    TotalAmount = items.Items.Sum(i => i.TotalAmount)
                };

                // ===== vincula taxas aos itens por (InvoiceId, ItemNumber) =====
                var taxesByItem = items.Taxes
                    .GroupBy(t => (t.InvoiceId, t.ItemNumber))
                    .ToDictionary(g => g.Key, g => g.ToList());

                // garante chaves nos itens
                foreach (var it in items.Items)
                {
                    it.InvoiceId = Invoice.InvoiceId;

                    // anexa as taxas desse item (se existirem)
                    if (taxesByItem.TryGetValue((Invoice.InvoiceId, it.ItemNumber), out var lst))
                        it.Taxes = lst;
                    else
                        it.Taxes = new List<InvoiceItemTax>();
                }

                // garante chaves nas taxas
                foreach (var tx in items.Taxes)
                {
                    tx.InvoiceId = Invoice.InvoiceId;
                }

                // popula a instância em memória para retorno/uso imediato (XML, impressão, etc.)
                Invoice.Items = items.Items;

                _invoices.Insert(Invoice);
                _invoiceItem.InsertBulk(items.Items);
                _invoiceTaxes.InsertBulk(items.Taxes);

                numberControl.LastNumber++;
                _controls.Update(numberControl);

                _db.Database.Commit();
                return (true, Invoice);
            }
            catch (Exception ex)
            {
                _db.Database.Rollback();
                ErrorMessage = $"Erro ao salvar nota: {ex}";
                return (false, null);
            }
        }


        private async Task<(bool Success, string Error, List<InvoiceItem> Items, List<InvoiceItemTax> Taxes)> ProcessInvoiceItemsAsync(
                         InvoicePostDto invoice,
                         int invoiceId)
        {
            var materials = _db.GetCollection<Material>("material");
            var cfopCollection = _db.GetCollection<CfopDetermination>("cfopdetermination");

            var items = new List<InvoiceItem>();
            var taxes = new List<InvoiceItemTax>();

            for (int i = 0; i < invoice.InvoiceItems.Count; i++)
            {
                var item = new InvoiceItem
                {
                    ClientId = _branche.ClientId,
                    MaterialId = invoice.InvoiceItems[i].MaterialId,
                    MaterialName = invoice.InvoiceItems[i].MaterialName,
                    Quantity = invoice.InvoiceItems[i].Quantity,
                    UnitPrice = invoice.InvoiceItems[i].UnitPrice,
                    DiscountAmount = invoice.InvoiceItems[i].DiscountAmount,
                    UnitId = invoice.InvoiceItems[i].UnitId,
                    ItemNumber = i + 1,
                    InvoiceId = invoiceId,
                    NCM = invoice.InvoiceItems[i].NCM,
                };

                item.TotalAmount = (item.UnitPrice * item.Quantity) - item.DiscountAmount;

                var material = await Task.Run(() => materials
                    .Find(m => m.Id == item.MaterialId && m.ClientId == _branche.ClientId)
                    .FirstOrDefault());

                if (material == null)
                    return (false, $"Material {item.MaterialId} não encontrado para o cliente {_branche.ClientId}.", new(), new());

                var cfop = GetCfopForItem(invoice, _branche, material);
                if (cfop == null)
                    return (false, $"CFOP não determinado para material {material.Id}", new(), new());

                item.CfopId = cfop.CfopId;

                var taxResult = CalculateTaxes(ref item, invoice, material, _branche, invoiceId);

                if (!taxResult.Success || taxResult.Taxes == null || !taxResult.Taxes.Any())
                    return (false, taxResult.Error ?? "Nenhum imposto encontrado para o item.", new(), new());

                items.Add(item);
                taxes.AddRange(taxResult.Taxes);
            }

            return (true, null, items, taxes);
        }
        string GerarChaveNFCE(string numeroNota, string companyId, string serie, Branche branch)
        {
            if (string.IsNullOrEmpty(branch.StateId) || string.IsNullOrEmpty(branch.Cnpj))
                throw new InvalidOperationException("Dados insuficientes para gerar a chave da NFC-e");

            // 1. Obter o código da UF (GovernmentCode) do estado da filial
            string cUF;
            if (!string.IsNullOrEmpty(branch.StateId))
            {
                var states = _db.GetCollection<State>("state");
                var estado = states.FindOne(s => s.Id == branch.StateId && s.CountryId == "BR");

                cUF = estado?.GovernmentCode ?? "35"; // Fallback para SP (35)
            }
            else
            {
                cUF = "35"; // Default para SP se não houver estado definido
            }

            // 2. Formatar os componentes da chave
            string anoMes = DateTime.Now.ToString("yyMM");
            string cnpj = Regex.Replace(branch.Cnpj, "[^0-9]", "").PadLeft(14, '0');
            string mod = "65"; // Modelo NFC-e
            string serieFormatada = serie.PadLeft(3, '0');
            string nNF = (numeroNota ?? "000000001").PadLeft(9, '0');
            string tpEmis = "1"; // Emissão normal

            string cNF = GerarNumeroAleatorioSeguro(cnpj, nNF, serieFormatada, mod, cUF, anoMes, tpEmis);

            // 3. Concatenar componentes (43 caracteres)
            string chave = $"{cUF.PadLeft(2, '0')}{anoMes}{cnpj}{mod}{serieFormatada}{nNF}{tpEmis}{cNF}";

            // 4. Calcular DV
            string dv = CalcularDigitoVerificador(chave);

            return chave + dv; // Chave completa com 44 caracteres
        }

        string GerarNumeroAleatorioSeguro(string cnpj, string nNF, string serie, string modelo, string uf, string anoMes, string tpEmis)
        {
            string baseString = $"{cnpj}{nNF}{serie}{modelo}{uf}{anoMes}{tpEmis}";
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(baseString + DateTime.Now.Ticks)); // Inclui ticks para aleatoriedade
            var valorNumerico = BitConverter.ToUInt32(hash, 0); // Pega os 4 primeiros bytes como uint

            return (valorNumerico % 100000000).ToString("D8"); // Garante 8 dígitos
        }

        string CalcularDigitoVerificador(string baseCalculo)
        {
            int soma = 0;
            int peso = 2;

            for (int i = baseCalculo.Length - 1; i >= 0; i--)
            {
                int digito = int.Parse(baseCalculo[i].ToString());
                soma += digito * peso;
                peso = (peso == 9) ? 2 : peso + 1;
            }

            int resto = soma % 11;
            int dv = (resto < 2) ? 0 : 11 - resto;

            return dv.ToString();
        }

        private CfopDetermination GetCfopForItem(
                             InvoicePostDto invoice,
                             Branche branch,
                             Material material)
        {
            // Coleta as coleções do LiteDB
            var states = _db.GetCollection<State>("state");
            var cfopCollection = _db.GetCollection<CfopDetermination>("cfopdetermination");

            // Localiza o estado com base no StateId e CountryId
            var state = states.FindOne(s =>
                s.Id == branch.StateId &&
                s.CountryId == branch.CountryId);

            if (state == null)
                return null;

            // -- Busca o CFOP com base nos critérios

            return cfopCollection.FindOne(c =>
                c.InvoiceTypeId == invoice.InvoiceTypeId &&
                c.CountryId == branch.CountryId &&
                c.StateFrom == state.Id &&
                c.StateTo == state.Id &&
                c.MaterialTypeId == material.Type);
        }

        private (bool Success, string Error, List<InvoiceItemTax> Taxes) CalculateTaxes(
                           ref InvoiceItem item,
                           InvoicePostDto invoice,
                           Material material,
                           Branche branch,
                           int InvoiceId)
        {
            string stateTo =  branch.StateId;

            var icmsCollection = _db.GetCollection<IcmsStDetermination>("icmsstdetermination");
            var pisCollection = _db.GetCollection<PisDetermination99>("pisdetermination99");
            var cofinsCollection = _db.GetCollection<CofinsDetermination99>("cofinsdetermination99");

            var taxes = new List<InvoiceItemTax>();

            // ICMS-ST
            var icms = icmsCollection.Find(icms =>
                icms.ClientId == branch.ClientId &&
                icms.CountryId == branch.CountryId &&
                icms.OriginState == branch.StateId &&
                icms.DestinationState == stateTo &&
                icms.NcmId == material.NcmId &&
                icms.InvoiceTypeId == invoice.InvoiceTypeId &&
                icms.StartDate <= DateTime.UtcNow)
                .OrderBy(c => c.StartDate)
                .FirstOrDefault();

            if (icms != null)
            {
                item.CstIcmsId = icms.CstId;

                if (icms.CstId == "40")
                {
                    taxes.Add(new InvoiceItemTax
                    {
                        ClientId = _branche.ClientId,
                        InvoiceId = InvoiceId,
                        ItemNumber = item.ItemNumber,
                        TaxTypeId = "STICMS",
                        BaseValue = 0,
                        Rate = 0,
                        Value = 0
                    });
                }
                else
                {
                    var baseICMSST = item.TotalAmount * (1 - icms.BaseReductionPercentage / 100m);
                    var valueICMSST = baseICMSST * (icms.Rate / 100m);

                    taxes.Add(new InvoiceItemTax
                    {
                        ClientId = _branche.ClientId,
                        InvoiceId = InvoiceId,
                        ItemNumber = item.ItemNumber,
                        TaxTypeId = "STICMS",
                        BaseValue = baseICMSST,
                        Rate = icms.Rate,
                        Value = valueICMSST
                    });
                }
            }

            // PIS
            var pis = pisCollection.Find(p =>
                p.ClientId == branch.ClientId &&
                p.CountryId == branch.CountryId &&
                p.InvoiceTypeId == invoice.InvoiceTypeId &&
                p.StartDate <= DateTime.UtcNow)
                .OrderBy(p => p.StartDate)
                .FirstOrDefault();

            if (pis != null)
            {
                item.CstPisId = pis.CstId;

                var basePis = item.TotalAmount * (1 - pis.BaseReductionPercentage / 100m);
                var valuePis = basePis * (pis.Rate / 100m);

                taxes.Add(new InvoiceItemTax
                {
                    ClientId = _branche.ClientId,
                    InvoiceId = InvoiceId,
                    ItemNumber = item.ItemNumber,
                    TaxTypeId = "PIS",
                    BaseValue = basePis,
                    Rate = pis.Rate,
                    Value = valuePis
                });
            }

            // COFINS
            var cofins = cofinsCollection.Find(c =>
                c.ClientId == branch.ClientId &&
                c.CountryId == branch.CountryId &&
                c.InvoiceTypeId == invoice.InvoiceTypeId &&
                c.StartDate <= DateTime.UtcNow)
                .OrderBy(c => c.StartDate)
                .FirstOrDefault();

            if (cofins != null)
            {
                item.CstCofinsId = cofins.CstId;

                var baseCofins = item.TotalAmount * (1 - cofins.BaseReductionPercentage / 100m);
                var valueCofins = baseCofins * (cofins.Rate / 100m);

                taxes.Add(new InvoiceItemTax
                {
                    ClientId = _branche.ClientId,
                    InvoiceId = InvoiceId,
                    ItemNumber = item.ItemNumber,
                    TaxTypeId = "COFINS",
                    BaseValue = baseCofins,
                    Rate = cofins.Rate,
                    Value = valueCofins
                });
            }

            return (true, null, taxes);
        }

        public (bool Success, string Error, int Invoices, int Items, int Taxes) DeleteInvoiceCascade(
                        int invoiceId,
                        bool allowAuthorizedDeletion = false)
        {
            try
            {
                _db.Database.BeginTrans();

                // 1) Localiza a nota (garantindo o escopo do cliente/empresa/filial atuais)
                var inv = _invoices.FindOne(i =>
                    i.InvoiceId == invoiceId &&
                    i.ClientId == _branche.ClientId &&
                    i.CompanyId == _branche.CompanyId &&
                    i.BrancheId == _branche.Id);

                if (inv == null)
                {
                    _db.Database.Rollback();
                    return (false, $"Invoice {invoiceId} não encontrada.", 0, 0, 0);
                }

                // 2) Segurança: evita apagar nota AUTORIZADA (100/150) por engano
                if (!allowAuthorizedDeletion && string.Equals(inv.NfeStatus, "AUTORIZADA", StringComparison.OrdinalIgnoreCase))
                {
                    _db.Database.Rollback();
                    return (false, $"Invoice {invoiceId} já AUTORIZADA. Use allowAuthorizedDeletion=true se quiser prosseguir.", 0, 0, 0);
                }

                // 3) Apaga impostos primeiro (dependem dos itens)
                int taxes = _invoiceTaxes.DeleteMany(t =>
                    t.InvoiceId == invoiceId &&
                    t.ClientId == _branche.ClientId);

                // 4) Apaga itens
                int items = _invoiceItem.DeleteMany(it =>
                    it.InvoiceId == invoiceId &&
                    it.ClientId == _branche.ClientId);

                // 5) Apaga a invoice
                int invoices = _invoices.DeleteMany(i =>
                    i.InvoiceId == invoiceId &&
                    i.ClientId == _branche.ClientId &&
                    i.CompanyId == _branche.CompanyId &&
                    i.BrancheId == _branche.Id);

                _db.Database.Commit();
                return (true, null, invoices, items, taxes);
            }
            catch (Exception ex)
            {
                _db.Database.Rollback();
                return (false, $"Erro ao deletar invoice {invoiceId}: {ex.Message}", 0, 0, 0);
            }
        }

    }
}
