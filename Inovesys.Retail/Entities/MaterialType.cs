using LiteDB;
using System;

namespace Inovesys.Retail.Entities
{
    public class MaterialType
    {
        [BsonId]
        [BsonField("id")]
        public string id { get; set; }
        [BsonField("description")]
        public string description { get; set; }
        [BsonField("sped_type")]
        public string sped_type { get; set; }
        [BsonField("last_change")]
        public DateTime last_change { get; set; }
    }
}