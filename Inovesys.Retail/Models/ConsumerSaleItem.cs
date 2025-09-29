namespace Inovesys.Retail.Models
{
    public class ConsumerSaleItem
    {
        public string Id { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; } = 1;
        public string NCM { get; set; }
    }

}
