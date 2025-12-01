using Inovesys.Retail;
using Inovesys.Retail.Entities;
using Inovesys.Retail.Services;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

public class SefazService
{
    private readonly LiteDbService _db;
    private readonly Company _company;
    private readonly Branche _branche;

    public SefazService(LiteDbService db, Company company,Branche branche)
    {
        _db = db;
        _company = company;
        _branche = branche;
    }

    public async Task<(  bool Success,            // autorizado (cStat 100/150)
                         bool TransportOk,        // comunicação OK com SEFAZ
                         string StatusCode,       // cStat (ex.: "100","204","539", etc.)
                         string StatusMessage,    // xMotivo
                         string ProtocolXml,
                         string ProcXml,
                         Invoice UpdatedInvoice)>
    SendToSefazAsync(int id, string signedXml, Invoice invoice, string EnvironmentSefaz, X509Certificate2 cert )
    {
        try
        {
            
            if (cert == null)
                return (false, false, "CERT", "Certificado digital não encontrado ou inválido", null, null, null);

            if (string.IsNullOrWhiteSpace(signedXml))
                return (false, false, "XML0", "XML assinado não fornecido", null, null, null);

            var nfeXml = new XmlDocument { PreserveWhitespace = true };
            try
            {
                nfeXml.LoadXml(signedXml);
            }
            catch (XmlException ex)
            {
                return (false, false, "XML1", $"XML inválido: {ex.Message}", null, null, null);
            }

            // Carrega certificado e cria HttpClient com ele
            var client = SefazHttpPlatform.CreateClient(cert);

            // Define ambiente SEFAZ
            string sefazBaseUrl = EnvironmentSefaz == "1"
                ? "https://www.nfce.fazenda.sp.gov.br"           // Produção
                : "https://homologacao.nfce.fazenda.sp.gov.br";  // Homologação

            // Configura a URL base e timeout
            client.BaseAddress = new Uri(sefazBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(60);

            var soapEnvelope = BuildSoapEnvelope(signedXml, "1", "4.00");
            var requestContent = new StringContent(soapEnvelope, Encoding.UTF8, "application/soap+xml");
            requestContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
                "application/soap+xml; charset=utf-8; action=\"http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4/nfeAutorizacaoLote\"");

            var response = await client.PostAsync("/ws/NFeAutorizacao4.asmx", requestContent);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return (false, false, ((int)response.StatusCode).ToString(), $"Erro HTTP ao enviar para SEFAZ: {responseContent}", null, null, null);

            // A partir daqui: transporte OK (200 + corpo)
            var retorno = new XmlDocument { PreserveWhitespace = true };
            retorno.LoadXml(responseContent);

            var ns = new XmlNamespaceManager(retorno.NameTable);
            ns.AddNamespace("soap", "http://www.w3.org/2003/05/soap-envelope");
            ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");
            ns.AddNamespace("nfe4", "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4");

            var retEnviNFe = retorno.SelectSingleNode("//soap:Body/nfe4:nfeResultMsg/nfe:retEnviNFe", ns)
                              ?? retorno.SelectSingleNode("//retEnviNFe");
            if (retEnviNFe == null)
                return (false, true, "RET0", "Resposta da SEFAZ não contém retEnviNFe.", null, null, null);

            var cStatLote = retEnviNFe.SelectSingleNode("nfe:cStat", ns)?.InnerText
                          ?? retEnviNFe.SelectSingleNode("cStat")?.InnerText
                          ?? "RET1";

            var xMotivoLote = retEnviNFe.SelectSingleNode("nfe:xMotivo", ns)?.InnerText
                            ?? retEnviNFe.SelectSingleNode("xMotivo")?.InnerText
                            ?? "Sem xMotivo";

            if (cStatLote != "104") // 104 = Lote processado
                return (false, true, cStatLote, $"Lote não processado: {xMotivoLote}", null, null, null);

            var protNFeNode = retEnviNFe.SelectSingleNode("nfe:protNFe", ns)
                           ?? retEnviNFe.SelectSingleNode("protNFe");
            if (protNFeNode == null)
                return (false, true, "RET2", "Lote processado, mas sem protNFe.", null, null, null);

            var infProtNode = protNFeNode.SelectSingleNode("nfe:infProt", ns)
                             ?? protNFeNode.SelectSingleNode("infProt");
            if (infProtNode == null)
                return (false, true, "RET3", "protNFe encontrado, mas sem infProt.", null, null, null);

            var cStatNFe = infProtNode.SelectSingleNode("nfe:cStat", ns)?.InnerText
                         ?? infProtNode.SelectSingleNode("cStat")?.InnerText
                         ?? "RET4";

            var xMotivoNFe = infProtNode.SelectSingleNode("nfe:xMotivo", ns)?.InnerText
                           ?? infProtNode.SelectSingleNode("xMotivo")?.InnerText
                           ?? "Sem xMotivo";

            // Sucesso de negócio (autorizada)
            var autorizado = (cStatNFe == "100" || cStatNFe == "150");

            // Duplicidade (comunicação OK, erro de negócio)
            if (!autorizado && cStatNFe == "204")
                return (false, true, cStatNFe, "Duplicidade de NF-e (204). Consulte protocolo para obter nProt.", null, null, null);

            if (!autorizado)
                return (false, true, cStatNFe, xMotivoNFe, null, null, null);

            // Autorizada: montar nfeProc
            var protXml = protNFeNode.OuterXml;
            var procXml = BuildProcNFe(nfeXml, protNFeNode);

            invoice.AuthorizedXml = Convert.ToBase64String(Encoding.UTF8.GetBytes(procXml));
            invoice.Protocol = protXml;
            invoice.NfeStatus = "AUTORIZADA";
            invoice.LastUpdate = DateTime.UtcNow;

            return (true, true, cStatNFe, xMotivoNFe, protXml, procXml, invoice);
        }
        catch (Exception ex)
        {
            return (false, false, "EXC", $"Erro ao enviar nota para SEFAZ: {ex.Message}", null, null, null);
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

    public (XmlDocument Xml, string IdEvento, Exception Error) BuildCancelEventXml(
    string chaveNFe,
    string protocoloAutorizacao,
    string justificativa,
    string ambiente, // "1" Prod / "2" Homolog
    X509Certificate2 cert)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(chaveNFe) || chaveNFe.Length != 44)
                throw new Exception("Chave NFe inválida.");

            if (string.IsNullOrWhiteSpace(protocoloAutorizacao))
                throw new Exception("Protocolo de autorização (nProt) não informado.");

            if (string.IsNullOrWhiteSpace(justificativa) || justificativa.Length < 15)
                throw new Exception("Justificativa inválida (mínimo 15 caracteres).");

            var xml = new XmlDocument { PreserveWhitespace = true };
            const string ns = "http://www.portalfiscal.inf.br/nfe";

            string tpEvento = "110111"; // CANCELAMENTO
            string idEvento = "ID" + tpEvento + chaveNFe + "01"; // nSeqEvento = 1

            // =========================
            // <envEvento xmlns="..." versao="1.00">
            // =========================
            var envEvento = xml.CreateElement("envEvento");

            var attrXmlnsEnv = xml.CreateAttribute("xmlns");
            attrXmlnsEnv.Value = ns;
            envEvento.Attributes.Append(attrXmlnsEnv);      // primeiro xmlns

            var attrVersaoEnv = xml.CreateAttribute("versao");
            attrVersaoEnv.Value = "1.00";
            envEvento.Attributes.Append(attrVersaoEnv);     // depois versao

            // <idLote>1</idLote>
            var idLoteElem = xml.CreateElement("idLote");
            idLoteElem.InnerText = "1";
            envEvento.AppendChild(idLoteElem);

            // =========================
            // <evento xmlns="..." versao="1.00">
            // =========================
            var evento = xml.CreateElement("evento");

            var attrXmlnsEvento = xml.CreateAttribute("xmlns");
            attrXmlnsEvento.Value = ns;
            evento.Attributes.Append(attrXmlnsEvento);

            var attrVersaoEvento = xml.CreateAttribute("versao");
            attrVersaoEvento.Value = "1.00";
            evento.Attributes.Append(attrVersaoEvento);

            envEvento.AppendChild(evento);

            // =========================
            // <infEvento Id="...">
            // =========================
            var infEvento = xml.CreateElement("infEvento");
            var attrId = xml.CreateAttribute("Id");
            attrId.Value = idEvento;
            infEvento.Attributes.Append(attrId);

            evento.AppendChild(infEvento);

            // Helpers locais pra evitar usar o CreateElem antigo
            XmlElement Elem(string name, string value)
            {
                var e = xml.CreateElement(name);
                e.InnerText = value;
                return e;
            }

            infEvento.AppendChild(Elem("cOrgao", chaveNFe.Substring(0, 2)));
            infEvento.AppendChild(Elem("tpAmb", ambiente));
            infEvento.AppendChild(Elem("CNPJ", _branche.Cnpj.Replace(".", "").Replace("/", "").Replace("-", "")));
            infEvento.AppendChild(Elem("chNFe", chaveNFe));
            infEvento.AppendChild(Elem("dhEvento", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz")));
            infEvento.AppendChild(Elem("tpEvento", tpEvento));
            infEvento.AppendChild(Elem("nSeqEvento", "1"));
            infEvento.AppendChild(Elem("verEvento", "1.00"));

            // =========================
            // <detEvento versao="1.00">
            // =========================
            var det = xml.CreateElement("detEvento");
            var attrVersaoDet = xml.CreateAttribute("versao");
            attrVersaoDet.Value = "1.00";
            det.Attributes.Append(attrVersaoDet);

            det.AppendChild(Elem("descEvento", "Cancelamento"));
            det.AppendChild(Elem("nProt", protocoloAutorizacao));
            det.AppendChild(Elem("xJust", justificativa));

            infEvento.AppendChild(det);

            // raiz
            xml.AppendChild(envEvento);

            // carregar em novo XmlDocument para assinar (mantendo preserve whitespace)
            var xmlSemIdentacao = new XmlDocument { PreserveWhitespace = true };
            xmlSemIdentacao.LoadXml(xml.InnerXml);

            // ASSINAR (sua implementação atual de assinatura)
            SignXml(xmlSemIdentacao, idEvento, cert);

            // --- Mover <Signature> para logo após </infEvento> ---
            var dsNs = "http://www.w3.org/2000/09/xmldsig#";
            var signatures = xmlSemIdentacao.GetElementsByTagName("Signature", dsNs);
            if (signatures != null && signatures.Count > 0)
            {
                var signatureNode = signatures[0];

                // agora buscamos por nome, sem namespace, já que criamos sem ns no DOM
                var eventoNodes = xmlSemIdentacao.GetElementsByTagName("evento");
                var infEventoNodes = xmlSemIdentacao.GetElementsByTagName("infEvento");

                if (eventoNodes.Count > 0 && infEventoNodes.Count > 0)
                {
                    var eventoNode = eventoNodes[0];
                    var infEventoNode = infEventoNodes[0];

                    if (signatureNode.ParentNode != null)
                        signatureNode.ParentNode.RemoveChild(signatureNode);

                    if (infEventoNode.NextSibling != null)
                        eventoNode.InsertAfter(signatureNode, infEventoNode);
                    else
                        eventoNode.AppendChild(signatureNode);
                }
            }

            return (xmlSemIdentacao, idEvento, null);
        }
        catch (Exception ex)
        {
            return (null, null, ex);
        }
    }


    private void SignXml(XmlDocument xml, string referenceId, X509Certificate2 cert)
    {
        if (xml == null)
            throw new ArgumentNullException(nameof(xml));
        if (cert == null)
            throw new ArgumentNullException(nameof(cert));

        var rsa = cert.GetRSAPrivateKey();
        if (rsa == null)
            throw new Exception("Certificado não possui chave privada.");

        var signed = new SignedXml(xml)
        {
            SigningKey = rsa
        };

        // <CanonicalizationMethod Algorithm="http://www.w3.org/TR/2001/REC-xml-c14n-20010315">
        signed.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;

        // <SignatureMethod Algorithm="http://www.w3.org/2000/09/xmldsig#rsa-sha1">
        signed.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;

        // Referência ao Id do infEvento
        var reference = new Reference("#" + referenceId)
        {
            // <DigestMethod Algorithm="http://www.w3.org/2000/09/xmldsig#sha1">
            DigestMethod = SignedXml.XmlDsigSHA1Url
        };

        // <Transform Algorithm="http://www.w3.org/2000/09/xmldsig#enveloped-signature" />
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());

        // <Transform Algorithm="http://www.w3.org/TR/2001/REC-xml-c14n-20010315" />
        reference.AddTransform(new XmlDsigC14NTransform());

        signed.AddReference(reference);

        // <KeyInfo><X509Data><X509Certificate>...</X509Certificate></X509Data></KeyInfo>
        var ki = new KeyInfo();
        ki.AddClause(new KeyInfoX509Data(cert));
        signed.KeyInfo = ki;

        // calcula assinatura
        signed.ComputeSignature();

        var signature = signed.GetXml();

        // Anexa dentro de <infEvento> (depois você já tem a rotina que move para depois)
        var infEvento = xml.GetElementsByTagName("infEvento")[0] as XmlElement;
        if (infEvento == null)
            throw new InvalidOperationException("Elemento <infEvento> não encontrado no XML.");

        var importedSignature = xml.ImportNode(signature, true);
        infEvento.AppendChild(importedSignature);
    }

    public async Task<(bool Success, bool TransportOk, string StatusCode, string StatusMessage, string ProtocolXml, string ProcXml)>
        SendCancelEventAsync(XmlDocument envEventoXml, string ambiente, X509Certificate2 cert)
    {
        try
        {
            var client = SefazHttpPlatform.CreateClient(cert);

            string baseUrl = ambiente == "1"
                ? "https://www.nfce.fazenda.sp.gov.br"
                : "https://homologacao.nfce.fazenda.sp.gov.br";

            client.BaseAddress = new Uri(baseUrl);

            // SOAP
            string soap = BuildEventSoapEnvelope(envEventoXml.OuterXml);

            var content = new StringContent(soap, Encoding.UTF8, "application/soap+xml");
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(
                "application/soap+xml; charset=utf-8; action=\"http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4/RecepcaoEvento\"");

            // Envio
            var resp = await client.PostAsync("/ws/NFeRecepcaoEvento4.asmx", content);
            string respXml = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return (false, false, ((int)resp.StatusCode).ToString(), "Erro HTTP", null, null);

            // Parse XML
            var xml = new XmlDocument { PreserveWhitespace = true };
            xml.LoadXml(respXml);

            var ns = new XmlNamespaceManager(xml.NameTable);
            ns.AddNamespace("soap", "http://www.w3.org/2003/05/soap-envelope");
            ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");

            var ret = xml.SelectSingleNode("//retEvento")
                   ?? xml.SelectSingleNode("//nfe:retEvento", ns);

            if (ret == null)
                return (false, true, "SEM_RET", "retEvento não encontrado", null, null);

            string cStat = ret.SelectSingleNode("infEvento/cStat")?.InnerText
                        ?? ret.SelectSingleNode("nfe:infEvento/nfe:cStat", ns)?.InnerText;

            string xMotivo = ret.SelectSingleNode("infEvento/xMotivo")?.InnerText
                          ?? ret.SelectSingleNode("nfe:infEvento/nfe:xMotivo", ns)?.InnerText;

            bool sucesso = cStat is "135" or "155"; // cancelamento homologado

            return (sucesso, true, cStat, xMotivo, ret.OuterXml, null);
        }
        catch (Exception ex)
        {
            return (false, false, "EXC", ex.Message, null, null);
        }
    }

    private string BuildEventSoapEnvelope(string innerXml)
    {
        return $@"<soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope""><soap:Body><nfeDadosMsg xmlns=""http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4"">{innerXml}</nfeDadosMsg></soap:Body></soap:Envelope>";
    }


}


