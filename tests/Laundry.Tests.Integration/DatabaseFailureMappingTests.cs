using Microsoft.EntityFrameworkCore;

namespace Laundry.Tests.Integration;

/// <summary>
/// Peta galat API: kegagalan koneksi database harus dikenali supaya layar tidak menampilkan
/// pesan bahasa Inggris dari EF Core dengan status 400, sementara galat aturan bisnis
/// (InvalidOperationException berbahasa Indonesia) tetap diteruskan apa adanya.
/// </summary>
public sealed class DatabaseFailureMappingTests
{
    [Fact]
    public void KegagalanKoneksiDikenaliBahkanSaatTerbungkusRapi()
    {
        var rantai = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.",
            new DbUpdateException(
                "An error occurred while executing the command definition.",
                new InvalidOperationException("Failed to connect", new TimeoutException("The operation has timed out."))));

        Assert.True(Program.DatabaseTidakTerhubung(rantai));
    }

    [Fact]
    public void GalatAturanBisnisTidakDianggapMasalahKoneksi()
    {
        Assert.False(Program.DatabaseTidakTerhubung(new InvalidOperationException("Admin sudah ada; bootstrap ditutup.")));
        Assert.False(Program.DatabaseTidakTerhubung(new KeyNotFoundException("Pesanan tidak ditemukan.")));
        Assert.False(Program.DatabaseTidakTerhubung(null));
    }
}
