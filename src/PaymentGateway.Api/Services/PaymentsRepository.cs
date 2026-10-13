using System.Collections.Concurrent;

using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Services;

public class PaymentsRepository : IPaymentsRepository
{
    private readonly ConcurrentDictionary<Guid, PostPaymentResponse> _payments = new();

    public Task AddAsync(PostPaymentResponse payment)
    {
        _payments[payment.Id] = payment;
        return Task.CompletedTask;
    }

    public Task<PostPaymentResponse?> GetAsync(Guid id)
    {
        _payments.TryGetValue(id, out var payment);
        return Task.FromResult(payment);
    }
}