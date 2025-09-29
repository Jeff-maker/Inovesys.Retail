using LiteDB;
using System;

namespace Inovesys.Retail.Entities
{
    public class InvoiceItemTax
    {
        [BsonId]
        public string CompositeKey => ClientId + "-" + InvoiceId + "-" + ItemNumber + "-" + TaxTypeId;

        [BsonField("client_id")]
        public int ClientId { get; set; }

        [BsonField("invoice_id")]
        public int InvoiceId { get; set; }

        [BsonField("item_number")]
        public int ItemNumber { get; set; }

        [BsonField("tax_type_id")]
        public string TaxTypeId { get; set; } = null!;

        [BsonField("rate")]
        public decimal Rate { get; set; }

        [BsonField("base")]
        public decimal BaseValue { get; set; }

        [BsonField("value")]
        public decimal Value { get; set; }

        public string IndexKey => $"{ClientId}|{InvoiceId}|{ItemNumber}";
    }
}
