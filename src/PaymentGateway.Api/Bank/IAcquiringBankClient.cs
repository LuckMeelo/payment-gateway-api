namespace PaymentGateway.Api.Bank;

public interface IAcquiringBankClient
{
    Task<BankAuthorizationResult> AuthorizeAsync(BankAuthorizationRequest request, CancellationToken cancellationToken = default);
}