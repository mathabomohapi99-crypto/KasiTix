## Rule-placement table

| Rule | Enforced where | Why there, and not (only) somewhere else |
|---|---|---|
| R1 | Validator (`CreateEventRequestValidator`), Entity (`Event` constructor), DB max length on `Name` (120) | The validator gives a fast 400 with a clear message before the domain is touched. |
| R2 | Entity (`Event.Publish`, `Event.Cancel`) | The status transitions belong to the object that owns the status.|
| R3 | Entity (`Event.Publish`) | Ticket types are never deleted, so there is no race to defend against. |
| R4 | Validator (`CreateTicketTypeRequestValidator`), Entity (`Event.AddTicketType`, `TicketType` constructor), DB (unique index on `(EventId, Name)`, CHECK `Price >= 0`) | The validator rejects bad price and capacity early.. |
| R5 | Service (`OrderService.PlaceAsync`, step 2) | It depends on the event's current status and the current time, which are loaded state and not part of the request. ] |
| R6 | Validator (`PlaceOrderRequestValidator`), Entity (`Order.AddLine`, `OrderLine` constructor), DB (CHECK `Quantity BETWEEN 1 AND 10`, unique `(OrderId, TicketTypeId)`) | The validator returns a 400 for the whole request up front.  |
| R7 | Entity (`TicketType.Reserve`), concurrency token (xmin `Version`), DB CHECK (`Sold >= 0 AND Sold <= Capacity`) | `Reserve` gives a clean 409 in the normal case, but it only sees its own in-memory copy. |
| R8 | Controller (header present), Service (lookup by key first), DB unique index on `IdempotencyKey` | The controller returns 400 for a missing header. T|
| R9 | Service (`BuyerHasConfirmedOrderAsync`, a database query), DB partial unique index on `(EventId, BuyerEmail) WHERE Status = 'Confirmed'` | The query gives a clear 409 without trying to insert. |
| R10 | Entity (`Order.AddLine` copies `ticketType.Price`), schema (`OrderLine.UnitPrice` column) | The price is captured at purchase time and stored on the line.  |
| R11 | Service (`EventService.CancelAsync` inside an explicit transaction), Entities (`Event.Cancel`, `Order.Cancel`, `TicketType.Release`) |

## Written answers

### 1. The CHECK that doesn't save you
Two requests can both read Sold = 0, both pass the "enough left" check in `Reserve` (`TicketType.cs`, line 41) and both save Sold = 1, which the CHECK allows, but the xmin token set by `IsRowVersion()` (`KasiTixDbContext.cs`, line 41) makes the second UPDATE match no rows, so `orders.SaveChangesAsync()` (`Services.cs`, line 167) throws and `ApiExceptionHandler.cs` (line 30) returns a 409.

### 2. Two places at once
**R4:** the in-memory name check in `Event.AddTicketType` (`Event.cs`, line 40) only sees what one request loaded, so the unique index on `(EventId, Name)` (`KasiTixDbContext.cs`, line 42) catches two simultaneous duplicates; **R9:** `BuyerHasConfirmedOrderAsync` (`Services.cs`, line 148) gives a friendly 409 but is check-then-insert, so the partial unique index (`KasiTixDbContext.cs`, lines 59-60) stops two simultaneous orders from the same buyer.

### 3. Lifetimes
In `Program.cs` (lines 27 to 36) the `KasiTixDbContext`, both repositories and both services are Scoped (validators are Scoped by default and `ApiExceptionHandler` is a Singleton), and if `EfOrderRepository` were a Singleton it would keep one `DbContext` for the whole app, which is not safe across requests and holds stale xmin values, so I would notice on the first request in Development or as random 409s and "a second operation was started" errors under overlapping requests.

### 4. The transaction you needed
Although one `SaveChangesAsync()` is already atomic, `CancelAsync` (`Services.cs`, line 89) wraps the event, order and ticket changes in an explicit transaction (`EfEventRepository.cs`, line 31 onwards) so a failure in any step leaves nothing half-done, and it must run inside `CreateExecutionStrategy()` because `EnableRetryOnFailure()` (`Program.cs`, line 28) makes EF refuse hand-made transactions outside the strategy, which can then safely replay the whole block on a retry.

### 5. The price on the line
`OrderLine.UnitPrice` is not duplication because it records what the buyer actually paid when `Order.AddLine` copied the price (`Order.cs`, line 57), whereas `TicketType.Price` is today's price, and without those columns on the line the order total would change whenever a price changed, breaking R10.
