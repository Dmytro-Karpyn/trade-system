using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trade.Application.Authentication;

namespace Trade.Tests;

// Deliberately lives only in the test assembly. Production never registers or trusts these headers.
internal sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "IntegrationTests";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Guid.TryParse(Request.Headers["X-Test-User-Id"], out var user) || user == Guid.Empty ||
            !Guid.TryParse(Request.Headers["X-Test-Organization-Id"], out var org) || org == Guid.Empty)
            return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.ToString()), new Claim("organization_id", org.ToString())], SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

internal sealed class TestCurrentPermissions : ICurrentPermissions
{
    public IReadOnlySet<string> Codes { get; } = PermissionCatalog.Codes.Where(x => x != "sale.change_price").ToHashSet();
    public IReadOnlyList<string> Roles => ["INTEGRATION_TEST"];
    public Task LoadAsync(ClaimsPrincipal principal, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class TestAntiforgery : IAntiforgery
{
    public AntiforgeryTokenSet GetAndStoreTokens(Microsoft.AspNetCore.Http.HttpContext context) => new("test", "test", "test", "X-CSRF-TOKEN");
    public AntiforgeryTokenSet GetTokens(Microsoft.AspNetCore.Http.HttpContext context) => GetAndStoreTokens(context);
    public Task<bool> IsRequestValidAsync(Microsoft.AspNetCore.Http.HttpContext context) => Task.FromResult(true);
    public Task ValidateRequestAsync(Microsoft.AspNetCore.Http.HttpContext context) => Task.CompletedTask;
    public void SetCookieTokenAndHeader(Microsoft.AspNetCore.Http.HttpContext context) { }
}
