using LiteDB;
using System;

namespace Inovesys.Retail.Entities
{
    public class PaymentMethod
    {
        [BsonId]
        [BsonField("payment_method_id")]
        public string payment_method_id { get; set; }
        [BsonField("description")]
        public string description { get; set; }
        [BsonField("last_change")]
        public DateTime last_change { get; set; }
    }
}