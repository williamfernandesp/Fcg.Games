using System.Net.Http.Json;

namespace Fcg.Games.Api.Services;

public class PaymentClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PaymentClient> _logger;

    public PaymentClient(HttpClient httpClient, ILogger<PaymentClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    private record PaymentResponse(Guid TransactionId, string Status);

    public async Task<(bool Success, Guid? TransactionId, string? Error)> ProcessPaymentAsync(Guid userId, decimal amount)
    {
        try
        {
            var req = new { UserId = userId, Amount = amount };
            var res = await _httpClient.PostAsJsonAsync("/api/payments", req);
            if (!res.IsSuccessStatusCode)
            {
                var text = await res.Content.ReadAsStringAsync();
                return (false, null, text);
            }

            var body = await res.Content.ReadFromJsonAsync<PaymentResponse>();
            return (true, body?.TransactionId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling payments");
            return (false, null, ex.Message);
        }
    }
}
