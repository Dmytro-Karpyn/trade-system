using System.Security.Claims;

namespace Trade.Api;

public sealed class RequestContext(IHttpContextAccessor accessor) : Trade.Application.IAuditContext
{
    public Guid OrganizationId => ReadId("organization_id");
    public Guid ActorId => ReadId(ClaimTypes.NameIdentifier);
    Guid? Trade.Application.IAuditContext.OrganizationId => TryRead("organization_id");
    Guid? Trade.Application.IAuditContext.ActorId => TryRead(ClaimTypes.NameIdentifier);
    private Guid? TryRead(string claim) => accessor.HttpContext?.User.Identity?.IsAuthenticated==true && Guid.TryParse(accessor.HttpContext.User.FindFirstValue(claim),out var id)&&id!=Guid.Empty ? id : null;

    private Guid ReadId(string claim)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException("Authentication is required.");
        return Guid.TryParse(principal.FindFirstValue(claim), out var id) && id != Guid.Empty
            ? id : throw new UnauthorizedAccessException("The authenticated user context is invalid.");
    }
}
