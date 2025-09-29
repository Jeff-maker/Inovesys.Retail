using System.Text.Json.Serialization;

namespace Inovesys.Retail.Models
{
    public class ClientDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("lastChange")]
        public DateTime LastChange { get; set; }

        [JsonPropertyName("environmentSefaz")]
        public string EnvironmentSefaz { get; set; } = string.Empty;
    }
}
