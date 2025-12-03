
using LiteDB;

namespace Inovesys.Retail.Models
{
    public sealed class BackofficeInvoiceRequest
    {
        [Newtonsoft.Json.JsonProperty("invoiceTypeId")]
        public string InvoiceTypeId { get; set; }

        [Newtonsoft.Json.JsonProperty("companyId")]
        public string CompanyId { get; set; }

        // ATENÇÃO: o endpoint espera "brancheId" (com 'e' no meio), mantendo igual ao cURL
        [Newtonsoft.Json.JsonProperty("brancheId")]
        public string BrancheId { get; set; }

        [Newtonsoft.Json.JsonProperty("customerId")]
        public int? CustomerId { get; set; }

        [Newtonsoft.Json.JsonProperty("serie")]
        public string Serie { get; set; }

        [Newtonsoft.Json.JsonProperty("nfe")]
        public string NFe { get; set; }

        [Newtonsoft.Json.JsonProperty("totalAmount")]
        public decimal TotalAmount { get; set; }

        [Newtonsoft.Json.JsonProperty("nfKey")]
        public string NFKey { get; set; }

        [Newtonsoft.Json.JsonProperty("invoiceItems")]
        public List<BackofficeInvoiceItem> InvoiceItems { get; set; } = new();

        [Newtonsoft.Json.JsonProperty("issueDate")]
        public DateTime IssueDate { get; set; }

        [Newtonsoft.Json.JsonProperty("authorizedXml")]
        public byte[] AuthorizedXml { get; set; }

        [Newtonsoft.Json.JsonProperty("nfeStatus")]
        public string NFeStatus { get; set; }

        [Newtonsoft.Json.JsonProperty("protocol")]

        public string Protocol { get; set; }



    }
}
