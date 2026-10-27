using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Trade.Tests;

// TestServer does not open a socket; explicitly provide the proxy's remote address for trust checks.
internal sealed class TestRemoteIpFilter(string address) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, run) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(address);
            return run(context);
        });
        next(app);
    };
}

[Collection("PostgreSQL")]
public sealed class ReverseProxyTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("127.0.0.1", false, true)]
    [InlineData("172.19.0.4", true, true)]
    [InlineData("203.0.113.17", false, false)]
    [InlineData("203.0.113.17", true, false)]
    public async Task Production_CSRF_cookie_requires_HTTPS_from_a_trusted_proxy(string remoteIp, bool configurePrivateNetwork, bool trusted)
    {
        await using var api = await fixture.CreateApiAsync("Production", testAuthentication: false,
            settings: configurePrivateNetwork ? new Dictionary<string, string?> { ["ReverseProxy:KnownNetworks:0"] = "172.16.0.0/12" } : null,
            testRemoteIp: remoteIp);
        api.Client.BaseAddress = new Uri("http://localhost");
        api.Client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        var response = await api.Client.GetAsync("/auth/csrf");
        // An untrusted HTTP request must not masquerade as HTTPS to obtain a Secure token.
        Assert.Equal(trusted ? HttpStatusCode.OK : HttpStatusCode.InternalServerError, response.StatusCode);
        if (trusted)
        {
            var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), x => x.StartsWith("trade.csrf="));
            Assert.Contains("secure", cookie); Assert.Contains("httponly", cookie);
        }
        else Assert.False(response.Headers.Contains("Set-Cookie"));
    }
}
