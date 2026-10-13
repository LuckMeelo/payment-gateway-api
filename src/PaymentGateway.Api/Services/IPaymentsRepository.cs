namespace PaymentGateway.Api.Services;

using PaymentGateway.Api.Models.Responses;

public interface IPaymentsRepository
{
    Task AddAsync(PostPaymentResponse payment);
    Task<PostPaymentResponse?> GetAsync(Guid id);
}