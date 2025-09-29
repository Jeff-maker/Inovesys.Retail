namespace Inovesys.Retail.Services;

public class UserSettings
{
    public string Token { get; set; } = string.Empty;
    public List<string> Clients { get; set; } = new();
}
