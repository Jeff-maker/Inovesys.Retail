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

    public async Task<(bool Success, string Message, string ProtocolXml, string ProcXml, Invoice UpdatedInvoice)>
    SendToSefazAsync(int id, string signedXml, Invoice invoice)
    {
        try
        {
            var cert = SefazHelpers.LoadCertificate(_company.CertificateId, _company.ClientId, _db);
            if (cert == null)
                return (false, "Certificado digital não encontrado ou inválido", null, null, null);

            if (string.IsNullOrWhiteSpace(signedXml))
                return (false, "XML assinado não fornecido", null, null, null);

            var nfeXml = new XmlDocument { PreserveWhitespace = true };
            try
            {
                nfeXml.LoadXml(signedXml);
            }
            catch (XmlException ex)
            {
                return (false, $"XML inválido: {ex.Message}", null, null, null);
            }

            // HTTP + certificado
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
                return (false, $"Erro ao enviar para SEFAZ: {responseContent}", null, null, null);

            // Carrega resposta
            var retorno = new XmlDocument { PreserveWhitespace = true };
            retorno.LoadXml(responseContent);

            var ns = new XmlNamespaceManager(retorno.NameTable);
            ns.AddNamespace("soap", "http://www.w3.org/2003/05/soap-envelope");
            ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");
            ns.AddNamespace("nfe4", "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4");

            // retEnviNFe (síncrono p/ NFC-e costuma trazer protNFe quando cStat = 104)
            var retEnviNFe = retorno.SelectSingleNode("//soap:Body/nfe4:nfeResultMsg/nfe:retEnviNFe", ns)
                             ?? retorno.SelectSingleNode("//retEnviNFe"); // fallback sem namespace

            if (retEnviNFe == null)
                return (false, "Resposta da SEFAZ não contém retEnviNFe.", null, null, null);

            var cStatLote = retEnviNFe.SelectSingleNode("nfe:cStat", ns)?.InnerText
                         ?? retEnviNFe.SelectSingleNode("cStat")?.InnerText;

            var xMotivoLote = retEnviNFe.SelectSingleNode("nfe:xMotivo", ns)?.InnerText
                           ?? retEnviNFe.SelectSingleNode("xMotivo")?.InnerText;

            if (cStatLote != "104") // 104 = Lote processado
                return (false, $"Lote não processado (cStat={cStatLote}): {xMotivoLote}", null, null, null);

            // pega o protNFe
            var protNFeNode = retEnviNFe.SelectSingleNode("nfe:protNFe", ns)
                          ?? retEnviNFe.SelectSingleNode("protNFe");

            if (protNFeNode == null)
                return (false, "Lote processado, mas sem protNFe na resposta.", null, null, null);

            var infProtNode = protNFeNode.SelectSingleNode("nfe:infProt", ns)
                            ?? protNFeNode.SelectSingleNode("infProt");
            if (infProtNode == null)
                return (false, "protNFe encontrado, mas sem infProt.", null, null, null);

            var cStatNFe = infProtNode.SelectSingleNode("nfe:cStat", ns)?.InnerText
                        ?? infProtNode.SelectSingleNode("cStat")?.InnerText;

            var xMotivoNFe = infProtNode.SelectSingleNode("nfe:xMotivo", ns)?.InnerText
                           ?? infProtNode.SelectSingleNode("xMotivo")?.InnerText;

            // Sucessos típicos
            var autorizado = (cStatNFe == "100" || cStatNFe == "150");

            // Observação: 204 (Duplicidade) normalmente requer CONSULTAR protocolo para obter o nProt
            if (!autorizado && cStatNFe == "204")
            {
                // Aqui você pode opcionalmente disparar uma consulta protocolo (não implementada nesta amostra).
                // Por ora, retorna como erro orientando a consulta.
                return (false, "Duplicidade de NF-e (204). Consulte protocolo para obter nProt.", null, null, null);
            }

            if (!autorizado)
                return (false, $"SEFAZ retornou cStat={cStatNFe}: {xMotivoNFe}", null, null, null);

            // Até aqui: autorizado (100/150). Vamos montar o nfeProc.
            // 1) protNFe completo:
            var protXml = protNFeNode.OuterXml;

            // 2) monta nfeProc (NFe + protNFe)
            var procXml = BuildProcNFe(nfeXml, protNFeNode);

            // Atualiza sua entidade
            invoice.AuthorizedXml = Convert.ToBase64String(Encoding.UTF8.GetBytes(procXml));
            invoice.Protocol = protXml; // só o protNFe
            invoice.NfeStatus = "AUTORIZADA";
            invoice.LastUpdate = DateTime.UtcNow;

            return (true, $"Nota autorizada: {xMotivoNFe}", protXml, procXml, invoice);
        }
        catch (Exception ex)
        {
            return (false, $"Erro ao enviar nota para SEFAZ: {ex.Message}", null, null, null);
        }
    }

    /// <summary>
    /// Monta o nfeProc (versão 4.00) com a NFe já assinada + protNFe retornado.
    /// </summary>
    private static string BuildProcNFe(XmlDocument nfeXml, XmlNode protNFeNode)
    {
        // Garante namespace da NFe
        var nfeNs = "http://www.portalfiscal.inf.br/nfe";

        // Cria nfeProc
        var proc = new XmlDocument { PreserveWhitespace = true };

        var nfeProc = proc.CreateElement("nfeProc", nfeNs);
        var versaoAttr = proc.CreateAttribute("versao");
        versaoAttr.Value = "4.00";
        nfeProc.Attributes.Append(versaoAttr);

        // IMPORTANTE: incluir o xmlns no elemento raiz
        var xmlnsAttr = proc.CreateAttribute("xmlns");
        xmlnsAttr.Value = nfeNs;
        nfeProc.Attributes.Append(xmlnsAttr);

        proc.AppendChild(nfeProc);

        // Importa <NFe> (do XML assinado original)
        XmlNode nfeNode = nfeXml.SelectSingleNode("/*[local-name()='NFe']") ?? nfeXml.DocumentElement;
        if (nfeNode == null || nfeNode.LocalName != "NFe")
            throw new InvalidOperationException("XML assinado não contém o elemento <NFe> válido.");

        var importedNFe = proc.ImportNode(nfeNode, true);
        nfeProc.AppendChild(importedNFe);

        // Importa <protNFe> da resposta
        var importedProt = proc.ImportNode(protNFeNode, true);
        nfeProc.AppendChild(importedProt);

        return proc.OuterXml;
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
        

