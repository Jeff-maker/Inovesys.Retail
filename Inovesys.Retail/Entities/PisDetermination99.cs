using LiteDB;
using System;
using System.Text.Json.Serialization;

namespace Inovesys.Retail.Entities
{
    public class PisDetermination99
    {
        [BsonId]
        public string CompositeKey => ClientId + "-" + CountryId + "-" + InvoiceTypeId + "-" + StartDate.ToString("yyyyMMdd");

        [JsonPropertyName("clientId")]
        [BsonField("client_id")]
        public int ClientId { get; set; }

        [JsonPropertyName("countryId")]
        [BsonField("country_id")]
        public string CountryId { get; set; }

        [JsonPropertyName("invoiceTypeId")]
        [BsonField("invoice_type_id")]
        public string InvoiceTypeId { get; set; }

        [JsonPropertyName("startDate")]
        [BsonField("start_date")]
        public DateTime StartDate { get; set; }

        [JsonPropertyName("cstId")]
        [BsonField("cst_id")]
        public string CstId { get; set; }

        [JsonPropertyName("rate")]
        [BsonField("rate")]
        public decimal Rate { get; set; }

        [JsonPropertyName("baseReductionPercentage")]
        [BsonField("base_reduction_percentage")]
        public decimal BaseReductionPercentage { get; set; }

        [JsonPropertyName("lastChange")]
        [BsonField("last_change")]
        public DateTime LastChange { get; set; }
    }
}
