namespace KasiTix.Infrastructure.Repositories;
using KasiTix.Domain.Entities;
using KasiTix.Domain.Repositories;
using KasiTix.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class EfEventRepository(KasiTixDbContext db) : IEventRepository
{
    public Task<Event?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Events.AsNoTracking().Include(e => e.TicketTypes)
            .FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<Event?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default) =>
        db.Events.Include(e => e.TicketTypes).FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<List<Event>> ListAsync(EventStatus? status, CancellationToken ct = default)
    {
        IQueryable<Event> query = db.Events.AsNoTracking().Include(e => e.TicketTypes);
        if (status is not null)
            query = query.Where(e => e.Status == status.Value);   // runs in SQL
        return query.OrderBy(e => e.StartsAt).ToListAsync(ct);
    }

    public async Task AddAsync(Event @event, CancellationToken ct = default) =>
        await db.Events.AddAsync(@event, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await action();
            await tx.CommitAsync(ct);
        });
    }
}