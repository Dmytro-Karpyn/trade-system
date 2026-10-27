using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Trade.Api.Authentication;
using Trade.Application;
using Trade.Application.Authentication;
using Trade.Infrastructure.Authentication;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Serilog;
using System.Text.Json.Serialization;
using Trade.Api;
using Trade.Infrastructure;
using Trade.Infrastructure.Persistence;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args.Where(x => x != "--migrate").ToArray());

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        // Caddy terminates TLS. Only explicitly trusted proxies may supply the original scheme.
        options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
        foreach (var network in builder.Configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [])
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    });
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<TradeExceptionHandler>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<RequestContext>();
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.SerializerOptions.RespectNullableAnnotations = true;
    });
    builder.Services.AddOpenApi(options => options.AddOperationTransformer((operation, context, _) =>
    {
        if (context.Description.HttpMethod is "POST" or "PUT" or "DELETE")
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-CSRF-TOKEN", In = ParameterLocation.Header, Required = true,
                Description = "Request token from GET /auth/csrf; fetch a new token after login.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });
        }
        return Task.CompletedTask;
    }));
    builder.Services.AddTradeIdentity();
    builder.Services.AddScoped<ISalePricePermissionProvider, SalePricePermissionProvider>();
    builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
    builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
        options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    }).AddIdentityCookies();
    builder.Services.ConfigureApplicationCookie(options =>
    {
        options.Cookie.Name = "trade.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Events.OnValidatePrincipal = CookieSession.ValidateAsync;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
    builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = "X-CSRF-TOKEN";
        options.Cookie.Name = "trade.csrf";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    });

    var app = builder.Build();
    if (args.Contains("--migrate"))
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TradeDbContext>().Database.MigrateAsync();
        await DevelopmentIdentitySeed.RunAsync(scope.ServiceProvider, app.Environment, app.Configuration);
        Log.Information("Database migrations applied");
        return;
    }
    await using (var scope = app.Services.CreateAsyncScope())
        await DevelopmentIdentitySeed.RunAsync(scope.ServiceProvider, app.Environment, app.Configuration);
    app.UseForwardedHeaders();
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi().AllowAnonymous();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Trade API v1"));
    }
    app.UseAuthentication();
    app.Use(async (http, next) =>
    {
        await http.RequestServices.GetRequiredService<ICurrentPermissions>().LoadAsync(http.User, http.RequestAborted);
        await next(http);
    });
    app.UseAuthorization();
    app.Use(async (http, next) =>
    {
        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && !HttpMethods.IsOptions(http.Request.Method))
        {
            try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
            catch (AntiforgeryValidationException)
            {
                await Results.Problem(statusCode: 400, detail: "Invalid or missing CSRF token.").ExecuteAsync(http);
                return;
            }
        }
        await next(http);
    });

    app.MapGet("/health", async (HealthCheckService healthChecks, CancellationToken cancellationToken) =>
    {
        var report = await healthChecks.CheckHealthAsync(cancellationToken);
        return Results.Json(new HealthResponse(report.Status.ToString()),
            statusCode: report.Status == HealthStatus.Healthy
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable);
    })
        .WithName("GetHealth")
        .WithTags("System")
        .Produces<HealthResponse>()
        .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable)
        .AllowAnonymous();

    app.MapAuthenticationEndpoints();
    var business = app.MapGroup("");
    ((IEndpointConventionBuilder)business).Add(BusinessAuthorization.Apply);
    business.MapTradeEndpoints();
    business.MapUnitEndpoints();
    business.MapSaleEndpoints();
    business.MapPricingEndpoints();
    business.MapSettlementEndpoints();
    business.MapMoneyEndpoints();
    business.MapExpenseEndpoints();
    business.MapCustomerReturnEndpoints();
    business.MapSupplierReturnEndpoints();
    business.MapStockTransferEndpoints();
    business.MapWriteOffEndpoints();
    business.MapCostEndpoints();

    app.Run();
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    Log.Fatal(exception, "Trade API terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public sealed record HealthResponse(string Status);

// Required by WebApplicationFactory integration tests.
public partial class Program;
