MediStock – Remaining Features and Requirements
Implement the following incomplete features without affecting the existing working features.

1. PDF Invoice Export

- Add an Export PDF button to the Invoice View Page.
- Include pharmacy details, invoice number, date, medicines, quantities, prices, and total amount.
- Allow users to save the PDF.
- Keep the existing print functionality.

2. AI Demand Prediction

- Replace the existing sales-average calculation with ML.NET.
- Use historical sales data for predictions.
- Support 7-day and 30-day predictions.
- Display prediction results using LiveCharts2.
- Compare predicted demand with available stock.
- Highlight possible stock shortages.
- Display predictions as estimates only.
- Handle insufficient historical sales data.

3. Multi-Medicine POS Cart

- Search and add multiple medicines to the cart.
- Display medicine name, quantity, unit price, and subtotal.
- Update quantities and remove medicines from the cart.
- Automatically calculate the total amount.
- Validate available stock and expiry dates.
- Complete all cart items in a single transaction.
- Automatically deduct stock and generate one invoice.

4. Light and Dark Theme

- Implement functional light and dark themes.
- Apply the selected theme to all pages and components.
- Save the selected theme preference.
- Restore the selected theme when the application restarts.

5. Dynamic Settings Integration

- Use the low-stock quantity configured in Settings.
- Use the expiry warning period configured in Settings.
- Remove hardcoded threshold values.
- Apply updated settings to the Dashboard, Stock Management, and Expiry Management pages.

6. Invoice Date Filtering

- Search invoices by invoice number.
- Filter invoices by a selected date.
- Filter invoices by a date range.
- Add a Clear Filters button.
- Display matching invoice records.

7. PDF Report Export

- Export Sales Reports as PDF.
- Export Stock Reports as PDF.
- Export Expiry Reports as PDF.
- Export Supplier Reports as PDF.
- Apply selected date filters to exported reports.
- Keep the existing CSV export functionality.

8. Profile Information Updates

- Refresh authenticated user information after profile updates.
- Immediately display the updated user name and profile details in the navigation.
- Update authentication claims where required.
- Avoid requiring users to log out and log in again.

9. Refund and Void History

- Allow only authorized users to process refunds and voids.
- Record the refund or void reason.
- Record the authorized user, date, time, and amount.
- Restore eligible stock quantities automatically.
- Prevent duplicate refunds and stock restoration.
- Display refund and void history on the Sale View Page.
- Preserve original transaction records.

10. Stock Deletion Confirmation

- Add a Delete button to eligible stock records.
- Display a confirmation dialog before deletion.
- Allow deletion of unused stock records only.
- Prevent deletion of stock linked to transactions.
- Display success and error messages.

11. Desktop Application – WPF + XAML

- Convert the existing Razor Pages interface to WPF + XAML.
- Use C# and .NET 10 LTS.
- Reuse existing business logic and database services where possible.
- Use SQLite and Entity Framework Core.
- Use LiveCharts2 for charts.
- Use ML.NET for demand prediction.
- Implement reusable WPF components.
- Maintain existing authentication and role-based access.
- Make the application work offline without requiring a browser.
- Support Windows desktop installation.
  General Requirements
- Do not remove any existing developed features.
- Preserve existing database records.
- Use real database-backed functionality.
- Maintain the existing green and white colour theme (75% white, 20% dark green, 5% light green) in light mode.
- Keep all pages clean, simple, and consistent.
- Include proper validation and error handling.
- Maintain accurate sales, invoice, refund, and stock records.
- Test all newly implemented features.
- Ensure all buttons and functionalities work correctly.
- Avoid placeholder functionality and unnecessary design changes.
