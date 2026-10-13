namespace PaymentGateway.Api.Validation;

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ValidationResult Valid() => new(true, Array.Empty<string>());
    public static ValidationResult Invalid(IReadOnlyList<string> errors) => new(false, errors);
}