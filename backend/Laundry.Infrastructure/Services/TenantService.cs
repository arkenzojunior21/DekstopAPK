using Laundry.Application.Core;
using Laundry.Domain;
using Laundry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Laundry.Infrastructure.Services;

public sealed class TenantService(LaundryDbContext db) : ITenantService
{
    public async Task<List<TenantDto>> ListAsync(CancellationToken ct)
    {
        return await db.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Nama)
            .Select(t => new TenantDto(t.Id, t.Code, t.Nama, t.Alamat, t.Latitude, t.Longitude,
                t.Telepon, t.Email, t.LogoUrl, t.TemaWarna, t.Status, t.CreatedAtUtc, t.ExpiredAtUtc))
            .ToListAsync(ct);
    }

    public async Task<TenantDto> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t == null) throw new KeyNotFoundException($"Tenant {id} tidak ditemukan");
        return new TenantDto(t.Id, t.Code, t.Nama, t.Alamat, t.Latitude, t.Longitude,
            t.Telepon, t.Email, t.LogoUrl, t.TemaWarna, t.Status, t.CreatedAtUtc, t.ExpiredAtUtc);
    }

    public async Task<TenantDto> CreateAsync(CreateTenantRequest req, CancellationToken ct)
    {
        if (await db.Tenants.AnyAsync(x => x.Code == req.Code, ct))
            throw new InvalidOperationException($"Kode tenant '{req.Code}' sudah dipakai");

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Code = req.Code.Trim().ToUpperInvariant(),
            Nama = req.Nama.Trim(),
            Alamat = req.Alamat?.Trim(),
            Latitude = req.Latitude,
            Longitude = req.Longitude,
            Telepon = req.Telepon?.Trim(),
            Email = req.Email?.Trim(),
            LogoUrl = req.LogoUrl?.Trim(),
            TemaWarna = req.TemaWarna?.Trim(),
            Status = TenantStatus.Trial,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiredAtUtc = req.ExpiredAtUtc ?? DateTime.UtcNow.AddDays(30),
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        return new TenantDto(tenant.Id, tenant.Code, tenant.Nama, tenant.Alamat, tenant.Latitude, tenant.Longitude,
            tenant.Telepon, tenant.Email, tenant.LogoUrl, tenant.TemaWarna, tenant.Status, tenant.CreatedAtUtc, tenant.ExpiredAtUtc);
    }

    public async Task<TenantDto> UpdateAsync(Guid id, UpdateTenantRequest req, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (tenant == null) throw new KeyNotFoundException($"Tenant {id} tidak ditemukan");

        tenant.Nama = req.Nama.Trim();
        tenant.Alamat = req.Alamat?.Trim();
        tenant.Latitude = req.Latitude;
        tenant.Longitude = req.Longitude;
        tenant.Telepon = req.Telepon?.Trim();
        tenant.Email = req.Email?.Trim();
        tenant.LogoUrl = req.LogoUrl?.Trim();
        tenant.TemaWarna = req.TemaWarna?.Trim();
        if (req.ExpiredAtUtc.HasValue) tenant.ExpiredAtUtc = req.ExpiredAtUtc.Value;

        await db.SaveChangesAsync(ct);

        return new TenantDto(tenant.Id, tenant.Code, tenant.Nama, tenant.Alamat, tenant.Latitude, tenant.Longitude,
            tenant.Telepon, tenant.Email, tenant.LogoUrl, tenant.TemaWarna, tenant.Status, tenant.CreatedAtUtc, tenant.ExpiredAtUtc);
    }

    public async Task<TenantDto> SetStatusAsync(Guid id, TenantStatus status, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (tenant == null) throw new KeyNotFoundException($"Tenant {id} tidak ditemukan");

        tenant.Status = status;
        await db.SaveChangesAsync(ct);

        return new TenantDto(tenant.Id, tenant.Code, tenant.Nama, tenant.Alamat, tenant.Latitude, tenant.Longitude,
            tenant.Telepon, tenant.Email, tenant.LogoUrl, tenant.TemaWarna, tenant.Status, tenant.CreatedAtUtc, tenant.ExpiredAtUtc);
    }
}