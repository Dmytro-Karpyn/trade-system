using System.Security.Claims;

namespace Trade.Api;

public sealed class RequestContext(IHttpContextAccessor accessor)
{
    public Guid OrganizationId => ReadId("organization_id");
    public Guid ActorId => ReadId(ClaimTypes.NameIdentifier);

    private Guid ReadId(string claim)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            throw new UnauthorizedAccessException("Authentication is required.");
        return Guid.TryParse(principal.FindFirstValue(claim), out var id) && id != Guid.Empty
            ? id : throw new UnauthorizedAccessException("The authenticated user context is invalid.");
    }
}
