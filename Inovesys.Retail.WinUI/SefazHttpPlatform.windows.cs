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

        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ClientCertificates = new X509CertificateCollection { cert }
            }
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(100)
        };
    }
}


