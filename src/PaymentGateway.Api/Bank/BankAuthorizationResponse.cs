namespace PaymentGateway.Api.Bank;

public sealed record BankAuthorizationResponse
{
    public bool Authorized { get; init; }
    public string? AuthorizationCode { get; init; }
}