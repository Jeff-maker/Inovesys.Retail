
namespace Inovesys.Retail.Models
{
    public sealed class BackofficeInvoiceItem
    {

        [Newtonsoft.Json.JsonProperty("itemNumber")]
        public int ItemNumber { get; set; }

        [Newtonsoft.Json.JsonProperty("materialId")]
        public string MaterialId { get; set; }

        [Newtonsoft.Json.JsonProperty("quantity")]
        public decimal Quantity { get; set; }

        [Newtonsoft.Json.JsonProperty("unitPrice")]
        public decimal UnitPrice { get; set; }

        [Newtonsoft.Json.JsonProperty("discountAmount")]
        public decimal DiscountAmount { get; set; }

        [Newtonsoft.Json.JsonProperty("totalAmount")]
        public decimal TotalAmount { get; set; }

        [Newtonsoft.Json.JsonProperty("unitId")]
        public string UnitId { get; set; }
        [Newtonsoft.Json.JsonProperty("cfopId")]
        public string CFOPId { get; set; } = null!;
    }
}
