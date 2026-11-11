using System.IO;
using System.Text.Json;

namespace Laundry.Desktop.Services;

/// <summary>
/// Preferensi mesin ini saja (alamat server, tema, nomor terakhir). Tidak ada rahasia di sini:
/// token hanya hidup di memori proses, jadi menutup aplikasi benar-benar mengakhiri sesi.
/// </summary>
public sealed class Preferensi
{
    private const string NamaFolder = "LaundryDesk";
    private static readonly string Jalur = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), NamaFolder, "preferensi.json");

    public string Server { get; set; } = "http://localhost:5104";

    public string Tema { get; set; } = "Terang";

    public string? NomorTerakhir { get; set; }

    public static Preferensi Baca()
    {
        try
        {
            if (!File.Exists(Jalur)) return new Preferensi();
            var teks = File.ReadAllText(Jalur);
            return JsonSerializer.Deserialize<Preferensi>(teks, Opsi) ?? new Preferensi();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Berkas preferensi rusak tidak boleh menutup aplikasi: mulai dari bawaan.
            return new Preferensi();
        }
    }

    private static readonly JsonSerializerOptions Opsi = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public void Simpan()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Jalur)!);
            File.WriteAllText(Jalur, JsonSerializer.Serialize(this, Opsi));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tidak bisa menulis preferensi bukan alasan untuk gagal memakai aplikasi.
            System.Diagnostics.Debug.WriteLine(ex.Message);
        }
    }
}
