using Microsoft.EntityFrameworkCore;
using Trade.Domain.Common;
using Trade.Domain.Documents;

namespace Trade.Infrastructure.Persistence;

public sealed partial class TradeDbContext
{
    private static readonly HashSet<string> Audited = ["Product","ProductCategory","Unit","StorageLocation","PriceType","Counterparty","ExpenseCategory","WriteOffReason",
        "Document","PurchaseInvoice","PurchaseInvoiceLine","SaleInvoice","SaleInvoiceLine","Expense","MoneyAccount","MoneyDocument","CustomerReturn","CustomerReturnLine",
        "SupplierReturn","SupplierReturnLine","StockTransfer","StockTransferLine","WriteOff","WriteOffLine","BatchPrice","ProductPrice"];
    private void PrepareAudit()
    {
        ChangeTracker.DetectChanges();
        if(ChangeTracker.Entries<AuditEntry>().Any(x=>x.State is EntityState.Modified or EntityState.Deleted))throw new DomainException("Audit entries are immutable.");
        if(audit?.OrganizationId is not { } org || audit.ActorId is not { } actor)return;
        var pending=new List<AuditEntry>();
        foreach(var entry in ChangeTracker.Entries().Where(x=>Audited.Contains(x.Metadata.ClrType.Name)&&x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray())
        {
            var properties=entry.Properties.ToArray();
            var changed=properties.Where(p=>entry.State!=EntityState.Modified || p.IsModified && !Equals(p.OriginalValue,p.CurrentValue)).ToArray();
            if(changed.Length==0)continue;
            var before=entry.State==EntityState.Added ? null : changed.ToDictionary(p=>p.Metadata.Name,p=>p.OriginalValue);
            var after=entry.State==EntityState.Deleted ? null : changed.ToDictionary(p=>p.Metadata.Name,p=>p.CurrentValue);
            var key=string.Join(":",entry.Metadata.FindPrimaryKey()!.Properties.Select(p=>entry.Property(p.Name).CurrentValue?.ToString()));
            Guid? documentId=entry.Entity is Document d ? d.Id : properties.Where(p=>p.Metadata.Name is "DocumentId" or "PurchaseInvoiceId" or "SaleInvoiceId" or "CustomerReturnId" or "SupplierReturnId" or "StockTransferId" or "WriteOffId").Select(p=>p.CurrentValue).OfType<Guid>().Cast<Guid?>().FirstOrDefault();
            var eventType=entry.State==EntityState.Added ? "CREATE" : entry.State==EntityState.Deleted ? "DELETE" : "UPDATE";
            if(entry.Entity is Document && before?.ContainsKey("DeletedAt")==true && after?["DeletedAt"] is not null)eventType="DELETE";
            else if(entry.Entity is Document && before?.ContainsKey("Status")==true)eventType=Equals(after?["Status"],DocumentStatus.POSTED)?"POST":"UNPOST";
            else if(before?.ContainsKey("Active")==true)eventType=Equals(after?["Active"],true)?"ACTIVATE":"DEACTIVATE";
            var label=new[] { "Name", "Number", "Code" }.Select(name=>properties.FirstOrDefault(p=>p.Metadata.Name==name)?.CurrentValue?.ToString()).FirstOrDefault(value=>!string.IsNullOrWhiteSpace(value));
            pending.Add(AuditEntry.Create(org,actor,eventType,entry.Metadata.ClrType.Name,key,documentId,label ?? entry.Metadata.ClrType.Name,before,after));
        }
        AuditEntries.AddRange(pending);
    }
}
