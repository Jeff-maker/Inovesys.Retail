using LiteDB;
using System;

namespace Inovesys.Retail.Entities
{
    public class InvoiceNumberControl
    {
        [BsonId]
        public string CompositeKey => ClientId + "-" + CompanyId + "-" + BrancheId + "-" + Serie;

        [BsonField("client_id")]
        public int ClientId { get; set; }

        [BsonField("company_id")]
        public string CompanyId { get; set; }

        [BsonField("branche_id")]
        public string BrancheId { get; set; }

        [BsonField("serie")]
        public string Serie { get; set; }

        [BsonField("last_number")]
        public int LastNumber { get; set; }
    }
}