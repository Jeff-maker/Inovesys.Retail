using System.IdentityModel.Tokens.Jwt;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inovesys.Retail.Entities;
using Inovesys.Retail.Services;

namespace Inovesys.Retail.Pages;
#pragma warning disable CS8632 // A anotação para tipos de referência anuláveis deve ser usada apenas em código em um contexto de anotações '#nullable'.
public partial class LoginPage : ContentPage
{
    private readonly HttpClient _http;
    private readonly LiteDbService _db;

    private string? _lastPassword;

    private List<ClientDto>? _tenants;

    private readonly ToastService _toast;

    public LoginPage(HttpClient http, LiteDbService db, ToastService toastService)
    {
        InitializeComponent();
        _http = http;
        _db = db;
        LoadUserSettings();
        _toast = toastService;
    }

    private void LoadUserSettings()
    {
        var col = _db.GetCollection<UserConfig>("user_config");
        var settings = col.FindById("CURRENT") ?? new UserConfig();
        entryUser.Text = settings.Email;
    }

    private async void OnConnectClicked(object sender, EventArgs e)
    {
        await Autenticar(entryUser.Text, entryPassword.Text);
    }

    private async Task Autenticar(string email, string senha, int? clientId = null)
    {
        loading.IsVisible = true;
        loading.IsRunning = true;
        lblError.IsVisible = false;

        var loginData = new
        {
            User = email.ToLower(),
            Password = senha,
            ClientId = clientId
        };

        var content = new StringContent(JsonSerializer.Serialize(loginData), Encoding.UTF8, "application/json");

        try
        {
            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("x-app-key", "v4KXh7mP2zTq@9rLu!38#yN1cFbXzKpA$gHdLsMqR5eWjYoTbC");

            var response = await _http.PostAsync("https://auth.inovesys.com.br/accesstoken", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                lblError.Text = "Usuário ou senha inválidos.";
                lblError.IsVisible = true;
                return;
            }

            var result = JsonSerializer.Deserialize<AuthResult>(responseBody);

            if (result?.accessToken != null)
            {
                // Aqui extrai os tenants do token
                var tenants = ExtractClientsFromToken(result.accessToken);

                if (tenants.Count > 1 )
                {
                    _lastPassword = senha;
                    _tenants = tenants;
                    tenantPicker.ItemsSource = _tenants;
                    tenantPicker.SelectedIndex = 0;
                    tenantPicker.IsVisible = true;
                    btnConfirmTenant.IsVisible = true;
                    btnConnect.IsVisible = false;
                    return;
                }else
                {

                }

                var selectClient = tenants.FirstOrDefault().Id;
                if (selectClient > 0)
                {
                    SaveAuth(result, email ?? string.Empty, selectClient);
                    //await DisplayAlert("Sucesso", "Login realizado com sucesso!", "OK");
                    await _toast.ShowToastAsync("Login realizado com sucesso!");
                    await Navigation.PopAsync();
                }
                
            }
        }
        catch (Exception ex)
        {
            lblError.Text = "Erro: " + ex.Message;
            lblError.IsVisible = true;
        }
        finally
        {
            loading.IsVisible = false;
            loading.IsRunning = false;
        }
    }

    private async void OnConfirmTenantClicked(object sender, EventArgs e)
    {
        if (tenantPicker.SelectedItem is not ClientDto selected)
        {
            await DisplayAlert("Erro", "Selecione um tenant válido.", "OK");
            return;
        }

        await Autenticar(entryUser.Text, _lastPassword, selected.Id);
    }

    private void SaveAuth(AuthResult auth, string email, int clientId)
    {
        var col = _db.GetCollection<UserConfig>("user_config");
        var existing = col.FindById("CURRENT");

        if (existing != null)
        {
            // Atualiza apenas os campos alteráveis
            existing.Token = auth.accessToken;
            existing.Email = email;
            existing.LastLogin = DateTime.UtcNow;
            existing.ClientId = clientId;

            col.Update(existing);
        }
        else
        {
            var config = new UserConfig
            {
                Id = "CURRENT",
                Token = auth.accessToken,
                Email = email,
                LastLogin = DateTime.UtcNow,
                ClientId = clientId
            };

            col.Insert(config);
        }
    }


    private List<ClientDto> ExtractClientsFromToken(string jwt)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.ReadJwtToken(jwt);

        var clientClaims = token.Claims
            .Where(c => c.Type == "client")
            .Select(c => c.Value)
            .ToList();

        var clients = new List<ClientDto>();

        foreach (var json in clientClaims)
        {
            try
            {
                var client = JsonSerializer.Deserialize<ClientDto>(json);
                if (client != null)
                    clients.Add(client);
            }
            catch { /* ignorar erro de parsing */ }
        }

        return clients;
    }



}

public class AuthResult
{
    [JsonPropertyName("accessToken")]
    public string accessToken { get; set; } = string.Empty;

    [JsonPropertyName("temporaryPassword")]
    public bool temporaryPassword { get; set; }

}

public class ClientDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
#pragma warning restore CS8632 // A anotação para tipos de referência anuláveis deve ser usada apenas em código em um contexto de anotações '#nullable'.