using Inovesys.Retail.Entities;
using Inovesys.Retail.Models;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
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

    public async Task SyncEntitiesAsync<T>(string endpoint, string entityName, bool ignoreLastChange = false) where T : class
    {
        
        if("materialprice" == entityName.ToLower())
        {
            // Preço de material é sempre sincronizado por completo
            var x = _db.GetLastSyncDate(entityName).Value;
        }
        DateTime? lastChange = ignoreLastChange ? null : _db.GetLastSyncDate(entityName).Value;
        
        int skip = 0;
        
        bool hasMore = true;

        // 🔹 Obtem o ClientId atual do usuário
        var user = _db.GetCollection<UserConfig>("user_config").FindById("CURRENT");
        var clientId = user?.ClientId ?? throw new Exception("ClientId não configurado.");

        int? totalCount = null; // Opcional, para controle externo

        // 🔹 Inclui expand de Address se for Branche
      
        while (hasMore)
        {
            // Solicita contagem apenas na primeira chamada
            var url = $"{endpoint}?$orderby=LastChange asc&$top={PageSize}&$skip={skip}";

            if (typeof(T).Name == "Branche")
                url += "&$expand=Address($expand=City($expand=State))";

            if (lastChange != null)
                url += $"&$filter=LastChange gt {lastChange:O}";

            if (skip == 0)
                url += "&$count=true";

            var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new Exception("Não autenticado. Por favor, efetue um novo login.");

                throw new Exception($"Erro ao sincronizar '{entityName}': {response.StatusCode}.");
            }


            var odata = await response.Content.ReadFromJsonAsync<ODataResponse<T>>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new ODataResponse<T>();

            var result = odata.Value ?? new List<T>();

            // Salva contagem total se estiver presente (útil para progress bar)
            if (skip == 0 && odata.Count.HasValue)
                totalCount = odata.Count;

            if (result.Count == 0)
                break;

            // 🔹 Aplica ClientId se existir na entidade
            var prop = typeof(T).GetProperty("ClientId");
            if (prop != null && prop.CanWrite)
            {
                foreach (var item in result)
                    prop.SetValue(item, clientId);
            }

            _db.SaveEntities(result);

            skip += PageSize;
            hasMore = result.Count == PageSize;
        }

        //if (!ignoreLastChange)
        _db.UpdateLastSyncDate(entityName);

        // Opcional: exibir total sincronizado
        if (totalCount.HasValue)
            Console.WriteLine($"Total sincronizado para {entityName}: {totalCount}");
    }

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
