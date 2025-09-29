using LiteDB;
using System;
using System.Text.Json.Serialization;

namespace Inovesys.Retail.Entities
{
    public class User
    {
        [BsonId]
        public string CompositeKey => ClientId + "-" + Id;

        [JsonPropertyName("clientId")]
        [BsonField("client_id")]
        public int ClientId { get; set; }

        [JsonPropertyName("id")]
        [BsonField("id")]
        public string Id { get; set; }

        [JsonPropertyName("passwordHash")]
        [BsonField("password_hash")]
        public string PasswordHash { get; set; }

        [JsonPropertyName("isTemporaryPassword")]
        [BsonField("is_temporary_password")]
        public bool IsTemporaryPassword { get; set; }

        [JsonPropertyName("temporaryPasswordHash")]
        [BsonField("temporary_password_hash")]
        public string TemporaryPasswordHash { get; set; }

        [JsonPropertyName("expirationDate")]
        [BsonField("expiration_date")]
        public DateTime ExpirationDate { get; set; }

        [JsonPropertyName("lastPasswordChange")]
        [BsonField("last_password_change")]
        public DateTime LastPasswordChange { get; set; }

        [JsonPropertyName("creationDate")]
        [BsonField("creation_date")]
        public DateTime CreationDate { get; set; }

        [JsonPropertyName("lastLogin")]
        [BsonField("last_login")]
        public DateTime LastLogin { get; set; }

        [JsonPropertyName("fullName")]
        [BsonField("full_name")]
        public string FullName { get; set; }

        [JsonPropertyName("nickname")]
        [BsonField("nickname")]
        public string Nickname { get; set; }

        [JsonPropertyName("documentId")]
        [BsonField("document_id")]
        public string DocumentId { get; set; }

        [JsonPropertyName("individualRegistration")]
        [BsonField("individual_registration")]
        public string IndividualRegistration { get; set; }

        [JsonPropertyName("lastChange")]
        [BsonField("last_change")]
        public DateTime LastChange { get; set; }

        [BsonIgnore]
        public string Display => $"{Id} - {FullName}";
    }
}
