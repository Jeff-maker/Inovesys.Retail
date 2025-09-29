using System.ComponentModel.DataAnnotations;

namespace Inovesys.Retail.Models
{
    public class InvoicePostDto
    {

        public string InvoiceTypeId { get; set; } = null!;
        public string CompanyId { get; set; } = null!;
        public string BrancheId { get; set; } = null!;
        public int? CustomerId { get; set; }  // Substitua pelo tipo correto

        [RegularExpression(@"^\d{3}$", ErrorMessage = "NFe must be a string with exactly 3 digits.")]
        public string Serie { get; set; } = null!;
        public virtual List<InvoiceItemPostDto> InvoiceItems { get; set; } = null!;

        
    }
}
