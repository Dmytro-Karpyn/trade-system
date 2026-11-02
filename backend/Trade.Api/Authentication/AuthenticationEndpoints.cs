using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trade.Application.Authentication;
using Trade.Infrastructure.Authentication;
using Trade.Infrastructure.Persistence;

namespace Trade.Api.Authentication;

public static class AuthenticationEndpoints
{
    public static void MapAuthenticationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/permissions",()=>PermissionCatalog.All).WithTags("Roles").RequireAuthorization("permission:roles.manage");
        var auth = app.MapGroup("/auth").WithTags("Authentication");
        auth.MapGet("/csrf", (HttpContext http, IAntiforgery antiforgery) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var tokens = antiforgery.GetAndStoreTokens(http);
            return Results.Ok(new { RequestToken = tokens.RequestToken, HeaderName = tokens.HeaderName });
        }).AllowAnonymous();
        auth.MapPost("/login", async (LoginRequest input, HttpContext http, UserManager<TradeUser> users,
            SignInManager<TradeUser> signIn, ILoggerFactory loggerFactory) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var logger = loggerFactory.CreateLogger("Trade.Authentication");
            IResult Rejected()
            {
                logger.LogWarning("Login rejected: invalid credentials, inactive account or lockout");
                return Results.Unauthorized();
            }
            if (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrEmpty(input.Password)) return Rejected();
            var user = await users.FindByNameAsync(input.Username.Trim());
            if (user is null || !user.Active) return Rejected();
            var result = await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true);
            if (!result.Succeeded) return Rejected();
            user.LastLoginAt = DateTimeOffset.UtcNow;
            IdentityAdministration.RequireSuccess(await users.UpdateAsync(user));
            await signIn.SignInAsync(user, input.RememberMe);
            return Results.NoContent();
        }).AllowAnonymous();
        auth.MapPost("/logout", async (HttpContext http, UserManager<TradeUser> users, SignInManager<TradeUser> signIn) =>
        {
            // Revoke the security stamp as well as clearing the browser cookie, so a captured cookie cannot be replayed.
            var user = await users.GetUserAsync(http.User);
            if (user is not null) IdentityAdministration.RequireSuccess(await users.UpdateSecurityStampAsync(user));
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
        auth.MapGet("/me", async (HttpContext http, TradeDbContext db, ICurrentPermissions permissions, RequestContext context, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == context.ActorId && x.OrganizationId == context.OrganizationId, ct);
            return new CurrentUserResponse(user.Id, user.UserName!, user.FullName, user.OrganizationId, permissions.Roles,
                permissions.Codes.Order(StringComparer.Ordinal).ToArray());
        }).RequireAuthorization();

        var userApi = app.MapGroup("/admin/users").WithTags("Users").RequireAuthorization("permission:users.manage");
        userApi.MapGet("", (RequestContext c, IdentityAdministration s, CancellationToken ct) => s.ListUsersAsync(c.OrganizationId, ct));
        userApi.MapGet("/lookups",(RequestContext c,IdentityAdministration s,CancellationToken ct)=>s.LookupsAsync(c.OrganizationId,ct));
        userApi.MapGet("/{id:guid}", (Guid id, RequestContext c, IdentityAdministration s, CancellationToken ct) => s.GetUserAsync(c.OrganizationId, id, ct));
        userApi.MapPost("", async (CreateUserRequest input, RequestContext c, IdentityAdministration s, CancellationToken ct) =>
        {
            var user = await s.CreateUserAsync(c.OrganizationId, input, ct);
            return Results.Created($"/admin/users/{user.UserId}", user);
        });
        userApi.MapPut("/{id:guid}", (Guid id, UpdateUserRequest input, RequestContext c, IdentityAdministration s, CancellationToken ct) => s.UpdateUserAsync(c.OrganizationId, id, input, ct));
        userApi.MapPost("/{id:guid}/activate", (Guid id, RequestContext c, IdentityAdministration s, CancellationToken ct) => s.SetActiveAsync(c.OrganizationId, id, true, ct));
        userApi.MapPost("/{id:guid}/deactivate", (Guid id, RequestContext c, IdentityAdministration s, CancellationToken ct) => s.SetActiveAsync(c.OrganizationId, id, false, ct));

        var roleApi = app.MapGroup("/admin/roles").WithTags("Roles").RequireAuthorization("permission:roles.manage");
        roleApi.MapGet("", (RequestContext c, IdentityAdministration s, CancellationToken ct) => s.ListRolesAsync(c.OrganizationId, ct));
        roleApi.MapGet("/{id:guid}", (Guid id, RequestContext c, IdentityAdministration s, CancellationToken ct) => s.GetRoleAsync(c.OrganizationId, id, ct));
        roleApi.MapPost("", async (RoleRequest input, RequestContext c, IdentityAdministration s, CancellationToken ct) =>
        {
            var role = await s.CreateRoleAsync(c.OrganizationId, input, ct);
            return Results.Created($"/admin/roles/{role.Id}", role);
        });
        roleApi.MapPut("/{id:guid}", (Guid id, RoleRequest input, RequestContext c, IdentityAdministration s, CancellationToken ct) => s.UpdateRoleAsync(c.OrganizationId, id, input, ct));
        roleApi.MapPut("/{id:guid}/permissions", (Guid id, RolePermissionsRequest input, RequestContext c, IdentityAdministration s, CancellationToken ct) => s.SetPermissionsAsync(c.OrganizationId, id, input, ct));
    }
}
