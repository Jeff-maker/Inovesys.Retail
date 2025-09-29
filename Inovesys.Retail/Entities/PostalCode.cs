using LiteDB;
using System;

namespace Inovesys.Retail.Entities
{
    public class PostalCode
    {
        [BsonId]
        public string CompositeKey => country_id + "-" + state_id + "-" + city_id + "-" + id;
        [BsonField("country_id")]
        public string country_id { get; set; }
        [BsonField("state_id")]
        public string state_id { get; set; }
        [BsonField("city_id")]
        public string city_id { get; set; }
        [BsonField("id")]
        public string id { get; set; }
        [BsonField("streeat")]
        public string streeat { get; set; }
        [BsonField("neighborhood")]
        public string neighborhood { get; set; }
        [BsonField("last_change")]
        public DateTime last_change { get; set; }
    }
}