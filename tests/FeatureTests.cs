using MediStock;
using MediStock.Data;
using MediStock.Services;
using MediStock.Pages.Invoices;
using MediStock.Pages.Sales;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Xunit;

namespace MediStock.Tests;

public sealed class FeatureTests
{
    [Fact]
    public async Task DashboardUsesConfiguredStockAndExpiryThresholds()
    {
        await using var store = await CreateDatabaseAsync();
        var database = store.Context;
        var (medicine, supplier) = await SeedMedicineAndSupplierAsync(database);
        var today = DateTime.UtcNow.Date;
        database.AppSettings.AddRange(
            new AppSetting { Key = "LowStockQuantity", Value = "3" },
            new AppSetting { Key = "ExpiryWarningDays", Value = "7" });
        var batch = new StockBatch
        {
            MedicineId = medicine.Id,
            SupplierId = supplier.Id,
            BatchNumber = "B-1",
            Quantity = 3,
            PurchasePrice = 1,
            ExpiryDate = today.AddDays(5),
            CreatedUtc = today.AddMonths(-1)
        };
        database.StockBatches.Add(batch);
        await database.SaveChangesAsync();

        var overview = await new DashboardService(database, new AppSettingsService(database))
            .GetOverviewAsync(today, CancellationToken.None);

        Assert.Equal(3, overview.LowStockThreshold);
        Assert.Equal(7, overview.ExpiryWarningDays);
        Assert.Equal(1, overview.LowStockMedicines);
        Assert.Equal(1, overview.ExpiringSoonCount);

        var details = new MediStock.Pages.Stock.DetailsModel(database, new AppSettingsService(database));
        AttachPageContext(details, new ClaimsPrincipal());
        Assert.IsType<PageResult>(await details.OnGetAsync(batch.Id, CancellationToken.None));
        Assert.Equal("Expiring soon", details.Status);
        Assert.Equal(3, details.MedicineAvailableQuantity);
    }

    [Fact]
    public async Task DemandPredictionReturnsSevenAndThirtyDayForecastsWhenHistoryIsSufficient()
    {
        await using var store = await CreateDatabaseAsync();
        var database = store.Context;
        var (medicine, supplier) = await SeedMedicineAndSupplierAsync(database);
        var today = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc);
        var batch = new StockBatch
        {
            MedicineId = medicine.Id,
            SupplierId = supplier.Id,
            BatchNumber = "B-1",
            Quantity = 80,
            PurchasePrice = 1,
            ExpiryDate = today.AddMonths(6),
            CreatedUtc = today.AddMonths(-6)
        };
        database.StockBatches.Add(batch);
        await database.SaveChangesAsync();

        for (var daysAgo = 1; daysAgo <= 30; daysAgo++)
        {
            var sale = new Sale
            {
                InvoiceNumber = $"INV-{daysAgo}",
                SoldAtUtc = today.AddDays(-daysAgo).AddHours(12),
                TotalAmount = 20,
                PaymentMethod = "Cash",
                Status = "Completed"
            };
            sale.Items.Add(new SaleItem
            {
                MedicineId = medicine.Id,
                StockBatchId = batch.Id,
                Quantity = 2,
                UnitPrice = 10,
                LineTotal = 20
            });
            database.Sales.Add(sale);
        }

        await database.SaveChangesAsync();
        var service = new DemandPredictionService(database);
        var sevenDayForecast = await service.PredictAsync(medicine.Id, 7, today, CancellationToken.None);
        var thirtyDayForecast = await service.PredictAsync(medicine.Id, 30, today, CancellationToken.None);

        Assert.True(sevenDayForecast.HasEnoughHistory);
        Assert.Equal(7, sevenDayForecast.Points.Count);
        Assert.Equal(30, thirtyDayForecast.Points.Count);
        Assert.Equal(80, thirtyDayForecast.CurrentStock);
        Assert.Equal(80, thirtyDayForecast.StockAvailableThroughForecast);
        Assert.StartsWith(
            "data:image/png;base64,",
            DemandForecastChartRenderer.RenderDataUrl(thirtyDayForecast.Points, darkMode: true));
    }

    [Fact]
    public async Task DemandPredictionReportsInsufficientHistoryInsteadOfGuessing()
    {
        await using var store = await CreateDatabaseAsync();
        var database = store.Context;
        var (medicine, _) = await SeedMedicineAndSupplierAsync(database);

        var forecast = await new DemandPredictionService(database)
            .PredictAsync(medicine.Id, 7, new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        Assert.False(forecast.HasEnoughHistory);
        Assert.Empty(forecast.Points);
        Assert.Equal(0, forecast.PredictedQuantity);
    }

    [Fact]
    public async Task PosCreatesOneInvoiceForMultipleMedicinesAndRollsBackInsufficientStock()
    {
        await using var store = await CreateDatabaseAsync();
        var database = store.Context;
        var (firstMedicine, supplier) = await SeedMedicineAndSupplierAsync(database);
        var secondMedicine = new Medicine
        {
            Name = "Second Medicine",
            SellingPrice = 15,
            CreatedUtc = DateTime.UtcNow
        };
        database.Medicines.Add(secondMedicine);
        await database.SaveChangesAsync();
        var firstBatch = new StockBatch
        {
            MedicineId = firstMedicine.Id,
            SupplierId = supplier.Id,
            BatchNumber = "B-1",
            Quantity = 5,
            PurchasePrice = 1,
            ExpiryDate = DateTime.UtcNow.AddMonths(3),
            CreatedUtc = DateTime.UtcNow
        };
        var secondBatch = new StockBatch
        {
            MedicineId = secondMedicine.Id,
            SupplierId = supplier.Id,
            BatchNumber = "B-2",
            Quantity = 4,
            PurchasePrice = 1,
            ExpiryDate = DateTime.UtcNow.AddMonths(3),
            CreatedUtc = DateTime.UtcNow
        };
        var expiredBatch = new StockBatch
        {
            MedicineId = firstMedicine.Id,
            SupplierId = supplier.Id,
            BatchNumber = "EXPIRED",
            Quantity = 100,
            PurchasePrice = 1,
            ExpiryDate = DateTime.UtcNow.AddDays(-1),
            CreatedUtc = DateTime.UtcNow.AddMonths(-1)
        };
        database.StockBatches.AddRange(firstBatch, secondBatch, expiredBatch);
        await database.SaveChangesAsync();

        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.None));
        var cartId = Guid.NewGuid().ToString("N");
        var page = new CreateModel(
            database,
            new StockReservationCleanupService(database),
            loggerFactory.CreateLogger<CreateModel>());
        AttachPageContext(page, new ClaimsPrincipal(), $"MediStock.SaleCart={cartId}");
        Assert.IsType<RedirectToPageResult>(
            await page.OnPostAddAsync(firstMedicine.Id, 2, CancellationToken.None));
        Assert.Equal(3, (await database.StockBatches.FindAsync(firstBatch.Id))!.Quantity);
        Assert.IsType<RedirectToPageResult>(
            await page.OnPostAddAsync(secondMedicine.Id, 3, CancellationToken.None));
        page.Input.PaymentMethod = "Cash";
        var result = await page.OnPostAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        var sale = await database.Sales.Include(item => item.Items).SingleAsync();
        Assert.Equal(65, sale.TotalAmount);
        Assert.Equal(2, sale.Items.Select(item => item.MedicineId).Distinct().Count());
        Assert.Equal(3, (await database.StockBatches.FindAsync(firstBatch.Id))!.Quantity);
        Assert.Equal(1, (await database.StockBatches.FindAsync(secondBatch.Id))!.Quantity);
        Assert.Equal(100, (await database.StockBatches.FindAsync(expiredBatch.Id))!.Quantity);

        var insufficientResult = await page.OnPostAddAsync(firstMedicine.Id, 100, CancellationToken.None);
        Assert.IsType<PageResult>(insufficientResult);
        Assert.Equal(1, await database.Sales.CountAsync());
        await database.Entry(firstBatch).ReloadAsync();
        Assert.Equal(3, (await database.StockBatches.FindAsync(firstBatch.Id))!.Quantity);
    }

    [Fact]
    public async Task PosSearchAndCartRemovalOrCancellationHandleReservations()
    {
        await using var store = await CreateDatabaseAsync();
        var database = store.Context;
        var (medicine, supplier) = await SeedMedicineAndSupplierAsync(database);
        var batch = new StockBatch
        {
            MedicineId = medicine.Id,
            SupplierId = supplier.Id,
            BatchNumber = "B-1",
            Quantity = 10,
            PurchasePrice = 1,
            ExpiryDate = DateTime.UtcNow.AddMonths(3),
            CreatedUtc = DateTime.UtcNow
        };
        database.StockBatches.Add(batch);
        await database.SaveChangesAsync();

        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.None));
        var cartId = Guid.NewGuid().ToString("N");
        var page = new CreateModel(
            database,
            new StockReservationCleanupService(database),
            loggerFactory.CreateLogger<CreateModel>());
        AttachPageContext(page, new ClaimsPrincipal(), $"MediStock.SaleCart={cartId}");

        await page.OnGetAsync(CancellationToken.None);
        Assert.Empty(page.Medicines);
        Assert.Empty(page.CartItems);

        page.Search = "Test";
        await page.OnGetAsync(CancellationToken.None);
        Assert.Single(page.Medicines);
        Assert.Equal(10, page.Medicines[0].AvailableQuantity);

        Assert.IsType<RedirectToPageResult>(
            await page.OnPostAddAsync(medicine.Id, 4, CancellationToken.None));
        Assert.Equal(6, (await database.StockBatches.SingleAsync()).Quantity);
        Assert.IsType<RedirectToPageResult>(
            await page.OnPostRemoveAsync(medicine.Id, CancellationToken.None));
        Assert.Equal(10, (await database.StockBatches.SingleAsync()).Quantity);

        Assert.IsType<RedirectToPageResult>(
            await page.OnPostAddAsync(medicine.Id, 3, CancellationToken.None));
        Assert.Equal(7, (await database.StockBatches.SingleAsync()).Quantity);
        page.Search = "Test";
        await page.OnGetAsync(CancellationToken.None);
        Assert.Equal(10, page.Medicines.Single().AvailableQuantity);

        var otherCartPage = new CreateModel(
            database,
            new StockReservationCleanupService(database),
            loggerFactory.CreateLogger<CreateModel>())
        {
            Search = "Test"
        };
        AttachPageContext(otherCartPage, new ClaimsPrincipal(), $"MediStock.SaleCart={Guid.NewGuid():N}");
        await otherCartPage.OnGetAsync(CancellationToken.None);
        Assert.Equal(7, otherCartPage.Medicines.Single().AvailableQuantity);

        batch.ExpiryDate = DateTime.UtcNow.Date.AddDays(-1);
        await database.SaveChangesAsync();
        Assert.IsType<PageResult>(await page.OnPostAsync(CancellationToken.None));
        Assert.Empty(await database.Sales.ToListAsync());
        Assert.Equal(7, (await database.StockBatches.SingleAsync()).Quantity);
        Assert.IsType<RedirectToPageResult>(await page.OnPostCancelAsync(CancellationToken.None));
        Assert.Equal(10, (await database.StockBatches.SingleAsync()).Quantity);
        Assert.Empty(await database.StockReservations.ToListAsync());

        batch.Quantity = 7;
        database.StockReservations.Add(new StockReservation
        {
            CartId = Guid.NewGuid().ToString("N"),
            MedicineId = medicine.Id,
            StockBatchId = batch.Id,
            Quantity = 3,
            UnitPrice = medicine.SellingPrice,
            ReservedAtUtc = DateTime.UtcNow.AddHours(-9)
        });
        await database.SaveChangesAsync();
        await new StockReservationCleanupService(database).ReleaseExpiredAsync(CancellationToken.None);
        Assert.Equal(10, (await database.StockBatches.SingleAsync()).Quantity);
        Assert.Empty(await database.StockReservations.ToListAsync());
    }

    [Fact]
    public async Task InvoiceListCombinesNumberSingleDateAndDateRangeFilters()
    {
        await using var store = await CreateDatabaseAsync();
        var database = store.Context;
        var firstDate = new DateTime(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc);
        database.Sales.AddRange(
            new Sale { InvoiceNumber = "INV-KEEP", SoldAtUtc = firstDate, TotalAmount = 10, PaymentMethod = "Cash" },
            new Sale { InvoiceNumber = "INV-NEXT-LOCAL-DAY", SoldAtUtc = firstDate.Date.AddHours(20), TotalAmount = 15, PaymentMethod = "Cash" },
            new Sale { InvoiceNumber = "INV-OTHER", SoldAtUtc = firstDate.AddDays(1), TotalAmount = 20, PaymentMethod = "Card" },
            new Sale { InvoiceNumber = "OLD-INV", SoldAtUtc = firstDate.AddDays(-1), TotalAmount = 30, PaymentMethod = "Cash" });
        await database.SaveChangesAsync();

        var page = new MediStock.Pages.Invoices.IndexModel(database)
        {
            Search = "INV-",
            SelectedDate = firstDate.Date,
            FromDate = firstDate.Date,
            ToDate = firstDate.Date.AddDays(1)
        };
        AttachPageContext(page, new ClaimsPrincipal());
        await page.OnGetAsync(CancellationToken.None);

        var invoice = Assert.Single(page.Items);
        Assert.Equal("INV-KEEP", invoice.InvoiceNumber);
    }

    [Fact]
    public async Task ReportExportsRespectSelectedDateRange()
    {
        await using var store = await CreateDatabaseAsync();
        var database = store.Context;
        var (medicine, supplier) = await SeedMedicineAndSupplierAsync(database);
        var batch = new StockBatch
        {
            MedicineId = medicine.Id,
            SupplierId = supplier.Id,
            BatchNumber = "B-1",
            Quantity = 10,
            PurchasePrice = 1,
            ExpiryDate = new DateTime(2026, 8, 1),
            CreatedUtc = new DateTime(2026, 4, 10)
        };
        database.StockBatches.Add(batch);
        await database.SaveChangesAsync();
        var matchingSale = new Sale
        {
            InvoiceNumber = "INV-IN-RANGE",
            SoldAtUtc = new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc),
            TotalAmount = 20,
            PaymentMethod = "Cash",
            Status = "Completed"
        };
        matchingSale.Items.Add(new SaleItem
        {
            MedicineId = medicine.Id,
            StockBatchId = batch.Id,
            Quantity = 2,
            UnitPrice = 10,
            LineTotal = 20
        });
        var nextLocalDaySale = new Sale
        {
            InvoiceNumber = "INV-NEXT-LOCAL-DAY",
            SoldAtUtc = new DateTime(2026, 4, 10, 20, 0, 0, DateTimeKind.Utc),
            TotalAmount = 10,
            PaymentMethod = "Cash",
            Status = "Completed"
        };
        nextLocalDaySale.Items.Add(new SaleItem
        {
            MedicineId = medicine.Id,
            StockBatchId = batch.Id,
            Quantity = 1,
            UnitPrice = 10,
            LineTotal = 10
        });
        database.Sales.AddRange(
            matchingSale,
            nextLocalDaySale,
            new Sale
            {
                InvoiceNumber = "INV-OUT-OF-RANGE",
                SoldAtUtc = new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc),
                TotalAmount = 30,
                PaymentMethod = "Cash",
                Status = "Completed"
            });
        await database.SaveChangesAsync();

        var page = new MediStock.Pages.Reports.IndexModel(
            database,
            new AppSettingsService(database),
            new PdfExportService())
        {
            ReportType = "Sales",
            From = new DateTime(2026, 4, 10),
            To = new DateTime(2026, 4, 10)
        };
        await page.OnGetAsync(CancellationToken.None);
        Assert.Single(page.Rows);
        Assert.Equal("INV-IN-RANGE", page.Rows[0][0]);

        var pdf = Assert.IsType<FileContentResult>(await page.OnPostPdfAsync(CancellationToken.None));
        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf.FileContents, 0, 5));

        var csv = Assert.IsType<FileContentResult>(await page.OnPostCsvAsync(CancellationToken.None));
        var csvContent = System.Text.Encoding.UTF8.GetString(csv.FileContents);
        Assert.Contains("INV-IN-RANGE", csvContent);
        Assert.DoesNotContain("INV-OUT-OF-RANGE", csvContent);
        Assert.DoesNotContain("INV-NEXT-LOCAL-DAY", csvContent);
    }

    [Fact]
    public async Task StockDeleteOnlyRemovesUnusedEmptyBatchAndReportsFailures()
    {
        await using var store = await CreateDatabaseAsync();
        var database = store.Context;
        var (medicine, supplier) = await SeedMedicineAndSupplierAsync(database);
        var deletable = new StockBatch
        {
            MedicineId = medicine.Id,
            SupplierId = supplier.Id,
            BatchNumber = "EMPTY-UNUSED",
            Quantity = 0,
            PurchasePrice = 1,
            ExpiryDate = DateTime.UtcNow.AddMonths(3),
            CreatedUtc = DateTime.UtcNow
        };
        var linked = new StockBatch
        {
            MedicineId = medicine.Id,
            SupplierId = supplier.Id,
            BatchNumber = "EMPTY-USED",
            Quantity = 0,
            PurchasePrice = 1,
            ExpiryDate = DateTime.UtcNow.AddMonths(3),
            CreatedUtc = DateTime.UtcNow
        };
        database.StockBatches.AddRange(deletable, linked);
        await database.SaveChangesAsync();
        database.SaleItems.Add(new SaleItem
        {
            Sale = new Sale
            {
                InvoiceNumber = "INV-LINK",
                SoldAtUtc = DateTime.UtcNow,
                TotalAmount = 10,
                PaymentMethod = "Cash"
            },
            MedicineId = medicine.Id,
            StockBatchId = linked.Id,
            Quantity = 1,
            UnitPrice = 10,
            LineTotal = 10
        });
        await database.SaveChangesAsync();

        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.None));
        var page = new MediStock.Pages.Stock.IndexModel(
            database,
            new AppSettingsService(database),
            new StockReservationCleanupService(database),
            loggerFactory.CreateLogger<MediStock.Pages.Stock.IndexModel>());
        AttachPageContext(page, new ClaimsPrincipal());
        await page.OnGetAsync(CancellationToken.None);
        Assert.True(page.Items.Single(item => item.Id == deletable.Id).CanDelete);
        Assert.False(page.Items.Single(item => item.Id == linked.Id).CanDelete);

        Assert.IsType<RedirectToPageResult>(await page.OnPostDeleteAsync(deletable.Id, CancellationToken.None));
        Assert.IsType<RedirectToPageResult>(await page.OnPostDeleteAsync(linked.Id, CancellationToken.None));
        Assert.Null(await database.StockBatches.FindAsync(deletable.Id));
        Assert.NotNull(await database.StockBatches.FindAsync(linked.Id));
        Assert.NotNull(page.ErrorMessage);
    }

    [Fact]
    public async Task RefundIsAuthorizedRecordedOnceAndRestoresStockOnce()
    {
        await using var store = await CreateDatabaseAsync();
        var database = store.Context;
        var (medicine, supplier) = await SeedMedicineAndSupplierAsync(database);
        var batch = new StockBatch
        {
            MedicineId = medicine.Id,
            SupplierId = supplier.Id,
            BatchNumber = "B-1",
            Quantity = 1,
            PurchasePrice = 1,
            ExpiryDate = DateTime.UtcNow.AddMonths(3),
            CreatedUtc = DateTime.UtcNow
        };
        database.StockBatches.Add(batch);
        await database.SaveChangesAsync();

        var sale = new Sale
        {
            InvoiceNumber = "INV-REFUND",
            SoldAtUtc = DateTime.UtcNow,
            TotalAmount = 20,
            PaymentMethod = "Cash",
            Status = "Completed"
        };
        sale.Items.Add(new SaleItem
        {
            MedicineId = medicine.Id,
            StockBatchId = batch.Id,
            Quantity = 2,
            UnitPrice = 10,
            LineTotal = 20
        });
        database.Sales.Add(sale);
        await database.SaveChangesAsync();

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "Admin"),
            new Claim(ClaimTypes.Email, "admin@example.com"),
            new Claim(ClaimTypes.Role, "Administrator")
        };
        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.None));
        var page = new DetailsModel(
            database,
            new AppSettingsService(database),
            new PdfExportService(),
            loggerFactory.CreateLogger<DetailsModel>());
        AttachPageContext(page, new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));

        Assert.IsType<RedirectToPageResult>(await page.OnPostRefundAsync(sale.Id, "Customer return", CancellationToken.None));
        Assert.IsType<RedirectToPageResult>(await page.OnPostRefundAsync(sale.Id, "Duplicate request", CancellationToken.None));
        var history = await database.RefundHistories.SingleAsync();
        Assert.Equal("Refund", history.Action);
        Assert.Equal("admin@example.com", history.AuthorizedUserEmail);
        Assert.Equal(20, history.Amount);
        Assert.Equal("Refunded", (await database.Sales.SingleAsync()).Status);
        Assert.Equal(3, (await database.StockBatches.SingleAsync()).Quantity);
        Assert.Equal(1, await database.StockAdjustments.CountAsync());

        var unauthorized = new DetailsModel(
            database,
            new AppSettingsService(database),
            new PdfExportService(),
            loggerFactory.CreateLogger<DetailsModel>());
        AttachPageContext(unauthorized, new ClaimsPrincipal());
        Assert.IsType<ForbidResult>(await unauthorized.OnPostVoidAsync(sale.Id, "Not authorized", CancellationToken.None));
    }

    [Fact]
    public void PdfExportProducesPdfDocumentsForInvoicesAndReports()
    {
        var service = new PdfExportService();
        var invoice = service.GenerateInvoice(
            "MediStock",
            "Main Street",
            "011-5550000",
            "INV-001",
            DateTime.UtcNow,
            "Cash",
            12.5m,
            [new PdfInvoiceLine("Medicine", "B-1", 1, 12.5m, 12.5m)],
            "LKR");
        var report = service.GenerateReport(
            "MediStock",
            "Sales",
            new DateTime(2026, 4, 1),
            new DateTime(2026, 4, 30),
            ["Invoice", "Amount"],
            [["INV-001", "12.50"]]);

        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(invoice, 0, 5));
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(report, 0, 5));
    }

    [Fact]
    public async Task ProfileChangesAreAtomicAndRefreshAuthenticatedUser()
    {
        var originalDatabasePath = Environment.GetEnvironmentVariable("MEDISTOCK_DATABASE_PATH");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"medistock-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var databasePath = Path.Combine(temporaryDirectory, "test.db");
        try
        {
            Environment.SetEnvironmentVariable("MEDISTOCK_DATABASE_PATH", databasePath);
            var auth = new AuthService();
            auth.Initialize();
            auth.Register("Old Name", "Old Pharmacy", "test@example.com", "0115550000", "Abcd1234xyz");

            var wrongPasswordUpdate = auth.UpdateProfile(
                "test@example.com",
                "New Name",
                "0115550001",
                "New Pharmacy",
                "incorrect",
                "Efgh1234xyz",
                out var passwordIncorrect);

            Assert.False(wrongPasswordUpdate);
            Assert.True(passwordIncorrect);
            Assert.Equal("Old Name", auth.GetProfile("test@example.com")!.FullName);
            Assert.True(auth.UpdateProfile(
                "test@example.com",
                "New Name",
                "0115550001",
                "New Pharmacy",
                "Abcd1234xyz",
                "Efgh1234xyz",
                out _));
            Assert.Equal("New Name", auth.GetAuthenticatedUser("test@example.com")!.FullName);
            Assert.Null(auth.Login("test@example.com", "Abcd1234xyz"));
            Assert.NotNull(auth.Login("test@example.com", "Efgh1234xyz"));

            auth.Initialize();
            Assert.Equal("New Pharmacy", auth.GetProfile("test@example.com")!.PharmacyName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MEDISTOCK_DATABASE_PATH", originalDatabasePath);
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void RestoringAnOlderBackupCreatesTheStockReservationTable()
    {
        var originalDatabasePath = Environment.GetEnvironmentVariable("MEDISTOCK_DATABASE_PATH");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"medistock-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var databasePath = Path.Combine(temporaryDirectory, "test.db");
        var backupPath = Path.Combine(temporaryDirectory, "backup.db");
        try
        {
            Environment.SetEnvironmentVariable("MEDISTOCK_DATABASE_PATH", databasePath);
            var auth = new AuthService();
            auth.Initialize();
            auth.BackupDatabase(backupPath);

            using (var backup = new SqliteConnection($"Data Source={backupPath}"))
            {
                backup.Open();
                using var command = backup.CreateCommand();
                command.CommandText = "DROP TABLE StockReservations;";
                command.ExecuteNonQuery();
            }

            auth.RestoreDatabase(backupPath);
            using var restored = new SqliteConnection(auth.ConnectionString);
            restored.Open();
            using var verify = restored.CreateCommand();
            verify.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'StockReservations';";
            Assert.Equal(1L, verify.ExecuteScalar());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MEDISTOCK_DATABASE_PATH", originalDatabasePath);
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static async Task<TestDatabase> CreateDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MediStockDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new MediStockDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return new TestDatabase(db, connection);
    }

    private static async Task<(Medicine Medicine, Supplier Supplier)> SeedMedicineAndSupplierAsync(MediStockDbContext database)
    {
        var medicine = new Medicine
        {
            Name = "Test Medicine",
            SellingPrice = 10,
            CreatedUtc = DateTime.UtcNow
        };
        var supplier = new Supplier
        {
            Name = "Test Supplier",
            Phone = "0115550000",
            CreatedUtc = DateTime.UtcNow
        };
        database.AddRange(medicine, supplier);
        await database.SaveChangesAsync();
        return (medicine, supplier);
    }

    private static void AttachPageContext(PageModel page, ClaimsPrincipal user, string? cookie = null)
    {
        var httpContext = new DefaultHttpContext { User = user };
        if (cookie is not null)
        {
            httpContext.Request.Headers.Cookie = cookie;
        }

        var actionContext = new ActionContext(
            httpContext,
            new Microsoft.AspNetCore.Routing.RouteData(),
            new Microsoft.AspNetCore.Mvc.RazorPages.PageActionDescriptor(),
            new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary());
        page.PageContext = new PageContext(actionContext);
        page.TempData = new TempDataDictionary(httpContext, new MemoryTempDataProvider());
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class TestDatabase(MediStockDbContext context, SqliteConnection connection) : IAsyncDisposable
    {
        public MediStockDbContext Context { get; } = context;

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
