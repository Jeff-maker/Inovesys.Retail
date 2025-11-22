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
                throw new Exception("O arquivo do certificado está vazio ou não foi fornecido.");

            if (certInfo.PfxFile.Length < 1000)
                throw new Exception("O conteúdo do PFX parece incorreto (tamanho muito pequeno).");

            helpers ??= new Helpers(_configuration);
            if (helpers == null)
                throw new Exception("Falha interna: helpers == null.");

            try
            {
                var password = helpers.Descriptografar(certInfo.Password) ?? string.Empty;

                X509KeyStorageFlags flags;

                if (OperatingSystem.IsWindows())
                {
                    // 💻 Windows usa persistência local no perfil do usuário
                    flags = X509KeyStorageFlags.UserKeySet |
                            X509KeyStorageFlags.PersistKeySet |
                            X509KeyStorageFlags.Exportable;
                }
                else if (OperatingSystem.IsAndroid())
                {
                    // 🤖 Android precisa ser efêmero para evitar falhas no KeyStore
                    flags = X509KeyStorageFlags.EphemeralKeySet |
                            X509KeyStorageFlags.Exportable;
                }
                else
                {
                    // 🍎 iOS/macOS também usa efêmero
                    flags = X509KeyStorageFlags.EphemeralKeySet |
                            X509KeyStorageFlags.Exportable;
                }

                //var certificate = new X509Certificate2(certInfo.PfxFile, password, flags);

                var certificate = X509CertificateLoader.LoadPkcs12(
                           certInfo.PfxFile,
                           password,
                            flags);

                if (!certificate.HasPrivateKey)
                    throw new Exception("O certificado foi carregado, mas não contém chave privada.");

                // 🔐 Testa suporte RSA/ECDSA
                using var rsa = certificate.GetRSAPrivateKey();
                using var ecdsa = certificate.GetECDsaPrivateKey();

                if (rsa is null && ecdsa is null)
                    throw new Exception("Não foi possível obter a chave privada (RSA/ECDSA) do certificado.");

                return certificate;
            }
            catch (CryptographicException ex)
            {
                throw new Exception("Senha do certificado incorreta ou PFX inválido.", ex);
            }
        }


    }
}
