using Inovesys.Retail.Entities;
using Inovesys.Retail.Pages;
using Inovesys.Retail.Services;
using System.Reflection;

namespace Inovesys.Retail
{
    public enum SyncMode
    {
        Partial, // usa LastChange (delta)
        Full     // ignora LastChange (força full)
    }

    public partial class MainPage : ContentPage
    {
        private readonly ToastService _toast;
        private readonly SyncService _syncService;
        private readonly LiteDbService _db;

        // roda auto-sync só uma vez por sessão
        private bool _didAutoSync;

        // Mapa único de todas as entidades de sync
        // (Type, endpoint plural, endpoint singular)
        private static readonly (Type Type, string Plural, string Singular)[] _syncMap =
        {
            (typeof(Client),                "clients",               "client"),
            (typeof(Company),               "companies",             "companie"),
            (typeof(Branche),               "branchies",             "branche"),
            (typeof(Certificate),           "certificates",          "certificate"),
            (typeof(CfopDetermination),     "cfop-determinations",   "cfopdetermination"),
            (typeof(PisDetermination99),    "pis-determinations",    "pisdetermination"),
            (typeof(CofinsDetermination99), "cofins-determinations", "cofinsdetermination"),
            (typeof(IcmsStDetermination),   "icms-st-determinations","icmsstdetermination"),
            (typeof(SalesChannel),          "saleschannels",         "saleschannel"),
            (typeof(Material),              "materials",             "material"),
            (typeof(MaterialPrice),         "material-prices",       "materialprice"),
            (typeof(State),                 "states",                "state"),
            (typeof(Ncm),                   "ncms",                  "ncm"),
        };

        public MainPage(SyncService syncService, ToastService toast, LiteDbService liteDatabase)
        {
            InitializeComponent();
            _syncService = syncService;
            _toast = toast;
            _db = liteDatabase;
        }

        // --- Botão "Sincronizar" (parcial)
        private async void OnSyncClicked(object sender, EventArgs e)
        {
            await RunSyncAsync(SyncMode.Partial, showToastOnSuccess: true);
        }

        // --- Auto-sync ao aparecer (parcial e silencioso)
        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (_didAutoSync) return;
            _didAutoSync = true;

            await RunSyncAsync(SyncMode.Partial, showToastOnSuccess: false);
        }

        // --- Botão "Sincronização Completa"
        private async void OnSyncAllClicked(object sender, EventArgs e)
        {
            if (IsBusy) return;

            bool confirmar = await DisplayAlert(
                "Sincronização completa",
                "Deseja realmente executar a sincronização completa? Este processo pode demorar alguns minutos.",
                "Sim", "Não");

            if (!confirmar) return;

            await RunSyncAsync(SyncMode.Full, showToastOnSuccess: true);
        }

        private async void OnSettingsClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(SettingsPage));
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(LoginPage));
        }

        private async void OnConsumerSaleClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync(nameof(ConsumerSalePage));
        }

        // ================= Núcleo unificado =================
        private async Task RunSyncAsync(SyncMode mode, bool showToastOnSuccess)
        {
            try
            {
                // 1) Checa login/token
                var user = _db.GetCollection<UserConfig>("user_config").FindById("CURRENT");
                if (user == null || string.IsNullOrEmpty(user.Token))
                    return; // não logado → não sincroniza

                if (IsBusy) return;
                IsBusy = true;

                bool ignoreLastChange = (mode == SyncMode.Full);

                // 2) Itera o mapa e chama o método genérico via reflection
                foreach (var (type, plural, singular) in _syncMap)
                {
                    await InvokeSyncEntitiesAsync(type, plural, singular, ignoreLastChange);
                }

                if (showToastOnSuccess)
                    await _toast.ShowToastAsync("Dados sincronizados com sucesso!");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro na sincronização", ex.Message, "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Chama _syncService.SyncEntitiesAsync<T>(plural, singular, ignoreLastChange) dinamicamente
        private Task InvokeSyncEntitiesAsync(Type entityType, string plural, string singular, bool ignoreLastChange)
        {
            // Obtém o método genérico
            var method = typeof(SyncService)
                .GetMethod("SyncEntitiesAsync", BindingFlags.Public | BindingFlags.Instance);

            if (method == null)
                throw new MissingMethodException("SyncService.SyncEntitiesAsync<T> não encontrado.");

            var generic = method.MakeGenericMethod(entityType);
            var taskObj = generic.Invoke(_syncService, new object[] { plural, singular, ignoreLastChange });

            return taskObj is Task t ? t : Task.CompletedTask;
        }
    }
}
