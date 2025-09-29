using Inovesys.Retail.Entities;
using Inovesys.Retail.Pages;
using Inovesys.Retail.Services;

namespace Inovesys.Retail
{
    public partial class MainPage : ContentPage
    {
        private readonly ToastService _toast;
        private readonly SyncService _syncService;
        public MainPage(SyncService syncService, ToastService toast )
        {
            InitializeComponent();
            _syncService = syncService;
            _toast = toast;
        }

        private async void OnSyncClicked(object sender, EventArgs e)
        {
            try
            {
                await DisplayAlert("Sincronização", "Dados sincronizados com sucesso!", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro", $"Erro ao sincronizar: {ex.Message}", "Fechar");
            }
        }


        private async void OnSettingsClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(SettingsPage));
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(LoginPage));
        }


        private void OnSyncAllClicked(object sender, EventArgs e)
        {
            IsBusy = true;

            Task.Run(async () =>
            {
                try
                {
                    await _syncService.SyncEntitiesAsync<Client>("clients", "clients", ignoreLastChange: true);
                    await _syncService.SyncEntitiesAsync<Company>("companies", "companie", ignoreLastChange: true);
                    await _syncService.SyncEntitiesAsync<Branche>("branchies", "branche", ignoreLastChange: true);
                    await _syncService.SyncEntitiesAsync<Certificate>("certificates", "certificate", ignoreLastChange: true);
                    await _syncService.SyncEntitiesAsync<CfopDetermination>("cfop-determinations", "cfopdetermination", ignoreLastChange: true);
                    await _syncService.SyncEntitiesAsync<PisDetermination99>("pis-determinations", "pisdetermination", ignoreLastChange: true);
                    await _syncService.SyncEntitiesAsync<CofinsDetermination99>("cofins-determinations", "cofinsdetermination", ignoreLastChange: true);
                    await _syncService.SyncEntitiesAsync<IcmsStDetermination>("icms-st-determinations", "icmsstdetermination", ignoreLastChange: true);
                    await _syncService.SyncEntitiesAsync<SalesChannel>("saleschannels", "saleschannel", ignoreLastChange: true);
                    await _syncService.SyncEntitiesAsync<Material>("materials", "material", ignoreLastChange: true);
                    await _syncService.SyncEntitiesAsync<MaterialPrice>("material-prices", "materialprice", ignoreLastChange: true);
                    await _syncService.SyncEntitiesAsync<State>("states", "state", ignoreLastChange: true);

                    // Precisa voltar para a UI thread para mexer em IsBusy ou exibir alerta
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        IsBusy = false;
                        await _toast.ShowToast("Dados sincronizados com sucesso!");
                    });
                }
                catch (Exception ex)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        IsBusy = false;
                        await DisplayAlert("Erro", ex.Message, "OK");
                    });
                }
            });
        }


        private async void OnConsumerSaleClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(ConsumerSalePage)); // Se tiver essa página
        }



    }

}
