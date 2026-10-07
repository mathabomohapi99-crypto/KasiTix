namespace KasiTix.Domain.Entities;
using KasiTix.Domain.Exceptions;

public class TicketType
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string Name { get; private set; } = null!;
    public decimal Price { get; private set; }
    public int Capacity { get; private set; }
    public int Sold { get; private set; }

    // Week 5: map this to PostgreSQL's xmin column in your DbContext.
    // Postgres changes it on every write, so nothing in this class ever sets it.
    public uint Version { get; private set; }

    public int Remaining => Capacity - Sold;

    // ADDED: for EF Core only
    private TicketType() { }

    public TicketType(Guid eventId, string name, decimal price, int capacity)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("A ticket type must belong to an event.", nameof(eventId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (price < 0)
            throw new ArgumentException("Price can't be negative.", nameof(price));
        if (capacity is < 1 or > 10_000)
            throw new ArgumentException("Capacity must be between 1 and 10 000.", nameof(capacity));

        Id = Guid.NewGuid();
        EventId = eventId;
        Name = name;
        Price = price;
        Capacity = capacity;
        Sold = 0;
    }

    public void Reserve(int quantity)
    {
        if (quantity < 1)
            throw new ArgumentException("Quantity must be at least 1.", nameof(quantity));
        if (quantity > Remaining)
            throw new ConflictException($"Only {Remaining} '{Name}' tickets are left.");

        Sold += quantity;
    }

    // TODO: give back the tickets from a cancelled order. Sold may never go below 0.
    public void Release(int quantity)
    {
        if (quantity < 1)
            throw new ArgumentException("Quantity must be at least 1.", nameof(quantity));
        if (quantity > Sold)
            throw new UnprocessableEntityException($"Can't release {quantity} '{Name}' tickets; only {Sold} are sold.");

        Sold -= quantity;
    }
}