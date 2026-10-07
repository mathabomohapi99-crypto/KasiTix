namespace KasiTix.Tests;
using System.Net;
using System.Net.Http.Json;
using KasiTix.Api.Models;
using KasiTix.Domain.Entities;
using KasiTix.Infrastructure.Persistence;
using KasiTix.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

[Collection("KasiTix")]
public class OrderTests(KasiTixFixture fx)
{
    private HttpClient Client => fx.Client;

    private async Task<(Guid EventId, Guid TicketTypeId)> CreatePublishedEventAsync(int capacity, decimal price = 80m)
    {
        var created = await Client.PostAsJsonAsync("/api/events", new
        {
            name = $"Jazz Night {Guid.NewGuid()}",
            venue = "Soweto Hall",
            startsAt = DateTime.UtcNow.AddDays(10)
        });
        created.EnsureSuccessStatusCode();
        var ev = (await created.Content.ReadFromJsonAsync<EventResponse>())!;

        var tt = await Client.PostAsJsonAsync($"/api/events/{ev.Id}/ticket-types",
            new { name = "General", price, capacity });
        tt.EnsureSuccessStatusCode();
        var ticketType = (await tt.Content.ReadFromJsonAsync<TicketTypeResponse>())!;

        var publish = await Client.PostAsync($"/api/events/{ev.Id}/publish", null);
        Assert.Equal(HttpStatusCode.NoContent, publish.StatusCode);
        return (ev.Id, ticketType.Id);
    }

    private Task<HttpResponseMessage> PlaceOrderAsync(Guid eventId, Guid ticketTypeId, int quantity, string key, string? email = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/events/{eventId}/orders")
        {
            Content = JsonContent.Create(new
            {
                buyerEmail = email ?? $"{Guid.NewGuid()}@example.co.za",
                lines = new[] { new { ticketTypeId, quantity } }
            })
        };
        request.Headers.Add("Idempotency-Key", key);
        return Client.SendAsync(request);
    }

    private async Task<int> RemainingAsync(Guid eventId)
    {
        var ev = await Client.GetFromJsonAsync<EventResponse>($"/api/events/{eventId}");
        return ev!.TicketTypes.Single().Remaining;
    }

    [Fact] // T1
    public async Task Order_WorksEndToEnd()
    {
        var (eventId, ticketTypeId) = await CreatePublishedEventAsync(capacity: 10, price: 80m);

        var response = await PlaceOrderAsync(eventId, ticketTypeId, 2, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal(160m, order.Total);
        Assert.Equal(8, await RemainingAsync(eventId));
    }

    [Fact] // T2
    public async Task OverCapacity_Returns409Problem_AndRemainingUnchanged()
    {
        var (eventId, ticketTypeId) = await CreatePublishedEventAsync(capacity: 2);

        var response = await PlaceOrderAsync(eventId, ticketTypeId, 3, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2, await RemainingAsync(eventId));
    }

    [Fact] // T3
    public async Task SameIdempotencyKey_ReturnsSameOrder_AndSellsOnce()
    {
        var (eventId, ticketTypeId) = await CreatePublishedEventAsync(capacity: 10);
        var key = Guid.NewGuid().ToString();
        var email = $"{Guid.NewGuid()}@example.co.za";

        var first = await PlaceOrderAsync(eventId, ticketTypeId, 2, key, email);
        var second = await PlaceOrderAsync(eventId, ticketTypeId, 2, key, email);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var a = (await first.Content.ReadFromJsonAsync<OrderResponse>())!;
        var b = (await second.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(8, await RemainingAsync(eventId));
    }

    [Fact] // T4
    public async Task ConcurrentReserve_SecondSaveThrowsConcurrencyException()
    {
        var (_, ticketTypeId) = await CreatePublishedEventAsync(capacity: 1);

        using var scopeA = fx.Factory.Services.CreateScope();
        using var scopeB = fx.Factory.Services.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<KasiTixDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<KasiTixDbContext>();

        var ticketA = await dbA.TicketTypes.SingleAsync(t => t.Id == ticketTypeId);
        var ticketB = await dbB.TicketTypes.SingleAsync(t => t.Id == ticketTypeId);

        ticketA.Reserve(1);
        ticketB.Reserve(1);   // both saw the last ticket

        await dbA.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync());
    }

    [Fact] // T5
    public async Task Database_RejectsDuplicateTicketTypeName()
    {
        using var scope = fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KasiTixDbContext>();

        var ev = new Event($"Market {Guid.NewGuid()}", "Khayelitsha", DateTime.UtcNow.AddDays(5));
        db.Events.Add(ev);
        db.TicketTypes.Add(new TicketType(ev.Id, "General", 80m, 10));
        await db.SaveChangesAsync();

        db.TicketTypes.Add(new TicketType(ev.Id, "General", 80m, 10));   // same name, same event

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal("23505", pg.SqlState);
    }
}