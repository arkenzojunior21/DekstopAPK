using System.Runtime.CompilerServices;

namespace Laundry.Tests.Integration;

/// <summary>
/// Test integrasi memakai instance PostgreSQL dev di laptop (5433 admin, 5434 cloud),
/// bukan database cloud yang sekarang ditunjuk .env. Variabel lingkungan proses menang atas
/// .env di EnvironmentConfig.Get, jadi nilai di sini mengunci arah koneksi sebelum kelas tes
/// mana pun sempat membaca .env.
/// </summary>
internal static class TestLingkungan
{
    internal const string AdminLokal =
        "Host=localhost;Port=5433;Database=laundry_admin;Username=laundry;Password=laundry_dev_2026";

    internal const string CloudLokal =
        "Host=localhost;Port=5434;Database=laundry_cloud;Username=laundry;Password=laundry_dev_2026";

    [ModuleInitializer]
    internal static void KunciKeLokal()
    {
        Pasang("CONN_ADMIN", AdminLokal);
        Pasang("CONN_CLOUD", CloudLokal);
    }

    private static void Pasang(string kunci, string nilaiLokal)
    {
        var sudah = Environment.GetEnvironmentVariable(kunci);
        if (!string.IsNullOrWhiteSpace(sudah))
        {
            if (!sudah.Contains("localhost", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"{kunci} menunjuk ke luar localhost. Test integrasi dilarang menjalankan " +
                    "KosongkanAsync/DROP DATABASE terhadap database cloud.");
            return;
        }

        Environment.SetEnvironmentVariable(kunci, nilaiLokal);
    }
}
