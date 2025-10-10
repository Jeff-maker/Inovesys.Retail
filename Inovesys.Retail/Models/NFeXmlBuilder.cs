using Inovesys.Retail.Entities;
using Inovesys.Retail.Services;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Inovesys.Retail.Models
{
    public class NFeXmlBuilder
    {
        private readonly Invoice _invoice;
        private readonly XmlDocument _xmlDoc;
        private readonly string _environmentSefaz;
        private readonly LiteDbService _liteDbService;
        private readonly Branche _branche;
        private readonly Company _company;

        public NFeXmlBuilder(Invoice invoice, string environmentSefaz, LiteDbService liteDbService, Branche branche, Company company)
        {
            _invoice = invoice;

            _xmlDoc = new XmlDocument { PreserveWhitespace = true };

            _environmentSefaz = environmentSefaz;
            _liteDbService = liteDbService;
            _branche = branche;
            _company = company;
        }

        public (XmlDocument Xml, string QrCode,string dateHoraEmissao,  Exception? Error) Build()
        {

            try
            {
                var root = _xmlDoc.CreateElement("NFe");

            root.SetAttribute("xmlns", "http://www.portalfiscal.inf.br/nfe");

            var infNFe = _xmlDoc.CreateElement("infNFe");

            infNFe.SetAttribute("versao", "4.00");

            infNFe.SetAttribute("Id", $"NFe{_invoice.NfKey}");

            var dateHoraEmissao = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");

            AddBasicInfo(infNFe, _environmentSefaz, dateHoraEmissao);

            AddEmitterInfo(infNFe);

            AddReceiverInfo(infNFe, _environmentSefaz == "2");

            AddItemsInfo(infNFe, _environmentSefaz);

            AddTotalInfo(infNFe);

            AddTransportInfo(infNFe);

            AddPaymentInfo(infNFe);

            var infAdic = _xmlDoc.CreateElement("infAdic");

            infAdic.AppendChild(CreateElement("infCpl", _invoice.AdditionalInfo));

            infNFe.AppendChild(infAdic);

            var infRespTec = _xmlDoc.CreateElement("infRespTec");

            infRespTec.AppendChild(CreateElement("CNPJ", "12277179000108"));

            infRespTec.AppendChild(CreateElement("xContato", "Responsável Técnico"));

            infRespTec.AppendChild(CreateElement("email", "tecnico@empresa.com"));

            infRespTec.AppendChild(CreateElement("fone", "11999999999"));

            infNFe.AppendChild(infRespTec);

            root.AppendChild(infNFe);

            _xmlDoc.AppendChild(root);

            var xmlSemmIdentacao = new XmlDocument { PreserveWhitespace = true };

            xmlSemmIdentacao.LoadXml(_xmlDoc.InnerXml);

            //SignXmlDocument(_invoice.NfKey, ref xmlSemmIdentacao, _invoice.Company.CertificateId, _dbContext, _invoice.ClientId, _helpers, dateHoraEmissa);
            SignXmlDocument(ref xmlSemmIdentacao, _environmentSefaz, dateHoraEmissao, out string QrCode);

            return (xmlSemmIdentacao, QrCode , dateHoraEmissao , null);

            }
            catch (Exception ex)
            {
                // Aqui você pode logar o erro se quiser
                // _logger.LogError(ex, "Erro ao gerar XML da NFe.");

                return (null, null, null, ex);
            }
        }
              

        private void AddBasicInfo(XmlElement infNFe, string ambiente, string dateHoraEmiss)
        {
            var ide = _xmlDoc.CreateElement("ide");

            bool isConsumidorFinal = true;

            var elements = new (string Name, string Value)[]
            {
                ("cUF", _invoice.NfKey ?.Length >= 2 ? _invoice.NfKey[..2] : ""),
                ("cNF", _invoice.NfKey?.Length >= 43 ? _invoice.NfKey.Substring(35, 8) : ""),
                ("natOp", "VENDA AO CONSUMIDOR"),
                ("mod", "65"),
                ("serie", _invoice.NfKey?.Length >= 25 ? _invoice.NfKey.Substring(22, 3).TrimStart('0') : ""),
                ("nNF", _invoice.NfKey?.Length >= 34 ? _invoice.NfKey.Substring(25, 9).TrimStart('0') : ""),
                ("dhEmi", dateHoraEmiss),
                ("tpNF", "1"),
                ("idDest", isConsumidorFinal ? "1" : "2"), // 1=Operação interna, 2=Interestadual
                ("cMunFG", _branche?.CityId ?? ""),
                ("tpImp", "4"), // 4=DANFE NFC-e
                ("tpEmis", "1"),
                ("cDV", _invoice.NfKey?.Length >= 44 ? _invoice.NfKey.Substring(43, 1) : ""),
                ("tpAmb", ambiente), // 2=Homologação
                ("finNFe", "1"), // 1=NF-e normal
                ("indFinal", isConsumidorFinal ? "1" : "0"), // 1=Consumidor final
                ("indPres", isConsumidorFinal ? "1" : "0"), // 1=Operação presencial
                ("procEmi", "0"), // 0=Emissão própria
                ("verProc", "1.0")
            };

            foreach (var (name, value) in elements)
            {
                ide.AppendChild(CreateElement(name, value));
            }

            infNFe.AppendChild(ide);
        }

        private void AddEmitterInfo(XmlElement infNFe)
        {
            var emit = _xmlDoc.CreateElement("emit");

            if (_branche.Cnpj != null)
            {
                emit.AppendChild(CreateElement("CNPJ", Regex.Replace(_branche.Cnpj, "[^0-9]", "")));
            }

            emit.AppendChild(CreateElement("xNome", _branche.Description));

            if (_branche.CityId != null) // Ensure address and City are not null
            {
                var addressElements = new (string Name, string Value)[]
                    {
                        ("xLgr", _branche.Street),
                        ("nro", _branche.HouseNumber),
                        ("xBairro", _branche.Neighborhood),
                        ("cMun", _branche.CityId),
                        ("xMun", _branche.Neighborhood),
                        ("UF", _branche.StateId),
                        ("CEP", Regex.Replace(_branche.PostalCode ?? string.Empty, @"\D", "")), // Remove caracteres não numéricos
                        ("cPais", "1058"),
                        ("xPais", "BRASIL")
                    };

                var enderEmit = _xmlDoc.CreateElement("enderEmit");
                foreach (var (name, value) in addressElements)
                {
                    if (value != null) // Ensure null values are skipped
                    {
                        enderEmit.AppendChild(CreateElement(name, value));
                    }
                }
                emit.AppendChild(enderEmit);
            }

            emit.AppendChild(CreateElement("IE", _branche.StateRegistration));


            string crtCode = string.Empty;

            switch (_branche.TaxRegime)
            {
                case "SimplesNacional":
                    crtCode = "1";
                    break;
                case "SimplesNacionalExcesso":
                    crtCode = "2";
                    break;
                case "Normal":
                    crtCode = "3";
                    break;
                default:
                    throw new InvalidOperationException($"Regime tributário inválido: {_branche.TaxRegime}");
            }

            emit.AppendChild(CreateElement("CRT", crtCode));

            infNFe.AppendChild(emit);
        }

        private void AddReceiverInfo(XmlElement infNFe, bool isHomologacao = true)
        {

            if (_invoice == null)
            {
                return;
            }
                        
            if (_invoice.Customer != null )
            {

                var dest = _xmlDoc.CreateElement("dest");

                string cleanedDocument = Regex.Replace(_invoice.Customer.Document ?? "", "[^0-9]", "");

                if (string.IsNullOrWhiteSpace(cleanedDocument))
                {
                    return;
                }

                switch (_invoice.Customer.DocumentType)
                {
                    case 0: // CPF
                        string cpf = cleanedDocument.Length > 11 ? cleanedDocument.Substring(0, 11) : cleanedDocument.PadLeft(11, '0');
                        dest.AppendChild(CreateElement("CPF", cpf));
                        break;

                    case 1: // CNPJ
                        string cnpj = cleanedDocument.Length > 14 ? cleanedDocument.Substring(0, 14) : cleanedDocument.PadLeft(14, '0');
                        dest.AppendChild(CreateElement("CNPJ", cnpj));
                        break;
                }


                if (isHomologacao)
                {
                    // Ambiente de homologação: NÃO identificar destinatário
                    // Não escreva CPF/CNPJ aqui
                    dest.AppendChild(CreateElement("xNome", "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"));
                    dest.AppendChild(CreateElement("indIEDest", "9")); // não contribuinte
                }
                else
                {
                    // Produção: identificar normalmente
                    switch (_invoice.Customer?.DocumentType)
                    {
                        case 0: // CPF
                            if (cleanedDocument.Length == 11)
                                dest.AppendChild(CreateElement("CPF", cleanedDocument));
                            break;

                        case 1: // CNPJ
                            if (cleanedDocument.Length == 14)
                                dest.AppendChild(CreateElement("CNPJ", cleanedDocument));
                            break;
                    }

                    var nome = string.IsNullOrWhiteSpace(_invoice.Customer?.Name) ? "CONSUMIDOR" : _invoice.Customer.Name;
                    dest.AppendChild(CreateElement("xNome", nome));
                    dest.AppendChild(CreateElement("indIEDest", "9"));
                }


                //dest.AppendChild(enderDest);
                infNFe.AppendChild(dest);


            }

            

        }

        private void AddItemsInfo(XmlElement infNFe, string ambiente)
        {
            foreach (var item in _invoice.Items)
            {
                var det = _xmlDoc.CreateElement("det");
                det.SetAttribute("nItem", item.ItemNumber.ToString());

                var prod = _xmlDoc.CreateElement("prod");
                var material = item.MaterialId;

                // Adiciona cada elemento individualmente
                prod.AppendChild(CreateElement("cProd", material));
                prod.AppendChild(CreateElement("cEAN", "SEM GTIN"));

                // Ajusta o xProd conforme o ambiente
                string descricao = item.MaterialName;
                if (ambiente == "2") // 2 = homologação
                {
                    descricao = "NOTA FISCAL EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";
                }
                prod.AppendChild(CreateElement("xProd", descricao));

                prod.AppendChild(CreateElement("NCM", item.NCM.Replace(".", "") ?? string.Empty));
                prod.AppendChild(CreateElement("CFOP", item.CfopId ?? string.Empty));
                prod.AppendChild(CreateElement("uCom", item.UnitId));
                prod.AppendChild(CreateElement("qCom", item.Quantity.ToString(CultureInfo.InvariantCulture)));
                prod.AppendChild(CreateElement("vUnCom", item.UnitPrice.ToString("0.0000000000", CultureInfo.InvariantCulture)));
                prod.AppendChild(CreateElement("vProd", (item.UnitPrice * item.Quantity).ToString("0.00", CultureInfo.InvariantCulture)));
                prod.AppendChild(CreateElement("cEANTrib", "SEM GTIN"));
                prod.AppendChild(CreateElement("uTrib", item.UnitId));
                prod.AppendChild(CreateElement("qTrib", item.Quantity.ToString(CultureInfo.InvariantCulture)));
                prod.AppendChild(CreateElement("vUnTrib", item.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture)));

                if (item.DiscountAmount > 0)
                {
                    prod.AppendChild(CreateElement("vDesc", item.DiscountAmount.ToString("0.00", CultureInfo.InvariantCulture)));
                }

                prod.AppendChild(CreateElement("indTot", "1"));

                det.AppendChild(prod);

                var imposto = _xmlDoc.CreateElement("imposto");

                var icms = _xmlDoc.CreateElement("ICMS");

                var icms40 = _xmlDoc.CreateElement("ICMS" + item.CstIcmsId);

                icms40.AppendChild(CreateElement("orig", "0"));

                icms40.AppendChild(CreateElement("CST", item.CstIcmsId?.ToString() ?? string.Empty));

                icms.AppendChild(icms40);

                imposto.AppendChild(icms);

                var pis = _xmlDoc.CreateElement("PIS");

                var cstPis = item.CstPisId ?? "01";

                XmlElement pisElement;

                if (cstPis == "01" || cstPis == "02") // Alíquota normal ou diferenciada
                {
                    pisElement = _xmlDoc.CreateElement("PISAliq");
                }
                else if (cstPis == "49" || cstPis == "99") // Outras operações (ex: com redução de base)
                {
                    pisElement = _xmlDoc.CreateElement("PISOutr");
                }
                else
                {
                    // Se não souber como tratar, usa "Outras" por padrão
                    pisElement = _xmlDoc.CreateElement("PISOutr");
                }

                // Adiciona CST
                pisElement.AppendChild(CreateElement("CST", cstPis));

                // Adiciona informações de cálculo, se houver imposto
                var pisTax = item.Taxes?.FirstOrDefault(t => t.TaxTypeId == "PIS");
                if (pisTax != null)
                {
                    pisElement.AppendChild(CreateElement("vBC", pisTax.BaseValue.ToString("0.00", CultureInfo.InvariantCulture)));
                    pisElement.AppendChild(CreateElement("pPIS", pisTax.Rate.ToString("0.0000", CultureInfo.InvariantCulture))); // 4 casas para % PIS
                    pisElement.AppendChild(CreateElement("vPIS", pisTax.Value.ToString("0.00", CultureInfo.InvariantCulture)));
                }

                pis.AppendChild(pisElement);
                imposto.AppendChild(pis);

                // ------------------ COFINS ------------------
                var cofins = _xmlDoc.CreateElement("COFINS");

                var cstCofins = item.CstCofinsId ?? "01";
                XmlElement cofinsElement;

                // Seleciona o grupo correto com base no CST
                if (cstCofins == "01" || cstCofins == "02") // Alíquota normal ou diferenciada
                {
                    cofinsElement = _xmlDoc.CreateElement("COFINSAliq");
                }
                else if (cstCofins == "49" || cstCofins == "99") // Outras operações (ex: redução de base)
                {
                    cofinsElement = _xmlDoc.CreateElement("COFINSOutr");
                }
                else
                {
                    // Grupo genérico caso CST não seja mapeado diretamente
                    cofinsElement = _xmlDoc.CreateElement("COFINSOutr");
                }

                // Adiciona CST
                cofinsElement.AppendChild(CreateElement("CST", cstCofins));

                // Adiciona informações de cálculo, se houver imposto
                var cofinsTax = item.Taxes?.FirstOrDefault(t => t.TaxTypeId == "COFINS");
                if (cofinsTax != null)
                {
                    cofinsElement.AppendChild(CreateElement("vBC", cofinsTax.BaseValue.ToString("0.00", CultureInfo.InvariantCulture)));
                    cofinsElement.AppendChild(CreateElement("pCOFINS", cofinsTax.Rate.ToString("0.0000", CultureInfo.InvariantCulture))); // 4 casas para %
                    cofinsElement.AppendChild(CreateElement("vCOFINS", cofinsTax.Value.ToString("0.00", CultureInfo.InvariantCulture)));
                }

                cofins.AppendChild(cofinsElement);
                imposto.AppendChild(cofins);

                // Adiciona a tag <imposto> ao det
                det.AppendChild(imposto);

                infNFe.AppendChild(det);

            }

        }

        private void AddTotalInfo(XmlElement infNFe)
        {
            var total = _xmlDoc.CreateElement("total");
            var icmsTot = _xmlDoc.CreateElement("ICMSTot");

            // Calcula os totais
            decimal vBC = 0, vICMS = 0, vICMSDeson = 0, vFCP = 0, vBCST = 0,
                    vST = 0, vFCPST = 0, vFCPSTRet = 0, vProd = 0, vFrete = 0,
                    vSeg = 0, vDesc = 0, vII = 0, vIPI = 0, vIPIDevol = 0,
                    vPIS = 0, vCOFINS = 0, vOutro = 0, vNF = 0, vTotTrib = 0;

            foreach (var item in _invoice.Items)
            {
                vProd += item.UnitPrice * item.Quantity;
                vDesc += item.DiscountAmount;

                var taxes = item.Taxes ?? new List<Inovesys.Retail.Entities.InvoiceItemTax>();
                foreach (var tax in taxes)
                {
                    switch (tax.TaxTypeId)
                    {
                        case "STICMS":
                            vBCST += tax.BaseValue;
                            vST += tax.Value;
                            break;
                        case "PIS":
                            vPIS += tax.Value;
                            break;
                        case "COFINS":
                            vCOFINS += tax.Value;
                            break;
                            // Adicione outros tipos de impostos conforme necessário
                    }
                }
            }

            vNF = vProd - vDesc + vST + vFrete + vSeg + vOutro;

            icmsTot.AppendChild(CreateElement("vBC", vBC.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vICMS", vICMS.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vICMSDeson", vICMSDeson.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vFCP", vFCP.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vBCST", vBCST.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vST", vST.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vFCPST", vFCPST.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vFCPSTRet", vFCPSTRet.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vProd", vProd.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vFrete", vFrete.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vSeg", vSeg.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vDesc", vDesc.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vII", vII.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vIPI", vIPI.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vIPIDevol", vIPIDevol.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vPIS", vPIS.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vCOFINS", vCOFINS.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vOutro", vOutro.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vNF", vNF.ToString("0.00", CultureInfo.InvariantCulture)));
            icmsTot.AppendChild(CreateElement("vTotTrib", vTotTrib.ToString("0.00", CultureInfo.InvariantCulture)));

            total.AppendChild(icmsTot);
            infNFe.AppendChild(total);
        }

        private void AddTransportInfo(XmlElement infNFe)
        {
            var transp = _xmlDoc.CreateElement("transp");
            transp.AppendChild(CreateElement("modFrete", "9")); // 9 = Sem frete
            infNFe.AppendChild(transp);
        }

        // Updated code to handle potential null reference for _invoice.InvoicePayments
        private void AddPaymentInfo(XmlElement infNFe)
        {
            var pag = _xmlDoc.CreateElement("pag");

            // Calcula o valor total da nota
            decimal totalAmount = _invoice.Items.Sum(i =>
                (i.UnitPrice * i.Quantity) - i.DiscountAmount);

            // Verifica se existem pagamentos válidos na invoice
            bool hasValidPayments = _invoice.InvoicePayments != null &&
                                    _invoice.InvoicePayments.Any(p => p.Amount > 0);

            if (hasValidPayments)
            {
                decimal paymentsSum = 0;

                // Processa cada pagamento individualmente
                foreach (var payment in _invoice.InvoicePayments!.Where(p => p.Amount > 0)) // Added null-forgiving operator (!)
                {
                    var detPag = _xmlDoc.CreateElement("detPag");

                    // Adiciona o tipo de pagamento (código SEFAZ)
                    detPag.AppendChild(CreateElement("tPag", payment.PaymentMethodId));

                    // Adiciona o valor do pagamento formatado
                    detPag.AppendChild(CreateElement("vPag",
                        payment.Amount.ToString("0.00", CultureInfo.InvariantCulture)));

                    // Se for cartão, adiciona informações adicionais
                    if (payment.PaymentMethodId == "03" || payment.PaymentMethodId == "04")
                    {
                        var card = _xmlDoc.CreateElement("card");
                        card.AppendChild(CreateElement("tpIntegra",
                            payment.Installments > 1 ? "2" : "1")); // 1=à vista, 2=parcelado

                        if (!string.IsNullOrEmpty(payment.CardIssuer))
                            card.AppendChild(CreateElement("CNPJ", payment.CardIssuer));

                        if (!string.IsNullOrEmpty(payment.AuthorizationCode))
                        {
                            card.AppendChild(CreateElement("tBand", "99")); // Bandeira genérica
                            card.AppendChild(CreateElement("cAut", payment.AuthorizationCode));
                        }

                        detPag.AppendChild(card);
                    }

                    pag.AppendChild(detPag);
                    paymentsSum += payment.Amount;
                }

                // Validação de diferença de centavos (arredondamento)
                if (Math.Abs(paymentsSum - totalAmount) > 0.01m)
                {
                    // Ajusta a diferença no último pagamento como "outros"
                    decimal difference = totalAmount - paymentsSum;
                    var ajustePag = _xmlDoc.CreateElement("detPag");

                    ajustePag.AppendChild(CreateElement("tPag", "99")); // Outros
                    ajustePag.AppendChild(CreateElement("vPag",
                        difference.ToString("0.00", CultureInfo.InvariantCulture)));

                    pag.AppendChild(ajustePag);
                }
            }
            else
            {
                // Fallback: considera o total como pago em dinheiro
                var detPag = _xmlDoc.CreateElement("detPag");
                detPag.AppendChild(CreateElement("tPag", "01")); // Dinheiro
                detPag.AppendChild(CreateElement("vPag",
                    totalAmount.ToString("0.00", CultureInfo.InvariantCulture)));

                pag.AppendChild(detPag);
            }

            infNFe.AppendChild(pag);
        }

        public string GenerateQRCodeV2(string? chaveAcesso, string cpfCnpjConsumidor, decimal valorTotal, decimal valorTotalICMS, string dataHoraEmissao, string? csc, string? cIdToken, int tipoEmissao, bool homologacao = false, string? digestValueBase64 = null)
        {
            if (csc == null)
                throw new InvalidOperationException("Dados de CSC/Token inválidos");

            if (cIdToken == null)
                throw new InvalidOperationException("Dados de CSC/Token inválidos");

            string nVersao = "2";

            string tpAmb = homologacao ? "2" : "1";

            string parametrosBase = $"{chaveAcesso}|{nVersao}|{tpAmb}";

            // 3. Verificar se é emissão offline (contingência)
            bool isOffline = tipoEmissao != 1; // 1 = Emissão normal

            if (isOffline)
            {
                // 3.1 Para contingência OFFLINE (versão 2.00)

                // Extrair dia da data de emissão (dd)
                string diaEmissao = dataHoraEmissao.Substring(8, 2);

                // Converter digestValue para HEX (se fornecido)
                string digValHex = !string.IsNullOrEmpty(digestValueBase64)
                    ? Base64ToHex(digestValueBase64)
                    : "";

                // Montar parâmetros específicos para contingência
                string parametrosContingencia = $"{diaEmissao}|" +
                    $"{valorTotal.ToString("0.00", CultureInfo.InvariantCulture)}|" +
                    $"{digValHex}|" +
                    $"{cIdToken.TrimStart('0')}"; // Remover zeros à esquerda do cIdToken

                parametrosBase += $"|{parametrosContingencia}";
            }
            else
            {
                // 3.2 Para emissão ONLINE (versão 2.00)
                parametrosBase += $"|{cIdToken.TrimStart('0')}"; // Remover zeros à esquerda
            }

            string parametrosComCsc = parametrosBase + csc;

            string hashQRCode = CalculateSHA1(parametrosComCsc);

            string urlBase = homologacao
                ? "https://www.homologacao.nfce.fazenda.sp.gov.br/qrcode"
                : "https://www.nfce.fazenda.sp.gov.br/qrcode";

            string urlQRCode = $"{urlBase}?p={parametrosBase}|{hashQRCode}";

            return urlQRCode;
        }

        private string Base64ToHex(string base64)
        {
            byte[] bytes = Convert.FromBase64String(base64);
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        private string CalculateSHA1(string input)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = sha1.ComputeHash(inputBytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }

        private void SignXmlDocument(ref XmlDocument xmlDoc, string sefazEnviroment, string dataHoraEmissao,out string qrCodeValue)
        {
            try
            {
                var cert = SefazHelpers.LoadCertificate(_company.CertificateId, _branche.ClientId, _liteDbService);
                if (cert == null)
                    throw new Exception("Certificado não encontrado.");

                XmlElement infNFeElement = xmlDoc
                    .GetElementsByTagName("infNFe")
                    .OfType<XmlElement>()
                    .FirstOrDefault();

                if (infNFeElement == null)
                {
                    Console.WriteLine("Tag <infNFe> não encontrada.");
                    throw new Exception("Tag <infNFe> não encontrada.");
                    ;
                }

                string id = infNFeElement.GetAttribute("Id");
                if (string.IsNullOrEmpty(id))
                {
                    Console.WriteLine("A tag <infNFe> não possui o atributo 'Id'.");
                    throw new Exception("A tag <infNFe> não possui o atributo 'Id'.");
                }

                var signedXml = new SignedXml(xmlDoc)
                {
                    SigningKey = cert.GetRSAPrivateKey()
                };

                var reference = new Reference($"#{id}")
                {
                    DigestMethod = "http://www.w3.org/2000/09/xmldsig#sha1"
                };
                reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
                reference.AddTransform(new XmlDsigC14NTransform());

                signedXml.AddReference(reference);

                var keyInfo = new KeyInfo();
                keyInfo.AddClause(new KeyInfoX509Data(cert));
                signedXml.KeyInfo = keyInfo;

                if (signedXml.SignedInfo != null)
                {
                    signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
                }
                else
                {
                    throw new InvalidOperationException("SignedInfo is null. Unable to set CanonicalizationMethod.");
                }
                signedXml.SignedInfo.SignatureMethod = "http://www.w3.org/2000/09/xmldsig#rsa-sha1";

                signedXml.ComputeSignature();

                string nfeNamespace = "http://www.portalfiscal.inf.br/nfe";

                XmlElement infNFeSuplNode = xmlDoc.CreateElement("infNFeSupl", nfeNamespace);

                XmlElement qrCodeNode = xmlDoc.CreateElement("qrCode", nfeNamespace);

                if (reference.DigestValue == null)
                    throw new InvalidOperationException("reference.DigestValue = null");

                string digestValueBase64 = Convert.ToBase64String(reference.DigestValue);

                string cpfCnpjConsumidor = Regex.Replace(_invoice.Customer?.Document ?? "", "[^0-9]", "");

                string cpfCnpjEmitente = Regex.Replace(_branche.Cnpj ?? "", "[^0-9]", "");

                // -- Total da NFC-e
                decimal valorTotal = _invoice.Items.Sum(i => (i.UnitPrice * i.Quantity) - i.DiscountAmount);

                decimal valorTotalICMS = _invoice.Items
                             .Sum(item => (item.Taxes ?? Enumerable.Empty<InvoiceItemTax>())
                                 .Where(tax => tax.TaxTypeId == "ICMS")
                                 .Sum(tax => tax.Value));


                // Antes de chamar GenerateQRCodeV2:
                if (_invoice is null) throw new InvalidOperationException("_invoice não instanciado.");
                var nfKey = Require(_invoice.NfKey, nameof(_invoice.NfKey));

                if (_branche is null) throw new InvalidOperationException("_branche não instanciado.");
                var csc = Require(_branche.CscSefaz, nameof(_branche.CscSefaz));

                // Se IdTokenSefaz for obrigatório:
                var idToken = _branche.IdTokenSefaz?.ToString()
                             ?? throw new ArgumentNullException(nameof(_branche.IdTokenSefaz), "IdTokenSefaz é obrigatório.");











                string qrCodeContent = GenerateQRCodeV2(chaveAcesso: _invoice.NfKey,
                                                        cpfCnpjConsumidor: cpfCnpjConsumidor,
                                                        valorTotal: valorTotal,
                                                        valorTotalICMS: valorTotalICMS,
                                                        dataHoraEmissao: dataHoraEmissao,
                                                        csc: _branche.CscSefaz.ToString(),
                                                        cIdToken: _branche.IdTokenSefaz?.ToString(),
                                                        tipoEmissao: 1, // 1=Normal
                                                        homologacao: sefazEnviroment == "2",
                                                        digestValueBase64: digestValueBase64);

                qrCodeValue = qrCodeContent;

                XmlCDataSection qrCodeCData = xmlDoc.CreateCDataSection(
                    qrCodeContent
                );
                qrCodeNode.AppendChild(qrCodeCData);

                XmlElement urlChaveNode = xmlDoc.CreateElement("urlChave", nfeNamespace);
                urlChaveNode.InnerText = "https://www.homologacao.nfce.fazenda.sp.gov.br/consulta";

                infNFeSuplNode.AppendChild(qrCodeNode);
                infNFeSuplNode.AppendChild(urlChaveNode);

                XmlNode infNFeSupl = infNFeSuplNode;
                infNFeElement.ParentNode?.AppendChild(xmlDoc.ImportNode(infNFeSupl, true));

                XmlElement signatureElement = signedXml.GetXml();
                infNFeElement.ParentNode?.AppendChild(xmlDoc.ImportNode(signatureElement, true));

                var settings = new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(false),
                    Indent = false,
                    NewLineHandling = NewLineHandling.None
                };

                using (var stringWriter = new Utf8StringWriter())
                using (var xmlWriter = new XmlTextWriter(stringWriter))
                {
                    xmlWriter.Formatting = Formatting.None;
                    xmlDoc.WriteTo(xmlWriter);
                    xmlWriter.Flush();
                }

            }
            catch (Exception ex)
            {
                throw new Exception($"Erro ao assinar XML: {ex.Message}", ex);
            }
            return;
        }

        private XmlElement CreateElement(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("O nome do elemento XML não pode ser vazio.");

            // Nome deve começar com letra ou underline
            if (!char.IsLetter(name[0]) && name[0] != '_')
                throw new ArgumentException($"O nome '{name}' do elemento XML deve começar com uma letra ou underline.");

            // Nome não pode ter espaço
            if (name.Any(char.IsWhiteSpace))
                throw new ArgumentException($"O nome '{name}' do elemento XML não pode conter espaços.");

            var element = _xmlDoc.CreateElement(name);
            element.InnerText = value ?? string.Empty;
            return element;
        }

        public class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding => new UTF8Encoding(false);
        }

        private static string Require(string value, string name)
                => !string.IsNullOrWhiteSpace(value) ? value
                   : throw new ArgumentNullException(name, $"{name} não pode ser nulo/vazio.");

       


    }
}
