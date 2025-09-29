using LiteDB;
using System;

namespace Inovesys.Retail.Entities
{
    public class Address
    {
        [BsonId]
        public string CompositeKey => ClientId + "-" + Id;

        [BsonField("client_id")]
        public int ClientId { get; set; }

        [BsonField("id")]
        public int Id { get; set; }

        [BsonField("country_id")]
        public string CountryId { get; set; }

        [BsonField("state_id")]
        public string StateId { get; set; }

        [BsonField("city_id")]
        public string CityId { get; set; }

        [BsonField("postal_code")]
        public string PostalCode { get; set; }

        [BsonField("neighborhood")]
        public string Neighborhood { get; set; }

        [BsonField("street")]
        public string Street { get; set; }

        [BsonField("house_number")]
        public string HouseNumber { get; set; }

        [BsonField("complement")]
        public string Complement { get; set; }

        [BsonField("last_change")]
        public DateTime LastChange { get; set; }

        [BsonField("city")]
        public City City { get; set; } = new City();

    }
}
