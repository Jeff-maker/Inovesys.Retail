namespace Inovesys.Retail.Models
{
    public class InvoiceItemPostDto
    {
        public string MaterialId { get; set; } = null!;
        public string MaterialName { get; set; } = null!;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string UnitId { get; set; } = null!;
        public string NCM { get; set; } = null!;
    }
}
