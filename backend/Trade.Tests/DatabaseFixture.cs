using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Trade.Application.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Trade.Application;
using Trade.Domain.Catalog;
using Trade.Domain.Documents;
using Trade.Domain.Purchasing;
using Trade.Infrastructure.Persistence;
using Xunit;

namespace Trade.Tests;

[CollectionDefinition("PostgreSQL")]
public sealed class PostgreSqlCollection : ICollectionFixture<DatabaseFixture>;

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("trade_tests").WithUsername("trade_tests").WithPassword("testcontainers_only").Build();

    public Task InitializeAsync() => database.StartAsync();
    public Task DisposeAsync() => database.DisposeAsync().AsTask();

    public async Task<ApiTestHost> CreateApiAsync(string environment = "Development",
        ISalePricePermissionProvider? pricePermissions = null, bool testAuthentication = true, IReadOnlyDictionary<string, string?>? settings = null,
        string? testRemoteIp = null)
    {
        var databaseName = $"slice_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(database.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection);
            await command.ExecuteNonQueryAsync();
        }
        var connectionString = new NpgsqlConnectionStringBuilder(database.GetConnectionString())
            { Database = databaseName }.ConnectionString;
        await using (var db = new TradeDbContext(new DbContextOptionsBuilder<TradeDbContext>().UseNpgsql(connectionString).Options))
            await db.Database.MigrateAsync();
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var useTestAuth = testAuthentication && environment == "Development";
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:TradeDatabase", connectionString);
            if (settings is not null)
                foreach (var pair in settings) builder.UseSetting(pair.Key, pair.Value);
            builder.ConfigureServices(services =>
            {
                if (testRemoteIp is not null) services.AddSingleton<IStartupFilter>(new TestRemoteIpFilter(testRemoteIp));
                if (useTestAuth)
                {
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
                    services.AddScoped<ICurrentPermissions, TestCurrentPermissions>();
                    services.AddSingleton<IAntiforgery, TestAntiforgery>();
                }
                if (pricePermissions is not null) services.AddSingleton(pricePermissions);
            });
        });
        var api = new ApiTestHost(factory, databaseName, organizationId, actorId);
        try
        {
            if (useTestAuth) await api.InitializeCounterpartyAsync();
            return api;
        }
        catch
        {
            await api.DisposeAsync();
            throw;
        }
    }
}

public sealed class ApiTestHost : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    private readonly WebApplicationFactory<Program> factory;
    public HttpClient Client { get; }
    public Guid OrganizationId { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid CounterpartyId { get; private set; }
    public string DatabaseName { get; }

    public ApiTestHost(WebApplicationFactory<Program> factory, string databaseName, Guid organizationId, Guid actorId)
    {
        this.factory = factory;
        OrganizationId = organizationId; ActorId = actorId;
        DatabaseName = databaseName;
        Client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        Client.DefaultRequestHeaders.Add("X-Test-Organization-Id", OrganizationId.ToString());
        Client.DefaultRequestHeaders.Add("X-Test-User-Id", ActorId.ToString());
    }

    public HttpClient NewClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    public async Task WithServicesAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }

    public void UseIdentity(Guid organizationId, Guid actorId) { OrganizationId = organizationId; ActorId = actorId; }

    public async Task WithDbAsync(Func<TradeDbContext, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TradeDbContext>());
    }

    public async Task<T> QueryAsync<T>(Func<TradeDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TradeDbContext>());
    }

    public async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    public ProductRequest ProductInput(ItemType type = ItemType.GOODS) => new(null, Guid.NewGuid(), type,
        $"P-{Guid.NewGuid():N}", null, "Test product", null, null);

    public async Task<ProductResponse> CreateProductAsync(ItemType type = ItemType.GOODS) =>
        await ReadAsync<ProductResponse>(await Client.PostAsJsonAsync("/products", ProductInput(type), Json), HttpStatusCode.Created);

    public async Task<CounterpartyResponse> CreateCounterpartyAsync(CounterpartyRequest? request = null) =>
        await ReadAsync<CounterpartyResponse>(await Client.PostAsJsonAsync("/counterparties",
            request ?? new CounterpartyRequest("Test counterparty", IsCustomer: true, IsSupplier: true), Json), HttpStatusCode.Created);

    public async Task InitializeCounterpartyAsync() => CounterpartyId = (await CreateCounterpartyAsync()).Id;

    public async Task<PurchaseResponse> CreatePurchaseAsync(Guid? productId = null, decimal quantity = 5.25m,
        DocumentType type = DocumentType.PURCHASE_INVOICE, Guid? storageLocationId = null)
    {
        productId ??= (await CreateProductAsync()).Id;
        var input = new PurchaseRequest(type, new DateOnly(2026, 10, 19), "Receipt", "Internal",
            type == DocumentType.OPENING_STOCK ? null : (await CreateCounterpartyAsync()).Id, storageLocationId ?? Guid.NewGuid(),
            Guid.NewGuid(), 1m, null, null, 0m, [new(null, 1, productId.Value, quantity, 10m)]);
        return await ReadAsync<PurchaseResponse>(await Client.PostAsJsonAsync("/purchases", input, Json), HttpStatusCode.Created);
    }

    public Task<PurchaseResponse> PostAsync(Guid id) => TransitionAsync(id, "post");
    public Task<PurchaseResponse> UnpostAsync(Guid id) => TransitionAsync(id, "unpost");

    private async Task<PurchaseResponse> TransitionAsync(Guid id, string action) =>
        await ReadAsync<PurchaseResponse>(await Client.PostAsync($"/purchases/{id}/{action}", null));

    public static UpdatePurchaseRequest Edit(PurchaseResponse p, IReadOnlyList<PurchaseLineDetails>? lines = null) =>
        new(p.Document.Version, p.Document.DocumentDate, p.Document.Comment, p.Document.InternalNote,
            p.CounterpartyId, p.StorageLocationId, p.CurrencyId, p.ExchangeRate, p.ExternalNumber, p.ExternalDate,
            p.DiscountPercent, lines ?? p.Lines.Where(x => x.RemovedAt is null)
                .Select(x => new PurchaseLineDetails(x.Id, x.LineNo, x.ProductId, x.Quantity, x.UnitPriceCurrency)).ToArray());

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        // EF caches one Npgsql data source per connection string; close this test database's pool explicitly.
        // Clearing only a newly constructed connection's default pool would miss EF's own data source.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TradeDbContext>();
            NpgsqlConnection.ClearPool((NpgsqlConnection)db.Database.GetDbConnection());
        }
        await factory.DisposeAsync();
    }
}
