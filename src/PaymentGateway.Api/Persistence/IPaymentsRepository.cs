namespace PaymentGateway.Api.Persistence;

using PaymentGateway.Api.Domain;

/// <summary>
/// Abstraction over the payment store.
/// The production implementation uses an in-memory ConcurrentDictionary.
/// The interface exists so integration tests can substitute a different store if needed.
/// </summary>
public interface IPaymentsRepository
{
    /// <summary>
    /// Persists a payment record. Overwrites any existing record with the same id.
    /// </summary>
    Task AddAsync(Payment payment);

    /// <summary>
    /// Retrieves a payment by its unique identifier.
    /// Returns null if no payment with the given id exists.
    /// </summary>
    Task<Payment?> GetAsync(Guid id);
}