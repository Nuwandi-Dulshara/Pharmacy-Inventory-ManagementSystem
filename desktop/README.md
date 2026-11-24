# MediStock Desktop

Native WPF client for Windows, targeting .NET 10. It uses the existing `AuthService` and `MediStockDbContext` via the backend project reference and opens the same SQLite database. The default database is `%LOCALAPPDATA%\MediStock\medistock.db`; set `MEDISTOCK_DATABASE_PATH` to use the same custom path as the web app.

Run on Windows with the .NET 10 SDK:

```powershell
dotnet run --project desktop\MediStock.Desktop.csproj
```

Sign in with an existing MediStock account. Light/dark mode reads and writes the shared `AppSettings.Theme` value. Administrators can manage medicines, suppliers, batches, categories, stock adjustments, expiry batches and alert settings; staff can view inventory and process sales. An empty batch can only be deleted after confirmation and when it has no sale or adjustment history. POS completion deducts unexpired stock and writes the sale and invoice items in one immediate SQLite transaction.

The client also provides ML.NET forecasts through the backend demand-prediction service and chart renderer, profile editing with immediate navigation refresh, and date-filtered sales/stock/expiry/supplier reports with CSV and backend-generated PDF export.

For a Windows deployment output, publish from Windows with:

```powershell
dotnet publish desktop\MediStock.Desktop.csproj -c Release -r win-x64 --self-contained true
```
