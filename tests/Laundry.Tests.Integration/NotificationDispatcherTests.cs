using Laundry.Application.Abstractions;
using Laundry.Domain;
using Laundry.Infrastructure.Persistence;
using Laundry.Infrastructure.Services;
using Laundry.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Laundry.Tests.Integration;

/// <summary>Uji pengirim antrean notifikasi di database sementara (tanpa side effect ke data dev).</summary>
[Collection("sync")]
public class NotificationDispatcherTests
{
    private readonly SyncDbFixture _db;
    public NotificationDispatcherTests(SyncDbFixture db) => _db = db;

    private static readonly string NomorBaik = ApiFactory.RandomPhone();
    private static readonly string NomorRusak = "bukan-nomor";

    private static (NotificationDispatcher mesin, PengirimPalsu pengirim) Mesin(
        LaundryDbContext db, int batch = 50, params string[] nomorGagal)
    {
        var pengirim = new PengirimPalsu(nomorGagal);
        return (new NotificationDispatcher(db, pengirim, new SyncConfig(batch, 1),
            NullLogger<NotificationDispatcher>.Instance), pengirim);
    }

    private static NotificationOutbox Baris(string nomor, string eventId, string template = "order_dibuat") =>
        new()
        {
            TemplateKey = template,
            TargetPhone = nomor,
            EventId = eventId,
            PayloadJson = """{"orderNumber":"LR-1"}""",
            CreatedAtUtc = DateTime.UtcNow,
        };

    [Fact]
    public async Task NomorTujuanTidakSahDitundaBukanDianggapTerkirim()
    {
        await _db.KosongkanAsync();
        await using var db = _db.Admin();
        var (mesin, pengirim) = Mesin(db);
        db.NotificationOutbox.Add(Baris(NomorRusak, "evt-rusak"));
        await db.SaveChangesAsync();

        var hasil = await mesin.DrainAsync();
        Assert.Equal(1, hasil.Diproses);
        Assert.Equal(0, hasil.Terkirim);
        Assert.Equal(1, hasil.Ditunda);
        Assert.Equal(0, pengirim.Panggilan);

        var row = await db.NotificationOutbox.SingleAsync();
        Assert.Equal(NotificationStatus.DitundaPerluKoneksi, row.Status);
        Assert.Null(row.ProviderMessageId);
    }

    [Fact]
    public async Task ProviderBelumPunyaCredentialMenandaiPerluKoneksi()
    {
        await _db.KosongkanAsync();
        await using var db = _db.Admin();
        db.NotificationOutbox.Add(Baris(NomorBaik, "evt-tanpa-credential"));
        await db.SaveChangesAsync();

        var mesin = new NotificationDispatcher(db, new PengirimBelumTerhubung(), new SyncConfig(50, 1),
            NullLogger<NotificationDispatcher>.Instance);
        var hasil = await mesin.DrainAsync();
        Assert.Equal(1, hasil.Ditunda);
        var row = await db.NotificationOutbox.SingleAsync();
        Assert.Equal(NotificationStatus.DitundaPerluKoneksi, row.Status);
        Assert.Contains("belum dipasang", row.LastError!);
    }

    [Fact]
    public async Task PengirimanBerhasilTercatatSatuKaliDanTidakDiulang()
    {
        await _db.KosongkanAsync();
        await using var db = _db.Admin();
        var (mesin, pengirim) = Mesin(db);
        db.NotificationOutbox.Add(Baris(NomorBaik, "evt-ok"));
        await db.SaveChangesAsync();

        var hasil = await mesin.DrainAsync();
        Assert.Equal(1, hasil.Terkirim);
        var row = await db.NotificationOutbox.SingleAsync();
        Assert.Equal(NotificationStatus.Terkirim, row.Status);
        Assert.NotNull(row.ProviderMessageId);
        Assert.NotNull(row.SentAtUtc);

        var ulang = await mesin.DrainAsync();
        Assert.Equal(0, ulang.Diproses);
        Assert.Equal(1, pengirim.Panggilan);
    }

    [Fact]
    public async Task LimaKegagalanBerturutJadiGagalTanpaMenghentikanBarisLain()
    {
        var nomorGagal = ApiFactory.RandomPhone();
        await _db.KosongkanAsync();
        await using var db = _db.Admin();
        var (mesin, _) = Mesin(db, nomorGagal: [nomorGagal]);
        db.NotificationOutbox.Add(Baris(nomorGagal, "evt-gagal"));
        db.NotificationOutbox.Add(Baris(NomorBaik, "evt-sehat"));
        await db.SaveChangesAsync();

        for (var i = 1; i <= SyncConfig.MaksimumRetry; i++)
        {
            var hasil = await mesin.DrainAsync();
            Assert.Equal(i == 1 ? 2 : 1, hasil.Diproses);
        }

        var gagal = await db.NotificationOutbox.SingleAsync(n => n.EventId == "evt-gagal");
        Assert.Equal(NotificationStatus.Gagal, gagal.Status);
        Assert.Equal(SyncConfig.MaksimumRetry, gagal.RetryCount);
        Assert.NotNull(gagal.LastError);
        var sehat = await db.NotificationOutbox.SingleAsync(n => n.EventId == "evt-sehat");
        Assert.Equal(NotificationStatus.Terkirim, sehat.Status);
    }

    [Fact]
    public async Task EventIdSamaTidakMasukAntreanDuaKali()
    {
        await _db.KosongkanAsync();
        await using var db = _db.Admin();
        var events = new EventWriter(db);
        events.EnqueueNotification("order_dibuat", NomorBaik, "evt-rangkap", new { orderNumber = "LR-1" });
        events.EnqueueNotification("order_dibuat", NomorBaik, "evt-rangkap", new { orderNumber = "LR-1" });
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.NotificationOutbox.CountAsync());

        // Antrean yang sama tetap tidak dikirim dua kali walau didrain berulang.
        var pengirim = new PengirimPalsu();
        var mesin = new NotificationDispatcher(db, pengirim, new SyncConfig(50, 1),
            NullLogger<NotificationDispatcher>.Instance);
        await mesin.DrainAsync();
        await mesin.DrainAsync();
        Assert.Equal(1, pengirim.Panggilan);
    }

    [Fact]
    public async Task BatchTerbatasMemotongAntrean()
    {
        await _db.KosongkanAsync();
        await using var db = _db.Admin();
        var (mesin, _) = Mesin(db, batch: 2);
        db.NotificationOutbox.AddRange(
            Baris(NomorBaik, "evt-1"), Baris(NomorBaik, "evt-2"), Baris(NomorBaik, "evt-3"));
        await db.SaveChangesAsync();

        Assert.Equal(2, (await mesin.DrainAsync()).Diproses);
        Assert.Equal(1, (await mesin.DrainAsync()).Diproses);
    }

    [Fact]
    public async Task StatusMenampilkanAntreanDanAdapterTidakLive()
    {
        var nomorGagal = ApiFactory.RandomPhone();
        await _db.KosongkanAsync();
        await using var db = _db.Admin();
        db.NotificationOutbox.AddRange(
            Baris(NomorBaik, "evt-ok"), Baris(nomorGagal, "evt-gagal"), Baris(NomorRusak, "evt-rusak"));
        await db.SaveChangesAsync();

        var mesin = new NotificationDispatcher(db, new PengirimPalsu(nomorGagal), new SyncConfig(50, 1),
            NullLogger<NotificationDispatcher>.Instance);
        await mesin.DrainAsync();
        for (var i = 0; i < SyncConfig.MaksimumRetry; i++) await mesin.DrainAsync();

        var status = await mesin.QueueStatusAsync(10);
        Assert.Equal(1, status.Terkirim);
        Assert.Equal(1, status.Gagal);
        Assert.Equal(1, status.Ditunda);
        Assert.Equal(0, status.Menunggu);
        Assert.Equal(2, status.ContohMacet.Count);
        Assert.All(status.ContohMacet, r => Assert.False(string.IsNullOrEmpty(r.LastError)));
    }
}
