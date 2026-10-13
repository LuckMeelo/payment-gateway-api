namespace PaymentGateway.Api.Persistence;

using PaymentGateway.Api.Domain;

public interface IPaymentsRepository
{
    Task AddAsync(Payment payment);
    Task<Payment?> GetAsync(Guid id);
}