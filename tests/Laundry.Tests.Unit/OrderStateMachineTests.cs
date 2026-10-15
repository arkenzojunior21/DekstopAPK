using Laundry.Domain;

namespace Laundry.Tests.Unit;

public class OrderStateMachineTests
{
    [Theory]
    [InlineData(OrderStatus.MenungguKonfirmasi, OrderStatus.Dikonfirmasi, true)]
    [InlineData(OrderStatus.MenungguKonfirmasi, OrderStatus.SedangDiproses, false)]
    [InlineData(OrderStatus.Dikonfirmasi, OrderStatus.DiterimaDiLaundry, true)]
    [InlineData(OrderStatus.DiterimaDiLaundry, OrderStatus.SiapDiambil, false)]
    [InlineData(OrderStatus.QualityCheck, OrderStatus.SiapDiambil, true)]
    [InlineData(OrderStatus.SiapDiambil, OrderStatus.DalamPengantaran, true)]
    [InlineData(OrderStatus.DalamPengantaran, OrderStatus.Selesai, true)]
    [InlineData(OrderStatus.Selesai, OrderStatus.Dibatalkan, false)]
    [InlineData(OrderStatus.Dibatalkan, OrderStatus.Dikonfirmasi, false)]
    public void TransisiSesuaiStateMartiks(OrderStatus from, OrderStatus to, bool expected)
        => Assert.Equal(expected, OrderStateMachine.CanTransition(from, to));

    [Theory]
    [InlineData(OrderStatus.MenungguKonfirmasi, true)]
    [InlineData(OrderStatus.Dikonfirmasi, true)]
    [InlineData(OrderStatus.MenungguPenjemputan, true)]
    [InlineData(OrderStatus.DiterimaDiLaundry, false)]
    [InlineData(OrderStatus.SedangDiproses, false)]
    [InlineData(OrderStatus.SiapDiambil, false)]
    public void PembatalanHanyaSebelumMulaiDiproses(OrderStatus current, bool canCancel)
        => Assert.Equal(canCancel, OrderStateMachine.CanCancel(current));

    [Fact]
    public void SelesaiWajibLunas()
    {
        Assert.False(OrderStateMachine.CanDeliver(OrderStatus.SiapDiambil, OrderStatus.Selesai, PaymentStatus.BelumBayar));
        Assert.False(OrderStateMachine.CanDeliver(OrderStatus.SiapDiambil, OrderStatus.Selesai, PaymentStatus.SebagianDibayar));
        Assert.True(OrderStateMachine.CanDeliver(OrderStatus.SiapDiambil, OrderStatus.Selesai, PaymentStatus.Lunas));
    }

    [Fact]
    public void SiapDiambilBelumLunas_TidakBisaSelesai()
    {
        // "Siap Diambil — Belum Lunas": transisi murni boleh, tapi penyerahan (Selesai) diblok.
        Assert.True(OrderStateMachine.CanTransition(OrderStatus.SiapDiambil, OrderStatus.Selesai));
        Assert.False(OrderStateMachine.CanDeliver(OrderStatus.SiapDiambil, OrderStatus.Selesai, PaymentStatus.DpSebagian));
    }

    [Fact]
    public void StatusProsesTerpisahDariStatusPembayaran()
    {
        var order = new Order
        {
            Status = OrderStatus.SiapDiambil,
            PaymentStatus = PaymentStatus.SebagianDibayar,
        };
        Assert.Equal(OrderStatus.SiapDiambil, order.Status);
        Assert.Equal(PaymentStatus.SebagianDibayar, order.PaymentStatus);
    }
}
