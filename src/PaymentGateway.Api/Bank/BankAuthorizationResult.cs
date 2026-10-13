namespace PaymentGateway.Api.Bank;

/// <summary>
/// Outcome codes returned by the acquiring bank client.
/// Unavailable covers all failure cases (timeout, network error, non-2xx, bad JSON)
/// so callers only need to handle three states.
/// </summary>
public enum BankResponseStatus
{
    Authorized,
    Declined,
    Unavailable
}

/// <summary>
/// Typed result of a bank authorization attempt.
/// AuthorizationCode is only present when Status is Authorized.
/// </summary>
public sealed record BankAuthorizationResult(BankResponseStatus Status, string? AuthorizationCode);