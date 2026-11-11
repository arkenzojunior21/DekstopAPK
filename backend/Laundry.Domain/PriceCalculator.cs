namespace Laundry.Domain;

/// <summary>
/// Kalkulasi tagihan dari item order + tarif snapshot (PRD 6.4-6.6).
/// Pembulatan default: rupiah terdekat, midpoint menjauhi nol.
/// Aturan pembulatan resmi menunggu Q8 (OPEN QUESTIONS).
/// </summary>
public static class PriceCalculator
{
    public static decimal RoundRupiah(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero);

    /// <summary>Subtotal satu item memakai nilai aktual bila ada; estimasi bila belum ditimbang.</summary>
    public static decimal ItemSubtotal(OrderItem item, bool useActual)
    {
        switch (item.UnitTypeSnapshot)
        {
            case ServiceUnitType.PerKg:
                decimal? bobot = useActual ? item.ActualWeightKg ?? item.EstimatedWeightKg : item.EstimatedWeightKg;
                if (bobot is null) throw new InvalidOperationException("Berat kiloan tidak diisi.");
                decimal ditagih = Math.Max(bobot.Value, item.MinimumWeightKgSnapshot ?? 0m);
                return RoundRupiah(ditagih * item.PriceSnapshot);
            case ServiceUnitType.PerItem:
                decimal? jumlah = useActual ? item.ActualItemCount ?? item.EstimatedItemCount : item.EstimatedItemCount;
                if (jumlah is null) throw new InvalidOperationException("Jumlah item tidak diisi.");
                return RoundRupiah(jumlah.Value * item.PriceSnapshot);
            case ServiceUnitType.PaketTetap:
                return RoundRupiah(item.PriceSnapshot);
            default:
                throw new InvalidOperationException("Tipe unit layanan tidak dikenal.");
        }
    }

    /// <summary>Total = subtotal item + biaya express (per order, snapshot) + biaya antar-jemput.</summary>
    public static decimal OrderTotal(Order order, bool useActual)
    {
        decimal subtotalItems = order.Items.Sum(i => ItemSubtotal(i, useActual));
        decimal express = order.Express ? order.ExpressSurchargeSnapshot : 0m;
        return RoundRupiah(subtotalItems + express + order.DeliveryFee);
    }

    public static decimal Recalculate(Order order, bool useActual)
    {
        decimal total = OrderTotal(order, useActual);
        foreach (var i in order.Items) i.Subtotal = ItemSubtotal(i, useActual);
        return total;
    }
}
