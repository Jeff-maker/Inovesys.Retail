using Inovesys.Retail.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Inovesys.Retail.Models
{
    public class ODataResponse<T>
    {
        [JsonPropertyName("@odata.context")]
        public string Context { get; set; } = null!;

        public List<T> Value { get; set; } = new();

        [JsonPropertyName("@odata.count")]
        public int? Count { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("@odata.nextLink")]
        public string NextLink { get; set; } = null;


    }

    public class BranchieDtoRoot
    {
        
        [JsonPropertyName("@odata.context")]
        public string Context { get; set; } = null!;

        [JsonProperty("value")]
        public List<BranchieDto> Value { get; set; }

        [JsonPropertyName("@odata.count")]
        public int? Count { get; set; }
    }

    public class BranchieDto
    {
        [JsonProperty("companyId")]
        public string CompanyId { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("address")]
        public Address Address;

        // outras propriedades...
    }

}
