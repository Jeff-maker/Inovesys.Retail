using System.Net.Http;
using System.Security.Cryptography.X509Certificates;

namespace Inovesys.Retail.Abstractions;

public interface ISefazHttpClientFactory
{
    HttpClient Create(X509Certificate2 clientCertificate);
}
