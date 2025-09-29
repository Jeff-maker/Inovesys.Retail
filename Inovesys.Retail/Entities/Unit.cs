using LiteDB;
using System;

namespace Inovesys.Retail.Entities
{
    public class Unit
    {
        [BsonId]
        [BsonField("id")]
        public string id { get; set; }
        [BsonField("description")]
        public string description { get; set; }
        [BsonField("unit_type_id")]
        public string unit_type_id { get; set; }
        [BsonField("last_change")]
        public DateTime last_change { get; set; }
    }
}