using System.Diagnostics.Metrics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Domain;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Observability;
using PaymentGateway.Api.Persistence;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Validation;

namespace PaymentGateway.Api.Tests;

public class PaymentServiceTests
{
    // Clock fixed at 2025-06-15 UTC
    // expiry tests relative to this date
    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero));

    private readonly IAcquiringBankClient _bankClient = Substitute.For<IAcquiringBankClient>();
    private readonly IPaymentsRepository _repository = Substitute.For<IPaymentsRepository>();
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        _service = new PaymentService(
            new PaymentRequestValidator(Clock),
            _bankClient,
            _repository,
            Substitute.For<ILogger<PaymentService>>());
    }

    private static PostPaymentRequest ValidRequest() => new()
    {
        CardNumber = "2222405343248877",
        ExpiryMonth = 12,
        ExpiryYear = 2030,
        Currency = "GBP",
        Amount = 100,
        Cvv = "123"
    };

    private void BankReturns(BankResponseStatus status, string? code = null) =>
        _bankClient
            .AuthorizeAsync(Arg.Any<BankAuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BankAuthorizationResult(status, code)));

    // Rejected (validation fails)

    [Fact]
    public async Task ProcessAsync_InvalidRequest_ReturnsRejected_DoesNotCallBankOrStore()
    {
        // Arrange — all-null request fails every validation rule
        var request = new PostPaymentRequest();

        // Act
        var result = await _service.ProcessAsync(request);

        // Assert
        Assert.Equal(PaymentOutcome.Rejected, result.Outcome);
        Assert.NotEmpty(result.Errors);
        await _bankClient.DidNotReceive()
            .AuthorizeAsync(Arg.Any<BankAuthorizationRequest>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().AddAsync(Arg.Any<Payment>());
    }

    // ── Authorized ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_BankAuthorizes_ReturnsAuthorizedAndStoresPayment()
    {
        // Arrange
        BankReturns(BankResponseStatus.Authorized, "auth-code-123");

        // Act
        var result = await _service.ProcessAsync(ValidRequest());

        // Assert
        Assert.Equal(PaymentOutcome.Authorized, result.Outcome);
        Assert.NotNull(result.Payment);
        Assert.Equal(PaymentStatus.Authorized, result.Payment.Status);
        await _repository.Received(1)
            .AddAsync(Arg.Is<Payment>(p => p.Status == PaymentStatus.Authorized));
    }

    // Declined

    [Fact]
    public async Task ProcessAsync_BankDeclines_ReturnsDeclinedAndStoresPayment()
    {
        // Arrange
        BankReturns(BankResponseStatus.Declined);

        // Act
        var result = await _service.ProcessAsync(ValidRequest());

        // Assert
        Assert.Equal(PaymentOutcome.Declined, result.Outcome);
        Assert.NotNull(result.Payment);
        Assert.Equal(PaymentStatus.Declined, result.Payment.Status);
        await _repository.Received(1)
            .AddAsync(Arg.Is<Payment>(p => p.Status == PaymentStatus.Declined));
    }

    // Bank unavailable

    [Fact]
    public async Task ProcessAsync_BankUnavailable_ReturnsBankUnavailable_DoesNotStore()
    {
        // Arrange
        BankReturns(BankResponseStatus.Unavailable);

        // Act
        var result = await _service.ProcessAsync(ValidRequest());

        // Assert
        Assert.Equal(PaymentOutcome.BankUnavailable, result.Outcome);
        await _repository.DidNotReceive().AddAsync(Arg.Any<Payment>());
    }

    // Security: only last four digits stored

    [Fact]
    public async Task ProcessAsync_StoresOnlyLastFourDigits()
    {
        // Arrange
        BankReturns(BankResponseStatus.Authorized, "code");
        var request = ValidRequest() with { CardNumber = "2222405343248877" };

        // Act
        await _service.ProcessAsync(request);

        // Assert
        await _repository.Received(1)
            .AddAsync(Arg.Is<Payment>(p => p.CardNumberLastFour == "8877"));
    }

    // Wire format: expiry date sent as MM/YYYY

    [Fact]
    public async Task ProcessAsync_SendsExpiryAsMmYyyy()
    {
        // Arrange — capture what is actually sent to the bank
        BankAuthorizationRequest? captured = null;
        _bankClient
            .AuthorizeAsync(
                Arg.Do<BankAuthorizationRequest>(r => captured = r),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BankAuthorizationResult(BankResponseStatus.Authorized, "code")));

        var request = ValidRequest() with { ExpiryMonth = 4, ExpiryYear = 2027 };

        // Act
        await _service.ProcessAsync(request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("04/2027", captured!.ExpiryDate);
    }

    // Metrics: payments.processed counter

    [Fact]
    public async Task ProcessAsync_Authorized_IncrementsPaymentsProcessedCounter()
    {
        // Arrange
        BankReturns(BankResponseStatus.Authorized, "code");

        long? measuredValue = null;
        string? statusTag = null;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == PaymentGatewayDiagnostics.ServiceName
                && instrument.Name == "payments.processed")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            measuredValue = value;
            foreach (var tag in tags)
            {
                if (tag.Key == "status")
                    statusTag = tag.Value?.ToString();
            }
        });
        listener.Start();

        // Act
        await _service.ProcessAsync(ValidRequest());

        // Assert counter incremented by 1, tagged with the outcome status
        Assert.Equal(1L, measuredValue);
        Assert.Equal("Authorized", statusTag);
    }
}