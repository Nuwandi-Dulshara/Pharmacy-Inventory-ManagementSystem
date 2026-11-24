using MediStock.Data;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Services;

public sealed class StockReservationCleanupService(MediStockDbContext db)
{
    public static readonly TimeSpan CartLifetime = TimeSpan.FromHours(8);

    public async Task ReleaseExpiredAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var expirationCutoff = DateTime.UtcNow - CartLifetime;
        var expiredReservations = await db.StockReservations
            .Where(reservation => reservation.ReservedAtUtc < expirationCutoff)
            .ToListAsync(cancellationToken);
        if (expiredReservations.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var quantitiesByBatch = expiredReservations
            .GroupBy(reservation => reservation.StockBatchId)
            .Select(group => new
            {
                StockBatchId = group.Key,
                Quantity = group.Sum(reservation => reservation.Quantity)
            })
            .ToList();
        foreach (var item in quantitiesByBatch)
        {
            var batch = await db.StockBatches.SingleAsync(
                stockBatch => stockBatch.Id == item.StockBatchId,
                cancellationToken);
            batch.Quantity = checked(batch.Quantity + item.Quantity);
        }

        db.StockReservations.RemoveRange(expiredReservations);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
