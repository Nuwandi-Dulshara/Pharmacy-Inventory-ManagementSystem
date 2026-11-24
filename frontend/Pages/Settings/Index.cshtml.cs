using System.ComponentModel.DataAnnotations;
using MediStock.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace MediStock.Pages.Settings;

[Authorize]
public sealed class IndexModel(
    MediStockDbContext db,
    AuthService auth,
    ILogger<IndexModel> logger) : PageModel
{
    [BindProperty]
    public SettingsInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var values = (await db.AppSettings.AsNoTracking().ToListAsync(cancellationToken))
            .ToDictionary(setting => setting.Key, setting => setting.Value);
        Input = new SettingsInput
        {
            PharmacyName = Get(values, "PharmacyName", "MediStock Pharmacy"),
            PharmacyAddress = Get(values, "PharmacyAddress", ""),
            ContactNumber = Get(values, "PharmacyContact", ""),
            Currency = Get(values, "Currency", "LKR"),
            LowStockQuantity = ReadInt(values, "LowStockQuantity", 10),
            ExpiryWarningDays = ReadInt(values, "ExpiryWarningDays", 30),
            Theme = Get(values, "Theme", "Light")
        };
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        await SaveAsync("PharmacyName", Input.PharmacyName.Trim(), cancellationToken);
        await SaveAsync("PharmacyAddress", Input.PharmacyAddress.Trim(), cancellationToken);
        await SaveAsync("PharmacyContact", Input.ContactNumber.Trim(), cancellationToken);
        await SaveAsync("Currency", Input.Currency.Trim(), cancellationToken);
        await SaveAsync("LowStockQuantity", Input.LowStockQuantity.ToString(), cancellationToken);
        await SaveAsync("ExpiryWarningDays", Input.ExpiryWarningDays.ToString(), cancellationToken);
        await SaveAsync("Theme", Input.Theme, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        StatusMessage = "Settings saved.";
        return RedirectToPage();
    }

    public IActionResult OnPostBackup()
    {
        var timestamp = DateTime.UtcNow;
        var path = Path.Combine(Path.GetTempPath(), $"MediStock-backup-{timestamp:yyyyMMdd-HHmmss}.db");
        var backup = auth.BackupDatabase(path);
        var bytes = System.IO.File.ReadAllBytes(backup);
        System.IO.File.Delete(backup);
        return File(bytes, "application/vnd.sqlite3", $"MediStock-backup-{timestamp:yyyyMMdd}.db");
    }

    public async Task<IActionResult> OnPostRestoreAsync(IFormFile? BackupFile, CancellationToken cancellationToken)
    {
        if (BackupFile is null || BackupFile.Length == 0)
        {
            StatusMessage = "Select a valid backup file.";
            return RedirectToPage();
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"medistock-restore-{Guid.NewGuid():N}.db");
        try
        {
            await using (var stream = System.IO.File.Create(tempPath))
            {
                await BackupFile.CopyToAsync(stream, cancellationToken);
            }

            auth.RestoreDatabase(tempPath);
            StatusMessage = "Database restored.";
        }
        catch (IOException exception)
        {
            logger.LogError(exception, "Could not read the uploaded MediStock backup.");
            StatusMessage = "The backup could not be read.";
        }
        catch (Microsoft.Data.Sqlite.SqliteException exception)
        {
            logger.LogError(exception, "Could not restore the MediStock database backup.");
            StatusMessage = "The backup file is invalid or could not be restored.";
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
            {
                System.IO.File.Delete(tempPath);
            }
        }

        return RedirectToPage();
    }

    private async Task SaveAsync(string key, string value, CancellationToken cancellationToken)
    {
        var setting = await db.AppSettings.FindAsync([key], cancellationToken);
        if (setting is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }
    }

    private static string Get(Dictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) ? value : fallback;

    private static int ReadInt(Dictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;
}

public sealed class SettingsInput
{
    [Required, StringLength(120)]
    public string PharmacyName { get; set; } = "MediStock Pharmacy";

    [StringLength(500)]
    public string PharmacyAddress { get; set; } = "";

    [Phone, StringLength(30)]
    public string ContactNumber { get; set; } = "";

    [Required, StringLength(8)]
    public string Currency { get; set; } = "LKR";

    [Range(0, 100000)]
    public int LowStockQuantity { get; set; } = 10;

    [Range(1, 365)]
    public int ExpiryWarningDays { get; set; } = 30;

    [RegularExpression("^(Light|Dark)$")]
    public string Theme { get; set; } = "Light";
}
