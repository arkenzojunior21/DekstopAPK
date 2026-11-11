namespace Laundry.Domain;

public enum UserRole
{
    Admin,
    Pelanggan,
}

public enum OrderStatus
{
    MenungguKonfirmasi,
    Dikonfirmasi,
    MenungguPenjemputan,
    DiterimaDiLaundry,
    DitimbangDanDiverifikasi,
    MenungguProses,
    SedangDiproses,
    Pengeringan,
    Penyetrikaan,
    QualityCheck,
    SiapDiambil,
    DalamPengantaran,
    Selesai,
    Dibatalkan,
}

/// <summary>Alur ringkas yang ditampilkan UI; tahap lengkap tetap tercatat di history.</summary>
public static class OrderStatusAlur
{
    public static IReadOnlyList<OrderStatus> TahapProses = new[]
    {
        OrderStatus.MenungguKonfirmasi,
        OrderStatus.Dikonfirmasi,
        OrderStatus.MenungguPenjemputan,
        OrderStatus.DiterimaDiLaundry,
        OrderStatus.DitimbangDanDiverifikasi,
        OrderStatus.MenungguProses,
        OrderStatus.SedangDiproses,
        OrderStatus.Pengeringan,
        OrderStatus.Penyetrikaan,
        OrderStatus.QualityCheck,
        OrderStatus.SiapDiambil,
        OrderStatus.DalamPengantaran,
        OrderStatus.Selesai,
    };

    public static bool SudahMulaiDiproses(OrderStatus status) =>
        status >= OrderStatus.DiterimaDiLaundry && status != OrderStatus.Dibatalkan;

    /// <summary>
    /// Tahap ketika cucian sudah diterima dan masih dikerjakan toko. Daftar eksplisit, bukan
    /// perbandingan enum, supaya query EF Core dapat diterjemahkan ke SQL.
    /// </summary>
    public static OrderStatus[] TahapDikerjakan =
    [
        OrderStatus.DiterimaDiLaundry, OrderStatus.MenungguProses, OrderStatus.SedangDiproses,
        OrderStatus.Pengeringan, OrderStatus.Penyetrikaan,
    ];
}

public enum PaymentStatus
{
    BelumBayar,
    DpSebagian,
    MenungguKonfirmasi,
    SebagianDibayar,
    Lunas,
    RefundSebagian,
    RefundPenuh,
    GagalAtauKedaluwarsa,
}

public enum PaymentMethod
{
    Tunai,
    Qris,
    TransferBank,
    SaldoKredit,
}

public enum PaymentKind
{
    Dp,
    Pelunasan,
}

public enum FulfillmentMethod
{
    AmbilSendiri,
    AntarJemput,
}

public enum ComplaintStatus
{
    Baru,
    Ditinjau,
    MenungguInformasi,
    Ditangani,
    Selesai,
    Ditolak,
}

public enum ServiceUnitType
{
    PerKg,
    PerItem,
    PaketTetap,
}

public enum SyncEventStatus
{
    Menunggu,
    Dikirim,
    Diproses,
    Gagal,
}

public enum NotificationStatus
{
    Menunggu,
    Terkirim,
    Gagal,
    DitundaPerluKoneksi,
}
