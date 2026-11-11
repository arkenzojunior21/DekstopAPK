namespace Laundry.Domain;

public class PickupDeliverySlot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int Capacity { get; set; } = 1;
    public int BookedCount { get; set; }
    public bool IsActive { get; set; } = true;
}

public class FulfillmentTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public FulfillmentMethod Method { get; set; }
    public bool IsPickup { get; set; }
    public string Address { get; set; } = string.Empty;
    public Guid? SlotId { get; set; }
    public string Status { get; set; } = "Terjadwal"; // Terjadwal | Dilakukan | DijadwalkanUlang | Batal
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
