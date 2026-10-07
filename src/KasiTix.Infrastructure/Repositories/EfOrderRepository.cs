namespace KasiTix.Infrastructure.Repositories;
using KasiTix.Domain.Entities;
using KasiTix.Domain.Repositories;
using KasiTix.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class EfOrderRepository(KasiTixDbContext db) : IOrderRepository
{
    public Task<Order?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default) =>
        db.Orders.AsNoTracking().Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey, ct);

    public Task<bool> BuyerHasConfirmedOrderAsync(Guid eventId, string buyerEmail, CancellationToken ct = default) =>
        db.Orders.AnyAsync(o => o.EventId == eventId
                             && o.BuyerEmail == buyerEmail
                             && o.Status == OrderStatus.Confirmed, ct);

    public Task<List<Order>> ListByEventAsync(
        Guid eventId, int pageSize, (DateTime CreatedAt, Guid Id)? after, CancellationToken ct = default)
    {
        IQueryable<Order> query = db.Orders.AsNoTracking().Include(o => o.Lines)
            .Where(o => o.EventId == eventId);

        if (after is { } c)
        {
            var createdAt = c.CreatedAt;
            var id = c.Id;
            query = query.Where(o => o.CreatedAt < createdAt
                                  || (o.CreatedAt == createdAt && o.Id.CompareTo(id) < 0));
        }

        return query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(pageSize + 1).ToListAsync(ct);
    }

    public Task<List<Order>> GetConfirmedByEventForUpdateAsync(Guid eventId, CancellationToken ct = default) =>
        db.Orders.Include(o => o.Lines)
            .Where(o => o.EventId == eventId && o.Status == OrderStatus.Confirmed)
            .ToListAsync(ct);

    public async Task AddAsync(Order order, CancellationToken ct = default) =>
        await db.Orders.AddAsync(order, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}