using LiteDB;

namespace Inovesys.Retail.Entities
{
    public class UserConfig
    {
        [BsonId]
        public string Id { get; set; } = "CURRENT"; // Sempre manter um único registro com essa chave

        public string Token { get; set; } = null!;


        public string Email { get; set; } = null!;

        public DateTime LastLogin { get; set; } = DateTime.UtcNow;

        public string DefaultCompanyId { get; set; }  

        public string DefaultBranche { get; set; } 

        public int ClientId { get; set; } = 0;

        public string SeriePDV { get; set; }
        public string FirstInvoiceNumber { get; set; }

        // 📌 Novas propriedades para impressão
        public string PrinterName { get; set; } = "MP-4200 TH"; // Nome da impressora no Windows
        public string PrinterIp { get; set; } = "127.0.0.1";    // IP do serviço de impressão
        public int PrinterPort { get; set; } = 9100;            // Porta do serviço de impressão

    }
}
