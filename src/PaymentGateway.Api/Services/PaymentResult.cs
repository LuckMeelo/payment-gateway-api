using PaymentGateway.Api.Domain;

namespace PaymentGateway.Api.Services;

public enum PaymentOutcome { Authorized, Declined, Rejected, BankUnavailable }

public sealed record PaymentResult
{
    public required PaymentOutcome Outcome { get; init; }
    public Payment? Payment { get; init; }                       // Authorized / Declined
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>(); // Rejected

    public static PaymentResult Authorized(Payment p) => new() { Outcome = PaymentOutcome.Authorized, Payment = p };
    public static PaymentResult Declined(Payment p) => new() { Outcome = PaymentOutcome.Declined, Payment = p };
    public static PaymentResult Rejected(IReadOnlyList<string> errors) => new() { Outcome = PaymentOutcome.Rejected, Errors = errors };
    public static PaymentResult BankUnavailable() => new() { Outcome = PaymentOutcome.BankUnavailable };
}