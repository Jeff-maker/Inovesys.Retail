using Inovesys.Retail.Entities;
using Inovesys.Retail.Models;
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Inovesys.Retail.Services;

public class SyncService
{
    private readonly HttpClient _http;
    private readonly LiteDbService _db;
    private const int PageSize = 100;

    public SyncService(IHttpClientFactory httpClientFactory, LiteDbService db)
    {
        _http = httpClientFactory.CreateClient("api"); // Usa o nome registrado no MauiProgram
        _db = db;
    }

    public async Task SyncEntitiesAsync<T>(
                                                string endpoint,           // ex.: $"{baseUrl}/ncms"
                                                string entityName,         // ex.: "NCM"
                                                bool ignoreLastChange = false) where T : class
    {
        // Última data sincronizada (UTC)
        
        DateTime? lastChange = ignoreLastChange ? null : _db.GetLastSyncDate(entityName)?.ToUniversalTime();

        // ClientId atual (se aplicável às entidades locais)
        var user = _db.GetCollection<UserConfig>("user_config").FindById("CURRENT")
                   ?? throw new Exception("ClientId não configurado.");
        var clientId = user.ClientId;

        // Monta a URL da 1ª chamada (NÃO use $skip/$top em server-driven paging)
        var urlBuilder = new StringBuilder();
        urlBuilder.Append(endpoint);
        urlBuilder.Append("?$orderby=LastChange asc"); // ordenação estável (Id desempata)

        // Expand opcional p/ tipos específicos
        if (typeof(T).Name == "Branche")
            urlBuilder.Append("&$expand=Address($expand=City($expand=State))");

        // Filtro por LastChange (formato OData v4 DateTimeOffset literal)
        if (lastChange.HasValue)
        {
            if (lastChange != null)
                urlBuilder.Append($"&$filter=LastChange gt {lastChange:O}");
        }

        // Só na primeira requisição pedimos a contagem total (opcional)
        urlBuilder.Append("&$count=true");

        string url = urlBuilder.ToString();
        int synced = 0;
        int? totalCount = null;

        // Loop até não haver @odata.nextLink
        while (!string.IsNullOrEmpty(url))
        {
            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                    throw new Exception("Não autenticado. Por favor, efetue um novo login.");

                var body = await resp.Content.ReadAsStringAsync();
                throw new Exception($"Erro ao sincronizar '{entityName}': {(int)resp.StatusCode} {resp.StatusCode}. {body}");
            }

            var odata = await resp.Content.ReadFromJsonAsync<ODataResponse<T>>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new ODataResponse<T>();
            var json = await resp.Content.ReadAsStringAsync();

            var items = odata.Value ?? new List<T>();

            // Guarda @odata.count na primeira página (se vier)
            if (!totalCount.HasValue && odata.Count.HasValue)
                totalCount = odata.Count;

            // Se a entidade tiver ClientId, injeta antes de salvar
            var clientIdProp = typeof(T).GetProperty("ClientId");
            if (clientIdProp is not null && clientIdProp.CanWrite)
            {
                foreach (var it in items)
                    clientIdProp.SetValue(it, clientId);
            }

            // Persiste
            if (items.Count > 0)
            {
                _db.SaveEntities(items);
                synced += items.Count;
            }

            // Prepara próxima página: siga @odata.nextLink (pode vir absoluta ou relativa)
            var next = odata.NextLink;
            if (string.IsNullOrWhiteSpace(next))
            {
                url = null; // acabou
            }
            else
            {
                // Normaliza: se vier relativa, combine com o host do endpoint
                if (Uri.IsWellFormedUriString(next, UriKind.Absolute))
                {
                    url = next;
                }
                else
                {
                    // Mantém o mesmo host/base do endpoint
                    var baseUri = new Uri(endpoint, UriKind.Absolute);
                    url = new Uri(baseUri, next).ToString();
                }
            }
        }

        // Atualiza o marcador de sincronização (use sempre o horário atual em UTC)
        _db.UpdateLastSyncDate(entityName);

        Console.WriteLine($"[{entityName}] Sincronizados: {synced}" + (totalCount is not null ? $" de {totalCount}" : ""));
    }


    //public async Task SyncEntitiesAsync<T>(string endpoint, string entityName, bool ignoreLastChange = false) where T : class
    //{

    //    if("" == entityName.ToLower())
    //    {
    //        // Preço de material é sempre sincronizado por completo
    //        var x = _db.GetLastSyncDate(entityName).Value;
    //    }
    //    DateTime? lastChange = ignoreLastChange
    //         ? null
    //         : _db.GetLastSyncDate(entityName); // cast implícito p/ nullable, sem .Value

    //    lastChange = lastChange?.ToUniversalTime();

    //    int skip = 0;

    //    bool hasMore = true;

    //    // 🔹 Obtem o ClientId atual do usuário
    //    var user = _db.GetCollection<UserConfig>("user_config").FindById("CURRENT");
    //    var clientId = user?.ClientId ?? throw new Exception("ClientId não configurado.");

    //    int? totalCount = null; // Opcional, para controle externo

    //    // 🔹 Inclui expand de Address se for Branche

    //    while (hasMore)
    //    {
    //        // Solicita contagem apenas na primeira chamada
    //        var url = $"{endpoint}?$orderby=LastChange asc&$skip={skip}";

    //        if (typeof(T).Name == "Branche")
    //            url += "&$expand=Address($expand=City($expand=State))";

    //        if (lastChange != null)
    //            url += $"&$filter=LastChange gt {lastChange:O}";

    //        if (skip == 0)
    //            url += "&$count=true";

    //        var response = await _http.GetAsync(url);

    //        if (!response.IsSuccessStatusCode)
    //        {
    //            if (response.StatusCode == HttpStatusCode.Unauthorized)
    //                throw new Exception("Não autenticado. Por favor, efetue um novo login.");

    //            throw new Exception($"Erro ao sincronizar '{entityName}': {response.StatusCode}.");
    //        }


    //        var odata = await response.Content.ReadFromJsonAsync<ODataResponse<T>>(new JsonSerializerOptions
    //        {
    //            PropertyNameCaseInsensitive = true
    //        }) ?? new ODataResponse<T>();

    //        var result = odata.Value ?? new List<T>();

    //        // Salva contagem total se estiver presente (útil para progress bar)
    //        if (skip == 0 && odata.Count.HasValue)
    //            totalCount = odata.Count;

    //        if (result.Count == 0)
    //            break;

    //        // 🔹 Aplica ClientId se existir na entidade
    //        var prop = typeof(T).GetProperty("ClientId");
    //        if (prop != null && prop.CanWrite)
    //        {
    //            foreach (var item in result)
    //                prop.SetValue(item, clientId);
    //        }

    //        _db.SaveEntities(result);

    //        skip += PageSize;
    //        hasMore = totalCount > skip;
    //    }

    //    //if (!ignoreLastChange)
    //    _db.UpdateLastSyncDate(entityName);

    //    // Opcional: exibir total sincronizado
    //    if (totalCount.HasValue)
    //        Console.WriteLine($"Total sincronizado para {entityName}: {totalCount}");
    //}

    // 🔸 Função auxiliar para copiar propriedades de Address para Branche
    private void CopyAddressToBranche(object branche, dynamic address)
    {
        var map = new (string targetProp, string sourceProp)[]
        {
        ("CountryId", "CountryId"),
        ("StateId", "StateId"),
        ("CityId", "CityId"),
        ("PostalCode", "PostalCode"),
        ("Neighborhood", "Neighborhood"),
        ("Street", "Street"),
        ("HouseNumber", "HouseNumber"),
        ("Complement", "Complement")
        };

        foreach (var (target, source) in map)
        {
            var targetProp = branche.GetType().GetProperty(target);
            var sourceProp = address.GetType().GetProperty(source);
            if (targetProp != null && sourceProp != null)
            {
                var value = sourceProp.GetValue(address);
                targetProp.SetValue(branche, value);
            }
        }
    }





}
