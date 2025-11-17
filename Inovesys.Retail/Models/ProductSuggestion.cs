

using System.ComponentModel;
using System.Globalization;

namespace Inovesys.Retail.Models
{
    public class ProductSuggestion: INotifyPropertyChanged
    {

        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

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



        public string Display
        {
            get
            {
                string code = (Id ?? "").Trim();
                string name = (Name ?? "").Trim();
                string pricePart = ((Price ?? 0m).ToString("C2", new CultureInfo("pt-BR")))
                                   + (string.IsNullOrWhiteSpace(PriceUnit) ? "" : $" / {PriceUnit}");

                const int widthCode = 30;   // coluna 1
                const int widthName = 100;  // coluna 2

                // Trunca ou completa
                code = code.Length > widthCode ? code[..(widthCode - 1)] + "…" : code.PadRight(widthCode);
                name = name.Length > widthName ? name[..(widthName - 1)] + "…" : name.PadRight(widthName);

                // 20 | 100 | resto (preço)
                return $"{code}{name}{pricePart}";
            }
        }

        public override string ToString() => Display;

        // Notificação
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));


    }
}
