using Trade.Domain.Inventory;
using Trade.Domain.Sales;

namespace Trade.Application;

public sealed class InsufficientStockException() : Exception("INSUFFICIENT_STOCK");

internal static class StockRules
{
    public static void RequireAvailable(IEnumerable<SaleInvoiceLine> lines, Guid locationId,
        IReadOnlyList<StockState> states, bool posting = false) => RequireAvailable(Demands(lines, locationId), states, posting);

    public static void RequireAvailable(IReadOnlyDictionary<(Guid BatchId, Guid StorageLocationId), decimal> demands,
        IReadOnlyList<StockState> states, bool posting = false)
    {
        var byKey = states.ToDictionary(x => (x.BatchId, x.StorageLocationId));
        foreach (var demand in demands)
        {
            byKey.TryGetValue(demand.Key, out var state);
            if ((state?.AvailableQuantity ?? 0m) < demand.Value || (posting && (state?.PhysicalQuantity ?? 0m) < demand.Value))
                throw new InsufficientStockException();
        }
    }

    public static void RequireSafeRemoval(IReadOnlyList<StockMovement> movements, IReadOnlyList<StockState> states,
        IEnumerable<SaleInvoiceLine>? reserveAfter = null, Guid locationId = default) =>
        RequireSafeRemoval(movements, states, Demands(reserveAfter ?? [], locationId));

    public static void RequireSafeRemoval(IReadOnlyList<StockMovement> movements, IReadOnlyList<StockState> states,
        IReadOnlyDictionary<(Guid BatchId, Guid StorageLocationId), decimal> reserves)
    {
        var removed = movements.GroupBy(x => (x.BatchId, x.StorageLocationId))
            .ToDictionary(x => x.Key, x => x.Sum(m => m.Quantity));
        var byKey = states.ToDictionary(x => (x.BatchId, x.StorageLocationId));
        foreach (var key in removed.Keys.Union(reserves.Keys))
        {
            byKey.TryGetValue(key, out var state);
            var physical = (state?.PhysicalQuantity ?? 0m) - removed.GetValueOrDefault(key);
            var reserved = (state?.ReservedQuantity ?? 0m) + reserves.GetValueOrDefault(key);
            if (physical < 0 || physical - reserved < 0) throw new InsufficientStockException();
        }
    }

    private static Dictionary<(Guid BatchId, Guid StorageLocationId), decimal> Demands(IEnumerable<SaleInvoiceLine> lines, Guid locationId) =>
        lines.Where(x => x.RemovedAt is null && x.BatchId is not null)
            .GroupBy(x => (BatchId: x.BatchId!.Value, StorageLocationId: locationId))
            .ToDictionary(x => x.Key, x => x.Sum(line => line.Quantity));
}
