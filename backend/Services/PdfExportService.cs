using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MediStock.Services;

public sealed class PdfExportService
{
    public PdfExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GenerateInvoice(
        string pharmacyName,
        string pharmacyAddress,
        string pharmacyContact,
        string invoiceNumber,
        DateTime soldAtUtc,
        string paymentMethod,
        decimal total,
        IReadOnlyList<PdfInvoiceLine> lines,
        string currency)
    {
        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(32);
                page.DefaultTextStyle(style => style.FontSize(12));
                page.Content().Column(column =>
                {
                    column.Spacing(14);
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(heading =>
                        {
                            heading.Item().Text(pharmacyName).FontSize(20).Bold().FontColor(Colors.Green.Darken3);
                            heading.Item().Text(pharmacyAddress);
                            heading.Item().Text(pharmacyContact);
                        });
                        row.ConstantItem(190).Column(invoice =>
                        {
                            invoice.Item().Text("INVOICE").FontSize(16).Bold();
                            invoice.Item().Text(invoiceNumber).Bold();
                            invoice.Item().Text(soldAtUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm"));
                        });
                    });
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });
                        table.Header(header =>
                        {
                            HeaderCell(header, "Medicine");
                            HeaderCell(header, "Batch");
                            HeaderCell(header, "Qty");
                            HeaderCell(header, "Unit price");
                            HeaderCell(header, "Subtotal");
                        });
                        foreach (var line in lines)
                        {
                            BodyCell(table, line.Medicine);
                            BodyCell(table, line.Batch);
                            BodyCell(table, line.Quantity.ToString());
                            BodyCell(table, $"{currency} {line.UnitPrice:N2}");
                            BodyCell(table, $"{currency} {line.LineTotal:N2}");
                        }
                    });
                    column.Item().AlignRight().Text($"Payment: {paymentMethod}");
                    column.Item().AlignRight().Text($"Total: {currency} {total:N2}").FontSize(15).Bold();
                });
                page.Footer().AlignCenter().Text("Thank you for your purchase");
            });
        }).GeneratePdf();
    }

    public byte[] GenerateReport(
        string pharmacyName,
        string reportType,
        DateTime from,
        DateTime to,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(style => style.FontSize(10));
                page.Content().Column(column =>
                {
                    column.Spacing(8);
                    column.Item().Text(pharmacyName).FontSize(17).Bold().FontColor(Colors.Green.Darken3);
                    column.Item().Text($"{reportType} report | {from:dd MMM yyyy} - {to:dd MMM yyyy}")
                        .FontSize(13)
                        .Bold();
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            foreach (var _ in headers)
                            {
                                columns.RelativeColumn();
                            }
                        });
                        table.Header(header =>
                        {
                            foreach (var text in headers)
                            {
                                HeaderCell(header, text);
                            }
                        });
                        foreach (var row in rows)
                        {
                            foreach (var cell in row)
                            {
                                BodyCell(table, cell);
                            }
                        }
                    });
                });
                page.Footer().AlignRight().Text(text =>
                {
                    text.Span("Generated ");
                    text.Span(DateTime.Now.ToString("g"));
                });
            });
        }).GeneratePdf();
    }

    private static void HeaderCell(TableCellDescriptor header, string text) =>
        header.Cell().Background(Colors.Green.Darken3).Padding(5).Text(text).FontColor(Colors.White).Bold();

    private static void BodyCell(TableDescriptor table, string text) =>
        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(text);
}

public sealed record PdfInvoiceLine(string Medicine, string Batch, int Quantity, decimal UnitPrice, decimal LineTotal);
