using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trade.Application;

namespace Trade.Infrastructure.Persistence;

internal sealed class AuditQueries(TradeDbContext db) : IAuditQueries
{
    public async Task<AuditLookups> LookupsAsync(Guid org,CancellationToken ct)=>new(
        await db.Users.AsNoTracking().Where(x=>x.OrganizationId==org).OrderBy(x=>x.FullName).Select(x=>new ReportOption(x.Id,x.FullName!=""?x.FullName:x.UserName!)).ToListAsync(ct),
        await db.AuditEntries.Where(x=>x.OrganizationId==org).Select(x=>x.EventType).Distinct().Order().ToListAsync(ct),
        await db.AuditEntries.Where(x=>x.OrganizationId==org).Select(x=>x.EntityType).Distinct().Order().ToListAsync(ct),
        await db.Documents.Where(x=>x.OrganizationId==org&&db.AuditEntries.Any(a=>a.OrganizationId==org&&a.DocumentId==x.Id)).OrderByDescending(x=>x.DocumentDate).Select(x=>new ReportOption(x.Id,x.Number??"Без номера")).ToListAsync(ct));
    public async Task<AuditPage> ListAsync(Guid org,AuditFilter f,CancellationToken ct)
    {
        if(f.From>f.To)throw new Trade.Domain.Common.DomainException("From must not be later than To.");
        var query=db.AuditEntries.AsNoTracking().Where(x=>x.OrganizationId==org);
        if(f.From is { } from)query=query.Where(x=>x.OccurredAt>=from);
        if(f.To is { } to)query=query.Where(x=>x.OccurredAt<=to);
        if(f.UserId is { } user)query=query.Where(x=>x.ActorId==user);
        if(!string.IsNullOrWhiteSpace(f.EventType))query=query.Where(x=>x.EventType==f.EventType);
        if(!string.IsNullOrWhiteSpace(f.EntityType))query=query.Where(x=>x.EntityType==f.EntityType);
        if(f.DocumentId is { } doc)query=query.Where(x=>x.DocumentId==doc);
        if(!string.IsNullOrWhiteSpace(f.Search)) { var escaped=f.Search.Trim().Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_");var pattern=$"%{escaped}%";query=query.Where(x=>EF.Functions.ILike(x.EntityId,pattern,"\\")||EF.Functions.ILike(x.Summary,pattern,"\\")); }
        var count=await query.CountAsync(ct);
        var rows=await query.OrderByDescending(x=>x.OccurredAt).ThenByDescending(x=>x.Id).Skip(Math.Max(0,f.Skip)).Take(Math.Clamp(f.Take,1,200))
            .Select(x=>new {x.Id,x.OccurredAt,x.ActorId,x.EventType,x.EntityType,x.EntityId,x.DocumentId,x.Summary,
                Username=db.Users.Where(u=>u.Id==x.ActorId&&u.OrganizationId==org).Select(u=>u.FullName!=""?u.FullName:u.UserName).FirstOrDefault(),
                Doc=db.Documents.Where(d=>d.Id==x.DocumentId&&d.OrganizationId==org).Select(d=>new {d.Number,d.DocumentType}).FirstOrDefault()}).ToListAsync(ct);
        return new(rows.Select(x=>new AuditRow(x.Id,x.OccurredAt,x.ActorId,x.Username,x.EventType,x.EntityType,x.EntityId,x.DocumentId,x.Doc?.Number,x.Doc?.DocumentType.ToString(),x.Summary)).ToArray(),count);
    }
    public async Task<AuditDetail> DetailAsync(Guid org,Guid id,CancellationToken ct)
    {
        var x=await db.AuditEntries.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.OrganizationId==org,ct)??throw new NotFoundException("Audit entry not found.");
        var username=await db.Users.Where(u=>u.Id==x.ActorId&&u.OrganizationId==org).Select(u=>u.FullName!=""?u.FullName:u.UserName).FirstOrDefaultAsync(ct);
        var doc=await db.Documents.Where(d=>d.Id==x.DocumentId&&d.OrganizationId==org).Select(d=>new {d.Number,d.DocumentType}).FirstOrDefaultAsync(ct);
        return new(new(x.Id,x.OccurredAt,x.ActorId,username,x.EventType,x.EntityType,x.EntityId,x.DocumentId,doc?.Number,doc?.DocumentType.ToString(),x.Summary),
            x.OldValues is null?null:JsonSerializer.Deserialize<JsonElement>(x.OldValues),x.NewValues is null?null:JsonSerializer.Deserialize<JsonElement>(x.NewValues));
    }
}
