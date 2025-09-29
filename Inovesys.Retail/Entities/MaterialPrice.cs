using LiteDB;
using System;
using System.Text.Json.Serialization;

namespace Inovesys.Retail.Entities
{
    public class MaterialPrice
    {
        [BsonId]
        public string CompositeKey => ClientId + "-" + SalesChannelId + "-" + MaterialId + "-" + By + "-" + StartDate;

        [JsonPropertyName("clientId")]
        [BsonField("client_id")]
        public int ClientId { get; set; }

        [JsonPropertyName("salesChannelId")]
        [BsonField("sales_channel_id")]
        public string SalesChannelId { get; set; }

        [JsonPropertyName("materialId")]
        [BsonField("material_id")]
        public string MaterialId { get; set; }

        [JsonPropertyName("by")]
        [BsonField("by")]
        public int By { get; set; }

        [JsonPropertyName("startDate")]
        [BsonField("start_date")]
        public DateTime StartDate { get; set; }

        [JsonPropertyName("price")]
        [BsonField("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("materialUnit")]
        [BsonField("material_unit")]
        public string MaterialUnit { get; set; }

        [JsonPropertyName("endDate")]
        [BsonField("end_date")]
        public DateTime EndDate { get; set; }

        [JsonPropertyName("lastChange")]
        [BsonField("last_change")]
        public DateTime LastChange { get; set; }
    }
}
