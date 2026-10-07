namespace KasiTix.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Xunit;

public class KasiTixFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        // Environment variables beat user-secrets, so the tests never touch your dev database.
        Environment.SetEnvironmentVariable("ConnectionStrings__KasiTix", _db.GetConnectionString());
        Factory = new WebApplicationFactory<Program>();
        Client = Factory.CreateClient();   // boots the app, which runs the migrations
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await _db.DisposeAsync();
    }
}

[CollectionDefinition("KasiTix")]
public class KasiTixCollection : ICollectionFixture<KasiTixFixture> { }