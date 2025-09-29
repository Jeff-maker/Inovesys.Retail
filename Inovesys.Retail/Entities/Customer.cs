using LiteDB;
using System;

namespace Inovesys.Retail.Entities
{
    public class Customer
    {
        [BsonId]
        public string CompositeKey => $"{ClientId}-{CustomerId}";

        [BsonField("client_id")]
        public int ClientId { get; set; }

        [BsonField("customer_id")]
        public int CustomerId { get; set; }

        [BsonField("name")]
        public string Name { get; set; } = string.Empty;

        [BsonField("document")]
        public string Document { get; set; } = string.Empty;

        [BsonField("document_type")]
        public int DocumentType { get; set; }

        [BsonField("address_id")]
        public int AddressId { get; set; }

        [BsonField("last_change")]
        public DateTime LastChange { get; set; }
    }
}
