using System.Windows.Media;

namespace Laundry.Desktop.Services;

/// <summary>Kotak angka di beranda. Nilainya sudah diformat agar XAML tidak perlu konverter khusus.</summary>
public sealed record Kartu(string Judul, string Nilai, string Keterangan, Brush Aksen);
