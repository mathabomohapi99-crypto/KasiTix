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
Say a ticket type has Capacity 1 and Sold 0, and two requests come in at the same moment. Both read the row and both see Sold = 0. Both then call `Reserve` in `TicketType.cs` (line 41), and the "is there enough left" check passes for both, because each one only looks at its own copy. Both set Sold to 1 and save, so the database ends up with Sold = 1, which is allowed by `Sold <= Capacity`. The CHECK never fires, but two people now hold the one seat. What stops this is the xmin token: `t.Property(x => x.Version).IsRowVersion()` in `KasiTixDbContext.cs` (line 41) makes EF add `AND xmin = @version` to the UPDATE. The second save matches no rows, so `orders.SaveChangesAsync()` in `OrderService.PlaceAsync` (`Services.cs`, line 167) throws `DbUpdateConcurrencyException`, and `ApiExceptionHandler.cs` (line 30) turns it into a 409. Test T4 shows this.

### 2. Two places at once
**R4 (unique ticket type name):** `Event.AddTicketType` in `Event.cs` (line 40) checks the list in memory and gives a friendly error, but it can only see what that one request loaded. If two requests add "General" at the same time, both pass. The unique index on `(EventId, Name)` in `KasiTixDbContext.cs` (line 42) catches the second one, even though the code check missed it. Test T5 proves it by skipping the service entirely.
**R9 (one Confirmed order per buyer):** `BuyerHasConfirmedOrderAsync`, called in `Services.cs` (line 148), lets us say "you already have an order" without trying to insert. But it's check first, then insert, so two requests can both pass the check. The partial unique index in `KasiTixDbContext.cs` (lines 59-60) stops the second insert, because it only counts rows where Status is Confirmed.

### 3. Lifetimes
In `Program.cs` (lines 27 to 36) I registered `KasiTixDbContext`, `IEventRepository`, `IOrderRepository`, `IEventService` and `IOrderService` all as Scoped, so each HTTP request gets its own set. The FluentValidation validators are also Scoped, which is the library default. `ApiExceptionHandler` is a Singleton, which is fine because it only uses the logger. If `EfOrderRepository` were a Singleton, it would hold on to one `DbContext` for the whole life of the app. That context isn't safe to share between requests, and it would keep old tracked tickets with old xmin values, so we'd get random 409s. In Development, .NET's scope check would complain on the first request. Without that check, we'd only see it when two requests overlapped, with an error like "a second operation was started on this context".

### 4. The transaction you needed
One `SaveChangesAsync()` is already all-or-nothing, and in my `CancelAsync` (`Services.cs`, line 89) all the changes do end up in one save. I still wrapped it in an explicit transaction in `EfEventRepository.cs` (line 31 onwards), because cancelling means several steps: mark the event Cancelled, cancel every order, and release every ticket. The transaction makes sure that if any step fails, none of it sticks, and it stays safe if someone later splits it into more than one save. It has to run inside `CreateExecutionStrategy()` because I turned on `EnableRetryOnFailure()` in `Program.cs` (line 28). With retries on, EF refuses to start your own transaction outside the strategy, since it wouldn't know how much already ran if it had to retry. Inside the strategy, the whole block is simply run again from the start.

### 5. The price on the line
`OrderLine.UnitPrice` isn't a normalisation mistake, because it isn't the same fact as `TicketType.Price`. The ticket type's price is what it costs today, and the line's price is what this buyer actually paid then. `Order.AddLine` copies the price at the moment of purchase (`Order.cs`, line 57). If we only looked up the ticket type's price, then changing a price from R80 to R100 would quietly change the total of every old order. If `OrderLine` were a plain many-to-many with no columns of its own, there would be nowhere to store the price or the quantity. `Total` could only use today's price, and R10 would break the first time a price changed.
