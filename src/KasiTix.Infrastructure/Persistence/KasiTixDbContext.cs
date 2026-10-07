namespace KasiTix.Infrastructure.Persistence;
using KasiTix.Domain.Entities;
using Microsoft.EntityFrameworkCore;

public class KasiTixDbContext(DbContextOptions<KasiTixDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<TicketType> TicketTypes => Set<TicketType>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Event>(e =>
        {
            e.ToTable("Events");
            e.HasKey(x => x.Id);
            // Ids are generated in the constructors. ValueGeneratedNever makes EF INSERT
            // new children added to a tracked Event instead of sending an UPDATE.
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).IsRequired().HasMaxLength(120);
            e.Property(x => x.Venue).IsRequired().HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

            e.HasMany(x => x.TicketTypes).WithOne().HasForeignKey(t => t.EventId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.TicketTypes).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<TicketType>(t =>
        {
            t.ToTable("TicketTypes", tb =>
            {
                tb.HasCheckConstraint("CK_TicketTypes_Sold_Range", "\"Sold\" >= 0 AND \"Sold\" <= \"Capacity\"");
                tb.HasCheckConstraint("CK_TicketTypes_Price", "\"Price\" >= 0");
            });
            t.HasKey(x => x.Id);
            t.Property(x => x.Id).ValueGeneratedNever();
            t.Property(x => x.Name).IsRequired().HasMaxLength(100);
            t.Property(x => x.Price).HasPrecision(10, 2);
            t.Property(x => x.Version).IsRowVersion();   // maps to Postgres xmin
            t.HasIndex(x => new { x.EventId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<Order>(o =>
        {
            o.ToTable("Orders");
            o.HasKey(x => x.Id);
            o.Property(x => x.Id).ValueGeneratedNever();
            o.Property(x => x.BuyerEmail).IsRequired().HasMaxLength(320);
            o.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(100);
            o.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

            o.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
            o.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
            o.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

            o.HasIndex(x => x.IdempotencyKey).IsUnique();                                  // R8
            o.HasIndex(x => new { x.EventId, x.BuyerEmail }).IsUnique()                    // R9 (partial)
                .HasFilter("\"Status\" = 'Confirmed'");
            o.HasIndex(x => new { x.EventId, x.CreatedAt, x.Id });                         // Tier 2 paging
        });

        modelBuilder.Entity<OrderLine>(l =>
        {
            l.ToTable("OrderLines", tb =>
                tb.HasCheckConstraint("CK_OrderLines_Quantity", "\"Quantity\" BETWEEN 1 AND 10"));
            l.HasKey(x => x.Id);
            l.Property(x => x.Id).ValueGeneratedNever();
            l.Property(x => x.UnitPrice).HasPrecision(10, 2);
            l.HasOne<TicketType>().WithMany().HasForeignKey(x => x.TicketTypeId).OnDelete(DeleteBehavior.Restrict);
            l.HasIndex(x => new { x.OrderId, x.TicketTypeId }).IsUnique();
        });
    }
}