using LiteDB;
using System.Text.Json.Serialization;

namespace Inovesys.Retail.Entities
{
    public class Company
    {
        [BsonId]
        public string CompositeKey => Id + "-" + ClientId;

        [JsonPropertyName("clientId")]
        [BsonField("client_id")]
        public int ClientId { get; set; }

        [JsonPropertyName("id")]
        [BsonField("id")]
        public string Id { get; set; }

        [JsonPropertyName("description")]
        [BsonField("description")]
        public string Description { get; set; }

        [JsonPropertyName("tradeName")]
        [BsonField("trade_name")]
        public string TradeName { get; set; }

        [JsonPropertyName("lastChange")]
        [BsonField("last_change")]
        public DateTime LastChange { get; set; }

        [JsonPropertyName("creationDate")]
        [BsonField("creation_date")]
        public DateTime CreationDate { get; set; }

        [JsonPropertyName("certificateId")]
        [BsonField("certificate_id")]
        public int CertificateId { get; set; }

        [BsonIgnore]
        public string Display => $"{Id} - {Description}";
    }
}
