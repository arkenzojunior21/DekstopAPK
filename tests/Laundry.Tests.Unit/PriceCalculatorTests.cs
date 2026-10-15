using Laundry.Domain;

namespace Laundry.Tests.Unit;

public class PriceCalculatorTests
{
    private static OrderItem PerKg(decimal harga, decimal? est, decimal? akt, decimal? minKg) => new()
    {
        UnitTypeSnapshot = ServiceUnitType.PerKg,
        PriceSnapshot = harga,
        EstimatedWeightKg = est,
        ActualWeightKg = akt,
        MinimumWeightKgSnapshot = minKg,
    };

    [Fact]
    public void PerKg_MenghormatiMinimumBerat()
    {
        var item = PerKg(10_000m, est: 2m, akt: 0.5m, minKg: 1m);
        Assert.Equal(10_000m, PriceCalculator.ItemSubtotal(item, useActual: true));
    }

    [Fact]
    public void PerKg_PakaiAktualSaatSudahDitimbang()
    {
        var item = PerKg(10_000m, est: 2m, akt: 3.5m, minKg: 1m);
        Assert.Equal(35_000m, PriceCalculator.ItemSubtotal(item, useActual: true));
        Assert.Equal(20_000m, PriceCalculator.ItemSubtotal(item, useActual: false));
    }

    [Fact]
    public void PerItem_KaliJumlah()
    {
        var item = new OrderItem
        {
            UnitTypeSnapshot = ServiceUnitType.PerItem,
            PriceSnapshot = 15_000m,
            EstimatedItemCount = 4m,
            ActualItemCount = 6m,
        };
        Assert.Equal(90_000m, PriceCalculator.ItemSubtotal(item, useActual: true));
    }

    [Fact]
    public void PaketTetap_HargaSnapshotSaja()
    {
        var item = new OrderItem { UnitTypeSnapshot = ServiceUnitType.PaketTetap, PriceSnapshot = 100_000m };
        Assert.Equal(100_000m, PriceCalculator.ItemSubtotal(item, useActual: true));
    }

    [Fact]
    public void OrderTotal_MenambahExpressDanAntar()
    {
        var order = new Order
        {
            Express = true,
            ExpressSurchargeSnapshot = 20_000m,
            DeliveryFee = 5_000m,
        };
        order.Items.Add(PerKg(10_000m, est: 2m, akt: null, minKg: 1m));
        Assert.Equal(45_000m, PriceCalculator.OrderTotal(order, useActual: false));

        order.Express = false;
        Assert.Equal(25_000m, PriceCalculator.OrderTotal(order, useActual: false));
    }

    [Fact]
    public void PembulatanRupiah_TengahMenjauhiNol()
    {
        Assert.Equal(10_500m, PriceCalculator.RoundRupiah(10_499.5m));
        Assert.Equal(-10_500m, PriceCalculator.RoundRupiah(-10_499.5m));
        Assert.Equal(10_500m, PriceCalculator.RoundRupiah(10_500.4m));
    }

    [Fact]
    public void PerubahanTarifTidakMengubahSnapshotOrderLama()
    {
        // Snapshot tersimpan di item; rekalkulasi hanya memakai snapshot.
        var item = PerKg(12_000m, est: 2m, akt: 2m, minKg: null);
        Assert.Equal(24_000m, PriceCalculator.ItemSubtotal(item, useActual: true));
    }

    [Fact]
    public void Recalculate_MenulisSubtotalPerItem()
    {
        var order = new Order();
        order.Items.Add(PerKg(10_000m, est: 2m, akt: 3m, minKg: null));
        order.Items.Add(new OrderItem { UnitTypeSnapshot = ServiceUnitType.PaketTetap, PriceSnapshot = 50_000m });
        decimal total = PriceCalculator.Recalculate(order, useActual: true);
        Assert.Equal(80_000m, total);
        Assert.Equal(30_000m, order.Items[0].Subtotal);
        Assert.Equal(50_000m, order.Items[1].Subtotal);
    }

    [Fact]
    public void BeratKiloanKosong_Menolak()
    {
        var item = PerKg(10_000m, est: null, akt: null, minKg: null);
        Assert.Throws<InvalidOperationException>(() => PriceCalculator.ItemSubtotal(item, useActual: true));
    }
}
