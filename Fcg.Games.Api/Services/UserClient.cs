using System.Net.Http.Json;

namespace Fcg.Games.Api.Services;

public class UserClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UserClient> _logger;

    public UserClient(HttpClient httpClient, ILogger<UserClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<(bool Success, string? Error)> AddGameToUserLibraryAsync(Guid userId, Guid gameId)
    {
        try
        {
            var req = new { UserId = userId, GameId = gameId };
            var res = await _httpClient.PostAsJsonAsync($"/api/users/{userId}/library", req);
            if (!res.IsSuccessStatusCode)
            {
                var text = await res.Content.ReadAsStringAsync();
                return (false, text);
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling users");
            return (false, ex.Message);
        }
    }
}
