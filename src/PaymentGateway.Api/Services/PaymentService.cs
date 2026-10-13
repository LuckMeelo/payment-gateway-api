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

public sealed class PaymentService(
    PaymentRequestValidator validator,
    IAcquiringBankClient bankClient,
    IPaymentsRepository repository,
    ILogger<PaymentService> logger)
{
    public async Task<PaymentResult> ProcessAsync(
        PostPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var validation = validator.Validate(request);
        if (!validation.IsValid)
        {
            logger.LogInformation("Payment rejected: {ErrorCount} validation error(s)", validation.Errors.Count);
            PaymentGatewayDiagnostics.PaymentsProcessed.Add(1,
                new KeyValuePair<string, object?>("status", "Rejected"));
            return PaymentResult.Rejected(validation.Errors);
        }

        // Custom span scoped to the bank HTTP call; W3C traceparent is
        // forwarded automatically by HttpClient instrumentation.
        BankAuthorizationResult bankResult;
        {
            using var _ = PaymentGatewayDiagnostics.ActivitySource.StartActivity("bank.authorize");
            var sw = Stopwatch.GetTimestamp();
            bankResult = await bankClient.AuthorizeAsync(ToBankRequest(request), cancellationToken);
            var elapsedMs = Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
            PaymentGatewayDiagnostics.BankRequestDuration.Record(elapsedMs, new KeyValuePair<string, object?>("outcome", bankResult.Status.ToString()));
        }

        if (bankResult.Status == BankResponseStatus.Unavailable)
        {
            logger.LogWarning("Payment not processed: acquiring bank unavailable");
            PaymentGatewayDiagnostics.PaymentsProcessed.Add(1,
                new KeyValuePair<string, object?>("status", "BankUnavailable"));
            return PaymentResult.BankUnavailable();
        }

        var payment = ToPayment(request, bankResult.Status);
        await repository.AddAsync(payment);

        logger.LogInformation("Payment {PaymentId} processed with status {Status}", payment.Id, payment.Status);
        PaymentGatewayDiagnostics.PaymentsProcessed.Add(1, new KeyValuePair<string, object?>("status", payment.Status.ToString()));

        return bankResult.Status == BankResponseStatus.Authorized
            ? PaymentResult.Authorized(payment)
            : PaymentResult.Declined(payment);
    }

    private static BankAuthorizationRequest ToBankRequest(PostPaymentRequest r) => new()
    {
        CardNumber = r.CardNumber!,
        ExpiryDate = $"{r.ExpiryMonth!.Value:D2}/{r.ExpiryYear!.Value}",    // "MM/YYYY"
        Currency = r.Currency!,
        Amount = r.Amount!.Value,
        Cvv = r.Cvv!
    };

    private static Payment ToPayment(PostPaymentRequest r, BankResponseStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Status = status == BankResponseStatus.Authorized ? PaymentStatus.Authorized : PaymentStatus.Declined,
        CardNumberLastFour = r.CardNumber![^4..],   // only last 4 kept
        ExpiryMonth = r.ExpiryMonth!.Value,
        ExpiryYear = r.ExpiryYear!.Value,
        Currency = r.Currency!,
        Amount = r.Amount!.Value
    };
}