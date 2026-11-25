using MediStock.Data;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Services;

public sealed class AppSettingsService(MediStockDbContext dbContext)
{
    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.AppSettings
            .AsNoTracking()
            .ToDictionaryAsync(setting => setting.Key, setting => setting.Value, cancellationToken);

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken = default)
    {
        var value = await dbContext.AppSettings
            .AsNoTracking()
            .Where(setting => setting.Key == key)
            .Select(setting => setting.Value)
            .SingleOrDefaultAsync(cancellationToken);
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    public async Task<string> GetStringAsync(
        string key,
        string fallback,
        CancellationToken cancellationToken = default)
    {
        var value = await dbContext.AppSettings
            .AsNoTracking()
            .Where(setting => setting.Key == key)
            .Select(setting => setting.Value)
            .SingleOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
