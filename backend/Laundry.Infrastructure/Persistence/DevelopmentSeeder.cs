using Laundry.Domain;
using Laundry.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Laundry.Infrastructure.Persistence;

/// <summary>
/// Pengisi data contoh untuk mesin dev. Tidak pernah jalan otomatis: aktif hanya bila
/// environment Development dan variabel SEED_DEV_CONTOH=1. Angka tarif di sini hanyalah
/// placeholder agar UI punya sesuatu untuk ditampilkan — tarif usaha asli harus diisi
/// sendiri lewat API/katalog (lihat docs/OPEN_QUESTIONS.md).
/// </summary>
public static class DevelopmentSeeder
{
    private static readonly (string Nama, ServiceUnitType Unit, decimal Harga, int Standar, int Express, decimal Tambahan)[] Contoh =
    {
        ("Cuci Kering (CONTOH)", ServiceUnitType.PerKg, 6000m, 2, 1, 5000m),
        ("Cuci Setrika (CONTOH)", ServiceUnitType.PerKg, 8000m, 2, 1, 6000m),
        ("Setrika Saja (CONTOH)", ServiceUnitType.PerKg, 4000m, 1, 1, 3000m),
        ("Laundry Sepatu (CONTOH)", ServiceUnitType.PerItem, 25000m, 3, 2, 10000m),
        ("Paket Bed Cover (CONTOH)", ServiceUnitType.PaketTetap, 45000m, 3, 2, 15000m),
    };

    public static async Task SeedAsync(LaundryDbContext db, CancellationToken ct = default)
    {
        var sudahAda = await db.Services.AnyAsync(ct);
        if (!sudahAda)
        {
            var sekarang = DateTime.UtcNow;
            foreach (var (nama, unit, harga, standar, express, tambahan) in Contoh)
            {
                db.Services.Add(new Service
                {
                    Name = nama,
                    UnitType = unit,
                    MinimumWeightKg = unit == ServiceUnitType.PerKg ? 1m : null,
                    IsActive = true,
                    CreatedAtUtc = sekarang,
                    PriceVersions =
                    {
                        new ServicePriceVersion
                        {
                            Price = harga,
                            StandardDurationDays = standar,
                            ExpressDurationDays = express,
                            ExpressSurcharge = tambahan,
                            ValidFromUtc = sekarang,
                        },
                    },
                });
            }
        }

        if (!await db.AppSettings.AnyAsync(a => a.Key == CatalogService.DeliveryFeeKey, ct))
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = CatalogService.DeliveryFeeKey,
                ValueJson = System.Text.Json.JsonSerializer.Serialize(5000m),
                UpdatedAtUtc = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
