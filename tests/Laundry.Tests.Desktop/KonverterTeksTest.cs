using System.Windows;
using Laundry.Desktop.Services;
using Xunit;

namespace Laundry.Tests.Desktop;

/// <summary>
/// Teks yang dibaca kasir dirakit oleh Format dan converter kecil ini. Salah tangani satu kasus
/// (bool mentah, angka halaman nol-based, nama status bergaris bawah) langsung terlihat di layar.
/// </summary>
public class KonverterTeksTest
{
    [Theory]
    [InlineData("belum_dikonfigurasi", "Belum Dikonfigurasi")]
    [InlineData("total_direvisi", "Total Direvisi")]
    [InlineData("DitimbangDanDiverifikasi", "Ditimbang Dan Diverifikasi")]
    [InlineData("SiapDiambil", "Siap Diambil")]
    [InlineData("DpSebagian", "DP Sebagian")]
    public void LabelMembacaNamaStatusIndonesia(string mentah, string harapan) =>
        Assert.Equal(harapan, Format.Label(mentah));

    [Fact]
    public void BoolPunyaTeksSendiriBukanTrueFalse()
    {
        var konverter = new YaTidakConverter();
        Assert.Equal("Ya", konverter.Convert(true, typeof(string), null, null!));
        Assert.Equal("Tidak", konverter.Convert(false, typeof(string), null, null!));
        Assert.Equal("Aktif", konverter.Convert(true, typeof(string), "Aktif|Nonaktif", null!));
        Assert.Equal("Nonaktif", konverter.Convert(false, typeof(string), "Aktif|Nonaktif", null!));
        Assert.Equal("-", konverter.Convert(null, typeof(string), null, null!));
    }

    [Fact]
    public void HalamanPertamaTerbacaSatu()
    {
        var konverter = new TambahConverter();
        Assert.Equal(1, konverter.Convert(0, typeof(int), null, null!));
        Assert.Equal(3, konverter.Convert(0, typeof(int), "3", null!));
    }

    [Theory]
    [InlineData("1", Visibility.Visible)]
    [InlineData("", Visibility.Collapsed)]
    public void KosongMengaturKelayakanTampilan(string teks, Visibility harapan) =>
        Assert.Equal(harapan, new KosongConverter().Convert(teks, typeof(string), null, null!));
}
