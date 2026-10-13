using PaymentGateway.Api.Bank;
using PaymentGateway.Api.Domain;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Persistence;
using PaymentGateway.Api.Validation;

namespace PaymentGateway.Api.Services;

public sealed class PaymentService(
    PaymentRequestValidator validator,
    IAcquiringBankClient bankClient,
    IPaymentsRepository repository)
{
    public async Task<PaymentResult> ProcessAsync(
        PostPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var validation = validator.Validate(request);
        if (!validation.IsValid)
            return PaymentResult.Rejected(validation.Errors);   // bank NOT called, nothing stored

        var bankResult = await bankClient.AuthorizeAsync(ToBankRequest(request), cancellationToken);
        if (bankResult.Status == BankResponseStatus.Unavailable)
            return PaymentResult.BankUnavailable(); // nothing stored

        var payment = ToPayment(request, bankResult.Status);
        await repository.AddAsync(payment);

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