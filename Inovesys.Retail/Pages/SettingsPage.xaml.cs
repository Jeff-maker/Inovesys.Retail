using Inovesys.Retail.Entities;
using Inovesys.Retail.Services;
using System.Net;

namespace Inovesys.Retail.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly LiteDbService _db;
    private UserConfig settings = null!;
    private readonly HttpClient _http;

    public SettingsPage(LiteDbService db, IHttpClientFactory httpClientFactory)
    {
        InitializeComponent();
        _db = db;
        _http = httpClientFactory.CreateClient("api"); // Usa o nome registrado no MauiProgram
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadUserSettings();
        LoadBranches();
    }

    private void LoadBranches()
    {
        var collection = _db.GetCollection<Branche>("branche");

        var branches = collection.FindAll()
            .OrderBy(b => b.CompanyId)
            .ThenBy(b => b.Id)
            .ToList();

        pickerDefaultPlant.ItemsSource = branches;
        pickerDefaultPlant.ItemDisplayBinding = new Binding("Display");

        if (!string.IsNullOrEmpty(settings.DefaultCompanyId) && !string.IsNullOrEmpty(settings.DefaultBranche))
        {
            var selected = branches.FirstOrDefault(b =>
                b.CompanyId == settings.DefaultCompanyId && b.Id == settings.DefaultBranche);

            if (selected != null)
                pickerDefaultPlant.SelectedItem = selected;
        }
    }

    private void LoadUserSettings()
    {
        var col = _db.GetCollection<UserConfig>("user_config");
        settings = col.FindById("CURRENT") ?? new UserConfig();

        // Usuário / PDV / NF
        entryUser.Text = settings.Email ?? string.Empty;
        entryPdvSerie.Text = settings.SeriePDV ?? string.Empty;
        entryFirstInvoiceNumber.Text = settings.FirstInvoiceNumber ?? string.Empty;

        // Impressora
        entryPrinterName.Text = string.IsNullOrWhiteSpace(settings.PrinterName)
            ? "MP-4200 TH"
            : settings.PrinterName;

        entryPrinterIp.Text = string.IsNullOrWhiteSpace(settings.PrinterIp)
            ? "127.0.0.1"
            : settings.PrinterIp;

        entryPrinterPort.Text = (settings.PrinterPort > 0 && settings.PrinterPort <= 65535
            ? settings.PrinterPort
            : 9100).ToString();
    }
    private void OnClearClicked(object sender, EventArgs e)
    {
        var col = _db.GetCollection<UserConfig>("user_config");
        col.Delete("CURRENT");
        LoadUserSettings();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        // Planta padrão
        if (pickerDefaultPlant.SelectedItem is Branche selected)
        {
            settings.DefaultCompanyId = selected.CompanyId;
            settings.DefaultBranche = selected.Id;
        }

        // E-mail/usuário
        if (!string.IsNullOrWhiteSpace(entryUser.Text))
            settings.Email = entryUser.Text.Trim();

        // Série do PDV (opcional, se você usa)
        if (!string.IsNullOrWhiteSpace(entryPdvSerie.Text))
        {
            var serie = entryPdvSerie.Text.Trim();
            if (!serie.All(char.IsDigit) || serie.Length is < 1 or > 3)
            {
                await DisplayAlert("Erro", "A série do PDV deve ter de 1 a 3 dígitos numéricos.", "OK");
                return;
            }
            settings.SeriePDV = serie;
        }

        // Número inicial da NF
        var numeroInicial = entryFirstInvoiceNumber.Text?.Trim();
        if (!string.IsNullOrEmpty(numeroInicial))
        {
            if (numeroInicial.Length != 9 || !numeroInicial.All(char.IsDigit))
            {
                await DisplayAlert("Erro", "O número inicial da nota fiscal deve conter exatamente 9 dígitos numéricos.", "OK");
                return;
            }
            settings.FirstInvoiceNumber = numeroInicial;
        }

        // -------- Impressora (Serviço 9100) --------
        // Nome da impressora no Windows
        var printerName = entryPrinterName.Text?.Trim();
        settings.PrinterName = string.IsNullOrWhiteSpace(printerName) ? "MP-4200 TH" : printerName!;

        // IP do serviço
        var printerIp = entryPrinterIp.Text?.Trim();
        if (string.IsNullOrWhiteSpace(printerIp))
            printerIp = "127.0.0.1";
        else if (!IPAddress.TryParse(printerIp, out _))
        {
            await DisplayAlert("Erro", "IP da impressora/serviço inválido.", "OK");
            return;
        }
        settings.PrinterIp = printerIp;

        // Porta do serviço
        var portText = entryPrinterPort.Text?.Trim();
        if (!int.TryParse(portText, out var port) || port <= 0 || port > 65535)
        {
            await DisplayAlert("Erro", "Porta inválida. Use um número entre 1 e 65535 (padrão 9100).", "OK");
            return;
        }
        settings.PrinterPort = port;
        // -------------------------------------------

        // Persiste
        var col = _db.GetCollection<UserConfig>("user_config");
        col.Upsert(settings);

        await DisplayAlert("Sucesso", "Configurações salvas.", "OK");
    }
    private async void OnReserveSerieClicked(object sender, EventArgs e)
    {
        var serie = entryPdvSerie.Text?.Trim();


        // Verifica se já existe uma série registrada localmente
        if (!string.IsNullOrWhiteSpace(settings.SeriePDV))
        {
            if (settings.SeriePDV == serie)
            {
                await DisplayAlert("Aviso", $"A série {serie} já foi registrada neste PDV.", "OK");
                return;
            }
            else
            {
                await DisplayAlert("Aviso", $"Este PDV já possui a série {settings.SeriePDV} registrada. Não é possível registrar a série {serie}.", "OK");
                return;
            }
        }

        if (!int.TryParse(serie, out int numero) || numero < 1 || numero > 999)
        {
            await DisplayAlert("Aviso", "Informe uma série numérica entre 1 e 999.", "OK");
            return;
        }

        if (pickerDefaultPlant.SelectedItem is not Branche selected)
        {
            await DisplayAlert("Aviso", "Selecione a planta padrão antes de reservar.", "OK");
            return;
        }

        var url = $"invoice-number-control/register-serie?companyId={selected.CompanyId}&brancheId={selected.Id}&serie={serie}";

        try
        {
            var response = await _http.PostAsync(url, null);
            if (response.IsSuccessStatusCode)
            {
                settings.SeriePDV = serie;
                var col = _db.GetCollection<UserConfig>("user_config");
                col.Upsert(settings);

                await DisplayAlert("Sucesso", $"Série {serie} registrada com sucesso!", "OK");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                await DisplayAlert("Erro", $"Erro ao registrar a série: {error}", "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", $"Falha na comunicação com o servidor: {ex.Message}", "OK");
        }
    }


    private bool ReservarSerieDoPdv(string serie)
    {
        // Aqui você pode integrar com API, banco local etc.
        return true; // mock
    }

}
