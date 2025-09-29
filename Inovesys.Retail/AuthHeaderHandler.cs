using System.Net.Http.Headers;
using Inovesys.Retail.Entities;
using Inovesys.Retail.Services;

public class AuthHeaderHandler : DelegatingHandler
{
    private readonly LiteDbService _db;

    public AuthHeaderHandler(LiteDbService dbService)
    {
        _db = dbService;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var col = _db.GetCollection<UserConfig>("user_config");
        var settings = col.FindById("CURRENT") ?? new UserConfig();
        if (settings != null && !string.IsNullOrWhiteSpace(settings.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
