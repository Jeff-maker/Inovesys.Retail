using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inovesys.Retail.Models
{
    public class BackofficeInvoiceCancelRequest
    {

        public string Reason { get; set; } = null!;
        public string? Protocol { get; set; }
        public string CancelXml { get; set; }
    }
}
