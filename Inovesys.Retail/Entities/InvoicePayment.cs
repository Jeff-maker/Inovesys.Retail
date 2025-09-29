using LiteDB;

namespace Inovesys.Retail.Entities
{
    public class InvoicePayment
    {
        [BsonId]
        public string CompositeKey => $"{ClientId}-{InvoiceId}-{PaymentId}";

        [BsonField("client_id")]
        public int ClientId { get; set; }

        [BsonField("invoice_id")]
        public int InvoiceId { get; set; }

        [BsonField("payment_id")]
        public int PaymentId { get; set; }

        [BsonField("payment_method_id")]
        public string PaymentMethodId { get; set; } = string.Empty;

        [BsonField("amount")]
        public decimal Amount { get; set; }

        [BsonField("payment_date")]
        public DateTime PaymentDate { get; set; }

        [BsonField("additional_info")]
        public string AdditionalInfo { get; set; } = string.Empty;

        [BsonField("card_issuer")]
        public string CardIssuer { get; set; } = string.Empty;

        [BsonField("installments")]
        public int Installments { get; set; }

        [BsonField("authorization_code")]
        public string AuthorizationCode { get; set; } = string.Empty;
    }
}
