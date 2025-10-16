﻿using Inovesys.Retail.Entities;
using Inovesys.Retail.Models;
using Inovesys.Retail.Services;
using LiteDB;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace Inovesys.Retail.Pages;

public partial class ConsumerSalePage : ContentPage
{
    private ObservableCollection<ConsumerSaleItem> _items = new();


    private LiteDbService _db;

    UserConfig userConfig;

    Client _client { set; get; }
    Company _company { set; get; }
    private readonly HttpClient _http;

    private Branche _branche { set; get; }

    // Defaults (pode ler de LiteDB/UserConfig depois)
    private const string DefaultPrinterName = "MP-4200 TH"; // nome no Windows

    private ToastService _toastService;

    public ConsumerSalePage(LiteDbService liteDatabase, ToastService toastService, IHttpClientFactory httpClientFactor)
    {
        InitializeComponent();
        ItemsListView.ItemsSource = _items;
        UpdateTotal();
        _db = liteDatabase;
        _toastService = toastService;
        _http = httpClientFactor.CreateClient("api");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        userConfig = _db.GetCollection<UserConfig>("user_config").FindById("CURRENT");
        if (userConfig == null)
        {
            await _toastService.ShowToast("Usuário não configurado.");
            return;
        }

        _client = _db.GetCollection<Client>("client").FindOne(c => c.Id == userConfig.ClientId);

        if (_client == null)
        {
            await _toastService.ShowToast("Erro na leitura de cliente.");
            return;
        }

        _branche = _db.GetCollection<Branche>("branche").FindOne(b =>
            b.ClientId == userConfig.ClientId &&
            b.CompanyId == userConfig.DefaultCompanyId &&
            b.Id == userConfig.DefaultBranche);

        if (_branche == null)
        {
            await _toastService.ShowToast("Erro na leitura de filial.");
            return;
        }
        else if (_company == null)
        {
            _company = _db.GetCollection<Company>("company").FindOne(c => c.Id == _branche.CompanyId && c.ClientId == _branche.ClientId);

            if (_company == null)
            {
                await _toastService.ShowToast("Erro na leitura de empresa.");
                return;
            }
        }


        // ✅ Verifica se há notas fiscais pendentes de envio
        var invoiceCol = _db.GetCollection<Invoice>("invoice");
        bool hasPendingInvoice = invoiceCol.Exists(i =>
            i.ClientId == _branche.ClientId &&
            i.CompanyId == _branche.CompanyId &&
            i.BrancheId == _branche.Id &&
            i.NfeStatus == "NO_SEND");

        if (hasPendingInvoice)
        {

            bool enviarAgora = await DisplayAlert(
                       "Nota fiscal pendente",
                       "Há nota(s) fiscal(is) pendente(s) de envio para a SEFAZ.\nDeseja enviar agora?",
                       "Sim", "Não");

            if (enviarAgora)
            {
                // ⚠️ Aqui você pode chamar o serviço de envio, ex:
                var pendente = invoiceCol.FindOne(i =>
                    i.ClientId == _branche.ClientId &&
                    i.CompanyId == _branche.CompanyId &&
                    i.BrancheId == _branche.Id &&
                    i.NfeStatus == "NO_SEND");

                if (pendente != null)
                {
                    // Exemplo: chamar método fictício de envio
                    var sefaz = await EnviarNotaParaSefaz(pendente);
                    if (sefaz.Success)

                        await _toastService.ShowToast("Nota enviada com sucesso.");
                    else
                        await _toastService.ShowToast("Falha ao enviar nota.");
                }
            }
        }

        await CheckAndSendAuthorizedInvoicesAsync();

        await Task.Delay(100);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            entryCustomerCpf.Focus();
        });
    }


    private async Task<(bool Success, string QrCode)> EnviarNotaParaSefaz(Invoice invoice)
    {
        try
        {
            // Carrega os itens da nota

            var itemCollection = _db.GetCollection<InvoiceItem>("invoiceitem");
            var items = itemCollection
                .Find(i => i.ClientId == invoice.ClientId && i.InvoiceId == invoice.InvoiceId)
                .ToList();

            // Carrega as taxas de cada item
            var taxCollection = _db.GetCollection<InvoiceItemTax>("invoiceitemtax");

            foreach (var item in items)
            {
                var taxes = taxCollection
                    .Find(t => t.ClientId == invoice.ClientId && t.InvoiceId == invoice.InvoiceId && t.ItemNumber == item.ItemNumber)
                    .ToList();

                item.Taxes = taxes;
            }

            // Atribui os itens com taxas à invoice
            invoice.Items = items;

            var xmlBuilder = new NFeXmlBuilder(invoice, _client.EnvironmentSefaz, _db, _branche, _company).Build();

            if (xmlBuilder.Error != null)
            {
                Console.WriteLine($"Erro ao gerar XML: {xmlBuilder.Error.Message}");
                // Opcional: detalhar exceção interna
                if (xmlBuilder.Error.InnerException != null)
                    await _toastService.ShowToast(xmlBuilder.Error.InnerException.Message);
                return (false, null);
            }

            var signedXml = xmlBuilder.Xml.InnerXml;
            var qrCode = xmlBuilder.QrCode;

            var sefazService = new SefazService(_db, _company); // instanciado com seus parâmetros

            var result = await sefazService.SendToSefazAsync(invoice.InvoiceId, signedXml, invoice);

            if (result.Success)
            {
                invoice.NfeStatus = "AUTORIZADA";
                invoice.Protocol = result.ProtocolXml;
                invoice.Nfe = invoice.Nfe;
                // xmlString = string com o XML que você recebeu
                var serializer = new XmlSerializer(typeof(ProtNFe));
                using var reader = new StringReader(result.ProtocolXml);
                var resultx = (ProtNFe)serializer.Deserialize(reader);

                invoice.LastUpdate = DateTime.UtcNow;
                invoice.QrCode = qrCode;
                invoice.IssueDate = DateTime.Parse(xmlBuilder.dateHoraEmissao);
                invoice.AuthorizationDate = resultx.InfProt.DhRecbto;
                invoice.Protocol = resultx.InfProt.NProt.ToString();
                invoice.AuthorizedXml = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml));

                var invoiceCollection = _db.GetCollection<Invoice>("invoice");
                invoiceCollection.Update(invoice);

                return (true, qrCode);
            }
            else
            {
                await _toastService.ShowToast(result.Message);
                return (false, null);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao carregar a nota: {ex.Message}");
            return (false, null);
        }
    }

    private void UpdateTotal()
    {
        var total = _items.Sum(i => i.Price * i.Quantity);
        lblTotal.Text = $"Total: R$ {total.ToString("N2", new CultureInfo("pt-BR"))}";
    }

    private async void OnProductCodeEntered(object sender, EventArgs e)
    {
        var code = entryProductCode.Text?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                entryProductCode.Focus();

            });
            return;
        }

        var col = _db.GetCollection<Material>("material");
        var product = col.FindOne(p =>
            p.ClientId == _branche.ClientId &&
            (p.Ean13 == code || p.Id == code));

        if (product == null)
        {

            entryProductCode.Text = string.Empty;
            await Task.Delay(100); // pequeno delay ajuda em dispositivos físicos
            MainThread.BeginInvokeOnMainThread(() =>
            {
                entryProductCode.Focus();
            });

            await _toastService.ShowToast("Produto não encontrado.");
            return;
        }

        var priceCol = _db.GetCollection<MaterialPrice>("materialprice");
        var today = DateTime.Today;

        var price = priceCol.FindOne(p =>
            p.ClientId == _branche.ClientId &&
            p.MaterialId == product.Id &&
            p.StartDate <= today &&
            p.EndDate >= today);

        if (price == null)
        {
            await _toastService.ShowToast("Preço não encontrado.");
            return;
        }

        int quantity = 1;
        if (!int.TryParse(entryQuantity.Text?.Trim(), out quantity))
            quantity = 1;

        var nextItemNumber = _items.Any()
            ? _items.Max(i => i.Id) + 1
            : 1;

        _items.Add(new ConsumerSaleItem
        {
            Id = nextItemNumber,
            MaterialId = product.Id,
            Description = product.Name,
            Price = price.Price * quantity,
            Quantity = quantity,
            NCM = product.NcmId
        });

        entryProductCode.Text = string.Empty;
        await Task.Delay(100); // pequeno delay ajuda em dispositivos físicos
        MainThread.BeginInvokeOnMainThread(() =>
        {
            entryProductCode.Focus();

        });
        entryQuantity.Text = "1";
        UpdateTotal();
    }

    private void OnCancelItemClicked(object sender, EventArgs e)
    {
        if (ItemsListView.SelectedItem is ConsumerSaleItem selectedItem)
        {
            _items.Remove(selectedItem);
            UpdateTotal();
        }
    }

    private void OnDiscountClicked(object sender, EventArgs e)
    {
        UpdateTotal();
    }

    private async void OnCancelSaleClicked(object sender, EventArgs e)
    {
        if (await DisplayAlert("Cancelar Venda", "Deseja cancelar a venda atual?", "Sim", "Não"))
        {
            _items.Clear();
            UpdateTotal();
        }
    }

    private async void OnFinalizeSaleClicked(object sender, EventArgs e)
    {
        if (_items.Count == 0)
        {
            await _toastService.ShowToast("Não há itens na venda para finalizar");
            return;
        }

        var body = new InvoicePostDto
        {
            CompanyId = _branche.CompanyId,
            BrancheId = _branche.Id,
            InvoiceTypeId = "01",
            Serie = userConfig.SeriePDV,
            InvoiceItems = _items.Select((item, index) => new InvoiceItemPostDto
            {
                Id = (item.Id),
                MaterialId = (item.MaterialId),
                MaterialName = (item.Description),
                Quantity = item.Quantity,
                UnitPrice = item.Price / item.Quantity,
                DiscountAmount = 0,
                UnitId = "UN",
                NCM = item.NCM,
            }).ToList()
        };

        var NCFE = new InvoiceService(_db, body, branche: _branche);
        var post = await NCFE.CreateAsync();
        if (!post.Sucess)
        {
            await _toastService.ShowToast(NCFE.ErrorMessage);
            return;
        }

        // CPF no XML: se o campo estiver preenchido e válido, grava no Invoice
        var cpfDigits = OnlyDigits(entryCustomerCpf.Text?.Trim() ?? "");
        if (cpfDigits.Length == 11 && IsValidCpf(cpfDigits))
        {
            post.Invoice.Customer ??= new Customer();
            post.Invoice.Customer.Document = cpfDigits;            // usado pelo NFeXmlBuilder para <dest><CPF>
            if (string.IsNullOrWhiteSpace(post.Invoice.Customer.Name))
                post.Invoice.Customer.Name = "CONSUMIDOR";

            // Se existir indicador de IE do destinatário no seu modelo, garanta '9' (não contribuinte):
            // post.Invoice.Customer.IndicadorIEDest = "9"; // ajuste o nome do campo conforme seu modelo
            // Se houver tipo de pessoa:
            // post.Invoice.Customer.Type = "F";            // Pessoa Física (se existir no seu modelo)

            // persiste no LiteDB antes da montagem/assinatura, por segurança
            var invCol = _db.GetCollection<Invoice>("invoice");
            invCol.Update(post.Invoice);
        }


        // CALCULA IBPT AQUI, ANTES DE ASSINAR/ENVIAR
        var ibpt = TaxUtils.CalculateApproximateTaxes(post.Invoice, _db, assumeImported: false);
        post.Invoice.AdditionalInfo = TaxUtils.BuildIbptObservation(ibpt);

        var send = await EnviarNotaParaSefaz(invoice: post.Invoice);
        if (!send.Success)
        {
            await _toastService.ShowToast("Erro ao enviar nota fiscal para SEFAZ.");

            return;
        }

        Task printed = ImprimirCupomViaEscPosAsync(invoice: post.Invoice, send.QrCode);
        await printed;
        if (printed.IsFaulted)
        {
            await _toastService.ShowToast("Erro ao imprimir o cupom.");
        }
        else
        {
            var invoiceCollection = _db.GetCollection<Invoice>("invoice");
            post.Invoice.Printed = true;
            invoiceCollection.Update(post.Invoice);
        }

        var backofficeReq = BuildBackofficeRequestFromInvoice(post.Invoice);
        var bo = await SendInvoiceToBackofficeAsync(backofficeReq);
        if (!bo)
        {
            // Mostra mensagem amigável + log opcional com bo.RawBody
            await _toastService.ShowToast($"Falha ao enviar para retaguarda");
            // opcional: Console.WriteLine(bo.RawBody);
        }
        else
        {
            await _toastService.ShowToast("Retaguarda sincronizada.");
            var invoiceCollection = _db.GetCollection<Invoice>("invoice");
            post.Invoice.Send = true;
            invoiceCollection.Update(post.Invoice);
        }

        _items.Clear();

        entryQuantity.Text = "1";
        entryCustomerCpf.Text = string.Empty;

        _ = Task.Delay(100); // pequeno delay ajuda em dispositivos físicos
        MainThread.BeginInvokeOnMainThread(() =>
        {
            entryCustomerCpf.Focus();

        });

        UpdateTotal();

        await _toastService.ShowToast("Venda finalizada com sucesso.");
    }
    private void OnQuantityFocused(object sender, FocusEventArgs e)
    {
        if (sender is Entry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Text))
            {
                entry.Text = "1";
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                entry.CursorPosition = 0;
                entry.SelectionLength = entry.Text.Length;
            });
        }
    }


    private BackofficeInvoiceRequest BuildBackofficeRequestFromInvoice(Invoice inv)
    {
        if (inv is null) throw new ArgumentNullException(nameof(inv));

        // customerId: se não houver numérico, fica 0 (consumidor final)

        var items = (inv.Items ?? new List<InvoiceItem>()).Select(it =>
        {
            var qty = it.Quantity <= 0 ? 1 : it.Quantity;
            var unitPrice = it.UnitPrice > 0
                ? it.UnitPrice
                : (it.TotalAmount > 0 ? it.TotalAmount / qty : 0m);

            var total = it.TotalAmount > 0
                ? it.TotalAmount
                : unitPrice * qty;

            return new BackofficeInvoiceItem
            {
                ItemNumber = it.ItemNumber,
                MaterialId = it.MaterialId,
                Quantity = qty,
                UnitPrice = unitPrice,
                DiscountAmount = it.DiscountAmount is decimal d ? d : 0m,
                TotalAmount = total,
                UnitId = string.IsNullOrWhiteSpace(it.UnitId) ? "UN" : it.UnitId,
                CFOPId = it.CfopId,
            };
        }).ToList();

        // Calcula o total da nota somando os itens
        var totalAmount = items.Sum(x => x.TotalAmount);

        var req = new BackofficeInvoiceRequest
        {
            InvoiceTypeId = string.IsNullOrWhiteSpace(inv.InvoiceTypeId) ? "01" : inv.InvoiceTypeId,
            CompanyId = string.IsNullOrWhiteSpace(inv.CompanyId) ? _branche.CompanyId : inv.CompanyId,
            BrancheId = string.IsNullOrWhiteSpace(inv.BrancheId) ? _branche.Id : inv.BrancheId,
            CustomerId = inv.Customer.Id,
            NFKey = inv.NfKey,
            NFe = inv.Nfe,
            IssueDate = inv.IssueDate,
            AuthorizedXml = string.IsNullOrWhiteSpace(inv.AuthorizedXml) ? null : Convert.FromBase64String(inv.AuthorizedXml),
            // usa a série da própria invoice; se vier vazia, cai para a config do PDV
            Serie = inv.Serie,
            // total calculado no cabeçalho
            TotalAmount = totalAmount,

            // itens
            InvoiceItems = items
        };

        return req;
    }


    private async Task<bool> SendInvoiceToBackofficeAsync(BackofficeInvoiceRequest req, CancellationToken ct = default)
    {
        try
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(req);

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, "invoices")
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            };
            httpReq.Headers.Accept.Clear();
            httpReq.Headers.Accept.ParseAdd("*/*");

            using var resp = await _http.SendAsync(httpReq, ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                Console.WriteLine($"[Backoffice] FAIL -> Status: {(int)resp.StatusCode} {resp.ReasonPhrase}");
                Console.WriteLine($"[Backoffice] Body: {body}");
                return false;
            }

            return true;
        }
        catch (TaskCanceledException tex) when (!ct.IsCancellationRequested)
        {
            Console.WriteLine($"[Backoffice] TIMEOUT: {tex.Message}");
            return false;
        }
        catch (HttpRequestException hex)
        {
            Console.WriteLine($"[Backoffice] NETWORK ERROR: {hex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Backoffice] UNEXPECTED ERROR: {ex}");
            return false;
        }
    }


    private async Task CheckAndSendAuthorizedInvoicesAsync()
    {
        try
        {
            var invoiceCol = _db.GetCollection<Invoice>("invoice");

            // busca notas AUTORIZADA que não foram enviadas (Send == false)
            var pendentes = invoiceCol.Find(i =>
                i.ClientId == _branche.ClientId &&
                i.CompanyId == _branche.CompanyId &&
                i.BrancheId == _branche.Id &&
                i.NfeStatus == "AUTORIZADA" &&
                (i.Send == false)
            ).ToList();

            if (pendentes == null || pendentes.Count == 0) return;

            bool enviarAgora = await DisplayAlert(
                "Notas autorizadas pendentes",
                $"Existem {pendentes.Count} nota(s) autorizada(s) e não enviadas para a retaguarda.\nDeseja enviar agora?",
                "Sim", "Não");

            if (!enviarAgora) return;

            int successCount = 0;
            int failCount = 0;

            foreach (var inv in pendentes)
            {
                try
                {

                    // monta request e envia
                    var backReq = BuildBackofficeRequestFromInvoice(inv);

                    var ok = await SendInvoiceToBackofficeAsync(backReq);

                    if (ok)
                    {
                        // só marca como enviado se o backoffice confirmou (ou se a chamada HTTP foi 2xx)
                        inv.Send = true;

                        invoiceCol.Update(inv);
                        successCount++;
                    }

                }
                catch (Exception exItem)
                {
                    Console.WriteLine($"[Backoffice] Erro ao enviar invoice {inv.InvoiceId}: {exItem}");

                    failCount++;
                }
            }

            await _toastService.ShowToast($"Envio concluído. Sucesso: {successCount}, Falhas: {failCount}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erro ao checar/enviar notas pendentes: {ex}");
            await _toastService.ShowToast("Erro ao verificar notas pendentes.");
        }
    }

    #region Impressão NFC-e via ESC/POS

    // Helper: escrever linha ESC/POS
    static void EscPos_SetLineSpacing(Stream s, byte dots) => s.Write(new byte[] { 0x1B, 0x33, dots }, 0, 3);  // ESC 3 n
    static void EscPos_Reset(Stream s) => s.Write(new byte[] { 0x1B, 0x40 });                         // ESC @
    static void EscPos_Align(Stream s, byte n) => s.Write(new byte[] { 0x1B, 0x61, n });                      // 0=left 1=center 2=right
    static void EscPos_Bold(Stream s, bool on) => s.Write(new byte[] { 0x1B, 0x45, (byte)(on ? 1 : 0) });     // ESC E n
    static void EscPos_Feed(Stream s, byte n) => s.Write(new byte[] { 0x1B, 0x64, n });                       // ESC d n
    static void EscPos_Cut(Stream s) => s.Write(new byte[] { 0x1D, 0x56, 0x42, 0x00 });             // GS V B 0

    // Encoding atual usada para converter string -> bytes
    static Encoding CurrentEnc = CodePagesEncodingProvider.Instance.GetEncoding(1252); // default

    static void SetEncoding(Encoding enc) => CurrentEnc = enc ?? Encoding.UTF8;

    static void EscPos_FontB(Stream s) => s.Write(new byte[] { 0x1B, 0x4D, 0x01 }, 0, 3);
    static void EscPos_FontA(Stream s) => s.Write(new byte[] { 0x1B, 0x4D, 0x00 }, 0, 3);
    static void EscPos_SetCodePage(Stream s, byte escT, int dotnetCodePage)
    {
        // ESC t n  → seleciona “character code table” na impressora
        s.Write(new byte[] { 0x1B, 0x74, escT }, 0, 3);
        SetEncoding(CodePagesEncodingProvider.Instance.GetEncoding(dotnetCodePage));
    }

    public async Task ImprimirCupomViaEscPosAsync(Invoice invoice, string qrCodeUrl)
    {
        try
        {

            const int COLS = 42;
            using var ms = new MemoryStream();

            EscPos_Reset(ms);
            EscPos_SetCodePage(ms, 0x00, 437);

            // ----- Cabeçalho (alinhado à esquerda, com algumas linhas em negrito/centrado) -----
            EscPos_Bold(ms, true);
            EscPos_Align(ms, 1);
            Writeln(ms, "Documento Auxiliar da NFC-e", COLS);
            EscPos_Bold(ms, false);
            EscPos_Align(ms, 0);

            EscPos_Align(ms, 1);
            Writeln(ms, $"CNPJ: {_branche?.Cnpj} ");
            Writeln(ms, $"{_company?.Description}", COLS);

            EscPos_Align(ms, 0);

            Writeln(ms, $"{_branche?.Street}, {_branche?.HouseNumber} - {_branche?.Neighborhood} - {_branche?.CityId} - {_branche?.StateId} - CEP: {_branche?.PostalCode}", COLS);

            EscPos_Align(ms, 1); // 0=esquerda, 1=centro, 2=direita
            Writeln(ms, $"IE: {_branche?.StateRegistration}", COLS);
            EscPos_Align(ms, 0); // volta para alinhamento à esquerda
                                 // Fonte menor + line spacing baixo
            EscPos_FontB(ms);                 // menor que a A
            EscPos_SetLineSpacing(ms, 8);     // compacto (6–8 costuma ficar bom)  

            Writeln(ms, $"Item Codigo Descricao        Qtde Un      Valor    Total");
            int seq = 1;
            foreach (var it in invoice.Items)
            {
                var code = !string.IsNullOrWhiteSpace(it.MaterialId) ? it.MaterialId : (it.MaterialId ?? "");
                var desc = string.IsNullOrWhiteSpace(it.MaterialName) ? code : $"{it.MaterialName}";
                LeftRight(ms, $"{seq:000} {code} {desc}", $"", COLS);

                var un = string.IsNullOrWhiteSpace(it.UnitId) ? "UN" : it.UnitId.ToUpperInvariant();
                var q = it.Quantity;
                var unit = it.UnitPrice;
                var tot = unit * q;


                EscPos_Align(ms, 2);
                Writeln(ms, $"{q:0.##} {un} x R$ {unit:N2} = R$ {tot:N2}", COLS);
                EscPos_Reset(ms);

                seq++;
            }

            Line(ms, COLS);
            LeftRight(ms, "Qtde. total de itens", $"{invoice.Items.Sum(i => i.Quantity):0.##}", COLS);

            // ----- Totais -----
            var vSub = invoice.Items.Sum(i => i.UnitPrice * i.Quantity);
            var vDesc = 0; //invoice.Discount ?? 0m;
            var vAcres = 0; // invoice.Surcharge ?? 0m;
            var vTotal = vSub - vDesc + vAcres;

            if (vDesc > 0) LeftRight(ms, "Descontos", $"R$ {vDesc:N2}", COLS);
            if (vAcres > 0) LeftRight(ms, "Acréscimos", $"R$ {vAcres:N2}", COLS);

            EscPos_Bold(ms, true);
            LeftRight(ms, "VALOR TOTAL R$", $"{vTotal:N2}", COLS);
            EscPos_Bold(ms, false);
            // ----- Informações complementares / IBPT -----
            if (!string.IsNullOrWhiteSpace(invoice.AdditionalInfo))
            {
                // largura maior só para este bloco
                const int INFO_COLS = 42; // ajuste conforme sua bobina (ex.: 40 ou 42 chars)

                Line(ms, COLS);
                EscPos_Bold(ms, true);
                Writeln(ms, "INFORMACOES COMPLEMENTARES", COLS); // sem acento por segurança ESC/POS
                EscPos_Bold(ms, false);

                foreach (var line in Wrap(invoice.AdditionalInfo, INFO_COLS))
                    Writeln(ms, line, INFO_COLS);
            }


            // ----- Formas de pagamento (como no cupom) -----
            if (invoice.InvoicePayments?.Any() == true)
            {
                Writeln(ms, "FORMA DE PAGAMENTO", COLS);
                foreach (var p in invoice.InvoicePayments)
                {
                    // Ex.: "Cartão de Crédito Visa            139,98"
                    LeftRight(ms, p.PaymentId.ToString(), $"{p.Amount:N2}", COLS);
                }
            }

            Line(ms, COLS);

            // Fonte menor + line spacing baixo
            EscPos_FontB(ms);                 // menor que a A
            EscPos_SetLineSpacing(ms, 6);     // compacto (6–8 costuma ficar bom)
            // ----- Consulta por chave/URL -----
            Writeln(ms, "Consulte pela Chave de Acesso em", COLS);
            Writeln(ms, "https://www.nfce.fazenda.sp.gov.br/consulta", COLS);
            if (!string.IsNullOrWhiteSpace(invoice.NfKey))
                Writeln(ms, Regex.Replace(invoice.NfKey, ".{4}", "$0 ").Trim(), COLS); // agrupa de 4 em 4

            // ----- Consumidor -----
            if (!string.IsNullOrWhiteSpace(invoice.Customer.Document))
            {
                Writeln(ms, $"CONSUMIDOR - CPF {CpfMask((invoice.Customer.Document))}", COLS);
            }

            //if (!string.IsNullOrWhiteSpace(invoice.Customer.AddressId))
            //    Writeln(ms, invoice.ConsumerAddress, COLS);

            // ----- Dados NFC-e (número/serie/data hora/protocolo/autorização) -----

            var dataAut = invoice.AuthorizationDate?.ToString("dd/MM/yyyy HH:mm:ss");
            var dataEmi = invoice.IssueDate.ToString("dd/MM/yyyy HH:mm:ss");
            Writeln(ms, $"NFC-e  {invoice.Nfe}  Série {invoice.Serie}");
            Writeln(ms, $"Data de Emissão {dataEmi}");
            if (!string.IsNullOrWhiteSpace(invoice.AuthorizationProtocol))
                Writeln(ms, $"Protocolo de Autorização: {invoice.AuthorizationProtocol}", COLS);
            if (!string.IsNullOrWhiteSpace(dataAut))
                Writeln(ms, $"Data de Autorização: {dataAut}", COLS);

            // ----- QR Code -----
            EscPos_Align(ms, 1);
            var qr = BuildQrCodeEscPos(qrCodeUrl);
            ms.Write(qr, 0, qr.Length);
            EscPos_Align(ms, 0);


            EscPos_Feed(ms, 3);
            EscPos_Cut(ms);

            var payload = ms.ToArray();

            // ===== Envio TCP (mantendo seu "header" com o nome da impressora) =====
            using var client = new TcpClient();
            await client.ConnectAsync(userConfig.PrinterIp, userConfig.PrinterPort).ConfigureAwait(false);
            using var stream = client.GetStream();

            var header = Encoding.UTF8.GetBytes((userConfig.PrinterName ?? DefaultPrinterName) + "\n");
            await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _toastService.ShowToast($"Falha ao enviar impressão: {ex.Message}");
        }
    }

    private static readonly Encoding Enc1252 =
     CodePagesEncodingProvider.Instance.GetEncoding(1252); // ou Encoding.GetEncoding(1252)

    static void Writeln(Stream s, string text, int width = 48)
    {
        // Sanitiza para ASCII antes de imprimir
        text = ToAsciiEscPos(text);


        var bytes = Enc1252.GetBytes(text ?? "");
        s.Write(bytes, 0, bytes.Length);
        //s.Write(new byte[] { 0x0A }, 0, 1);
        // avança bem pouco (10 dots)
        //s.Write(new byte[] { 0x1B, 0x33, 10, 0x0A }, 0, 4);
        //s.Write(new byte[] { 0x1B, 0x33, 8, 0x0A }, 0, 4); // mais compacto
        s.Write(new byte[] { 0x1B, 0x33, 6, 0x0A }, 0, 4); // bem compacto

    }


    static string ToAsciiEscPos(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // 1) Normaliza e remove diacríticos (acentos)
        string norm = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);

        foreach (var ch in norm)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
                continue; // descarta acento
            sb.Append(ch);
        }

        // 2) Recompõe e aplica mapeamentos comuns para ESC/POS
        string s = sb.ToString().Normalize(NormalizationForm.FormC)
            .Replace('’', '\'')
            .Replace('‘', '\'')
            .Replace('“', '"')
            .Replace('”', '"')
            .Replace('–', '-')  // en dash
            .Replace('—', '-')  // em dash
            .Replace('•', '*')
            .Replace('º', 'o')
            .Replace('ª', 'a')
            // Alguns modelos “comem” o símbolo de moeda; se preferir, troque por “R$”
            .Replace('€', 'E')
            .Replace('£', 'L')
            .Replace('¥', 'Y')
            .Replace('₩', 'W');

        // 3) Filtra para ASCII imprimível (32..126). Troca fora disso por espaço
        var outSb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c >= 32 && c <= 126) outSb.Append(c);
            else outSb.Append(' ');
        }
        return outSb.ToString();
    }



    static IEnumerable<string> Wrap(string text, int width)
    {
        if (string.IsNullOrEmpty(text)) { yield return ""; yield break; }

        var words = Regex.Split(text, @"\s+");
        var line = new StringBuilder();
        foreach (var w in words)
        {
            if (line.Length == 0) { line.Append(w); continue; }
            if (line.Length + 1 + w.Length <= width) line.Append(' ').Append(w);
            else { yield return line.ToString(); line.Clear(); line.Append(w); }
        }
        if (line.Length > 0) yield return line.ToString();
    }

    static void LeftRight(Stream s, string left, string right, int width = 48)
    {
        left ??= "";
        right ??= "";
        if (left.Length + right.Length > width)
        {
            // quebra o lado esquerdo e imprime o lado direito na última linha
            var leftLines = Wrap(left, width - (right.Length + 1)).ToList();
            for (int i = 0; i < leftLines.Count; i++)
            {
                if (i == leftLines.Count - 1)
                {
                    var pad = width - (leftLines[i].Length + right.Length);
                    Writeln(s, leftLines[i] + new string(' ', Math.Max(1, pad)) + right, width);
                }
                else Writeln(s, leftLines[i], width);
            }
        }
        else
        {
            var pad = width - (left.Length + right.Length);
            Writeln(s, left + new string(' ', pad) + right, width);
        }
    }

    static void Line(Stream s, int width = 48) => Writeln(s, new string('-', width), width);

    static string CpfMask(string? cpf)
    {
        cpf = cpf?.Trim().Replace(".", "").Replace("-", "");
        if (string.IsNullOrEmpty(cpf) || cpf.Length != 11) return cpf ?? "";
        return Convert.ToUInt64(cpf).ToString(@"000\.000\.000\-00");
    }

    static byte[] BuildQrCodeEscPos(string data)
    {
        // QR Model 2, ECC M, tamanho ~ 6
        var enc = Encoding.UTF8;
        byte[] model = { 0x1D, 0x28, 0x6B, 0x04, 0x00, 0x31, 0x41, 0x32, 0x00 };               // model 2
        byte[] ecc = { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x45, 0x32 };                      // ECC M
        byte[] size = { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x43, 0x06 };                      // module size 6

        var payload = enc.GetBytes(data ?? "");
        int pL = (payload.Length + 3) & 0xFF;
        int pH = (payload.Length + 3) >> 8;
        byte[] store = { 0x1D, 0x28, 0x6B, (byte)pL, (byte)pH, 0x31, 0x50, 0x30 };
        byte[] print = { 0x1D, 0x28, 0x6B, 0x03, 0x00, 0x31, 0x51, 0x30 };

        using var ms = new MemoryStream();
        ms.Write(model); ms.Write(ecc); ms.Write(size); ms.Write(store); ms.Write(payload); ms.Write(print);
        return ms.ToArray();
    }

    #endregion  Impressão NFC-e via ESC/POS

    private async void OnCadastrarClienteClicked(object sender, EventArgs e)
    {
        _fromButton = true; // marca que o foco saiu por causa do clique

        string cpf = entryCustomerCpf.Text?.Trim();

        if (string.IsNullOrWhiteSpace(cpf))
        {
            await _toastService.ShowToast("Informe um CPF para cadastrar.");
            return;
        }


        if (!IsValidCpf(cpf))
        {
            await DisplayAlert("CPF inválido", "Por favor, informe um CPF válido.", "OK");
            return;
        }

        // aqui você chama sua lógica de cadastro
        // por exemplo: abre uma nova página ou envia para API
        await Navigation.PushAsync(new CustomerRegistrationPage(cpf, _db));
    }

    private void OnCpfCompleted(object sender, EventArgs e)
    {
        validarCPF();
    }
    private bool _fromButton = false;
    private void OnCpfUnfocused(object sender, FocusEventArgs e)
    {
        validarCPF();
    }

    private async void validarCPF()
    {
        var cpf = entryCustomerCpf.Text?.Trim();
        if (string.IsNullOrEmpty(cpf))
        {
            entryProductCode.Focus(); // não valida se estiver vazio
            return;
        }

        if (!IsValidCpf(cpf))
        {
            // limpa o campo para não ficar preso em loop
            entryCustomerCpf.Text = string.Empty;

            await DisplayAlert("CPF inválido", "Por favor, informe um CPF válido.", "OK");
            entryCustomerCpf.Focus();
        }
        else
        {
            // se válido, formata
            entryCustomerCpf.Text = MaskCpf(cpf);


            // joga o foco no código do produto
            MainThread.BeginInvokeOnMainThread(() =>
            {
                entryProductCode.Focus();
            });
        }
    }


    private static string OnlyDigits(string input) =>
        new string(input.Where(char.IsDigit).ToArray());

    private static bool IsValidCpf(string cpf)
    {
        cpf = OnlyDigits(cpf);
        if (cpf.Length != 11) return false;
        if (cpf.Distinct().Count() == 1) return false; // todos iguais

        int[] mult1 = { 10, 9, 8, 7, 6, 5, 4, 3, 2 };
        int[] mult2 = { 11, 10, 9, 8, 7, 6, 5, 4, 3, 2 };

        string temp = cpf.Substring(0, 9);
        int sum = 0;
        for (int i = 0; i < 9; i++) sum += int.Parse(temp[i].ToString()) * mult1[i];
        int resto = sum % 11;
        int dig1 = resto < 2 ? 0 : 11 - resto;

        temp += dig1;
        sum = 0;
        for (int i = 0; i < 10; i++) sum += int.Parse(temp[i].ToString()) * mult2[i];
        resto = sum % 11;
        int dig2 = resto < 2 ? 0 : 11 - resto;

        return cpf.EndsWith(dig1.ToString() + dig2.ToString());
    }

    private static string MaskCpf(string cpf)
    {
        cpf = OnlyDigits(cpf);
        return Convert.ToUInt64(cpf).ToString(@"000\.000\.000\-00");
    }



}


