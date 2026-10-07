namespace KasiTix.Tests;
using KasiTix.Domain.Entities;
using KasiTix.Domain.Exceptions;
using Xunit;

public class DomainTests
{
    [Fact] // T0
    public void Reserve_MoreThanRemaining_Throws_AndSoldIsUnchanged()
    {
        var ticketType = new TicketType(Guid.NewGuid(), "General", 80m, 5);
        ticketType.Reserve(3);

        Assert.Throws<ConflictException>(() => ticketType.Reserve(3));

        Assert.Equal(3, ticketType.Sold);
    }
}