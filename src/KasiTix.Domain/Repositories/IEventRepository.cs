namespace KasiTix.Domain.Repositories;
using KasiTix.Domain.Entities;

public interface IEventRepository
{
    // Read-only: serialized into a response (AsNoTracking), includes ticket types.
    Task<Event?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // TRACKED: something is about to change it (Sold, Status, new ticket type).
    Task<Event?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default);

    // Tier 2: filtered in the database, ticket types in the same SQL query.
    Task<List<Event>> ListAsync(EventStatus? status, CancellationToken ct = default);

    Task AddAsync(Event @event, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);

    // Runs the action in an explicit transaction, inside the retry execution strategy.
    Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken ct = default);
}