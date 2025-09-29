using LiteDB;
using System;
using System.Text.Json.Serialization;

namespace Inovesys.Retail.Entities
{
    public class Cfop
    {
        [BsonId]
        [JsonPropertyName("id")]
        [BsonField("id")]
        public string Id { get; set; }

        [JsonPropertyName("description")]
        [BsonField("description")]
        public string Description { get; set; }

        [JsonPropertyName("lastChange")]
        [BsonField("last_change")]
        public DateTime LastChange { get; set; }

        [BsonIgnore]
        public string Display => $"{Id} - {Description}";
    }
}
