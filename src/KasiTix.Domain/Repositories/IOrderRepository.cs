namespace KasiTix.Domain.Repositories;
using KasiTix.Domain.Entities;

public interface IOrderRepository
{
    // Database lookups, not C# filtering. No tracking: only used to build a response.
    Task<Order?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);
    Task<bool> BuyerHasConfirmedOrderAsync(Guid eventId, string buyerEmail, CancellationToken ct = default);

    // Tier 2 keyset paging, newest first. Returns up to pageSize + 1 rows.
    Task<List<Order>> ListByEventAsync(
        Guid eventId, int pageSize, (DateTime CreatedAt, Guid Id)? after, CancellationToken ct = default);

    // TRACKED: the cancellation flow changes these.
    Task<List<Order>> GetConfirmedByEventForUpdateAsync(Guid eventId, CancellationToken ct = default);

    Task AddAsync(Order order, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}