MediStock – Pharmacy Management System

Features and Page Requirements

Technology Stack

Language: C#

Desktop UI: WPF + XAML

Framework: .NET 10 LTS

Database: SQLite

ORM: Entity Framework Core

Charts: LiveCharts2

AI: ML.NET

UI Design

Use a modern green and white theme (75% white, 20% dark green, 5% light green). Keep all pages clean, simple, consistent, and easy to use.

4. Dashboard

Display the main business overview after login.

Summary Cards:

Total Medicines

Total Stock Quantity

Total Sales

Low Stock Medicines

Expired Medicines

Total Suppliers

Other Features:

Monthly Sales Chart using LiveCharts2.

Recent Sales Table.

Low Stock Alerts.

Medicines Expiring Soon.

Quick Add Medicine button.

Quick New Sale button.

Use real data from SQLite.

5Hmmm… can't reach this page
localhost refused to connect.
Try:

Search the web for localhost
Checking the connection
Checking the proxy and the firewall
ERR_CONNECTION_REFUSED

Medicine Index Page

Display medicines in a table.

Columns: Name, Category, Brand, Unit Price, Status, Actions.

Search medicines.

Add Medicine button.

View, Edit, Delete buttons.

Create Medicine Page

Medicine Name

Generic Name

Category

Brand/Manufacturer

Selling Price

Description (optional)

Save and Cancel buttons.

View Medicine Page

Display medicine information.

Show batches and available stock.

Edit button.

Update Medicine Page

Edit medicine details.

Save Changes and Cancel buttons.

Delete Medicine

Show confirmation dialog.

Deactivate medicines linked to transactions instead of deleting them.

6. Category Management

Category Index Page

Display categories in a table.

Search categories.

Add, Edit, Delete buttons.

Create Category Page

Category Name

Description (optional)

Save and Cancel buttons.

Update Category Page

Edit category details.

Save Changes button.

Delete Category

Show confirmation dialog.

Prevent deletion of categories used by medicines.

7. Supplier Management

Supplier Index Page

Display suppliers in a table.

Columns: Name, Phone, Email, Address, Actions.

Search suppliers.

Add Supplier button.

View, Edit, Delete buttons.

Create Supplier Page

Supplier Name

Contact Person

Phone Number

Email Address

Address

Save and Cancel buttons.

View Supplier Page

Display supplier information.

Show supplied medicines and batches.

Update Supplier Page

Edit supplier details.

Save Changes button.

Delete Supplier

Show confirmation dialog.

Deactivate suppliers linked to stock records.

8. Stock Management

Stock Index Page

Display stock records in a table.

Columns: Medicine, Batch Number, Quantity, Expiry Date, Supplier, Actions.

Search by medicine or batch.

Filter by stock status.

Add Stock button.

View and Edit buttons.

Create Stock Page

Select Medicine

Select Supplier

Batch Number

Quantity

Purchase Price

Expiry Date

Save and Cancel buttons.

View Stock Page

Display batch information.

Show available quantity and expiry status.

Update Stock Page

Edit permitted batch details.

Record stock quantity corrections with a reason.

Save Changes button.

Delete Stock

Show confirmation dialog.

Delete only unused stock records.

Use stock adjustments for existing records.

9. Sales / POS Management

Sales Index Page

Display sales transactions.

Columns: Invoice Number, Date, Total Amount, Payment Method, Actions.

Search by invoice number.

New Sale button.

View Sale and Print Invoice buttons.

Create Sale (POS) Page

Search and select medicines.

Select available, non-expired batches.

Enter quantity.

Add medicines to cart.

Calculate total automatically.

Select Cash or Card payment.

Complete Sale button.

Automatically update stock.

Generate invoice.

View Sale Page

Display invoice number and date.

Show sold medicines and quantities.

Display total and payment method.

Print or Export Invoice button.

Update / Delete Sale

Do not directly edit or delete completed sales.

Allow authorized voids and refunds.

Keep transaction history.

10. Invoice Management

Invoice Index Page

Display all invoices.

Search by invoice number or date.

View and Print buttons.

Invoice View Page

Pharmacy Name and Address

Invoice Number and Date

Medicine Details

Quantity and Unit Price

Total Amount

Payment Method

Other Features:

Automatically generate invoices after sales.

Export invoices as PDF.

Print invoices.

No separate Create, Update, or Delete page is required.

11. Expiry Management

Expiry Index Page

Display medicine expiry records.

Columns: Medicine, Batch Number, Quantity, Expiry Date, Status.

Search medicines.

Filter by Expiring Soon or Expired.

Highlight medicines approaching expiry.

Identify expired batches.

Prevent expired stock from being sold.

Open related stock batch details.

No separate Create or Delete page is required.

12. AI Stock Prediction

AI Prediction Dashboard

Select Medicine.

Select 7-day or 30-day prediction.

Predict medicine demand using ML.NET.

Display predicted quantity.

Display prediction chart using LiveCharts2.

Highlight possible stock shortages.

Use historical sales data. Show predictions as estimates only. Do not automatically purchase or dispense medicines.

13. Reports

Reports Index Page

Sales Report

Stock Report

Expiry Report

Supplier Report

Features:

Select report type.

Filter by date.

Display report in a table.

Export to PDF or CSV.

14. Profile Management

Profile View Page

Full Name

Email Address

Phone Number

Pharmacy Name

Update Profile Page

Edit personal information.

Change Password.

Save Changes button.

15. Settings

Settings Page

Pharmacy Name

Pharmacy Address

Pharmacy Contact Number

Currency (LKR)

Low Stock Alert Quantity

Expiry Warning Period (default: 30 days)

Light / Dark Theme

Database Backup

Database Restore

Logout

General Requirements

Use SQLite for storing application data.

Connect every page to real database records.

Use reusable WPF components.

Include search and validation where needed.

Display confirmation messages before deletion.

Keep sales and stock history accurate.

Use a consistent green and white design.

Keep navigation simple with a sidebar.

Make the application work offline.
