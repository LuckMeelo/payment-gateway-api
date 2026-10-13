using PaymentGateway.Api.Models.Requests;

namespace PaymentGateway.Api.Validation;

public sealed class PaymentRequestValidator(TimeProvider timeProvider)
{
    private static readonly HashSet<string> AllowedCurrencies =
        new(StringComparer.Ordinal) { "GBP", "EUR", "USD" };

    public ValidationResult Validate(PostPaymentRequest request)
    {
        var errors = new List<string>();

        // Card number
        if (request.CardNumber is null)
            errors.Add("card_number is required.");
        else if (request.CardNumber.Length < 14 || request.CardNumber.Length > 19)
            errors.Add("card_number must be 14–19 digits.");
        else if (request.CardNumber.Any(c => c is < '0' or > '9'))
            errors.Add("card_number must contain digits only.");

        // Expiry month
        if (request.ExpiryMonth is null)
            errors.Add("expiry_month is required.");
        else if (request.ExpiryMonth < 1 || request.ExpiryMonth > 12)
            errors.Add("expiry_month must be between 1 and 12.");

        // Expiry year
        if (request.ExpiryYear is null)
            errors.Add("expiry_year is required.");

        // Combined expiry: card is valid through end of expiry month
        if (request.ExpiryMonth is >= 1 and <= 12 && request.ExpiryYear is not null)
        {
            var endOfExpiry = new DateTimeOffset(
                request.ExpiryYear.Value, request.ExpiryMonth.Value, 1,
                0, 0, 0, TimeSpan.Zero).AddMonths(1);

            if (timeProvider.GetUtcNow() >= endOfExpiry)
                errors.Add("Card has expired.");
        }

        // Currency
        if (request.Currency is null)
            errors.Add("currency is required.");
        else if (!AllowedCurrencies.Contains(request.Currency))
            errors.Add("currency must be GBP, EUR, or USD.");

        // Amount
        if (request.Amount is null)
            errors.Add("amount is required.");
        else if (request.Amount <= 0)
            errors.Add("amount must be greater than 0.");

        // CVV
        if (request.Cvv is null)
            errors.Add("cvv is required.");
        else if (request.Cvv.Length < 3 || request.Cvv.Length > 4)
            errors.Add("cvv must be 3–4 digits.");
        else if (request.Cvv.Any(c => c is < '0' or > '9'))
            errors.Add("cvv must contain digits only.");

        return errors.Count == 0
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(errors);
    }
}