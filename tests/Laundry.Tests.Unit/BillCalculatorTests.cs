using Laundry.Domain;

namespace Laundry.Tests.Unit;

public class BillCalculatorTests
{
    private static Payment Bayar(Guid orderId, decimal nominal, PaymentStatus status = PaymentStatus.Lunas) =>
        new() { OrderId = orderId, Amount = nominal, Status = status };

    [Fact]
    public void SisaTagihan_KurangDariPembayaranBerhasil()
    {
        var order = new Order { FinalTotal = 100_000m };
        var payments = new[] { Bayar(order.Id, 40_000m), Bayar(order.Id, 10_000m) };
        Assert.Equal(50_000m, BillCalculator.Remaining(order, payments));
    }

    [Fact]
    public void PembayaranGagalTidakDihitung()
    {
        var order = new Order { FinalTotal = 100_000m };
        var payments = new[] { Bayar(order.Id, 60_000m, PaymentStatus.GagalAtauKedaluwarsa) };
        Assert.Equal(100_000m, BillCalculator.Remaining(order, payments));
    }

    [Fact]
    public void KreditTerpakaiDikurangiSekali()
    {
        var order = new Order { FinalTotal = 100_000m, AppliedCredit = 30_000m };
        var payments = new[] { Bayar(order.Id, 50_000m) };
        Assert.Equal(20_000m, BillCalculator.Remaining(order, payments));
    }

    [Fact]
    public void KelebihanBayarMenjadiKredit()
    {
        var order = new Order { FinalTotal = 90_000m };
        var payments = new[] { Bayar(order.Id, 100_000m) };
        Assert.Equal(10_000m, BillCalculator.Overpayment(order, payments));
        Assert.Equal(0m, BillCalculator.Remaining(order, payments));
    }

    [Fact]
    public void TotalBerubahPembayaranSuksesTetapTerhitung()
    {
        var order = new Order { FinalTotal = 80_000m };
        var payments = new[] { Bayar(order.Id, 50_000m) };
        // Revisi berat aktual menaikkan total; pembayaran lama tidak dihapus.
        order.FinalTotal = 120_000m;
        Assert.Equal(70_000m, BillCalculator.Remaining(order, payments));
    }

    [Theory]
    [InlineData(0, 0, 100_000, PaymentStatus.BelumBayar)]
    [InlineData(30_000, 0, 100_000, PaymentStatus.DpSebagian)]
    [InlineData(70_000, 0, 100_000, PaymentStatus.SebagianDibayar)]
    [InlineData(100_000, 0, 100_000, PaymentStatus.Lunas)]
    [InlineData(0, 100_000, 100_000, PaymentStatus.Lunas)]
    public void DeriveStatusPembayaran(decimal dibayar, decimal kredit, decimal total, PaymentStatus expected)
    {
        var order = new Order { FinalTotal = total, AppliedCredit = kredit };
        order.Items.Add(new OrderItem());
        var payments = dibayar > 0 ? new[] { Bayar(order.Id, dibayar) } : Array.Empty<Payment>();
        Assert.Equal(expected, BillCalculator.DerivePaymentStatus(order, payments));
    }

    [Fact]
    public void SaldoKreditDirangkaiDariLedger()
    {
        var pelanggan = Guid.NewGuid();
        var ledger = new[]
        {
            new CreditLedgerEntry { CustomerId = pelanggan, Delta = 10_000m },
            new CreditLedgerEntry { CustomerId = pelanggan, Delta = -4_000m },
            new CreditLedgerEntry { CustomerId = Guid.NewGuid(), Delta = 99_000m },
        };
        Assert.Equal(6_000m, BillCalculator.CreditBalance(ledger, pelanggan));
    }

    private static Refund Retour(Guid orderId, Guid paymentId, decimal nominal) =>
        new() { OrderId = orderId, PaymentId = paymentId, Amount = nominal, Status = PaymentStatus.Lunas };

    [Fact]
    public void RefundMengurangiPembayaranNeto()
    {
        var order = new Order { FinalTotal = 100_000m };
        var bayar = Bayar(order.Id, 60_000m);
        order.Payments.Add(bayar);
        order.Refunds.Add(Retour(order.Id, bayar.Id, 20_000m));
        Assert.Equal(40_000m, BillCalculator.Paid(order, order.Refunds));
        Assert.Equal(60_000m, BillCalculator.Remaining(order, order.Payments));
    }

    [Fact]
    public void RefundPenuhTidakMenjadiNegatif()
    {
        var order = new Order { FinalTotal = 50_000m };
        var bayar = Bayar(order.Id, 50_000m);
        order.Payments.Add(bayar);
        order.Refunds.Add(Retour(order.Id, bayar.Id, 50_000m));
        Assert.Equal(0m, BillCalculator.Paid(order, order.Refunds));
        Assert.Equal(50_000m, BillCalculator.Remaining(order, order.Payments));
    }

    [Fact]
    public void RefundPesananLainTidakMempengaruhiTagihan()
    {
        var order = new Order { FinalTotal = 100_000m };
        var bayar = Bayar(order.Id, 70_000m);
        order.Payments.Add(bayar);
        order.Refunds.Add(Retour(Guid.NewGuid(), Guid.NewGuid(), 30_000m));
        Assert.Equal(70_000m, BillCalculator.Paid(order, order.Refunds));
    }
}
