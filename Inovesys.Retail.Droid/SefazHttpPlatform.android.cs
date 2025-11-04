using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Inovesys.Retail.Shared; // MESMO namespace

public static partial class SefazHttpPlatform
{
    public static HttpClient CreateClient(X509Certificate2 cert)
    {
        if (cert is null || !cert.HasPrivateKey)
            throw new CryptographicException("Certificado inválido: sem chave privada.");

        var h = new Xamarin.Android.Net.AndroidMessageHandler
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        };

        h.ClientCertificates ??= new X509Certificate2Collection();
        h.ClientCertificates.Add(cert);

        // aceita só o host da SEFAZ homolog; demais exigem cadeia OK
        h.ServerCertificateCustomValidationCallback = (req, srvCert, chain, errors) =>
        {
            var host = req?.RequestUri?.Host ?? "";
            if (host.Equals("homologacao.nfce.fazenda.sp.gov.br", StringComparison.OrdinalIgnoreCase))
                return true;
            return errors == SslPolicyErrors.None;
        };

        return new HttpClient(h, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(100)
        };
    }
}
