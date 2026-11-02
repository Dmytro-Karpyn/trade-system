using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Trade.Api.Operations;
using Trade.Application.Authentication;
using Trade.Infrastructure.Authentication;
using Xunit;

namespace Trade.Tests;

public sealed class ProductionConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-connection-string")]
    [InlineData("Host=db;Database=trade;Username=app")]
    [InlineData("Host=db;Database=trade;Username=app;Password=trade_local_password")]
    [InlineData("Host=db;Database=trade;Username=app;Password=Test123!Test")]
    [InlineData("Host=db;Database=trade;Username=app;Password=replace-password")]
    [InlineData("Host=db;Database=trade;Username=app;Password=private-canary;Include Error Detail=true")]
    [InlineData("Host=db;Database=trade;Username=app;Password=private-canary;Log Parameters=true")]
    [InlineData("Host=db;Database=trade;Username=app;Password=private-canary;Persist Security Info=true")]
    [InlineData("Host=db;Database=trade;Username=app;Password=private-canary;Port=invalid")]
    public void Production_rejects_missing_development_or_unsafe_database_configuration_without_echoing_secrets(string? connection)
    {
        var config = Config(new() { ["ConnectionStrings:TradeDatabase"] = connection });
        var error = Assert.Throws<InvalidOperationException>(() => ProductionConfiguration.Validate(new EnvironmentStub("Production"), config));
        Assert.DoesNotContain("private-canary", error.ToString());
        Assert.DoesNotContain("trade_local_password", error.ToString());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Explicit_safe_database_configuration_is_accepted(string environment) =>
        ProductionConfiguration.Validate(new EnvironmentStub(environment), Config(new()
        {
            ["ConnectionStrings:TradeDatabase"] = "Host=db;Database=trade;Username=app;Password=private-canary"
        }));

    [Fact]
    public void Development_keeps_its_local_configuration() =>
        ProductionConfiguration.Validate(new EnvironmentStub("Development"), Config(new()));

    internal static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    internal sealed class EnvironmentStub(string environment) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environment;
        public string ApplicationName { get; set; } = "Trade.Api";
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

[Collection("PostgreSQL")]
public sealed class ProductionReadinessTests(DatabaseFixture fixture)
{
    private static Dictionary<string, string?> BootstrapSettings(Guid organizationId, string? password = null) => new()
    {
        ["BootstrapAdmin:Username"] = "staging-operator",
        ["BootstrapAdmin:Password"] = password ?? AuthenticationTestSupport.Password,
        ["BootstrapAdmin:OrganizationId"] = organizationId.ToString(),
        // Even if accidentally supplied, the development seed must remain disabled.
        ["DevelopmentSeed:AdminPassword"] = AuthenticationTestSupport.Password
    };

    [Fact]
    public async Task Production_bootstrap_is_explicit_and_cannot_overwrite_an_existing_administrator()
    {
        var organizationId = Guid.NewGuid();
        await using var api = await fixture.CreateApiAsync("Production", testAuthentication: false, settings: BootstrapSettings(organizationId));
        Assert.Equal(0, await api.QueryAsync(db => db.Users.CountAsync()));
        await api.WithServicesAsync(services => ProductionAdminBootstrap.RunAsync(services,
            services.GetRequiredService<IHostEnvironment>(), services.GetRequiredService<IConfiguration>()));
        Assert.Equal(1, await api.QueryAsync(db => db.Users.CountAsync()));
        Assert.Equal(HttpStatusCode.NoContent, (await api.Client.LoginAsync("staging-operator")).StatusCode);
        var me = await api.ReadAsync<CurrentUserResponse>(await api.Client.GetAsync("/auth/me"));
        Assert.Equal(organizationId, me.OrganizationId);
        Assert.Contains("OWNER", me.Roles);
        Assert.Equal(34, me.Permissions.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.WithServicesAsync(services => ProductionAdminBootstrap.RunAsync(services,
            services.GetRequiredService<IHostEnvironment>(), services.GetRequiredService<IConfiguration>())));
        Assert.Equal(1, await api.QueryAsync(db => db.Users.CountAsync()));
        Assert.Equal(HttpStatusCode.NoContent, (await api.NewClient().LoginAsync("staging-operator")).StatusCode);
    }

    [Fact]
    public async Task Failed_Identity_password_validation_rolls_back_the_bootstrap_role_and_user()
    {
        await using var api = await fixture.CreateApiAsync("Production", testAuthentication: false,
            settings: BootstrapSettings(Guid.NewGuid(), "abcdefghijklmnopqrst"));
        await Assert.ThrowsAsync<Trade.Domain.Common.DomainException>(() => api.WithServicesAsync(services => ProductionAdminBootstrap.RunAsync(services,
            services.GetRequiredService<IHostEnvironment>(), services.GetRequiredService<IConfiguration>())));
        Assert.Equal(0, await api.QueryAsync(db => db.Users.CountAsync()));
        Assert.Equal(0, await api.QueryAsync(db => db.Roles.CountAsync()));
    }

    [Fact]
    public async Task Production_errors_are_problem_details_without_stack_traces_and_swagger_is_not_public()
    {
        await using var api = await fixture.IdentityApiAsync("Production", seed: false);
        var unauthorized = await api.Client.GetAsync("/products");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal("application/problem+json", unauthorized.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client.GetAsync("/openapi/v1.json")).StatusCode);
        using var insecure = api.NewClient();
        insecure.BaseAddress = new Uri("http://localhost");
        // Generating a Secure antiforgery cookie over HTTP deliberately raises an unhandled exception.
        var error = await insecure.GetAsync("/auth/csrf");
        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        Assert.Equal("application/problem+json", error.Content.Headers.ContentType?.MediaType);
        var body = await error.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain("StackTrace", body);
        Assert.DoesNotContain("CookieSecurePolicy", body);
        Assert.False(error.Headers.Contains("Set-Cookie"));
    }
}
