using System.Diagnostics;

using Microsoft.Extensions.Logging;

using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Domain;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Observability;
using PaymentGateway.Api.Persistence;
using PaymentGateway.Api.Validation;

namespace PaymentGateway.Api.Services;

/// <summary>
/// Orchestrates the full payment processing flow: validate the request,
/// call the acquiring bank, persist the result, and return a typed outcome.
/// This class contains all business logic; the controller is kept thin.
/// </summary>
public sealed class PaymentService
{
    private readonly PaymentRequestValidator _validator;
    private readonly IAcquiringBankClient _bankClient;
    private readonly IPaymentsRepository _repository;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        PaymentRequestValidator validator,
        IAcquiringBankClient bankClient,
        IPaymentsRepository repository,
        ILogger<PaymentService> logger)
    {
        _validator = validator;
        _bankClient = bankClient;
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Processes a payment request end to end and returns a typed result.
    /// The method never throws for expected outcomes; all failure cases are
    /// expressed through the <see cref="PaymentResult"/> discriminated union.
    /// </summary>
    public async Task<PaymentResult> ProcessAsync(
        PostPaymentRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting payment validation");

        var validation = _validator.Validate(request);
        if (!validation.IsValid)
        {
            // Rejected payments are an expected business outcome, not an error.
            // We log at Information so rejection rates appear in dashboards without
            // polluting the warning/error signal that operators use for alerts.
            _logger.LogInformation(
                "Payment rejected: {ErrorCount} validation error(s)", validation.Errors.Count);
            PaymentGatewayDiagnostics.PaymentsProcessed.Add(1,
                new KeyValuePair<string, object?>("status", "Rejected"));
            return PaymentResult.Rejected(validation.Errors);
        }

        _logger.LogDebug("Validation passed, forwarding request to acquiring bank");

        // Open a dedicated child span for the bank HTTP call so that distributed traces
        // show the bank latency as its own segment, separate from gateway processing time.
        // W3C traceparent is forwarded automatically by HttpClient instrumentation.
        // Stopwatch.GetTimestamp() is used instead of DateTime.UtcNow because it reads
        // the high-resolution hardware counter and is unaffected by clock adjustments.
        BankAuthorizationResult bankResult;
        {
            using var _ = PaymentGatewayDiagnostics.ActivitySource.StartActivity("bank.authorize");
            var sw = Stopwatch.GetTimestamp();
            bankResult = await _bankClient.AuthorizeAsync(ToBankRequest(request), cancellationToken);
            var elapsedMs = Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
            PaymentGatewayDiagnostics.BankRequestDuration.Record(
                elapsedMs,
                new KeyValuePair<string, object?>("outcome", bankResult.Status.ToString()));
        }

        if (bankResult.Status == BankResponseStatus.Unavailable)
        {
            // Warning because the gateway is operating in a degraded state.
            // The payment is not persisted so a client retry cannot cause a double charge.
            _logger.LogWarning("Payment not processed: acquiring bank unavailable");
            PaymentGatewayDiagnostics.PaymentsProcessed.Add(1,
                new KeyValuePair<string, object?>("status", "BankUnavailable"));
            return PaymentResult.BankUnavailable();
        }

        var payment = ToPayment(request, bankResult.Status);
        await _repository.AddAsync(payment);

        _logger.LogInformation(
            "Payment {PaymentId} processed with status {Status}", payment.Id, payment.Status);
        PaymentGatewayDiagnostics.PaymentsProcessed.Add(1,
            new KeyValuePair<string, object?>("status", payment.Status.ToString()));

        return bankResult.Status == BankResponseStatus.Authorized
            ? PaymentResult.Authorized(payment)
            : PaymentResult.Declined(payment);
    }

    // Translates the API request into the bank DTO.
    // The null-forgiving operators (!) are safe here because the validator above has
    // already confirmed that all these fields are present and within valid ranges.
    // ExpiryDate is formatted as "MM/YYYY" as required by the acquiring bank wire format.
    private static BankAuthorizationRequest ToBankRequest(PostPaymentRequest r) => new()
    {
        CardNumber = r.CardNumber!,
        ExpiryDate = $"{r.ExpiryMonth!.Value:D2}/{r.ExpiryYear!.Value}",
        Currency = r.Currency!,
        Amount = r.Amount!.Value,
        Cvv = r.Cvv!
    };

    // Builds the domain Payment record that will be persisted.
    // Only the last four digits of the card number are retained; the full PAN is never stored.
    // The C# range operator [^4..] slices the last 4 characters of a string.
    private static Payment ToPayment(PostPaymentRequest r, BankResponseStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Status = status == BankResponseStatus.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined,
        CardNumberLastFour = r.CardNumber![^4..],
        ExpiryMonth = r.ExpiryMonth!.Value,
        ExpiryYear = r.ExpiryYear!.Value,
        Currency = r.Currency!,
        Amount = r.Amount!.Value
    };
}