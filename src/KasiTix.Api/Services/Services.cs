namespace KasiTix.Api.Services;
using System.Text;
using FluentValidation;
using KasiTix.Api.Models;
using KasiTix.Domain.Entities;
using KasiTix.Domain.Exceptions;
using KasiTix.Domain.Repositories; // your interfaces, in Domain

public interface IEventService
{
    Task<EventResponse> CreateAsync(CreateEventRequest request);
    Task<EventResponse> GetByIdAsync(Guid id); // throws NotFoundException

    // TODO: AddTicketTypeAsync, PublishAsync (Tier 1); ListAsync, CancelAsync (Tier 2)
    Task<TicketTypeResponse> AddTicketTypeAsync(Guid eventId, CreateTicketTypeRequest request);
    Task PublishAsync(Guid id);
    Task<IReadOnlyList<EventResponse>> ListAsync(string? status);
    Task CancelAsync(Guid id);
}

public interface IOrderService
{
    Task<(OrderResponse Order, bool IsReplay)> PlaceAsync(
        Guid eventId, string idempotencyKey, PlaceOrderRequest request);

    // ADDED (Tier 2)
    Task<PagedResponse<OrderResponse>> ListAsync(Guid eventId, int? pageSize, string? pageToken);
}

// TODO: write EventService yourself.
public class EventService(
    IEventRepository events,
    IOrderRepository orders,
    IValidator<CreateEventRequest> eventValidator,
    IValidator<CreateTicketTypeRequest> ticketTypeValidator) : IEventService
{
    public async Task<EventResponse> CreateAsync(CreateEventRequest request)
    {
        await eventValidator.ValidateAndThrowAsync(request);

        var ev = new Event(request.Name.Trim(), request.Venue.Trim(), request.StartsAt.ToUniversalTime());
        await events.AddAsync(ev);
        await events.SaveChangesAsync();
        return EventResponse.FromEntity(ev);
    }

    public async Task<EventResponse> GetByIdAsync(Guid id)
    {
        var ev = await events.GetByIdAsync(id)
            ?? throw new NotFoundException($"Event {id} was not found.");
        return EventResponse.FromEntity(ev);
    }

    public async Task<TicketTypeResponse> AddTicketTypeAsync(Guid eventId, CreateTicketTypeRequest request)
    {
        await ticketTypeValidator.ValidateAndThrowAsync(request);

        var ev = await events.GetByIdForUpdateAsync(eventId)   // tracked: we're adding a child
            ?? throw new NotFoundException($"Event {eventId} was not found.");

        var ticketType = ev.AddTicketType(request.Name.Trim(), request.Price, request.Capacity);
        await events.SaveChangesAsync();   // a racing duplicate hits the unique index -> 23505 -> 409
        return TicketTypeResponse.FromEntity(ticketType);
    }

    public async Task PublishAsync(Guid id)
    {
        var ev = await events.GetByIdForUpdateAsync(id)
            ?? throw new NotFoundException($"Event {id} was not found.");
        ev.Publish();
        await events.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<EventResponse>> ListAsync(string? status)
    {
        EventStatus? parsed = null;
        if (status is not null)
        {
            if (!Enum.TryParse<EventStatus>(status, ignoreCase: true, out var s) || !Enum.IsDefined(s))
                throw new ArgumentException($"Unknown status '{status}'. Use Draft, Published or Cancelled.", nameof(status));
            parsed = s;
        }

        var list = await events.ListAsync(parsed);
        return list.Select(EventResponse.FromEntity).ToList();
    }

    // R11: all-or-nothing. Event status, every order and every Sold change commit together.
    public async Task CancelAsync(Guid id)
    {
        await events.ExecuteInTransactionAsync(async () =>
        {
            var ev = await events.GetByIdForUpdateAsync(id)
                ?? throw new NotFoundException($"Event {id} was not found.");
            if (ev.Status == EventStatus.Cancelled) return;

            ev.Cancel();

            var ticketTypes = ev.TicketTypes.ToDictionary(t => t.Id);
            var open = await orders.GetConfirmedByEventForUpdateAsync(id);
            foreach (var order in open)
            {
                foreach (var line in order.Lines)
                    ticketTypes[line.TicketTypeId].Release(line.Quantity);
                order.Cancel();
            }

            await events.SaveChangesAsync();
        });
    }
}

public class OrderService(
    IEventRepository events,
    IOrderRepository orders,
    IValidator<PlaceOrderRequest> validator) : IOrderService
{
    public async Task<(OrderResponse Order, bool IsReplay)> PlaceAsync(
        Guid eventId, string idempotencyKey, PlaceOrderRequest request)
    {
        await validator.ValidateAndThrowAsync(request);

        // TODO: in this order. Think about each step before you write it.
        //
        // 1. Replay? Look the key up. If an order already has it, return that
        //    order with IsReplay = true. (What happens if two requests with the
        //    same NEW key arrive at the same moment? Which constraint saves you?)
        //    ANSWER: the unique index on Orders.IdempotencyKey. The second INSERT fails
        //    with 23505 and the exception handler returns 409.
        var existing = await orders.GetByIdempotencyKeyAsync(idempotencyKey);
        if (existing is not null)
        {
            var original = await events.GetByIdAsync(existing.EventId)
                ?? throw new NotFoundException($"Event {existing.EventId} was not found.");
            return (OrderResponse.FromEntity(existing, NameMap(original)), true);
        }

        // 2. Load the event WITH its ticket types, TRACKED: you're about to
        //    change Sold. Missing -> 404. Not Published, or already started -> 422 (R5).
        var ev = await events.GetByIdForUpdateAsync(eventId)
            ?? throw new NotFoundException($"Event {eventId} was not found.");
        if (ev.Status != EventStatus.Published || ev.StartsAt <= DateTime.UtcNow)
            throw new UnprocessableEntityException("This event is not on sale.");

        // 3. R9: does this buyer already hold a Confirmed order for this event?
        //    -> 409. Ask the database. Don't load every order into memory.
        var buyerEmail = request.BuyerEmail.Trim().ToLowerInvariant();
        if (await orders.BuyerHasConfirmedOrderAsync(eventId, buyerEmail))
            throw new ConflictException("This buyer already has an order for this event.");

        // 4. Create the Order. For each requested line, find the ticket type on
        //    the event (an id that isn't on this event: 404 or 400? Decide),
        //    reserve the tickets, and add the line at today's price.
        //    DECISION: 404. The ticket type doesn't exist as far as this event is concerned.
        //    Order.AddLine reserves the tickets and records the current price.
        var order = new Order(eventId, buyerEmail, idempotencyKey);
        foreach (var line in request.Lines)
        {
            var ticketType = ev.TicketTypes.FirstOrDefault(t => t.Id == line.TicketTypeId)
                ?? throw new NotFoundException($"Ticket type {line.TicketTypeId} was not found on this event.");
            order.AddLine(ticketType, line.Quantity);
        }

        // 5. Save ONCE. If xmin catches a concurrent sale, let the exception
        //    surface: your exception handler turns it into a 409.
        await orders.AddAsync(order);
        await orders.SaveChangesAsync();

        return (OrderResponse.FromEntity(order, NameMap(ev)), false);
    }

    // ADDED (Tier 2): keyset paging. Token = base64url("<ticks>|<id>") of the last item on the page.
    public async Task<PagedResponse<OrderResponse>> ListAsync(Guid eventId, int? pageSize, string? pageToken)
    {
        var size = pageSize ?? 20;
        if (size is < 1 or > 100)
            throw new ArgumentException("pageSize must be between 1 and 100.", nameof(pageSize));

        (DateTime CreatedAt, Guid Id)? after = null;
        if (!string.IsNullOrEmpty(pageToken))
            after = DecodeToken(pageToken);

        var ev = await events.GetByIdAsync(eventId)
            ?? throw new NotFoundException($"Event {eventId} was not found.");

        var rows = await orders.ListByEventAsync(eventId, size, after);
        var page = rows.Take(size).ToList();
        var next = rows.Count > size ? EncodeToken(page[^1]) : "";

        var names = NameMap(ev);
        return new PagedResponse<OrderResponse>(page.Select(o => OrderResponse.FromEntity(o, names)).ToList(), next);
    }

    private static Dictionary<Guid, string> NameMap(Event ev) =>
        ev.TicketTypes.ToDictionary(t => t.Id, t => t.Name);

    private static string EncodeToken(Order o) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.CreatedAt.Ticks}|{o.Id}"))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static (DateTime, Guid) DecodeToken(string token)
    {
        try
        {
            var b64 = token.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Split('|');
            return (new DateTime(long.Parse(parts[0]), DateTimeKind.Utc), Guid.Parse(parts[1]));
        }
        catch (Exception)
        {
            throw new ArgumentException("pageToken is not valid.", nameof(token));
        }
    }
}