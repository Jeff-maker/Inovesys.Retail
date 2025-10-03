using Inovesys.Retail.Entities;
using Inovesys.Retail.Pages;
using Inovesys.Retail.Services;

namespace Inovesys.Retail
{
    public partial class MainPage : ContentPage
    {
        private readonly ToastService _toast;
        private readonly SyncService _syncService;

         // roda auto-sync só uma vez por sessão
        private bool _didAutoSync;

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


        // >>> AUTO-SYNC após a tela aparecer
        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (_didAutoSync) return;      // evita rodar toda vez que voltar pra MainPage
            _didAutoSync = true;

            await SyncAllAsync(showToastOnSuccess: false); // silencioso (ou true se quiser)
        }



        // ============ Núcleo da sincronização ============
        private async Task SyncAllAsync(bool showToastOnSuccess)
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;

                await _syncService.SyncEntitiesAsync<Client>("clients", "clients", ignoreLastChange: false);
                await _syncService.SyncEntitiesAsync<Company>("companies", "companie", ignoreLastChange: false);
                await _syncService.SyncEntitiesAsync<Branche>("branchies", "branche", ignoreLastChange: false);
                await _syncService.SyncEntitiesAsync<Certificate>("certificates", "certificate", ignoreLastChange: false);
                await _syncService.SyncEntitiesAsync<CfopDetermination>("cfop-determinations", "cfopdetermination", ignoreLastChange: false);
                await _syncService.SyncEntitiesAsync<PisDetermination99>("pis-determinations", "pisdetermination", ignoreLastChange: false);
                await _syncService.SyncEntitiesAsync<CofinsDetermination99>("cofins-determinations", "cofinsdetermination", ignoreLastChange: false);
                await _syncService.SyncEntitiesAsync<IcmsStDetermination>("icms-st-determinations", "icmsstdetermination", ignoreLastChange: false);
                await _syncService.SyncEntitiesAsync<SalesChannel>("saleschannels", "saleschannel", ignoreLastChange: false);
                await _syncService.SyncEntitiesAsync<Material>("materials", "material", ignoreLastChange: false);
                await _syncService.SyncEntitiesAsync<MaterialPrice>("material-prices", "materialprice", ignoreLastChange: false);
                await _syncService.SyncEntitiesAsync<State>("states", "state", ignoreLastChange: false);
                await _syncService.SyncEntitiesAsync<Ncm>("ncms", "ncm", ignoreLastChange: false);

                if (showToastOnSuccess)
                    await _toast.ShowToast("Dados sincronizados com sucesso!");
            }
            catch (Exception ex)
            {
                // Mostra erro sempre que falhar (tanto no auto quanto no botão)
                await DisplayAlert("Erro na sincronização", ex.Message, "OK");
            }
            finally
            {
                IsBusy = false;
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


        private async void OnSyncAllClicked(object sender, EventArgs e)
        {
            if (IsBusy) return;

            // Confirmação antes de iniciar
            bool confirmar = await DisplayAlert(
                "Sincronização completa",
                "Deseja realmente executar a sincronização completa? Este processo pode demorar alguns minutos.",
                "Sim", "Não");

            if (!confirmar) return;

            try
            {
                IsBusy = true;

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
                await _syncService.SyncEntitiesAsync<Ncm>("ncms", "ncm", ignoreLastChange: true);

                
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro", ex.Message, "OK");
            }
            finally
            {
                IsBusy = false;
                await _toast.ShowToast("Sincronização concluída!");
            }
        }



        private async void OnConsumerSaleClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(ConsumerSalePage)); // Se tiver essa página
        }



    }

}
