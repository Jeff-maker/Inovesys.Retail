using LiteDB;

namespace Inovesys.Retail.Entities
{
    public class State
    {
        [BsonId]
        public string CompositeKey => CountryId + "-" + Id;

        [BsonField("country_id")]
        public string CountryId { get; set; }

        [BsonField("id")]
        public string Id { get; set; }

        [BsonField("description")]
        public string Description { get; set; }

        [BsonField("government_code")]
        public string GovernmentCode { get; set; }

        [BsonField("last_change")]
        public DateTime LastChange { get; set; }
    }
}
