using Microsoft.Extensions.Time.Testing;

using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Validation;

namespace PaymentGateway.Api.Tests;

public class PaymentRequestValidatorTests
{
    // Clock fixed at 2025-06-15 UTC — all expiry cases are relative to this date
    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero));

    private readonly PaymentRequestValidator _validator = new(Clock);

    private static PostPaymentRequest ValidRequest() => new()
    {
        CardNumber = "1234567890123456",
        ExpiryMonth = 12,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidRequest_ReturnsValid()
    {
        // Arrange
        var request = ValidRequest();

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    // ── Card number ───────────────────────────────────────────────────────────

    [Fact]
    public void Validate_NullCardNumber_ReturnsInvalid()
    {
        // Arrange
        var request = ValidRequest() with { CardNumber = null };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_CardNumberTooShort_ReturnsInvalid()
    {
        // Arrange — 13 digits (minimum is 14)
        var request = ValidRequest() with { CardNumber = "1234567890123" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_CardNumberTooLong_ReturnsInvalid()
    {
        // Arrange — 20 digits (maximum is 19)
        var request = ValidRequest() with { CardNumber = "12345678901234567890" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_CardNumberWithNonDigits_ReturnsInvalid()
    {
        // Arrange — 14 chars but contains letters
        var request = ValidRequest() with { CardNumber = "1234abcd567890" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_CardNumber14Digits_ReturnsValid()
    {
        // Arrange — lower boundary
        var request = ValidRequest() with { CardNumber = "12345678901234" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_CardNumber19Digits_ReturnsValid()
    {
        // Arrange — upper boundary
        var request = ValidRequest() with { CardNumber = "1234567890123456789" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
    }

    // ── Expiry month ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_NullExpiryMonth_ReturnsInvalid()
    {
        // Arrange
        var request = ValidRequest() with { ExpiryMonth = null };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_ExpiryMonthZero_ReturnsInvalid()
    {
        // Arrange
        var request = ValidRequest() with { ExpiryMonth = 0 };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_ExpiryMonthThirteen_ReturnsInvalid()
    {
        // Arrange
        var request = ValidRequest() with { ExpiryMonth = 13 };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    // ── Expiry date (combined month + year) ───────────────────────────────────

    [Fact]
    public void Validate_NullExpiryYear_ReturnsInvalid()
    {
        // Arrange
        var request = ValidRequest() with { ExpiryYear = null };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_ExpiredCard_ReturnsInvalid()
    {
        // Arrange — expired end of May 2025; clock is 2025-06-15
        var request = ValidRequest() with { ExpiryMonth = 5, ExpiryYear = 2025 };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_CardExpiringThisMonth_ReturnsValid()
    {
        // Arrange — expires June 2025; clock is 2025-06-15 (valid until end of month)
        var request = ValidRequest() with { ExpiryMonth = 6, ExpiryYear = 2025 };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_FutureCard_ReturnsValid()
    {
        // Arrange
        var request = ValidRequest() with { ExpiryMonth = 12, ExpiryYear = 2030 };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
    }

    // ── Currency ──────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_NullCurrency_ReturnsInvalid()
    {
        // Arrange
        var request = ValidRequest() with { Currency = null };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_UnsupportedCurrency_ReturnsInvalid()
    {
        // Arrange
        var request = ValidRequest() with { Currency = "JPY" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_LowercaseCurrency_ReturnsInvalid()
    {
        // Arrange — GBP is valid but only uppercase is accepted
        var request = ValidRequest() with { Currency = "gbp" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    // ── Amount ────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_NullAmount_ReturnsInvalid()
    {
        // Arrange
        var request = ValidRequest() with { Amount = null };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_ZeroAmount_ReturnsInvalid()
    {
        // Arrange
        var request = ValidRequest() with { Amount = 0 };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_NegativeAmount_ReturnsInvalid()
    {
        // Arrange
        var request = ValidRequest() with { Amount = -1 };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    // ── CVV ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_NullCvv_ReturnsInvalid()
    {
        // Arrange
        var request = ValidRequest() with { Cvv = null };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_CvvTooShort_ReturnsInvalid()
    {
        // Arrange — 2 digits (minimum is 3)
        var request = ValidRequest() with { Cvv = "12" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_CvvTooLong_ReturnsInvalid()
    {
        // Arrange — 5 digits (maximum is 4)
        var request = ValidRequest() with { Cvv = "12345" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_CvvWithNonDigits_ReturnsInvalid()
    {
        // Arrange
        var request = ValidRequest() with { Cvv = "12a" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_Cvv3Digits_ReturnsValid()
    {
        // Arrange — lower boundary
        var request = ValidRequest() with { Cvv = "123" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Cvv4Digits_ReturnsValid()
    {
        // Arrange — upper boundary
        var request = ValidRequest() with { Cvv = "1234" };

        // Act
        var result = _validator.Validate(request);

        // Assert
        Assert.True(result.IsValid);
    }
}