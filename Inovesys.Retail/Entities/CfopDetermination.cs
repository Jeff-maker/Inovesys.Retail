using LiteDB;
using System;
using System.Text.Json.Serialization;

namespace Inovesys.Retail.Entities
{
    public class CfopDetermination
    {
        [BsonId]
        public string CompositeKey => $"{ClientId}-{InvoiceTypeId}-{CountryId}-{StateFrom}-{StateTo}-{MaterialTypeId}";

        [JsonPropertyName("clientId")]
        [BsonField("client_id")]
        public int ClientId { get; set; }

        [JsonPropertyName("invoiceTypeId")]
        [BsonField("invoice_type_id")]
        public string InvoiceTypeId { get; set; }

        [JsonPropertyName("countryId")]
        [BsonField("country_id")]
        public string CountryId { get; set; }

        [JsonPropertyName("stateFrom")]
        [BsonField("state_from")]
        public string StateFrom { get; set; }

        [JsonPropertyName("stateTo")]
        [BsonField("state_to")]
        public string StateTo { get; set; }

        [JsonPropertyName("materialTypeId")]
        [BsonField("material_type_id")]
        public string MaterialTypeId { get; set; }

        [JsonPropertyName("lastChange")]
        [BsonField("last_change")]
        public DateTime LastChange { get; set; }

        [JsonPropertyName("cfopId")]
        [BsonField("cfop_id")]
        public string CfopId { get; set; }

        [BsonIgnore]
        public string Display => $"{StateFrom} → {StateTo} | CFOP: {CfopId}";
    }
}
