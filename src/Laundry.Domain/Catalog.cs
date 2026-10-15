namespace Laundry.Domain;

public class Service
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ServiceUnitType UnitType { get; set; }
    public decimal? MinimumWeightKg { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public List<ServicePriceVersion> PriceVersions { get; set; } = new();

    public ServicePriceVersion EffectivePrice(DateTime atUtc) =>
        PriceVersions.Where(p => p.ValidFromUtc <= atUtc)
            .OrderByDescending(p => p.ValidFromUtc)
            .FirstOrDefault()
        ?? throw new InvalidOperationException($"Layanan '{Name}' belum punya tarif berlaku.");
}

public class ServicePriceVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServiceId { get; set; }
    /// <summary>Harga per kg (unit PerKg) atau per item (PerItem) atau harga tetap paket.</summary>
    public decimal Price { get; set; }
    public int StandardDurationDays { get; set; } = 3;
    public int ExpressDurationDays { get; set; } = 1;
    public decimal ExpressSurcharge { get; set; }
    public DateTime ValidFromUtc { get; set; }
}
