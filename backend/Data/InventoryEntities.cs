namespace MediStock.Data;

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Medicine> Medicines { get; set; } = new List<Medicine>();
}

public sealed class Medicine
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public string? Brand { get; set; }
    public decimal SellingPrice { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public ICollection<StockBatch> StockBatches { get; set; } = new List<StockBatch>();
    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
}

public sealed class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; }
    public ICollection<StockBatch> StockBatches { get; set; } = new List<StockBatch>();
}

public sealed class StockBatch
{
    public int Id { get; set; }
    public int MedicineId { get; set; }
    public Medicine Medicine { get; set; } = null!;
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public string BatchNumber { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal PurchasePrice { get; set; }
    public DateTime ExpiryDate { get; set; }
    public DateTime CreatedUtc { get; set; }
    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
}

public sealed class Sale
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime SoldAtUtc { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string Status { get; set; } = "Completed";
    public string? VoidReason { get; set; }
    public DateTime? RefundedAtUtc { get; set; }
    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
    public RefundHistory? RefundHistory { get; set; }
}

public sealed class SaleItem
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public int MedicineId { get; set; }
    public Medicine Medicine { get; set; } = null!;
    public int StockBatchId { get; set; }
    public StockBatch StockBatch { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class StockAdjustment
{
    public int Id { get; set; }
    public int StockBatchId { get; set; }
    public StockBatch StockBatch { get; set; } = null!;
    public int QuantityChange { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime AdjustedAtUtc { get; set; }
}

public sealed class RefundHistory
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string AuthorizedUser { get; set; } = string.Empty;
    public string AuthorizedUserEmail { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
