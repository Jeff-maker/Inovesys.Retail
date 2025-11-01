

namespace Inovesys.Retail.Models
{
    public class ProductSuggestion
    {
        /// <summary>
        /// Código ou SKU do produto.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Descrição ou nome do produto.
        /// </summary>
        public string Name { get; set; } = string.Empty;


        /// <summary>
        /// Preço unitário do produto.
        /// </summary>
        public decimal? Price { get; set; }

        /// <summary>
        /// Unidade de preço (ex: "UN", "KG", "CX").
        /// </summary>
        public string PriceUnit { get; set; } = string.Empty;


        public override string ToString() => $"{Id} - {Name}";

        /// <summary>
        /// Usado pelo Zoft via DisplayMemberPath.
        /// </summary>
        public string Display => $"{Id} - {Name} ({Price:C} / {PriceUnit})";
    }
}
