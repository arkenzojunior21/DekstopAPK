using System.Collections.Concurrent;
using System.Threading;

namespace Laundry.Tests.Desktop;

/// <summary>
/// Semua objek WPF menolak dibuat atau disentuh dari thread MTA, padahal runner test berjalan MTA.
/// Helper ini menjalankan seluruh pekerjaan XAML di satu thread STA milik sendiri (satu-satunya
/// tempat Application dibuat) dan meneruskan galatnya kembali ke thread test.
/// </summary>
internal static class JalankanDiSta
{
    private static readonly BlockingCollection<(Action Kerja, ManualResetEventSlim Selesai)> Antrean = new();

    static JalankanDiSta()
    {
        var thread = new Thread(Kerjakan)
        {
            IsBackground = true,
            Name = "sta-xaml",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void Kerjakan()
    {
        _ = new System.Windows.Application();
        foreach (var (kerja, selesai) in Antrean.GetConsumingEnumerable())
        {
            try
            {
                kerja();
            }
            finally
            {
                selesai.Set();
            }
        }
    }

    public static void Aksi(Action pekerjaan)
    {
        Exception? galat = null;
        var selesai = new ManualResetEventSlim(false);
        Antrean.Add((() =>
        {
            try
            {
                pekerjaan();
            }
            catch (Exception ex)
            {
                galat = ex;
            }
        }, selesai));

        Assert.True(selesai.Wait(TimeSpan.FromSeconds(30)), "Pekerjaan XAML tidak selesai dalam 30 detik.");
        if (galat is not null) throw galat;
    }

    public static T Nilai<T>(Func<T> pekerjaan)
    {
        T hasil = default!;
        Aksi(() => hasil = pekerjaan());
        return hasil;
    }
}
