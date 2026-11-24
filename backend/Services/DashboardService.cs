using MediStock.Data;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Services;

public sealed class DashboardService(MediStockDbContext dbContext)
{
    public const int LowStockThreshold = 10;
    public const int ExpiryWarningDays = 30;

    public async Task<DashboardOverview> GetOverviewAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var today = utcNow.Date;
        var expiryCutoff = today.AddDays(ExpiryWarningDays);

        var totalMedicines = await dbContext.Medicines
            .AsNoTracking()
            .CountAsync(medicine => medicine.IsActive, cancellationToken);

        var totalStockQuantity = await dbContext.StockBatches
            .AsNoTracking()
            .Where(batch => batch.Quantity > 0)
            .SumAsync(batch => (int?)batch.Quantity, cancellationToken) ?? 0;

        var totalSales = await dbContext.Sales
            .AsNoTracking()
            .Where(sale => sale.Status == "Completed")
            .SumAsync(sale => (decimal?)sale.TotalAmount, cancellationToken) ?? 0m;

        var totalSuppliers = await dbContext.Suppliers
            .AsNoTracking()
            .CountAsync(supplier => supplier.IsActive, cancellationToken);

        var expiredMedicineCount = await dbContext.StockBatches
            .AsNoTracking()
            .Where(batch => batch.Quantity > 0 && batch.ExpiryDate < today)
            .Select(batch => batch.MedicineId)
            .Distinct()
            .CountAsync(cancellationToken);

        var lowStockQuantities = dbContext.Medicines
            .AsNoTracking()
            .Where(medicine => medicine.IsActive)
            .Select(medicine => new
            {
                medicine.Id,
                medicine.Name,
                Quantity = dbContext.StockBatches
                    .Where(batch => batch.MedicineId == medicine.Id &&
                                    batch.Quantity > 0 &&
                                    batch.ExpiryDate >= today)
                    .Sum(batch => (int?)batch.Quantity) ?? 0
            });

        var lowStockMedicineCount = await lowStockQuantities
            .CountAsync(alert => alert.Quantity <= LowStockThreshold, cancellationToken);
        var lowStockAlerts = await lowStockQuantities
            .Where(alert => alert.Quantity <= LowStockThreshold)
            .OrderBy(alert => alert.Quantity)
            .ThenBy(alert => alert.Name)
            .Take(5)
            .Select(alert => new LowStockAlert(alert.Id, alert.Name, alert.Quantity))
            .ToListAsync(cancellationToken);

        var expiringSoonQuery = dbContext.StockBatches
            .AsNoTracking()
            .Where(batch => batch.Quantity > 0 &&
                            batch.ExpiryDate >= today &&
                            batch.ExpiryDate <= expiryCutoff);
        var expiringSoonCount = await expiringSoonQuery.CountAsync(cancellationToken);
        var expiringSoon = await expiringSoonQuery
            .OrderBy(batch => batch.ExpiryDate)
            .ThenBy(batch => batch.Medicine.Name)
            .Select(batch => new ExpiringBatchAlert(
                batch.Medicine.Name,
                batch.BatchNumber,
                batch.Quantity,
                batch.ExpiryDate,
                batch.Supplier.Name))
            .Take(6)
            .ToListAsync(cancellationToken);

        var twelveMonthsAgo = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-11);
        var monthlyTotals = await dbContext.Sales
            .AsNoTracking()
            .Where(sale => sale.Status == "Completed" && sale.SoldAtUtc >= twelveMonthsAgo)
            .GroupBy(sale => new { sale.SoldAtUtc.Year, sale.SoldAtUtc.Month })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Amount = group.Sum(sale => sale.TotalAmount)
            })
            .ToListAsync(cancellationToken);

        var monthlySales = Enumerable.Range(0, 12)
            .Select(offset =>
            {
                var month = twelveMonthsAgo.AddMonths(offset);
                var total = monthlyTotals
                    .FirstOrDefault(item => item.Year == month.Year && item.Month == month.Month)
                    ?.Amount ?? 0m;
                return new MonthlySalesPoint(month.ToString("MMM yy"), total);
            })
            .ToList();

        var recentSales = await dbContext.Sales
            .AsNoTracking()
            .Where(sale => sale.Status == "Completed")
            .OrderByDescending(sale => sale.SoldAtUtc)
            .Take(6)
            .Select(sale => new RecentSale(
                sale.InvoiceNumber,
                sale.SoldAtUtc,
                sale.TotalAmount,
                sale.PaymentMethod))
            .ToListAsync(cancellationToken);

        return new DashboardOverview(
            totalMedicines,
            totalStockQuantity,
            totalSales,
            lowStockMedicineCount,
            expiredMedicineCount,
            totalSuppliers,
            monthlySales,
            recentSales,
            lowStockAlerts,
            expiringSoonCount,
            expiringSoon);
    }
}

public sealed record DashboardOverview(
    int TotalMedicines,
    int TotalStockQuantity,
    decimal TotalSales,
    int LowStockMedicines,
    int ExpiredMedicines,
    int TotalSuppliers,
    IReadOnlyList<MonthlySalesPoint> MonthlySales,
    IReadOnlyList<RecentSale> RecentSales,
    IReadOnlyList<LowStockAlert> LowStockAlerts,
    int ExpiringSoonCount,
    IReadOnlyList<ExpiringBatchAlert> ExpiringSoon);

public sealed record MonthlySalesPoint(string Month, decimal Amount);
public sealed record RecentSale(string InvoiceNumber, DateTime SoldAtUtc, decimal TotalAmount, string PaymentMethod);
public sealed record LowStockAlert(int MedicineId, string MedicineName, int Quantity);
public sealed record ExpiringBatchAlert(string MedicineName, string BatchNumber, int Quantity, DateTime ExpiryDate, string SupplierName);
