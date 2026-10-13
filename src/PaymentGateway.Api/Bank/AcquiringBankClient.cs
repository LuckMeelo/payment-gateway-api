using System.Text.Json;

namespace PaymentGateway.Api.Bank;

public sealed class AcquiringBankClient(HttpClient httpClient, ILogger<AcquiringBankClient> logger) : IAcquiringBankClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task<BankAuthorizationResult> AuthorizeAsync(
        BankAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("payments", request, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Bank returned {StatusCode}", (int)response.StatusCode);
                return new BankAuthorizationResult(BankResponseStatus.Unavailable, null);
            }

            var bankResponse = await response.Content
                .ReadFromJsonAsync<BankAuthorizationResponse>(JsonOptions, cancellationToken);

            if (bankResponse is null)
            {
                logger.LogWarning("Bank returned null response body");
                return new BankAuthorizationResult(BankResponseStatus.Unavailable, null);
            }

            return bankResponse.Authorized
                ? new BankAuthorizationResult(BankResponseStatus.Authorized, bankResponse.AuthorizationCode)
                : new BankAuthorizationResult(BankResponseStatus.Declined, null);
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or JsonException)
        {
            logger.LogWarning(ex, "Bank call failed");
            return new BankAuthorizationResult(BankResponseStatus.Unavailable, null);
        }
    }
}