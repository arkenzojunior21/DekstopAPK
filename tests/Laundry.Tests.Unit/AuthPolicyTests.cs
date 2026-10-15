using Laundry.Application.Auth;
using Laundry.Domain;
using Laundry.Infrastructure.Security;

namespace Laundry.Tests.Unit;

public class AuthPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(1, null)]
    [InlineData(4, null)]
    [InlineData(5, 15)]
    [InlineData(6, 15)]
    public void LockoutTepatLimaKegagalan(int gagal, int? menitKunci)
    {
        var until = LockoutPolicy.NewLockoutUntil(gagal, Now);
        Assert.Equal(menitKunci is null ? (DateTime?)null : Now.AddMinutes(menitKunci.Value), until);
    }

    [Fact]
    public void LockoutBerakhirSetelah15Menit()
    {
        var until = Now.AddMinutes(15);
        Assert.True(LockoutPolicy.IsLocked(until, Now.AddMinutes(14)));
        Assert.False(LockoutPolicy.IsLocked(until, Now.AddMinutes(15)));
        Assert.False(LockoutPolicy.IsLocked(null, Now));
    }

    [Theory]
    [InlineData("081234567890", "6281234567890")]
    [InlineData("+62 812-3456-7890", "6281234567890")]
    [InlineData("6281234567890", "6281234567890")]
    [InlineData("006281234567890", "6281234567890")]
    public void NormalisasiNomor(string masuk, string keluar)
        => Assert.Equal(keluar, PhoneNormalizer.Normalize(masuk));

    [Theory]
    [InlineData("123")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void NomorTidakValidDitolak(string? masuk) => Assert.Null(PhoneNormalizer.Normalize(masuk));

    [Fact]
    public void OtpKedaluwarsaDanPercobaanHabis()
    {
        Assert.True(OtpPolicy.IsExpired(Now.AddMinutes(-1), Now));
        Assert.False(OtpPolicy.IsExpired(Now.AddMinutes(1), Now));
        Assert.True(OtpPolicy.TooManyAttempts(5));
        Assert.False(OtpPolicy.TooManyAttempts(4));
        Assert.False(OtpPolicy.ResendAllowed(Now.AddSeconds(-30), Now));
        Assert.True(OtpPolicy.ResendAllowed(Now.AddSeconds(-61), Now));
        Assert.True(OtpPolicy.ResendAllowed(null, Now));
    }

    [Fact]
    public void PasswordMinimalDelapan()
    {
        Assert.False(PhoneNormalizer.IsPasswordAcceptable("1234567"));
        Assert.True(PhoneNormalizer.IsPasswordAcceptable("12345678"));
    }

    [Fact]
    public void Pbkdf2HashVerify()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.Hash("RahasiaKu!2026");
        Assert.NotEqual("RahasiaKu!2026", hash);
        Assert.True(hasher.Verify(hash, "RahasiaKu!2026"));
        Assert.False(hasher.Verify(hash, "salah"));
        Assert.False(hasher.Verify("bukan-hash", "apa"));
    }

    [Fact]
    public void RefreshTokenHashDeterministikPanjang64()
    {
        var a = JwtTokenService.HashRefreshToken("token-sama");
        var b = JwtTokenService.HashRefreshToken("token-sama");
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
    }
}
