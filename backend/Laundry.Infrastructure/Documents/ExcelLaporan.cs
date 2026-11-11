using ClosedXML.Excel;
using Laundry.Application.Core;

namespace Laundry.Infrastructure.Documents;

/// <summary>
/// XLSX lewat ClosedXML (MIT): berkas laporan harus bisa dihitung ulang oleh pembacanya, jadi
/// angka ditulis sebagai angka sungguhan dengan format mata uang, bukan teks "Rp1.000".
/// </summary>
public static class ExcelLaporan
{
    private const string FormatUang = "\"Rp\"#,##0";
    private const string FormatBilangan = "#,##0";
    private const string FormatKuantitas = "#,##0.00";

    public static byte[] Buat(TabelLaporan tabel)
    {
        using var workbook = new XLWorkbook();
        var lembar = workbook.Worksheets.Add(NamaLembar(tabel.Judul));

        lembar.Cell(1, 1).Value = tabel.Judul;
        lembar.Cell(1, 1).Style.Font.Bold = true;
        lembar.Cell(1, 1).Style.Font.FontSize = 14;
        lembar.Cell(2, 1).Value = tabel.Subjudul;
        lembar.Cell(2, 1).Style.Font.Italic = true;

        var awal = 4;
        for (var i = 0; i < tabel.Kolom.Count; i++)
        {
            var sel = lembar.Cell(awal, i + 1);
            sel.Value = tabel.Kolom[i];
            sel.Style.Font.Bold = true;
            sel.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        var baris = awal + 1;
        foreach (var row in tabel.Baris)
        {
            for (var i = 0; i < row.Count && i < tabel.Kolom.Count; i++)
            {
                var sel = row[i];
                var tujuan = lembar.Cell(baris, i + 1);
                if (sel.IsAngka && sel.Angka is { } angka)
                {
                    tujuan.Value = angka;
                    tujuan.Style.NumberFormat.Format = sel.Jenis switch
                    {
                        JenisSel.Uang => FormatUang,
                        JenisSel.Kuantitas => FormatKuantitas,
                        _ => FormatBilangan,
                    };
                }
                else
                {
                    tujuan.Value = sel.Isi;
                }
            }

            baris++;
        }

        lembar.Columns().AdjustToContents();
        if (baris > awal + 1)
        {
            // Baris judul kolom dibekukan supaya tabel panjang tidak kehilangan konteks saat digeser.
            lembar.SheetView.FreezeRows(awal);
        }

        using var memori = new MemoryStream();
        workbook.SaveAs(memori);
        return memori.ToArray();
    }

    private static string NamaLembar(string judul)
    {
        var bersih = new string(judul.Where(char.IsLetterOrDigit).ToArray());
        if (bersih.Length == 0) bersih = "Laporan";
        return bersih.Length > 31 ? bersih[..31] : bersih;
    }
}
