namespace KasiTix.Api.Controllers;
using KasiTix.Api.Models;
using KasiTix.Api.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/events")]
public class EventsController(IEventService events) : ControllerBase
{
    /// <summary>Creates an event in Draft.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EventResponse>> CreateAsync(CreateEventRequest request)
    {
        var created = await events.CreateAsync(request);
        return CreatedAtAction(nameof(GetByIdAsync), new { id = created.Id }, created);
    }

    /// <summary>Gets one event with its ticket types and what's left of each.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventResponse>> GetByIdAsync(Guid id) =>
        Ok(await events.GetByIdAsync(id));

    // TODO: POST /api/events/{id}/ticket-types (endpoint 3: 201, 400, 404, 409, 422)
    /// <summary>Adds a ticket type to a Draft event.</summary>
    [HttpPost("{id:guid}/ticket-types")]
    [ProducesResponseType(typeof(TicketTypeResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TicketTypeResponse>> AddTicketTypeAsync(Guid id, CreateTicketTypeRequest request)
    {
        var created = await events.AddTicketTypeAsync(id, request);
        // Location points at the event, which lists its ticket types.
        return CreatedAtAction(nameof(GetByIdAsync), new { id }, created);
    }

    // TODO: POST /api/events/{id}/publish (endpoint 4: 204, 404, 422)
    /// <summary>Publishes an event (opens it for sale). Safe to repeat.</summary>
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PublishAsync(Guid id)
    {
        await events.PublishAsync(id);
        return NoContent();
    }

    // ADDED (Tier 2, endpoint 6): GET /api/events?status=Published
    /// <summary>Lists events, optionally filtered by status.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<EventResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<EventResponse>>> ListAsync([FromQuery] string? status) =>
        Ok(await events.ListAsync(status));

    // ADDED (Tier 2, endpoint 8): POST /api/events/{id}/cancel
    /// <summary>Cancels an event and all of its orders. Safe to repeat.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelAsync(Guid id)
    {
        await events.CancelAsync(id);
        return NoContent();
    }
    // Every status code goes on the action as [ProducesResponseType].
}

[ApiController]
[Route("api/events/{eventId:guid}/orders")]
public class OrdersController(IOrderService orders) : ControllerBase
{
    /// <summary>Places an order. Safe to retry with the same Idempotency-Key.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OrderResponse>> PlaceAsync(
        Guid eventId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        PlaceOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("The Idempotency-Key header is required.", nameof(idempotencyKey));

        var (order, isReplay) = await orders.PlaceAsync(eventId, idempotencyKey, request);

        // TODO: a replay returns 200 with the original order; a new order returns 201.
        if (isReplay) return Ok(order);
        return Created($"/api/events/{eventId}/orders/{order.Id}", order);
    }

    // ADDED (Tier 2, endpoint 7): keyset paging
    /// <summary>Lists an event's orders, newest first (AIP-158 paging).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<OrderResponse>>> ListAsync(
        Guid eventId, [FromQuery] int? pageSize, [FromQuery] string? pageToken) =>
        Ok(await orders.ListAsync(eventId, pageSize, pageToken));
}