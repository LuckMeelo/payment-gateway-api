namespace PaymentGateway.Api.Bank;

/// <summary>
/// Abstraction over the acquiring bank's authorization API.
/// Decoupling through this interface lets tests substitute the bank without
/// starting a real HTTP server.
/// </summary>
public interface IAcquiringBankClient
{
    /// <summary>
    /// Requests authorization for a payment from the acquiring bank.
    /// Implementations must never throw for expected failure cases (network errors,
    /// non-2xx responses, timeouts); those must be returned as
    /// <see cref="BankResponseStatus.Unavailable"/>.
    /// </summary>
    Task<BankAuthorizationResult> AuthorizeAsync(
        BankAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}