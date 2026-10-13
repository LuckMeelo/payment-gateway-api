using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Validation;

/// <summary>
/// Validates an incoming <see cref="PostPaymentRequest"/> against the payment gateway
/// business rules. All fields are validated before returning so the caller receives
/// a complete list of errors in a single response rather than one error at a time.
/// </summary>
public sealed class PaymentRequestValidator
{
    // Only these three ISO-4217 currency codes are accepted by this gateway.
    // StringComparer.Ordinal is used because currency codes are always uppercase ASCII;
    // ordinal comparison is the fastest option and avoids locale-specific case folding.
    private static readonly HashSet<string> AllowedCurrencies =
        new(StringComparer.Ordinal) { "GBP", "EUR", "USD" };

    private readonly TimeProvider _timeProvider;

    public PaymentRequestValidator(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Validates all fields of the request and returns a result that contains
    /// every validation error found, or an empty error list on success.
    /// </summary>
    public ValidationResult Validate(PostPaymentRequest request)
    {
        var errors = new List<string>();

        // Card number: required, digits only, 14 to 19 characters.
        // No Luhn check is performed here; that is the acquiring bank's responsibility.
        if (request.CardNumber is null)
            errors.Add("card_number is required.");
        else if (request.CardNumber.Length < 14 || request.CardNumber.Length > 19)
            errors.Add("card_number must be 14-19 digits.");
        else if (request.CardNumber.Any(c => c is < '0' or > '9'))
            errors.Add("card_number must contain digits only.");

        // Expiry month: must be a valid calendar month (1 through 12).
        if (request.ExpiryMonth is null)
            errors.Add("expiry_month is required.");
        else if (request.ExpiryMonth < 1 || request.ExpiryMonth > 12)
            errors.Add("expiry_month must be between 1 and 12.");

        // Expiry year: presence-only check here; the future-date check is done below
        // once we know both month and year are present and the month is in range.
        if (request.ExpiryYear is null)
            errors.Add("expiry_year is required.");

        // Combined expiry check: a card is valid until the end of its expiry month.
        // We calculate the first moment of the following month in UTC and compare against
        // the injected TimeProvider so tests can control the clock without touching real time.
        if (request.ExpiryMonth is >= 1 and <= 12 && request.ExpiryYear is not null)
        {
            var endOfExpiry = new DateTimeOffset(
                request.ExpiryYear.Value, request.ExpiryMonth.Value, 1,
                0, 0, 0, TimeSpan.Zero).AddMonths(1);

            if (_timeProvider.GetUtcNow() >= endOfExpiry)
                errors.Add("Card has expired.");
        }

        // Currency: must be one of the allowed ISO-4217 codes, uppercase only.
        // Lowercase or mixed-case values are rejected to keep the contract strict.
        if (request.Currency is null)
            errors.Add("currency is required.");
        else if (!AllowedCurrencies.Contains(request.Currency))
            errors.Add("currency must be GBP, EUR, or USD.");

        // Amount: positive integer in minor units (pence for GBP, cents for USD/EUR).
        if (request.Amount is null)
            errors.Add("amount is required.");
        else if (request.Amount <= 0)
            errors.Add("amount must be greater than 0.");

        // CVV: 3 digits for Visa and Mastercard, 4 digits for American Express.
        if (request.Cvv is null)
            errors.Add("cvv is required.");
        else if (request.Cvv.Length < 3 || request.Cvv.Length > 4)
            errors.Add("cvv must be 3-4 digits.");
        else if (request.Cvv.Any(c => c is < '0' or > '9'))
            errors.Add("cvv must contain digits only.");

        return errors.Count == 0
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(errors);
    }
}