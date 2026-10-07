using FluentValidation;
using KasiTix.Api.Common;
using KasiTix.Api.Services;
using KasiTix.Domain.Repositories;
using KasiTix.Infrastructure.Persistence;
using KasiTix.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Keeps "Async" in action names, so CreatedAtAction(nameof(GetByIdAsync), ...) finds its route.
builder.Services.AddControllers(options => options.SuppressAsyncSuffixInActionNames = false);
builder.Services.AddOpenApi();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

// TODO (Week 5):
// - read the connection string from user-secrets, and fail loudly at startup if it's missing
var connectionString = builder.Configuration.GetConnectionString("KasiTix")
    ?? throw new InvalidOperationException(
        "Connection string 'KasiTix' is missing. Run: dotnet user-secrets set \"ConnectionStrings:KasiTix\" \"<value>\"");

// - AddDbContext with UseNpgsql + EnableRetryOnFailure
// DbContext is Scoped by default: one per request, never shared between requests.
builder.Services.AddDbContext<KasiTixDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

// - register every repository and service, each with a lifetime you can defend
// All Scoped: repositories hold the Scoped DbContext, services hold repositories.
// A Singleton would capture one DbContext for the app's lifetime (not thread-safe, stale tracking).
builder.Services.AddScoped<IEventRepository, EfEventRepository>();
builder.Services.AddScoped<IOrderRepository, EfOrderRepository>();
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<IOrderService, OrderService>();

var app = builder.Build();

// TODO (Week 5): apply pending migrations on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KasiTixDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseExceptionHandler();
app.MapControllers();

app.Run();

// Lets KasiTix.Tests boot this app with WebApplicationFactory<Program>.
public partial class Program { }