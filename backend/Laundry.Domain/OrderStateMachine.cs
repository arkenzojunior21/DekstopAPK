namespace Laundry.Domain;

/// <summary>
/// State machine status pesanan (PRD 6.7). Transisi eksplisit, tanpa teks bebas.
/// Aturan pembayaran/penyerahan dievaluasi terpisah dari transisi murni.
/// </summary>
public static class OrderStateMachine
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.MenungguKonfirmasi] = new[] { OrderStatus.Dikonfirmasi, OrderStatus.Dibatalkan },
        [OrderStatus.Dikonfirmasi] = new[] { OrderStatus.MenungguPenjemputan, OrderStatus.DiterimaDiLaundry, OrderStatus.Dibatalkan },
        [OrderStatus.MenungguPenjemputan] = new[] { OrderStatus.DiterimaDiLaundry, OrderStatus.Dibatalkan },
        [OrderStatus.DiterimaDiLaundry] = new[] { OrderStatus.DitimbangDanDiverifikasi },
        [OrderStatus.DitimbangDanDiverifikasi] = new[] { OrderStatus.MenungguProses },
        [OrderStatus.MenungguProses] = new[] { OrderStatus.SedangDiproses },
        [OrderStatus.SedangDiproses] = new[] { OrderStatus.Pengeringan },
        [OrderStatus.Pengeringan] = new[] { OrderStatus.Penyetrikaan },
        [OrderStatus.Penyetrikaan] = new[] { OrderStatus.QualityCheck },
        [OrderStatus.QualityCheck] = new[] { OrderStatus.SiapDiambil },
        [OrderStatus.SiapDiambil] = new[] { OrderStatus.DalamPengantaran, OrderStatus.Selesai },
        [OrderStatus.DalamPengantaran] = new[] { OrderStatus.Selesai },
        [OrderStatus.Selesai] = Array.Empty<OrderStatus>(),
        [OrderStatus.Dibatalkan] = Array.Empty<OrderStatus>(),
    };

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<OrderStatus> Next(OrderStatus from) => Allowed[from];

    /// <summary>Pembatalan hanya diajukan sebelum pesanan mulai diproses (PRD 6.8).</summary>
    public static bool CanCancel(OrderStatus current) =>
        current is OrderStatus.MenungguKonfirmasi
            or OrderStatus.Dikonfirmasi
            or OrderStatus.MenungguPenjemputan;

    /// <summary>
    /// Pesanan tidak boleh diserahkan/ditandai selesai sebelum lunas (PRD aturan kritis 6).
    /// Untuk transisi selain menuju Selesai, cukup cek transisi murni.
    /// </summary>
    public static bool CanDeliver(OrderStatus from, OrderStatus to, PaymentStatus payment)
    {
        if (!CanTransition(from, to)) return false;
        if (to == OrderStatus.Selesai) return payment == PaymentStatus.Lunas;
        return true;
    }
}
