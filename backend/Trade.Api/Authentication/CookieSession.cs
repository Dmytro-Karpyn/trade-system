using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Trade.Infrastructure.Persistence;

namespace Trade.Api.Authentication;

public static class CookieSession
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var db = context.HttpContext.RequestServices.GetRequiredService<TradeDbContext>();
        var user = Guid.TryParse(principal?.FindFirstValue(options.ClaimsIdentity.UserIdClaimType), out var id)
            ? await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, context.HttpContext.RequestAborted) : null;
        if (user is null || !user.Active || user.OrganizationId.ToString() != principal?.FindFirstValue("organization_id") ||
            user.SecurityStamp != principal?.FindFirstValue(options.ClaimsIdentity.SecurityStampClaimType))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }
}
