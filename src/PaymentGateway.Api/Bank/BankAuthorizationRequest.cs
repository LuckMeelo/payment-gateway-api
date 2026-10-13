namespace PaymentGateway.Api.Bank;

/// <summary>
/// Request body sent to the acquiring bank's POST /payments endpoint.
/// Property names are serialized as snake_case to match the bank's wire format
/// (e.g. CardNumber becomes card_number in the JSON body).
/// </summary>
public sealed record BankAuthorizationRequest
{
    /// <summary>Full card number as a digit string. Never stored after this call.</summary>
    public required string CardNumber { get; init; }

    /// <summary>Card expiry in "MM/YYYY" format as required by the bank API.</summary>
    public required string ExpiryDate { get; init; }

    /// <summary>ISO-4217 currency code (e.g. "GBP").</summary>
    public required string Currency { get; init; }

    /// <summary>Payment amount in minor units (e.g. pence for GBP).</summary>
    public required int Amount { get; init; }

    /// <summary>Card verification value. Never stored after this call.</summary>
    public required string Cvv { get; init; }
}