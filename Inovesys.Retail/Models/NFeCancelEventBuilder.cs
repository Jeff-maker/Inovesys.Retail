
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text.RegularExpressions;
using System.Xml;

namespace Inovesys.Retail.Models
{
    /// <summary>
    /// Monta o XML de ENVIO DE EVENTO de CANCELAMENTO (tpEvento = 110111).
    /// Não faz o envio ao webservice — apenas monta e assina o XML pronto.
    /// </summary>
    public class NFeCancelEventBuilder
    {
        private readonly string _chave;             // chave da NFe
        private readonly string _nProt;             // número do protocolo de autorização (do envio da NF-e)
        private readonly string _justificativa;     // justificativa do cancelamento (até 255 chars, sem acentuação proibida)
        private readonly int _nSeqEvento;           // geralmente 1 para o 1º evento
        private readonly string _tpAmb;             // 1=Prod, 2=Homolog
        private readonly X509Certificate2 _cert;
        private readonly XmlDocument _xmlDoc;

        public NFeCancelEventBuilder(string chave, string nProt, string justificativa, int nSeqEvento, string tpAmb, X509Certificate2 cert)
        {
            _chave = chave ?? throw new ArgumentNullException(nameof(chave));
            _nProt = nProt ?? throw new ArgumentNullException(nameof(nProt));
            _justificativa = justificativa ?? throw new ArgumentNullException(nameof(justificativa));
            _nSeqEvento = nSeqEvento <= 0 ? 1 : nSeqEvento;
            _tpAmb = tpAmb ?? "1";
            _cert = cert ?? throw new ArgumentNullException(nameof(cert));
            _xmlDoc = new XmlDocument { PreserveWhitespace = true };
        }
                
        public (XmlDocument Xml, string IdEvento, Exception Error) Build()
        {
            try
            {
                Validate();

                // root envEvento versao="1.00"
                var ns = "http://www.portalfiscal.inf.br/nfe";
                var envEvento = _xmlDoc.CreateElement("envEvento", ns);
                envEvento.SetAttribute("versao", "1.00");

                // idLote (pode ser gerado dinamicamente)
                envEvento.AppendChild(CreateElement("idLote", GenerateLoteId()));

                // evento
                var evento = _xmlDoc.CreateElement("evento", ns);
                evento.SetAttribute("versao", "1.00");

                // infEvento
                // Id = "ID" + tpEvento + chave + nSeqEvento (ex: ID110111<chave>01)
                string tpEvento = "110111";
                string seqStr = _nSeqEvento.ToString("00");
                string idEvento = $"ID{tpEvento}{_chave}{seqStr}";

                var infEvento = _xmlDoc.CreateElement("infEvento", ns);
                infEvento.SetAttribute("Id", idEvento);

                // cOrgao -> primeiro 2 dígitos da chave (UF code)
                string cUF = _chave?.Length >= 2 ? _chave.Substring(0, 2) : throw new InvalidOperationException("Chave inválida");
                infEvento.AppendChild(CreateElement("cOrgao", cUF));
                infEvento.AppendChild(CreateElement("tpAmb", _tpAmb));

                // Se o certificado for de pessoa jurídica: CNPJ; caso contrário CPF
                var subject = _cert.Subject ?? string.Empty;
                var cnpj = ExtractCnpjFromCertificate(_cert);
                if (!string.IsNullOrEmpty(cnpj))
                    infEvento.AppendChild(CreateElement("CNPJ", Regex.Replace(cnpj, @"\D", "")));
                else
                    infEvento.AppendChild(CreateElement("CPF", "")); // ajustar se for CPF

                infEvento.AppendChild(CreateElement("chNFe", _chave));
                infEvento.AppendChild(CreateElement("dhEvento", DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz")));
                infEvento.AppendChild(CreateElement("tpEvento", tpEvento));
                infEvento.AppendChild(CreateElement("nSeqEvento", _nSeqEvento.ToString()));
                infEvento.AppendChild(CreateElement("verEvento", "1.00"));

                // detEvento (versao="1.00")
                var detEvento = _xmlDoc.CreateElement("detEvento", ns);
                detEvento.SetAttribute("versao", "1.00");
                detEvento.AppendChild(CreateElement("descEvento", "Cancelamento"));
                detEvento.AppendChild(CreateElement("nProt", _nProt));
                detEvento.AppendChild(CreateElement("xJust", _justificativa));

                infEvento.AppendChild(detEvento);
                evento.AppendChild(infEvento);
                envEvento.AppendChild(evento);

                _xmlDoc.AppendChild(envEvento);

                // *** Assinar o infEvento ***
                SignInfEvento(idEvento);

                return (_xmlDoc, idEvento, null);
            }
            catch (Exception ex)
            {
                return (null, null, ex);
            }
        }

        private void Validate()
        {
            if (string.IsNullOrWhiteSpace(_chave) || _chave.Length != 44)
                throw new ArgumentException("ChaveNFe deve ter 44 caracteres.");

            if (string.IsNullOrWhiteSpace(_nProt))
                throw new ArgumentException("Número do protocolo (nProt) é obrigatório para cancelamento.");

            if (string.IsNullOrWhiteSpace(_justificativa) || _justificativa.Length < 15)
                throw new ArgumentException("Justificativa é obrigatória e deve ter pelo menos 15 caracteres (conforme regras do SEFAZ).");

            if (_justificativa.Length > 255)
                throw new ArgumentException("Justificativa não pode exceder 255 caracteres.");
        }

        private void SignInfEvento(string idEvento)
        {
            var rsa = _cert.GetRSAPrivateKey() ?? throw new InvalidOperationException("Certificado não possui chave privada acessível.");

            // localizar o elemento infEvento pelo atributo Id
            var infEventos = _xmlDoc.GetElementsByTagName("infEvento");
            XmlElement infEventoElem = null;
            foreach (XmlElement el in infEventos)
            {
                if (el.GetAttribute("Id") == idEvento) { infEventoElem = el; break; }
            }
            if (infEventoElem == null) throw new InvalidOperationException("infEvento não encontrado para assinatura.");

            var signedXml = new SignedXml(_xmlDoc)
            {
                SigningKey = rsa
            };

            // Referência ao Id do infEvento
            var reference = new Reference($"#{idEvento}")
            {
                DigestMethod = SignedXml.XmlDsigSHA1Url // ajuste para SHA256 se o SEFAZ exigir
            };
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.AddTransform(new XmlDsigC14NTransform());
            signedXml.AddReference(reference);

            var keyInfo = new KeyInfo();
            keyInfo.AddClause(new KeyInfoX509Data(_cert));
            signedXml.KeyInfo = keyInfo;

            signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
            signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url; // ajustar se necessário

            signedXml.ComputeSignature();

            var signature = signedXml.GetXml();

            // Assinatura deve ficar como irmão do infEvento (ou dentro do parent evento) — aqui adicionamos após infEvento
            infEventoElem.ParentNode?.AppendChild(_xmlDoc.ImportNode(signature, true));
        }

        private string GenerateLoteId()
        {
            // idLote pode ser YYYYMMDDHHmmss + random, por exemplo
            return DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        }

        private XmlElement CreateElement(string name, string value)
        {
            var el = _xmlDoc.CreateElement(name, "http://www.portalfiscal.inf.br/nfe");
            el.InnerText = value ?? string.Empty;
            return el;
        }

        private string ExtractCnpjFromCertificate(X509Certificate2 cert)
        {
            // tenta extrair CNPJ do Subject (ex: "CN=..., O=Empresa, CNPJ=12345678000199, ...")
            var subj = cert.Subject ?? string.Empty;
            var m = Regex.Match(subj, @"CNPJ\s*=\s*(\d+)");
            if (m.Success) return m.Groups[1].Value;
            // alternativa: buscar no SubjectName ou extensões, conforme seu provedor
            return null;
        }
    }
}
