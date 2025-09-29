using LiteDB;
using System.Text.Json.Serialization;

namespace Inovesys.Retail.Entities
{
    public class SalesChannel
    {
        [BsonId]
        public string CompositeKey => ClientId + "-" + Id;

        [JsonPropertyName("clientId")]
        [BsonField("client_id")]
        public int ClientId { get; set; }

        [JsonPropertyName("id")]
        [BsonField("id")]
        public string Id { get; set; }

        [JsonPropertyName("description")]
        [BsonField("description")]
        public string Description { get; set; }

        [JsonPropertyName("isDefault")]
        [BsonField("is_default")]
        public bool IsDefault { get; set; }

        [JsonPropertyName("lastChange")]
        [BsonField("last_change")]
        public DateTime LastChange { get; set; }
    }
}
