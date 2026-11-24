using System.ComponentModel.DataAnnotations;

namespace MediStock.Services;

public sealed class MedicineFormInput
{
    [Required]
    [StringLength(160)]
    public string Name { get; set; } = string.Empty;

    [StringLength(160)]
    public string? GenericName { get; set; }

    public int? CategoryId { get; set; }

    [StringLength(160)]
    public string? Brand { get; set; }

    [Range(typeof(decimal), "0.01", "999999999")]
    public decimal SellingPrice { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }
}
