using LiteDB;
using System;
using System.Text.Json.Serialization;

namespace Inovesys.Retail.Entities
{
    public class Material
    {
        [BsonId]
        public string CompositeKey => ClientId + "-" + Id;

        [JsonPropertyName("clientId")]
        [BsonField("client_id")]
        public int ClientId { get; set; }

        [JsonPropertyName("id")]
        [BsonField("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        [BsonField("name")]
        public string Name { get; set; }

        [JsonPropertyName("materialTypeId")]
        [BsonField("type")]
        public string Type { get; set; }

        [JsonPropertyName("basicUnitId")]
        [BsonField("basic_unit")]
        public string BasicUnit { get; set; }

        [JsonPropertyName("weightUnitId")]
        [BsonField("weight_unit_id")]
        public string WeightUnitId { get; set; }

        [JsonPropertyName("ncmId")]
        [BsonField("ncm_id")]
        public string NcmId { get; set; }

        [JsonPropertyName("eaN13")]
        [BsonField("ean_13")]
        public string Ean13 { get; set; }

        [JsonPropertyName("netWeight")]
        [BsonField("net_weight")]
        public decimal? NetWeight { get; set; }

        [JsonPropertyName("grossWeight")]
        [BsonField("gross_weight")]
        public decimal? GrossWeight { get; set; }

        [JsonPropertyName("taxSubCodeId")]
        [BsonField("tax_sub_code_id")]
        public string TaxSubCodeId { get; set; }

        [JsonPropertyName("lastChange")]
        [BsonField("last_change")]
        public DateTime LastChange { get; set; }

    }
}
