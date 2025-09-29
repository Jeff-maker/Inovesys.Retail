using Inovesys.Retail.Entities;
using Inovesys.Retail;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using Inovesys.Retail.Services;

public class SefazService
{
    private readonly LiteDbService _db;
    private readonly Company _company;

    public SefazService(LiteDbService db, Company company)
    {
        _db = db;
        _company = company;
    }

    public async Task<(bool Success, string Message, string ProtocolXml, Invoice UpdatedInvoice)> SendToSefazAsync(int id, string signedXml, Invoice invoice)
    {
        try
        {
            var cert = SefazHelpers.LoadCertificate(_company.CertificateId, _company.ClientId, _db);
            if (cert == null)
                return (false, "Certificado digital não encontrado ou inválido", null, null);

            if (string.IsNullOrWhiteSpace(signedXml))
                return (false, "XML assinado não fornecido", null, null);

            var xmlDoc = new XmlDocument { PreserveWhitespace = true };
            try
            {
                xmlDoc.LoadXml(signedXml);
            }
            catch (XmlException ex)
            {
                return (false, $"XML inválido: {ex.Message}", null, null);
            }

            var handler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12
            };
            handler.ClientCertificates.Add(cert);

            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://homologacao.nfce.fazenda.sp.gov.br"),
                Timeout = TimeSpan.FromSeconds(60)
            };

            var soapEnvelope = BuildSoapEnvelope(signedXml, "1", "4.00");
            var requestContent = new StringContent(soapEnvelope, Encoding.UTF8, "application/soap+xml");
            requestContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
                "application/soap+xml; charset=utf-8; action=\"http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4/nfeAutorizacaoLote\"");

            var response = await client.PostAsync("/ws/NFeAutorizacao4.asmx", requestContent);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return (false, $"Erro ao enviar para SEFAZ: {responseContent}", null, null);

            var retorno = new XmlDocument();
            retorno.LoadXml(responseContent);

            var nsManager = new XmlNamespaceManager(retorno.NameTable);
            nsManager.AddNamespace("soap", "http://www.w3.org/2003/05/soap-envelope");
            nsManager.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");
            nsManager.AddNamespace("nfe4", "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4");

            var retEnviNFe = retorno.SelectSingleNode("//soap:Body/nfe4:nfeResultMsg/nfe:retEnviNFe", nsManager);
            if (retEnviNFe == null)
                return (false, "Resposta da SEFAZ não contém dados de protocolo válidos", null, null);

            var cStatLote = retEnviNFe.SelectSingleNode("nfe:cStat", nsManager)?.InnerText;
            var xMotivoLote = retEnviNFe.SelectSingleNode("nfe:xMotivo", nsManager)?.InnerText;

            if (cStatLote != "104")
                return (false, $"Erro no processamento do lote: {xMotivoLote}", null, null);

            var protNFe = retEnviNFe.SelectSingleNode("nfe:protNFe", nsManager);
            var infProt = protNFe?.SelectSingleNode("nfe:infProt", nsManager);

            if (infProt != null)
            {
                var cStatNFe = infProt.SelectSingleNode("nfe:cStat", nsManager)?.InnerText;
                var xMotivoNFe = infProt.SelectSingleNode("nfe:xMotivo", nsManager)?.InnerText;

                if (cStatNFe == "100" || cStatNFe == "204")
                {
                    invoice.AuthorizedXml = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXml));
                    invoice.Protocol = protNFe.OuterXml;
                    invoice.NfeStatus = "AUTORIZADA";
                    invoice.LastUpdate = DateTime.UtcNow;

                    return (true, $"Nota autorizada: {xMotivoNFe}", protNFe.OuterXml, invoice);
                }
                else
                {
                    return (false, $"SEFAZ retornou erro na autorização: {xMotivoNFe}", null, null);
                }
            }

            return (false, "Resposta da SEFAZ não contém protocolo de autorização", null, null);
        }
        catch (Exception ex)
        {
            return (false, $"Erro ao enviar nota para SEFAZ: {ex.Message}", null, null);
        }
    }

    public async Task<(bool Success, string Message)> ConsultarStatusServicoAsync()
    {
        try
        {
            var cert = SefazHelpers.LoadCertificate(_company.CertificateId, _company.ClientId, _db);
            if (cert == null)
                return (false, "Certificado digital não encontrado ou inválido");

            var handler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12
            };
            handler.ClientCertificates.Add(cert);

            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://homologacao.nfce.fazenda.sp.gov.br"),
                Timeout = TimeSpan.FromSeconds(5)
            };

            var soapEnvelope = BuildStatusServicoEnvelope("4.00", "35"); // versão e UF (35 = SP)
            var content = new StringContent(soapEnvelope, Encoding.UTF8, "application/soap+xml");
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(
                "application/soap+xml; charset=utf-8; action=\"http://www.portalfiscal.inf.br/nfe/wsdl/NFeStatusServico4/nfeStatusServicoNF\"");

            var response = await client.PostAsync("/ws/NFeStatusServico4.asmx", content);
            var xmlResponse = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return (false, $"Erro na requisição: {response.StatusCode}");

            var xml = new XmlDocument();
            xml.LoadXml(xmlResponse);

            var ns = new XmlNamespaceManager(xml.NameTable);
            ns.AddNamespace("soap", "http://www.w3.org/2003/05/soap-envelope");
            ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");
            ns.AddNamespace("nfe4", "http://www.portalfiscal.inf.br/nfe/wsdl/NFeStatusServico4");

            var statusNode = xml.SelectSingleNode("//soap:Body/nfe4:nfeResultMsg/nfe:retConsStatServ/nfe:cStat", ns);
            var motivoNode = xml.SelectSingleNode("//soap:Body/nfe4:nfeResultMsg/nfe:retConsStatServ/nfe:xMotivo", ns);

            if (statusNode?.InnerText == "107") // 107 = Serviço em operação
            {
                return (true, $"SEFAZ Online: {motivoNode?.InnerText}");
            }

            return (false, $"SEFAZ Offline ou instável: {motivoNode?.InnerText}");
        }
        catch (Exception ex)
        {
            return (false, $"Erro ao consultar status do serviço da SEFAZ: {ex.Message}");
        }
    }

    private string BuildStatusServicoEnvelope(string versao, string ufCodigo)
    {
        return $@"
            <soap12:Envelope xmlns:soap12='http://www.w3.org/2003/05/soap-envelope'>
              <soap12:Body>
                <nfeDadosMsg xmlns='http://www.portalfiscal.inf.br/nfe/wsdl/NFeStatusServico4'>
                  <consStatServ xmlns='http://www.portalfiscal.inf.br/nfe' versao='{versao}'>
                    <tpAmb>2</tpAmb> <!-- 1 = Produção / 2 = Homologação -->
                    <cUF>{ufCodigo}</cUF> <!-- Código da UF, ex: 35 = SP -->
                    <xServ>STATUS</xServ>
                  </consStatServ>
                </nfeDadosMsg>
              </soap12:Body>
            </soap12:Envelope>";
    }

    public string BuildSoapEnvelope(string xmlNFe, string idLote, string versaoDados = "4.00")
    {
        var sb = new StringBuilder();
        using (var stringWriter = new StringWriter(sb))
        using (var writer = XmlWriter.Create(stringWriter, new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true
        }))
        {
            // <soap:Envelope>
            writer.WriteStartElement("soap", "Envelope", "http://www.w3.org/2003/05/soap-envelope");
            writer.WriteAttributeString("xmlns", "nfe", null, "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4");

            // <soap:Body>
            writer.WriteStartElement("soap", "Body", null);

            // <nfe:nfeDadosMsg>
            writer.WriteStartElement("nfe", "nfeDadosMsg", "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4");

            // Corpo do enviNFe
            writer.WriteRaw($@"<enviNFe xmlns=""http://www.portalfiscal.inf.br/nfe"" versao=""{versaoDados}""><idLote>{idLote.PadLeft(15, '0')}</idLote><indSinc>1</indSinc>{xmlNFe}</enviNFe>");

            // Fecha nfeDadosMsg, Body, Envelope
            writer.WriteEndElement(); // nfeDadosMsg
            writer.WriteEndElement(); // Body
            writer.WriteEndElement(); // Envelope

            writer.Flush();
        }

        return sb.ToString();
    }
}
        

