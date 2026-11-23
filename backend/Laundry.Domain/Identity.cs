using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Laundry.Domain;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [MaxLength(32)]
    public string Code { get; set; } = string.Empty; // unik, slug: "laundry-jaya", "cuci-cepat"
    
    [MaxLength(128)]
    public string Nama { get; set; } = string.Empty;
    
    public string? Alamat { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Telepon { get; set; }
    public string? Email { get; set; }
    public string? LogoUrl { get; set; }
    public string? TemaWarna { get; set; } // hex color untuk branding tenant
    
    public TenantStatus Status { get; set; } = TenantStatus.Aktif;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiredAtUtc { get; set; } // untuk trial/berlangganan
    
    public List<User> Users { get; set; } = new();
}

public enum TenantStatus
{
    Aktif,
    Nonaktif,
    Trial,
    Expired,
    Diblokir,
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public UserRole Role { get; set; }
    
    /// <summary>FK ke Tenant (null untuk PlatformAdmin).</summary>
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    
    /// <summary>Nomor WhatsApp format internasional ternormalisasi (62...).</summary>
    public string NormalizedPhone { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public CustomerProfile? Profile { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutUntilUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
}

public class CustomerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Nama { get; set; } = string.Empty;
    public string? Alamat { get; set; }
    public bool IsDeleted { get; set; }
}

public class OtpChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Purpose { get; set; } = string.Empty; // registrasi | reset_password
    public string TargetPhone { get; set; } = string.Empty;
    public string OtpHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime? LastResendAtUtc { get; set; }
    public bool IsUsed { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string ReplacedByHash { get; set; } = string.Empty;
}
