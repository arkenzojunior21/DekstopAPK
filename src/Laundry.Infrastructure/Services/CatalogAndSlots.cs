using Laundry.Application.Core;
using Laundry.Domain;
using Laundry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Laundry.Infrastructure.Services;

public sealed class CatalogService(LaundryDbContext db, SettingsStore settings) : ICatalogService
{
    public const string DeliveryFeeKey = "delivery_fee_idr";

    public async Task<List<ServiceDto>> ListAsync(bool activeOnly, CancellationToken ct)
    {
        var query = db.Services.Include(s => s.PriceVersions).AsQueryable();
        if (activeOnly) query = query.Where(s => s.IsActive);
        var now = DateTime.UtcNow;
        var list = await query.OrderBy(s => s.Name).ToListAsync(ct);
        return list.Select(s =>
        {
            var p = s.PriceVersions.Where(v => v.ValidFromUtc <= now)
                .OrderByDescending(v => v.ValidFromUtc).FirstOrDefault();
            return new ServiceDto(s.Id, s.Name, s.UnitType, s.MinimumWeightKg, s.IsActive,
                p?.Price ?? 0m, p?.StandardDurationDays ?? 3, p?.ExpressDurationDays ?? 1,
                p?.ExpressSurcharge ?? 0m);
        }).ToList();
    }

    public async Task<ServiceDto> UpsertAsync(UpsertServiceRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Nama))
            throw new InvalidOperationException("Nama layanan wajib diisi.");
        if (req.Price < 0) throw new InvalidOperationException("Tarif tidak boleh negatif.");
        if (req.UnitType == ServiceUnitType.PerKg && req.MinimumWeightKg is < 0)
            throw new InvalidOperationException("Minimum berat tidak boleh negatif.");

        var service = await db.Services.Include(s => s.PriceVersions)
            .FirstOrDefaultAsync(s => s.Name == req.Nama, ct);
        if (service is null)
        {
            service = new Service { Name = req.Nama.Trim(), CreatedAtUtc = DateTime.UtcNow };
            db.Services.Add(service);
        }
        service.UnitType = req.UnitType;
        service.MinimumWeightKg = req.UnitType == ServiceUnitType.PerKg ? req.MinimumWeightKg : null;
        service.IsActive = req.IsActive;

        var current = service.PriceVersions.OrderByDescending(p => p.ValidFromUtc).FirstOrDefault();
        if (current is null || current.Price != req.Price
            || current.StandardDurationDays != req.StandardDurationDays
            || current.ExpressDurationDays != req.ExpressDurationDays
            || current.ExpressSurcharge != req.ExpressSurcharge)
        {
            // Perubahan tarif = versi baru; order lama memakai snapshot, tidak berubah (PRD 6.4).
            service.PriceVersions.Add(new ServicePriceVersion
            {
                ServiceId = service.Id,
                Price = req.Price,
                StandardDurationDays = req.StandardDurationDays,
                ExpressDurationDays = req.ExpressDurationDays,
                ExpressSurcharge = req.ExpressSurcharge,
                ValidFromUtc = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync(ct);
        var now = DateTime.UtcNow;
        var p = service.EffectivePrice(now);
        return new ServiceDto(service.Id, service.Name, service.UnitType, service.MinimumWeightKg,
            service.IsActive, p.Price, p.StandardDurationDays, p.ExpressDurationDays, p.ExpressSurcharge);
    }

    public Task<decimal> GetDeliveryFeeAsync(CancellationToken ct) =>
        settings.GetDecimalAsync(DeliveryFeeKey, 0m, ct);

    public Task SetDeliveryFeeAsync(decimal fee, CancellationToken ct)
    {
        if (fee < 0) throw new InvalidOperationException("Biaya antar-jemput tidak boleh negatif.");
        return settings.SetDecimalAsync(DeliveryFeeKey, fee, ct);
    }
}

public sealed class SlotService(LaundryDbContext db) : ISlotService
{
    public async Task<List<SlotDto>> ListAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
        (await db.PickupDeliverySlots
            .Where(s => s.StartsAtUtc >= fromUtc && s.StartsAtUtc <= toUtc)
            .OrderBy(s => s.StartsAtUtc).ToListAsync(ct))
        .Select(s => new SlotDto(s.Id, s.StartsAtUtc, s.EndsAtUtc, s.Capacity, s.BookedCount, s.IsActive))
        .ToList();

    public async Task<SlotDto> CreateAsync(CreateSlotRequest req, CancellationToken ct)
    {
        if (req.EndsAtUtc <= req.StartsAtUtc)
            throw new InvalidOperationException("Akhir slot harus setelah awal slot.");
        if (req.Capacity < 1) throw new InvalidOperationException("Kapasitas slot minimal 1.");
        var slot = new PickupDeliverySlot
        {
            StartsAtUtc = req.StartsAtUtc,
            EndsAtUtc = req.EndsAtUtc,
            Capacity = req.Capacity,
        };
        db.PickupDeliverySlots.Add(slot);
        await db.SaveChangesAsync(ct);
        return new SlotDto(slot.Id, slot.StartsAtUtc, slot.EndsAtUtc, slot.Capacity, slot.BookedCount, slot.IsActive);
    }
}

public sealed class CustomerService(LaundryDbContext db, EventWriter events) : ICustomerService
{
    private static CustomerDto Map(User u) => new(
        u.Profile!.Id, u.Profile.Nama, u.NormalizedPhone, u.Profile.Alamat, u.IsActive, u.CreatedAtUtc);

    public async Task<CustomerDto> GetOrCreateAsync(Guid? customerId, string? nama, string? phone, CancellationToken ct)
    {
        if (customerId is not null)
        {
            var existing = await db.CustomerProfiles.Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == customerId, ct);
            if (existing?.User is null) throw new KeyNotFoundException("Pelanggan tidak ditemukan.");
            return Map(existing.User);
        }

        var normalized = Laundry.Application.Auth.PhoneNormalizer.Normalize(phone);
        if (normalized is null) throw new InvalidOperationException("Nomor WhatsApp walk-in tidak valid.");
        if (string.IsNullOrWhiteSpace(nama)) throw new InvalidOperationException("Nama pelanggan wajib diisi.");

        var dup = await db.Users.AnyAsync(u => u.NormalizedPhone == normalized, ct);
        if (dup) throw new InvalidOperationException("Nomor sudah terdaftar sebagai akun pelanggan.");

        // Akun walk-in: belum bisa login (belum registrasi OTP) — PasswordHash kosong.
        var user = new User
        {
            Role = UserRole.Pelanggan,
            NormalizedPhone = normalized,
            PasswordHash = string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
            Profile = new CustomerProfile { Nama = nama.Trim() },
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        events.EnqueueCustomer(user.Profile, user, OrderService.DeviceId);
        await db.SaveChangesAsync(ct);
        return Map(user);
    }

    public async Task<List<CustomerDto>> SearchAsync(string? query, int page, int pageSize, CancellationToken ct)
    {
        var q = db.Users.Include(u => u.Profile).Where(u => u.Role == UserRole.Pelanggan);
        if (!string.IsNullOrWhiteSpace(query))
            q = q.Where(u => u.Profile!.Nama.ToLower().Contains(query.ToLower())
                || u.NormalizedPhone.Contains(query));
        return await q.OrderByDescending(u => u.CreatedAtUtc)
            .Skip(page * pageSize).Take(pageSize)
            .Select(u => new CustomerDto(u.Profile!.Id, u.Profile!.Nama, u.NormalizedPhone,
                u.Profile!.Alamat, u.IsActive, u.CreatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<CustomerDto> UpdateAsync(Guid customerId, UpdateCustomerRequest req, CancellationToken ct)
    {
        var profile = await db.CustomerProfiles.Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == customerId, ct);
        if (profile?.User is null) throw new KeyNotFoundException("Pelanggan tidak ditemukan.");
        if (string.IsNullOrWhiteSpace(req.Nama)) throw new InvalidOperationException("Nama wajib diisi.");
        profile.Nama = req.Nama.Trim();
        profile.Alamat = req.Alamat?.Trim();
        await db.SaveChangesAsync(ct);
        events.EnqueueCustomer(profile, profile.User, OrderService.DeviceId);
        await db.SaveChangesAsync(ct);
        return Map(profile.User);
    }

    public async Task<CustomerDto> SetActiveAsync(Guid customerId, bool aktif, CancellationToken ct)
    {
        // Nonaktifkan = ubah status, transaksi historis tetap utuh (PRD 6.14).
        var profile = await db.CustomerProfiles.Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == customerId, ct);
        if (profile?.User is null) throw new KeyNotFoundException("Pelanggan tidak ditemukan.");
        profile.User.IsActive = aktif;
        if (!aktif)
        {
            foreach (var t in await db.RefreshTokens.Where(r => r.UserId == profile.UserId
                         && r.RevokedAtUtc == null).ToListAsync(ct))
                t.RevokedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        events.EnqueueCustomer(profile, profile.User, OrderService.DeviceId);
        await db.SaveChangesAsync(ct);
        return Map(profile.User);
    }
}
