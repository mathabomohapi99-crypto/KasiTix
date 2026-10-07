namespace KasiTix.Api.Models;
using KasiTix.Domain.Entities;

// -- Requests --
public record CreateEventRequest(string Name, string Venue, DateTime StartsAt);
public record CreateTicketTypeRequest(string Name, decimal Price, int Capacity);
public record PlaceOrderRequest(string BuyerEmail, IReadOnlyList<OrderLineRequest> Lines);
public record OrderLineRequest(Guid TicketTypeId, int Quantity);

// -- Responses --
public record TicketTypeResponse(Guid Id, string Name, decimal Price, int Capacity, int Remaining)
{
    public static TicketTypeResponse FromEntity(TicketType t) =>
        new(t.Id, t.Name, t.Price, t.Capacity, t.Remaining);
}

public record EventResponse(
    Guid Id, string Name, string Venue, DateTime StartsAt, string Status,
    IReadOnlyList<TicketTypeResponse> TicketTypes)
{
    public static EventResponse FromEntity(Event e) =>
        new(e.Id, e.Name, e.Venue, e.StartsAt, e.Status.ToString(),
            e.TicketTypes.Select(TicketTypeResponse.FromEntity).ToList());
}

public record OrderLineResponse(Guid TicketTypeId, string TicketTypeName, int Quantity, decimal UnitPrice);

// TODO: add a FromEntity for OrderResponse. OrderLine only holds a
// TicketTypeId, so where does TicketTypeName come from?
// ANSWER: from the event's ticket types. The caller passes an id -> name map.
public record OrderResponse(
    Guid Id, Guid EventId, string BuyerEmail, string Status, DateTime CreatedAt,
    IReadOnlyList<OrderLineResponse> Lines, decimal Total)
{
    public static OrderResponse FromEntity(Order o, IReadOnlyDictionary<Guid, string> ticketTypeNames) =>
        new(o.Id, o.EventId, o.BuyerEmail, o.Status.ToString(), o.CreatedAt,
            o.Lines.Select(l => new OrderLineResponse(
                l.TicketTypeId, ticketTypeNames[l.TicketTypeId], l.Quantity, l.UnitPrice)).ToList(),
            o.Total);
}

// AIP-158: an empty NextPageToken is the only end-of-list signal (Tier 2).
public record PagedResponse<T>(IReadOnlyList<T> Items, string NextPageToken);