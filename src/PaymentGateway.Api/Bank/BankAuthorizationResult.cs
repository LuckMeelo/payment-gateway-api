namespace PaymentGateway.Api.Bank;

public enum BankResponseStatus
{
    Authorized,
    Declined,
    Unavailable
}

public sealed record BankAuthorizationResult(BankResponseStatus Status, string? AuthorizationCode);