using LiteDB;
using System;
using System.Text.Json.Serialization;

namespace Inovesys.Retail.Entities
{
    public class Certificate
    {
        [BsonId]
        public string CompositeKey => ClientId + "-" + Id;

        [JsonPropertyName("id")]
        [BsonField("id")]
        public int Id { get; set; }

        [JsonPropertyName("clientId")]
        [BsonField("client_id")]
        public int ClientId { get; set; }

        [JsonPropertyName("name")]
        [BsonField("name")]
        public string Name { get; set; }

        [JsonPropertyName("createdDate")]
        [BsonField("created_date")]
        public DateTime CreatedDate { get; set; }

        [JsonPropertyName("base64File")]
        [BsonField("pfx_file")]
        public byte[] PfxFile { get; set; } // ✅ agora correto

        [JsonPropertyName("validFromDate")]
        [BsonField("valid_from_date")]
        public DateTime ValidFromDate { get; set; }

        [JsonPropertyName("validToDate")]
        [BsonField("valid_to_date")]
        public DateTime ValidToDate { get; set; }

        [JsonPropertyName("password")]
        [BsonField("password")]
        public string Password { get; set; }

        [JsonPropertyName("lastChange")]
        [BsonField("last_change")]
        public DateTime LastChange { get; set; }

        [BsonIgnore]
        public string Display => $"{Name} (válido até {ValidToDate:yyyy-MM-dd})";
    }
}
