using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using Inovesys.Retail.Services;
using Inovesys.Infrastructure;
using Inovesys.Retail.Entities;
using Microsoft.Extensions.Configuration;

namespace Inovesys.Retail
{
    public static class SefazHelpers
    {
        static Helpers helpers;

        static IConfiguration _configuration;

        public static X509Certificate2 LoadCertificate(int? certificateId, int clientKey, LiteDbService liteDbService)
        {
            var collection = liteDbService.GetCollection<Certificate>("certificate");

            var certInfo = collection
                .FindOne(c => c.Id == certificateId && c.ClientId == clientKey)
                ?? throw new Exception("Certificado não encontrado.");

            if (certInfo.PfxFile == null || certInfo.PfxFile.Length == 0)
            {
                throw new Exception("O arquivo do certificado está vazio ou não foi fornecido.");
            }

            helpers = helpers ?? new Helpers(_configuration);

            if (helpers == null)
            {
                throw new Exception("helpers == null");
            }

            try
            {
                var password = helpers.Descriptografar(certInfo.Password);



                // Flags recomendadas para certificados A1
#pragma warning disable SYSLIB0057 // O tipo ou membro é obsoleto
                var certificate = new X509Certificate2(
                          certInfo.PfxFile,
                          password,
                          X509KeyStorageFlags.UserKeySet | // 🔑 Usa perfil do usuário, compatível com MAUI/WinUI
                          X509KeyStorageFlags.PersistKeySet |
                          X509KeyStorageFlags.Exportable);

                // Verifica se a chave privada está disponível
                if (!certificate.HasPrivateKey)
                {
                    throw new Exception("O certificado não contém uma chave privada válida.");
                }


                if (certInfo.PfxFile == null || certInfo.PfxFile.Length < 1000)
                    throw new Exception("O conteúdo binário do PFX está incorreto ou muito pequeno.");

                var privateKey = certificate.GetRSAPrivateKey();
                if (privateKey == null)
                    throw new Exception("A chave privada RSA não pôde ser obtida.");


                return certificate;
            }
            catch (CryptographicException ex)
            {
                throw new Exception("Senha do certificado incorreta ou certificado inválido.", ex);
            }
        }

    }
}
