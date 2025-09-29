using LiteDB;
using System;

namespace Inovesys.Retail.Entities
{
    public class City
    {
        [BsonId]
        public string CompositeKey => $"{CountryId}-{StateId}-{Id}";

        [BsonField("country_id")]
        public string CountryId { get; set; }

        [BsonField("state_id")]
        public string StateId { get; set; }

        [BsonField("id")]
        public string Id { get; set; }

        [BsonField("description")]
        public string Description { get; set; }

        [BsonField("last_change")]
        public DateTime LastChange { get; set; }
    }
}