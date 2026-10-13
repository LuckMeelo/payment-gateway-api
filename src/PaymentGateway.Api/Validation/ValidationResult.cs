namespace PaymentGateway.Api.Validation;

/// <summary>
/// Represents the outcome of a validation pass.
/// Use the static factory methods <see cref="Valid"/> and <see cref="Invalid"/>
/// instead of constructing directly.
/// </summary>
public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    /// <summary>Returns a result indicating all fields passed validation.</summary>
    public static ValidationResult Valid() => new(true, Array.Empty<string>());

    /// <summary>Returns a result carrying one or more validation error messages.</summary>
    public static ValidationResult Invalid(IReadOnlyList<string> errors) => new(false, errors);
}