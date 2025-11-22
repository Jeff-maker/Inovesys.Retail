using System;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Inovesys.Retail;

public static class SefazHttpPlatform
{
   
    public static HttpClient CreateClient(X509Certificate2 cert)
    {
        if (cert is null || !cert.HasPrivateKey)
            throw new CryptographicException("Certificado inválido: sem chave privada.");

        if (OperatingSystem.IsAndroid())
            return CreateAndroidClientViaReflection(cert);

        // Windows / Linux / etc.
        var sh = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ClientCertificates = new X509CertificateCollection { cert }
            }
        };
        return new HttpClient(sh, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(100)
        };
    }

    private static HttpClient CreateAndroidClientViaReflection(X509Certificate2 cert)
    {
        const string handlerTypeName = "Xamarin.Android.Net.AndroidMessageHandler, Mono.Android";
        var handlerType = Type.GetType(handlerTypeName, throwOnError: false)
                         ?? throw new PlatformNotSupportedException("AndroidMessageHandler não disponível nesta runtime.");

        // new AndroidMessageHandler()
        var handler = Activator.CreateInstance(handlerType)
                     ?? throw new PlatformNotSupportedException("Falha ao instanciar AndroidMessageHandler.");

        // handler.ClientCertificateOptions = ClientCertificateOption.Manual;
        var ccProp = handlerType.GetProperty("ClientCertificateOptions");
        if (ccProp != null && ccProp.PropertyType.IsEnum)
        {
            // pega o tipo do enum pela própria propriedade (sem Type.GetType)
            var manualValue = Enum.Parse(ccProp.PropertyType, "Manual"); // ok, sem assembly-qualified
            ccProp.SetValue(handler, manualValue);
        }

        // handler.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
        var spProp = handlerType.GetProperty("SslProtocols");
        if (spProp != null)
        {
            var proto = SslProtocols.Tls12 | SslProtocols.Tls13;
            spProp.SetValue(handler, proto);
        }

        // handler.ClientCertificates ??= new X509Certificate2Collection();
        var ccColProp = handlerType.GetProperty("ClientCertificates");
        var coll = ccColProp?.GetValue(handler) as X509Certificate2Collection;
        if (coll == null)
        {
            coll = new X509Certificate2Collection();
            ccColProp?.SetValue(handler, coll);
        }
        coll!.Add(cert);

        // handler.ServerCertificateCustomValidationCallback = (req, srvCert, chain, errors) => { ... }
        var scvProp = handlerType.GetProperty("ServerCertificateCustomValidationCallback");
        if (scvProp != null)
        {
            // assinatura: Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>
            Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool> cb =
                (req, srvCert, chain, errors) =>
                {
                    var host = req?.RequestUri?.Host ?? "";
                    // whitelist mínima
                    if (string.Equals(host, "homologacao.nfce.fazenda.sp.gov.br", StringComparison.OrdinalIgnoreCase))
                        return true;
                    return errors == SslPolicyErrors.None;
                };

            scvProp.SetValue(handler, cb);
        }

        // monta HttpClient
        var http = new HttpClient((HttpMessageHandler)handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(100)
        };
        return http;
    }

}
