using Microsoft.EntityFrameworkCore;

namespace Trade.Infrastructure.Persistence;

internal static class DirectoryLabels
{
    public static async Task<Dictionary<Guid,string>> Categories(TradeDbContext db,Guid org,CancellationToken ct)
    {
        var nodes=await db.ProductCategories.AsNoTracking().Where(x=>x.OrganizationId==org).ToDictionaryAsync(x=>x.Id,ct);
        return nodes.Values.ToDictionary(x=>x.Id,x=>{var parts=new List<string>();var seen=new HashSet<Guid>();var node=x;while(seen.Add(node.Id)){parts.Add(node.Name);if(node.ParentId is not {} parent||!nodes.TryGetValue(parent,out var next))break;node=next;}parts.Reverse();return string.Join(" / ",parts);});
    }
}
