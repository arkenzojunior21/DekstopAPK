namespace Laundry.Domain;

/// <summary>
/// Rumus tagihan yang konsisten (PRD 6.6): sisa = total final − pembayaran berhasil − kredit terpakai.
/// Pembayaran sukses tidak pernah dihapus/diubah saat total berubah; kelebihan bayar jadi saldo kredit.
/// </summary>
public static class BillCalculator
{
    public static decimal SuccessfulPayments(IEnumerable<Payment> payments, Guid orderId) =>
        payments.Where(p => p.OrderId == orderId && p.Status == PaymentStatus.Lunas)
            .Sum(p => p.Amount);

    /// <summary>Total uang yang sudah dikembalikan pada satu pesanan.</summary>
    public static decimal RefundedPayments(IEnumerable<Refund> refunds, Guid orderId) =>
        refunds.Where(r => r.OrderId == orderId && r.Status == PaymentStatus.Lunas)
            .Sum(r => r.Amount);

    /// <summary>Uang riil yang masih menjadi pembayaran pesanan setelah dikurangi refund.</summary>
    public static decimal NetPayments(Order order, IEnumerable<Refund> refunds) =>
        Math.Max(SuccessfulPayments(order.Payments, order.Id) - RefundedPayments(refunds, order.Id), 0m);

    /// <summary>Dasar semua perhitungan sisa/tagihan: pembayaran neto + kredit terpakai.</summary>
    public static decimal Paid(Order order, IEnumerable<Refund> refunds) =>
        NetPayments(order, refunds) + order.AppliedCredit;

    public static decimal Remaining(Order order, IEnumerable<Payment> payments)
    {
        decimal paid = SuccessfulPayments(payments, order.Id) - RefundedPayments(order.Refunds, order.Id);
        return Math.Max(order.FinalTotal - paid - order.AppliedCredit, 0m);
    }

    /// <summary>Positif bila pembayaran melebihi total final → menjadi saldo kredit pelanggan.</summary>
    public static decimal Overpayment(Order order, IEnumerable<Payment> payments)
    {
        decimal paid = SuccessfulPayments(payments, order.Id) - RefundedPayments(order.Refunds, order.Id);
        return Math.Max(paid + order.AppliedCredit - order.FinalTotal, 0m);
    }

    /// <summary>Status pembayaran hasil rekalkulasi setelah total berubah atau pembayaran dicatat.</summary>
    public static PaymentStatus DerivePaymentStatus(Order order, IEnumerable<Payment> payments)
    {
        decimal paid = SuccessfulPayments(payments, order.Id) - RefundedPayments(order.Refunds, order.Id)
            + order.AppliedCredit;
        if (paid == 0) return PaymentStatus.BelumBayar;
        if (paid >= order.FinalTotal) return PaymentStatus.Lunas;
        if (order.Items.Count > 0 && paid <= order.FinalTotal * 0.5m) return PaymentStatus.DpSebagian;
        return PaymentStatus.SebagianDibayar;
    }

    /// <summary>Saldo kredit pelanggan dirangkai dari ledger append-only.</summary>
    public static decimal CreditBalance(IEnumerable<CreditLedgerEntry> ledger, Guid customerId) =>
        ledger.Where(e => e.CustomerId == customerId).Sum(e => e.Delta);
}
