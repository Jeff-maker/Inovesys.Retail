using Inovesys.Retail.Pages;

namespace Inovesys.Retail
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
            Routing.RegisterRoute(nameof(LoginPage), typeof(LoginPage));
            Routing.RegisterRoute(nameof(ConsumerSalePage), typeof(ConsumerSalePage));
            

        }
    }
}
