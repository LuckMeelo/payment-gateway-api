namespace PaymentGateway.Api.Bank;

public sealed record BankAuthorizationRequest
{
    public required string CardNumber { get; init; }
    public required string ExpiryDate { get; init; }    // "MM/YYYY"
    public required string Currency { get; init; }
    public required int Amount { get; init; }
    public required string Cvv { get; init; }
}