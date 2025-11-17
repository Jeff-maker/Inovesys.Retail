using System.ComponentModel;

namespace Inovesys.Retail.Models
{
    public class ConsumerSaleItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string MaterialId { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }

        private decimal _quantity;
        public decimal Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    OnPropertyChanged(nameof(Quantity));
                    OnPropertyChanged(nameof(Total));
                }
            }
        }

        public string NCM { get; set; }

        public decimal Total => Price * Quantity;

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }


    }

}
