namespace KasiTix.Domain.Entities;

// The explicit join entity between Order and TicketType: the relationship
// carries its own data (Quantity, UnitPrice), so it has to be a real entity.
public class OrderLine
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid TicketTypeId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }

    // ADDED: for EF Core only
    private OrderLine() { }

    public OrderLine(Guid orderId, Guid ticketTypeId, int quantity, decimal unitPrice)
    {
        if (quantity is < 1 or > 10)
            throw new ArgumentException("Quantity must be between 1 and 10.", nameof(quantity));

        Id = Guid.NewGuid();
        OrderId = orderId;
        TicketTypeId = ticketTypeId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }
}