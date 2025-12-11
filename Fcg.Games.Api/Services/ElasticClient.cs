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

        // Try to derive base URI from settings; fall back to localhost:9200 when not configured or invalid
        Uri baseUri;
        try
        {
            baseUri = settings.GetBaseUri();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not derive ElasticSearch URI from settings; falling back to http://localhost:9200");
            baseUri = new Uri("http://localhost:9200");
        }

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
            _logger.LogWarning("Falhou em indexar o jogo {Id} no elastic: {Status} {Body}", game.Id, resp.StatusCode, text);
        }
    }

    // Delete a game document from the index
    public async Task DeleteGameAsync(Guid id)
    {
        if (id == Guid.Empty) return;
        var indexName = "games";
        try
        {
            var resp = await _httpClient.DeleteAsync($"/{indexName}/_doc/{id}");
            if (!resp.IsSuccessStatusCode)
            {
                var text = await resp.Content.ReadAsStringAsync();
                // 404 is fine (document already missing), log other failures
                if (resp.StatusCode != System.Net.HttpStatusCode.NotFound)
                    _logger.LogWarning("Falhou em deletar game {Id} no elastic: {Status} {Body}", id, resp.StatusCode, text);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro deletando jogo {Id} no elastic", id);
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
                _logger.LogWarning("Falhou em criar indice {Index}: {Status} {Body}", indexName, createRes.StatusCode, t);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro certificando indice");
        }
    }

    // Ensure a search-hits index exists with appropriate mapping (gameId as keyword, timestamp date)
    private async Task EnsureSearchHitsIndexAsync()
    {
        var indexName = "search-hits";
        try
        {
            var head = new HttpRequestMessage(HttpMethod.Head, $"/{indexName}");
            var res = await _httpClient.SendAsync(head);
            if (res.IsSuccessStatusCode) return;

            var mapping = new
            {
                mappings = new
                {
                    properties = new
                    {
                        gameId = new { type = "keyword" },
                        timestamp = new { type = "date" }
                    }
                }
            };

            var json = JsonSerializer.Serialize(mapping);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var createRes = await _httpClient.PutAsync($"/{indexName}", content);
            if (!createRes.IsSuccessStatusCode)
            {
                var t = await createRes.Content.ReadAsStringAsync();
                _logger.LogWarning("Falhou em criar indice {Index}: {Status} {Body}", indexName, createRes.StatusCode, t);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro certificando indice search-hits");
        }
    }

    // Log returned search hits into a dedicated index for analytics (uses bulk)
    public async Task LogSearchHitsAsync(IEnumerable<Guid> gameIds)
    {
        if (gameIds == null) return;
        var ids = gameIds.Where(g => g != Guid.Empty).ToList();
        if (!ids.Any()) return;

        await EnsureSearchHitsIndexAsync();

        var indexName = "search-hits";
        // Build NDJSON bulk payload
        var sb = new StringBuilder();
        var now = DateTime.UtcNow.ToString("o");
        foreach (var id in ids)
        {
            sb.AppendLine("{ \"index\":{} }");
            var doc = JsonSerializer.Serialize(new { gameId = id.ToString(), timestamp = now });
            sb.AppendLine(doc);
        }

        var content = new StringContent(sb.ToString(), Encoding.UTF8, "application/x-ndjson");
        try
        {
            var resp = await _httpClient.PostAsync($"/{indexName}/_bulk", content);
            if (!resp.IsSuccessStatusCode)
            {
                var t = await resp.Content.ReadAsStringAsync();
                _logger.LogWarning("Falha ao gravar search-hits no elastic: {Status} {Body}", resp.StatusCode, t);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro ao gravar search-hits");
        }
    }

    // Return top searched game ids with counts using terms aggregation on search-hits index
    public async Task<IEnumerable<(Guid GameId, long Count)>> GetTopSearchedGamesAsync(int size = 10)
    {
        var indexName = "search-hits";
        var request = new
        {
            size = 0,
            aggs = new
            {
                top_games = new
                {
                    terms = new { field = "gameId", size }
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
                _logger.LogWarning("Elastic aggregation failed: {Status} {Body}", resp.StatusCode, t);
                return Enumerable.Empty<(Guid, long)>();
            }

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("aggregations", out var aggs)) return Enumerable.Empty<(Guid, long)>();
            if (!aggs.TryGetProperty("top_games", out var top)) return Enumerable.Empty<(Guid, long)>();
            if (!top.TryGetProperty("buckets", out var buckets)) return Enumerable.Empty<(Guid, long)>();

            var results = new List<(Guid, long)>();
            foreach (var b in buckets.EnumerateArray())
            {
                if (b.TryGetProperty("key", out var key) && b.TryGetProperty("doc_count", out var dc))
                {
                    var keyStr = key.GetString();
                    if (Guid.TryParse(keyStr, out var gid) && dc.ValueKind == JsonValueKind.Number && dc.TryGetInt64(out var cnt))
                    {
                        results.Add((gid, cnt));
                    }
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro agregando search-hits");
            return Enumerable.Empty<(Guid, long)>();
        }
    }

    // Search games with fuzzy matching on title and description
    public async Task<IEnumerable<ElasticSearchHit>> SearchGamesAsync(string query, int size = 10)
    {
        var indexName = "games";
        if (string.IsNullOrWhiteSpace(query)) return Enumerable.Empty<ElasticSearchHit>();

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
                return Enumerable.Empty<ElasticSearchHit>();
            }

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("hits", out var hits)) return Enumerable.Empty<ElasticSearchHit>();
            if (!hits.TryGetProperty("hits", out var inner)) return Enumerable.Empty<ElasticSearchHit>();

            var results = new List<ElasticSearchHit>();
            foreach (var h in inner.EnumerateArray())
            {
                if (h.TryGetProperty("_source", out var src))
                {
                    double? score = null;
                    if (h.TryGetProperty("_score", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetDouble(out var sd))
                        score = sd;

                    results.Add(new ElasticSearchHit(src.Clone(), score));
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro procurando elastic");
            return Enumerable.Empty<ElasticSearchHit>();
        }
    }

    // Advanced search: Query DSL allowing search by name (multi_match) and optional genre filter
    public async Task<IEnumerable<ElasticSearchHit>> SearchGamesAdvancedAsync(string? name, int? genre, int size = 10)
    {
        var indexName = "games";
        if (string.IsNullOrWhiteSpace(name) && !genre.HasValue) return Enumerable.Empty<ElasticSearchHit>();

        // Build DSL: if name present use multi_match in must; if genre present add term filter
        object queryPart;
        if (!string.IsNullOrWhiteSpace(name) && genre.HasValue)
        {
            queryPart = new
            {
                @bool = new
                {
                    must = new object[] {
                        new {
                            multi_match = new {
                                query = name,
                                fields = new[] { "title^3", "description" },
                                fuzziness = "AUTO"
                            }
                        }
                    },
                    filter = new object[] {
                        new { term = new { genre = genre.Value } }
                    }
                }
            };
        }
        else if (!string.IsNullOrWhiteSpace(name))
        {
            queryPart = new
            {
                multi_match = new
                {
                    query = name,
                    fields = new[] { "title^3", "description" },
                    fuzziness = "AUTO"
                }
            };
        }
        else
        {
            // genre only
            queryPart = new
            {
                term = new { genre = genre.Value }
            };
        }

        var request = new
        {
            size,
            query = queryPart
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        try
        {
            var resp = await _httpClient.PostAsync($"/{indexName}/_search", content);
            if (!resp.IsSuccessStatusCode)
            {
                var t = await resp.Content.ReadAsStringAsync();
                _logger.LogWarning("Elastic advanced search failed: {Status} {Body}", resp.StatusCode, t);
                return Enumerable.Empty<ElasticSearchHit>();
            }

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("hits", out var hits)) return Enumerable.Empty<ElasticSearchHit>();
            if (!hits.TryGetProperty("hits", out var inner)) return Enumerable.Empty<ElasticSearchHit>();

            var results = new List<ElasticSearchHit>();
            foreach (var h in inner.EnumerateArray())
            {
                if (h.TryGetProperty("_source", out var src))
                {
                    double? score = null;
                    if (h.TryGetProperty("_score", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetDouble(out var sd))
                        score = sd;

                    results.Add(new ElasticSearchHit(src.Clone(), score));
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro procurando elastic (advanced)");
            return Enumerable.Empty<ElasticSearchHit>();
        }
    }

    // Busca aleatória por gênero usando random_score (retorna até 'size' hits)
    public async Task<IEnumerable<ElasticSearchHit>> GetRandomGamesByGenreAsync(int genre, int size = 5)
    {
        var indexName = "games";
        var request = new
        {
            size,
            query = new
            {
                function_score = new
                {
                    query = new
                    {
                        term = new { genre }
                    },
                    random_score = new { seed = DateTime.UtcNow.Ticks & 0x7FFFFFFF }
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
                _logger.LogWarning("Elastic random search failed: {Status} {Body}", resp.StatusCode, t);
                return Enumerable.Empty<ElasticSearchHit>();
            }

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("hits", out var hits)) return Enumerable.Empty<ElasticSearchHit>();
            if (!hits.TryGetProperty("hits", out var inner)) return Enumerable.Empty<ElasticSearchHit>();

            var results = new List<ElasticSearchHit>();
            foreach (var h in inner.EnumerateArray())
            {
                if (h.TryGetProperty("_source", out var src))
                {
                    double? score = null;
                    if (h.TryGetProperty("_score", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetDouble(out var sd))
                        score = sd;

                    results.Add(new ElasticSearchHit(src.Clone(), score));
                }
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro buscando jogos aleatórios por gênero");
            return Enumerable.Empty<ElasticSearchHit>();
        }
    }
}
