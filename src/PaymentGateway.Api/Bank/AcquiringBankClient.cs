using System.Text.Json;

namespace PaymentGateway.Api.Bank;

/// <summary>
/// HTTP client for the acquiring bank's authorization endpoint.
/// Wraps all network and protocol failures so that callers always receive
/// a typed <see cref="BankAuthorizationResult"/> and never need to handle exceptions.
/// </summary>
public sealed class AcquiringBankClient : IAcquiringBankClient
{
    // The bank API uses snake_case JSON property names (e.g. card_number, expiry_date).
    // This options instance is shared across all calls; JsonSerializerOptions is thread-safe.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<AcquiringBankClient> _logger;

    public AcquiringBankClient(HttpClient httpClient, ILogger<AcquiringBankClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Sends an authorization request to the acquiring bank and returns a typed result.
    /// Never throws; all failure cases are returned as <see cref="BankResponseStatus.Unavailable"/>.
    /// </summary>
    public async Task<BankAuthorizationResult> AuthorizeAsync(
        BankAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Sending authorization request to acquiring bank");

        try
        {
            // PostAsJsonAsync serializes the request body, sets Content-Type: application/json,
            // and resolves the relative path "payments" against the configured BaseAddress.
            var response = await _httpClient.PostAsJsonAsync(
                "payments", request, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Any non-2xx status from the bank is treated as unavailable.
                // POST requests are never retried because the bank may have partially
                // processed the payment and retrying could cause a duplicate charge.
                _logger.LogWarning(
                    "Bank returned non-success status {StatusCode}", (int)response.StatusCode);
                return new BankAuthorizationResult(BankResponseStatus.Unavailable, null);
            }

            var bankResponse = await response.Content
                .ReadFromJsonAsync<BankAuthorizationResponse>(JsonOptions, cancellationToken);

            if (bankResponse is null)
            {
                // A 2xx with a null or unparseable body is unexpected behavior from the bank.
                _logger.LogWarning("Bank returned a null or empty response body");
                return new BankAuthorizationResult(BankResponseStatus.Unavailable, null);
            }

            var status = bankResponse.Authorized
                ? BankResponseStatus.Authorized
                : BankResponseStatus.Declined;

            _logger.LogDebug("Bank responded with authorization status {BankStatus}", status);

            return new BankAuthorizationResult(status, bankResponse.AuthorizationCode);
        }
        catch (TaskCanceledException ex)
        {
            // Thrown when HttpClient.Timeout elapses or the caller's CancellationToken is cancelled.
            // We log the message only; the full exception object is not logged to avoid
            // large stack traces appearing in the structured log output for a routine timeout.
            _logger.LogWarning("Bank request timed out or was cancelled: {Message}", ex.Message);
            return new BankAuthorizationResult(BankResponseStatus.Unavailable, null);
        }
        catch (HttpRequestException ex)
        {
            // Thrown on connection failures, DNS resolution errors, or transport-level problems.
            _logger.LogWarning("Bank request failed due to a network error: {Message}", ex.Message);
            return new BankAuthorizationResult(BankResponseStatus.Unavailable, null);
        }
        catch (JsonException ex)
        {
            // Thrown when the bank returns a 2xx but with a body that does not match
            // the expected BankAuthorizationResponse schema.
            _logger.LogWarning("Bank response could not be deserialized: {Message}", ex.Message);
            return new BankAuthorizationResult(BankResponseStatus.Unavailable, null);
        }
    }
}