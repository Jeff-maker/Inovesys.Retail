

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

        public override string ToString() => $"{Id} - {Name}";


        /// Usado pelo Zoft via DisplayMemberPath
        public string Display => $"{Id} - {Name}";
    }
}
