namespace KasiTix.Domain.Entities;

public enum OrderStatus { Confirmed, Cancelled }

public class Order
{
    private readonly List<OrderLine> _lines = new();

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string BuyerEmail { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();
    public decimal Total => _lines.Sum(l => l.Quantity * l.UnitPrice);

    // ADDED: for EF Core only
    private Order() { }

    public Order(Guid eventId, string buyerEmail, string idempotencyKey)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("An order must belong to an event.", nameof(eventId));
        if (string.IsNullOrWhiteSpace(buyerEmail))
            throw new ArgumentException("Buyer email is required.", nameof(buyerEmail));
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));

        Id = Guid.NewGuid();
        EventId = eventId;
        BuyerEmail = buyerEmail;
        IdempotencyKey = idempotencyKey;
        Status = OrderStatus.Confirmed;
        CreatedAt = DateTime.UtcNow;
    }

    // TODO (R6, R10): add a line for this ticket type.
    // - an order holds at most 5 lines
    // - the same ticket type can't appear twice
    // - the line keeps the price the ticket type has RIGHT NOW
    // Should this method also call ticketType.Reserve(quantity)? Decide, and be
    // ready to defend your choice.
    // DECISION: yes. A line should never exist without its tickets being held, so
    // "add line" and "reserve" can't get out of sync. The line is built first so a
    // bad quantity throws before any tickets are reserved.
    public void AddLine(TicketType ticketType, int quantity)
    {
        ArgumentNullException.ThrowIfNull(ticketType);
        if (ticketType.EventId != EventId)
            throw new ArgumentException("That ticket type doesn't belong to this order's event.", nameof(ticketType));
        if (_lines.Count >= 5)
            throw new ArgumentException("An order can have at most 5 lines.", nameof(ticketType));
        if (_lines.Any(l => l.TicketTypeId == ticketType.Id))
            throw new ArgumentException("The same ticket type can't appear twice in one order.", nameof(ticketType));

        var line = new OrderLine(Id, ticketType.Id, quantity, ticketType.Price);
        ticketType.Reserve(quantity);
        _lines.Add(line);
    }

    public void Cancel() => Status = OrderStatus.Cancelled;
}