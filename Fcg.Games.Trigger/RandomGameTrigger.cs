using System.Net.Http.Headers;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Fcg.Games.Trigger;

public class RandomGameTrigger
{
    private readonly ILogger _logger;
    private readonly HttpClient _http;

    public RandomGameTrigger(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<RandomGameTrigger>();
        _http = new HttpClient();

        // Ler a variável de ambiente (suporta ambas as formas)
        var baseUrl = Environment.GetEnvironmentVariable("GAMES_API_BASE")
                      ?? Environment.GetEnvironmentVariable("games_api_base")
                      ?? "http://localhost:5000";

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            _logger.LogWarning("GAMES_API_BASE inválido: {BaseUrl} — fallback para http://localhost:5000", baseUrl);
            baseUri = new Uri("http://localhost:5000");
        }

        // Garantir que BaseAddress seja uma URI absoluta
        _http.BaseAddress = baseUri;

        // Token opcional para endpoints protegidos
        var token = Environment.GetEnvironmentVariable("SERVICE_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Function("RandomGameTrigger")]
    public async Task Run([TimerTrigger("0 */1 * * * *")] object timer) // every 1 minute
    {
        try
        {
            _logger.LogInformation("RandomGameTrigger fired at {Time}", DateTime.UtcNow);
            var resp = await _http.GetAsync("/api/games/random");
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("API returned {Status}", resp.StatusCode);
                return;
            }

            var payload = await resp.Content.ReadAsStringAsync();
            _logger.LogInformation("Random game: {Payload}", payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling games API");
        }
    }
}
