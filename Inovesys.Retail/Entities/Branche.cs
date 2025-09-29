using LiteDB;
using System;
using System.Text.Json.Serialization;

namespace Inovesys.Retail.Entities
{
    public class Branche
    {
        [BsonId]
        public string CompositeKey => ClientId + "-" + CompanyId + "-" + Id;

        [BsonField("client_id")]
        public int ClientId { get; set; }

        [JsonPropertyName("companyId")]
        [BsonField("company_id")]
        public string CompanyId { get; set; }

        [JsonPropertyName("id")]
        [BsonField("id")]
        public string Id { get; set; }

        [JsonPropertyName("description")]
        [BsonField("description")]
        public string Description { get; set; }

        [JsonPropertyName("lastChange")]
        [BsonField("last_change")]
        public DateTime LastChange { get; set; }

        [JsonPropertyName("creationDate")]
        [BsonField("creation_date")]
        public DateTime CreationDate { get; set; }

        [JsonPropertyName("cnpj")]
        [BsonField("cnpj")]
        public string Cnpj { get; set; }

        [JsonPropertyName("isMainPlant")]
        [BsonField("is_main_plant")]
        public bool IsMainPlant { get; set; }

        [JsonPropertyName("stateRegistration")]
        [BsonField("state_registration")]
        public string StateRegistration { get; set; }

        [JsonPropertyName("taxRegime")]
        [BsonField("tax_regime")]
        public string TaxRegime { get; set; }

        [JsonPropertyName("idTokenSefaz")]
        [BsonField("id_token_sefaz")]
        public string IdTokenSefaz { get; set; }

        [JsonPropertyName("cscSefaz")]
        [BsonField("csc_sefaz")]
        public string CscSefaz { get; set; }

        [JsonPropertyName("countryId")]
        [BsonField("country_id")]
        public string CountryId { get; set; }

        [JsonPropertyName("stateId")]
        [BsonField("state_id")]
        public string StateId { get; set; }

        [JsonPropertyName("cityId")]
        [BsonField("city_id")]
        public string CityId { get; set; }

        [JsonPropertyName("cityDescription")]
        [BsonField("city_description")]
        public string CityDescription { get; set; }

        [JsonPropertyName("postalCode")]
        [BsonField("postal_code")]
        public string PostalCode { get; set; }

        [JsonPropertyName("neighborhood")]
        [BsonField("neighborhood")]
        public string Neighborhood { get; set; }

        [JsonPropertyName("street")]
        [BsonField("street")]
        public string Street { get; set; }

        [JsonPropertyName("houseNumber")]
        [BsonField("house_number")]
        public string HouseNumber { get; set; }

        [JsonPropertyName("complement")]
        [BsonField("complement")]
        public string Complement { get; set; }

        [BsonIgnore]
        public string Display => $"{CompanyId} / {Id} - {Description}";

        [JsonPropertyName("address")]
        public Address Address
        {
            set
            {
                if (value == null) return;
                CountryId = value.CountryId;
                StateId = value.StateId;
                CityId = value.CityId;
                CityDescription = value.City.Description;
                PostalCode = value.PostalCode;
                Neighborhood = value.Neighborhood;
                Street = value.Street;
                HouseNumber = value.HouseNumber;
                Complement = value.Complement;
            }
        }
    }
}
