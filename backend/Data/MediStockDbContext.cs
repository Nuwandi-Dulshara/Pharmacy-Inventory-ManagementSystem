using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MediStock.Data;

public sealed class MediStockDbContext(DbContextOptions<MediStockDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<StockBatch> StockBatches => Set<StockBatch>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var moneyConverter = new ValueConverter<decimal, long>(
            value => decimal.ToInt64(decimal.Round(value * 100m, MidpointRounding.AwayFromZero)),
            value => value / 100m);

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasIndex(category => category.Name).IsUnique();
            entity.Property(category => category.Name).UseCollation("NOCASE");
        });

        modelBuilder.Entity<Medicine>(entity =>
        {
            entity.ToTable("Medicines");
            entity.Property(medicine => medicine.SellingPrice).HasConversion(moneyConverter).HasColumnType("INTEGER");
            entity.HasOne(medicine => medicine.Category)
                .WithMany(category => category.Medicines)
                .HasForeignKey(medicine => medicine.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Supplier>(entity => entity.ToTable("Suppliers"));

        modelBuilder.Entity<StockBatch>(entity =>
        {
            entity.ToTable("StockBatches");
            entity.HasIndex(batch => new { batch.MedicineId, batch.BatchNumber }).IsUnique();
            entity.Property(batch => batch.PurchasePrice).HasConversion(moneyConverter).HasColumnType("INTEGER");
            entity.HasOne(batch => batch.Medicine)
                .WithMany(medicine => medicine.StockBatches)
                .HasForeignKey(batch => batch.MedicineId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(batch => batch.Supplier)
                .WithMany(supplier => supplier.StockBatches)
                .HasForeignKey(batch => batch.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Sale>(entity =>
        {
            entity.ToTable("Sales");
            entity.HasIndex(sale => sale.InvoiceNumber).IsUnique();
            entity.Property(sale => sale.TotalAmount).HasConversion(moneyConverter).HasColumnType("INTEGER");
        });

        modelBuilder.Entity<SaleItem>(entity =>
        {
            entity.ToTable("SaleItems");
            entity.Property(item => item.UnitPrice).HasConversion(moneyConverter).HasColumnType("INTEGER");
            entity.Property(item => item.LineTotal).HasConversion(moneyConverter).HasColumnType("INTEGER");
            entity.HasOne(item => item.Sale)
                .WithMany(sale => sale.Items)
                .HasForeignKey(item => item.SaleId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Medicine)
                .WithMany(medicine => medicine.SaleItems)
                .HasForeignKey(item => item.MedicineId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.StockBatch)
                .WithMany(batch => batch.SaleItems)
                .HasForeignKey(item => item.StockBatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockAdjustment>(entity =>
        {
            entity.ToTable("StockAdjustments");
            entity.HasOne(adjustment => adjustment.StockBatch)
                .WithMany()
                .HasForeignKey(adjustment => adjustment.StockBatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.ToTable("AppSettings");
            entity.HasKey(setting => setting.Key);
        });
    }
}
