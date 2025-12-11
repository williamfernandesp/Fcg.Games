using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Fcg.Games.Api.Services;

public class ElasticClientService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ElasticClientService> _logger;

    public ElasticClientService(ElasticSettings settings, ILogger<ElasticClientService> logger)
    {
        _logger = logger;

        var baseUri = settings.GetBaseUri();
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = baseUri;

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            // Elastic Cloud API key authentication: "Authorization: ApiKey {base64}"
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", settings.ApiKey);
        }
    }

    // Index a game document
    public async Task IndexGameAsync(Fcg.Games.Api.Models.Game game)
    {
        if (game == null) throw new ArgumentNullException(nameof(game));
        var indexName = "games";

        await EnsureIndexAsync(indexName);

        var doc = new {
            id = game.Id,
            title = game.Title,
            description = game.Description,
            price = game.Price,
            genre = game.Genre
        };

        var json = JsonSerializer.Serialize(doc);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await _httpClient.PutAsync($"/{indexName}/_doc/{game.Id}", content);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to index game {Id} in elastic: {Status} {Body}", game.Id, resp.StatusCode, text);
        }
    }

    private async Task EnsureIndexAsync(string indexName)
    {
        try
        {
            var head = new HttpRequestMessage(HttpMethod.Head, $"/{indexName}");
            var res = await _httpClient.SendAsync(head);
            if (res.IsSuccessStatusCode) return;

            // Create index with simple mappings
            var mapping = new
            {
                mappings = new
                {
                    properties = new
                    {
                        title = new { type = "text" },
                        description = new { type = "text" },
                        price = new { type = "double" },
                        genre = new { type = "integer" }
                    }
                }
            };

            var json = JsonSerializer.Serialize(mapping);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var createRes = await _httpClient.PutAsync($"/{indexName}", content);
            if (!createRes.IsSuccessStatusCode)
            {
                var t = await createRes.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to create index {Index}: {Status} {Body}", indexName, createRes.StatusCode, t);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error ensuring index");
        }
    }

    // Search games with fuzzy matching on title and description
    public async Task<IEnumerable<JsonElement>> SearchGamesAsync(string query, int size = 10)
    {
        var indexName = "games";
        if (string.IsNullOrWhiteSpace(query)) return Enumerable.Empty<JsonElement>();

        var request = new
        {
            size,
            query = new
            {
                multi_match = new
                {
                    query,
                    fields = new[] { "title^3", "description" },
                    fuzziness = "AUTO"
                }
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            var resp = await _httpClient.PostAsync($"/{indexName}/_search", content);
            if (!resp.IsSuccessStatusCode)
            {
                var t = await resp.Content.ReadAsStringAsync();
                _logger.LogWarning("Elastic search failed: {Status} {Body}", resp.StatusCode, t);
                return Enumerable.Empty<JsonElement>();
            }

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("hits", out var hits)) return Enumerable.Empty<JsonElement>();
            if (!hits.TryGetProperty("hits", out var inner)) return Enumerable.Empty<JsonElement>();

            var results = new List<JsonElement>();
            foreach (var h in inner.EnumerateArray())
            {
                if (h.TryGetProperty("_source", out var src)) results.Add(src);
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching elastic");
            return Enumerable.Empty<JsonElement>();
        }
    }
}
