using LiteDB;
using System;

namespace Inovesys.Retail.Entities
{
    public class Invoice
    {
        [BsonId]
        public string CompositeKey => ClientId + "-" + Nfe + "-" + Serie;

        [BsonField("client_id")]
        public int? ClientId { get; set; }

        [BsonField("invoice_id")]
        public int InvoiceId { get; set; }

        [BsonField("invoice_type_id")]
        public string InvoiceTypeId { get; set; }

        [BsonField("company_id")]
        public string CompanyId { get; set; }

        [BsonField("branche_id")]
        public string BrancheId { get; set; }

        [BsonField("issue_date")]
        public DateTime IssueDate { get; set; }

        // Nova propriedade → Data/hora da autorização (dhRecbto)
        [BsonField("authorization_date")]
        public DateTime? AuthorizationDate { get; set; }

        [BsonField("authorization_protocol")]
        public string AuthorizationProtocol { get; set; }

        [BsonField("customer_id")]
        public int? CustomerId { get; set; }

        [BsonField("nfe")]
        public string Nfe { get; set; }

        [BsonField("serie")]
        public string Serie { get; set; }

        [BsonField("total_amount")]
        public decimal TotalAmount { get; set; }

        [BsonField("authorized_xml")]
        public string AuthorizedXml { get; set; }

        [BsonField("protocol")]
        public string Protocol { get; set; }

        [BsonField("nfe_status")]
        public string NfeStatus { get; set; }

        [BsonField("last_update")]
        public DateTime LastUpdate { get; set; }

        [BsonField("nf_key")]
        public string NfKey { get; set; }

        [BsonField("qr_code")]
        public string QrCode  { get; set; }

        [BsonField("printed")]
        public bool Printed { get; set; }

        [BsonField("sent")]
        public bool Send { get; set; }

        [BsonField("last_change")]
        public DateTime LastChange { get; set; }

        public string IndexKey => $"{ClientId}|{CompanyId}|{BrancheId}|{NfeStatus}";

        [BsonIgnore]
        public List<InvoiceItem> Items { get; set; } = new();

        [BsonIgnore]
        public Customer Customer { get; set; } = new();

        [BsonIgnore]
        public List<InvoicePayment> InvoicePayments { get; set; } = new();

        [BsonIgnore]
        public string AdditionalInfo { get; set; } = string.Empty;

        [BsonField("contingency")]
        public bool Contingency { get; set; }

        [BsonField("canceled_xml")]
        public string CanceledXml { get; set; }

        [BsonField("sent_cancel")]
        public bool SendCancel { get; set; }

        [BsonField("Invoice_backend_id")]
        public int InvoiceBackEndId { get; set; }


    }
}
