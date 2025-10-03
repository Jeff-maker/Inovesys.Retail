using LiteDB;

namespace Inovesys.Retail.Entities
{

    public class Ncm
    {
        [BsonId]
        [BsonField("id")]
        public string id { get; set; } = default!;

        [BsonField("description")]
        public string description { get; set; } = string.Empty;

        [BsonField("last_change")]
        public DateTime last_change { get; set; } = DateTime.UtcNow;

        // ───── Impostos (percentuais) ─────
        [BsonField("national_tax")]
        public decimal NationalTax { get; set; }    // Percentual nacional

        [BsonField("state_tax")]
        public decimal StateTax { get; set; }       // Percentual estadual

        [BsonField("municipal_tax")]
        public decimal MunicipalTax { get; set; }   // Percentual municipal

        [BsonField("imported_tax")]
        public decimal ImportedTax { get; set; }    // Percentual importado

        // ───── Campos calculados (não persistir) ─────
        [BsonIgnore]
        public decimal TotalTaxNational => Math.Round(NationalTax + StateTax + MunicipalTax, 4);

        [BsonIgnore]
        public decimal TotalTaxImported => Math.Round(ImportedTax + StateTax + MunicipalTax, 4);
    }

}