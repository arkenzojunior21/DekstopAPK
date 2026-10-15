using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Laundry.Application.Core;
using Laundry.Application.Sync;
using Laundry.Domain;
using Laundry.Infrastructure.Persistence;
using Laundry.Infrastructure.Services;
using Laundry.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Laundry.Tests.Integration;

/// <summary>Dua database PostgreSQL sementara (sisi admin &amp; sisi cloud) untuk uji sinkronisasi.</summary>
public sealed class SyncDbFixture : IAsyncLifetime
{
    private const string NamaAdmin = "laundry_synctest_admin";
    private const string NamaCloud = "laundry_synctest_cloud";

    public string AdminConn { get; private set; } = string.Empty;
    public string CloudConn { get; private set; } = string.Empty;

    /// <summary>Connection string ke port mati: dipakai untuk mensimulasikan sisi yang sedang offline.</summary>
    public string ConnMati { get; private set; } = string.Empty;

    private static string Basis => EnvironmentConfig.Get("CONN_ADMIN",
        "Host=localhost;Port=5433;Database=laundry_admin;Username=laundry;Password=laundry_dev_2026");

    private static string DenganDatabase(string db) =>
        new NpgsqlConnectionStringBuilder(Basis) { Database = db }.ConnectionString;

    public async Task InitializeAsync()
    {
        AdminConn = await BuatAsync(NamaAdmin);
        CloudConn = await BuatAsync(NamaCloud);
        ConnMati = new NpgsqlConnectionStringBuilder(Basis)
        {
            Database = NamaAdmin, Port = 5999, Timeout = 2,
        }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(DenganDatabase("postgres"));
        await admin.OpenAsync();
        foreach (var name in new[] { NamaAdmin, NamaCloud })
        {
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS {name} WITH (FORCE)";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task<string> BuatAsync(string name)
    {
        await using (var admin = new NpgsqlConnection(DenganDatabase("postgres")))
        {
            await admin.OpenAsync();
            await using (var cmd = admin.CreateCommand())
            {
                cmd.CommandText = $"DROP DATABASE IF EXISTS {name} WITH (FORCE)";
                await cmd.ExecuteNonQueryAsync();
            }
            await using (var cmd = admin.CreateCommand())
            {
                cmd.CommandText = $"CREATE DATABASE {name}";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        var conn = DenganDatabase(name);
        var options = new DbContextOptionsBuilder<LaundryDbContext>().UseNpgsql(conn).Options;
        await using var db = new LaundryDbContext(options);
        await db.Database.MigrateAsync();
        return conn;
    }

    public LaundryDbContext Admin() => AdminDengan(AdminConn);

    public LaundryDbContext AdminDengan(string conn) =>
        new(new DbContextOptionsBuilder<LaundryDbContext>().UseNpgsql(conn).Options);

    public CloudDbContext Cloud() =>
        new(new DbContextOptionsBuilder<CloudDbContext>().UseNpgsql(CloudConn).Options);

    /// <summary>Mulai dari nol: semua tabel isi dikosongkan di kedua sisi (skema tetap dari migration).</summary>
    public async Task KosongkanAsync()
    {
        await using var admin = Admin();
        await using LaundryDbContext cloud = Cloud();
        foreach (var db in new[] { admin, cloud })
        {
            var tabel = db.Model.GetEntityTypes()
                .Select(t => t.GetTableName())
                .Where(n => n is not null && !n.Contains("EFMigrations"))
                .Select(n => $"\"{n}\"")
                .ToList();
            // Nama tabel diambil dari model EF (bukan input user) → tidak ada celah injeksi.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(
                $"TRUNCATE TABLE {string.Join(", ", tabel)} RESTART IDENTITY CASCADE");
#pragma warning restore EF1002
        }
    }

    public SyncEngine Mesin(LaundryDbContext admin, CloudDbContext cloud, int batch = 100) =>
        new(admin, cloud, new SyncConfig(batch, 1), NullLogger<SyncEngine>.Instance);
}

[CollectionDefinition("sync")]
public class SyncCollection : ICollectionFixture<SyncDbFixture> { }

[Collection("sync")]
public class SyncEngineTests
{
    private readonly SyncDbFixture _db;
    public SyncEngineTests(SyncDbFixture db) => _db = db;

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static string NomorTelepon() => $"628{RandomNumberGenerator.GetInt32(10_000_000, 99_999_999)}";

    /// <summary>Layanan, admin, dan pelanggan siap pakai pada satu sisi database.</summary>
    private static async Task<(Guid adminId, Guid serviceId, Guid customerId)> SisiSiapAsync(LaundryDbContext db)
    {
        var admin = new User
        {
            Role = UserRole.Admin,
            NormalizedPhone = NomorTelepon(),
            PasswordHash = "rahasia-yang-tidak-boleh-ikut-sync",
            CreatedAtUtc = DateTime.UtcNow,
        };
        var pelanggan = new User
        {
            Role = UserRole.Pelanggan,
            NormalizedPhone = NomorTelepon(),
            PasswordHash = string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
            Profile = new CustomerProfile { Nama = "Pelanggan Sinkron" },
        };
        var layanan = new Service
        {
            Name = "Kilo Reguler",
            UnitType = ServiceUnitType.PerKg,
            MinimumWeightKg = 1m,
            CreatedAtUtc = DateTime.UtcNow,
        };
        layanan.PriceVersions.Add(new ServicePriceVersion
        {
            ServiceId = layanan.Id, Price = 10_000m, StandardDurationDays = 3,
            ExpressDurationDays = 1, ExpressSurcharge = 5_000m, ValidFromUtc = DateTime.UtcNow.AddDays(-1),
        });
        db.Users.AddRange(admin, pelanggan);
        db.Services.Add(layanan);
        await db.SaveChangesAsync();
        return (admin.Id, layanan.Id, pelanggan.Profile!.Id);
    }

    private static OrderService PesananService(LaundryDbContext db) =>
        new(db, new AuditWriter(db), new EventWriter(db), new SettingsStore(db));

    private static PaymentService PembayaranService(LaundryDbContext db) =>
        new(db, new AuditWriter(db), new EventWriter(db), UjiWebhook.Gateway);

    private static async Task<OrderDetailDto> BuatPesananAsync(LaundryDbContext db, Guid adminId, Guid serviceId,
        Guid customerId, decimal kg, bool dariCloud = false)
    {
        var req = new CreateOrderRequest(customerId, null, null,
            [new OrderItemInput(serviceId, kg, null)], false, FulfillmentMethod.AmbilSendiri, null, null, null);
        return dariCloud
            ? await PesananService(db).CreateOnlineAsync(req, customerId, CancellationToken.None)
            : await PesananService(db).CreateWalkInAsync(req, adminId, CancellationToken.None);
    }

    /// <summary>
    /// Suntik event langsung ke antrean terima sisi tujuan: dipakai untuk kasus kirim ulang,
    /// urutan acak, jam simpang, dan isi finansial yang berbeda.
    /// </summary>
    private static async Task SusunInboxAsync(LaundryDbContext sisi, string agregat, Guid id, string eventId,
        object payload, long sequence)
    {
        sisi.SyncInbox.Add(new SyncInbox
        {
            EventId = eventId,
            Sequence = sequence,
            AggregateType = agregat,
            AggregateId = id,
            Operation = "disuntik-tes",
            PayloadJson = JsonSerializer.Serialize(payload, Json),
            ReceivedAtUtc = DateTime.UtcNow,
        });
        await sisi.SaveChangesAsync();
    }

    private static PaymentEvent Event(Payment p) => new(p.Id, p.OrderId, p.CustomerId, p.Amount, p.Method,
        p.Kind, p.Status, p.ProviderReference, p.IdempotencyKey, p.RecordedByUserId, p.CreatedAtUtc,
        p.ConfirmedAtUtc);

    private static CreditLedgerEvent Event(CreditLedgerEntry e) => new(e.Id, e.CustomerId, e.Delta, e.OrderId,
        e.PaymentId, e.RefundId, e.ActorUserId, e.IdempotencyKey, e.CreatedAtUtc, e.Reason);

    private static async Task<OrderEvent> SnapshotPesananAsync(LaundryDbContext db, Guid orderId)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items).Include(o => o.StatusHistory)
            .Include(o => o.Customer).ThenInclude(c => c!.User)
            .FirstAsync(o => o.Id == orderId);
        return EventWriter.BuildOrder(order);
    }

    /// <summary>Simulasi backoff sudah lewat tanpa menunggu sungguhan.</summary>
    private static async Task LepaskanBackoffAsync(params LaundryDbContext[] sisi)
    {
        foreach (var db in sisi)
        {
            foreach (var row in await db.SyncInbox.Where(i => i.NextRetryAtUtc != null).ToListAsync())
                row.NextRetryAtUtc = null;
            foreach (var row in await db.SyncOutbox.Where(o => o.NextRetryAtUtc != null).ToListAsync())
                row.NextRetryAtUtc = null;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task PesananLokalNaikKeCloudDanKirimUlangTidakMenduplikasi()
    {
        await _db.KosongkanAsync();
        await using var admin = _db.Admin();
        await using var cloud = _db.Cloud();
        var (adminId, serviceId, customerId) = await SisiSiapAsync(admin);
        var order = await BuatPesananAsync(admin, adminId, serviceId, customerId, 2m);

        var mesin = _db.Mesin(admin, cloud);
        await mesin.JalankanSatuSiklusAsync(CancellationToken.None);

        Assert.Equal(1, await cloud.Orders.CountAsync(o => o.Id == order.Id));
        Assert.Equal(0, await admin.SyncOutbox.CountAsync(o => o.Status == SyncEventStatus.Menunggu));

        // Dua siklus tambahan = pengiriman ulang; tidak boleh menambah baris mana pun.
        await mesin.JalankanSatuSiklusAsync(CancellationToken.None);
        await mesin.JalankanSatuSiklusAsync(CancellationToken.None);

        Assert.Equal(1, await cloud.Orders.CountAsync(o => o.Id == order.Id));
        Assert.Equal(1, await cloud.OrderItems.CountAsync(i => i.OrderId == order.Id));
        Assert.Equal(1, await cloud.CustomerProfiles.CountAsync(c => c.Id == customerId));
        Assert.Equal(20_000m, (await cloud.Orders.FirstAsync(o => o.Id == order.Id)).EstimatedTotal);
        var eventIds = await cloud.SyncInbox.Select(i => i.EventId).ToListAsync();
        Assert.Equal(eventIds.Count, eventIds.Distinct().Count());
    }

    [Fact]
    public async Task PembayaranRefundDanLedgerMenyebrangTanpaRangkap()
    {
        await _db.KosongkanAsync();
        await using var admin = _db.Admin();
        await using var cloud = _db.Cloud();
        var (adminId, serviceId, customerId) = await SisiSiapAsync(admin);
        var order = await BuatPesananAsync(admin, adminId, serviceId, customerId, 2m);

        var bayar = PembayaranService(admin);
        await bayar.RecordCashAsync(
            new RecordCashPaymentRequest(order.Id, 30_000m, PaymentKind.Pelunasan, null, null),
            adminId, CancellationToken.None);
        await bayar.RefundAsync(
            new RefundRequest(order.Id, 10_000m, "Kelebihan bayar dikembalikan", null, null),
            adminId, CancellationToken.None);

        var mesin = _db.Mesin(admin, cloud);
        await mesin.JalankanSatuSiklusAsync(CancellationToken.None);
        await mesin.JalankanSatuSiklusAsync(CancellationToken.None);

        Assert.Equal(1, await cloud.Payments.CountAsync(p => p.OrderId == order.Id));
        Assert.Equal(30_000m, await cloud.Payments.Where(p => p.OrderId == order.Id).SumAsync(p => p.Amount));
        Assert.Equal(1, await cloud.Refunds.CountAsync(r => r.OrderId == order.Id));
        Assert.Equal(2, await cloud.CreditLedger.CountAsync(e => e.OrderId == order.Id));
        Assert.Equal(0m, await cloud.CreditLedger.Where(e => e.OrderId == order.Id).SumAsync(e => e.Delta));
        Assert.Equal(PaymentStatus.RefundSebagian,
            (await cloud.Orders.FirstAsync(o => o.Id == order.Id)).PaymentStatus);

        // Hash kata sandi tidak pernah ikut menyeberang.
        var semuaPayload = string.Concat(await cloud.SyncInbox.Select(i => i.PayloadJson).ToListAsync());
        Assert.DoesNotContain("assword", semuaPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PasswordHash", semuaPayload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UrutanTibaAcakTetapDiprosesBerdasarkanDependensi()
    {
        await _db.KosongkanAsync();
        await using var admin = _db.Admin();
        await using var cloud = _db.Cloud();
        var (adminId, serviceId, customerId) = await SisiSiapAsync(admin);
        var order = await BuatPesananAsync(admin, adminId, serviceId, customerId, 2m);
        var pelanggan = await admin.Users.Include(u => u.Profile).FirstAsync(u => u.Profile!.Id == customerId);

        // Pembayaran "tiba lebih dulu" (sequence 0) padahal induk-induknya masih di antrean keluar.
        var pembayaran = new Payment
        {
            OrderId = order.Id, CustomerId = customerId, Amount = 20_000m, Method = PaymentMethod.Tunai,
            Kind = PaymentKind.Pelunasan, Status = PaymentStatus.Lunas, IdempotencyKey = "bayar-dulu-kunci",
            RecordedByUserId = adminId, CreatedAtUtc = DateTime.UtcNow, ConfirmedAtUtc = DateTime.UtcNow,
        };
        await SusunInboxAsync(cloud, SyncAggregate.Payment, pembayaran.Id, "bayar-dulu", Event(pembayaran), 0);

        await _db.Mesin(admin, cloud).JalankanSatuSiklusAsync(CancellationToken.None);

        // Dependency ordering: pelanggan → pesanan → pembayaran dalam satu siklus, tanpa retry.
        Assert.Equal(1, await cloud.CustomerProfiles.CountAsync(c => c.Id == customerId));
        Assert.Equal(1, await cloud.Orders.CountAsync(o => o.Id == order.Id));
        Assert.Equal(1, await cloud.Payments.CountAsync(p => p.Id == pembayaran.Id));
        Assert.Equal(20_000m, (await cloud.Payments.FirstAsync(p => p.Id == pembayaran.Id)).Amount);
        var selip = await cloud.SyncInbox.FirstAsync(i => i.EventId == "bayar-dulu");
        Assert.Equal(SyncEventStatus.Diproses, selip.Status);
        Assert.Equal(0, selip.RetryCount);
        Assert.Null(selip.LastError);
        Assert.Equal(pelanggan.Profile!.Nama,
            (await cloud.CustomerProfiles.FirstAsync(c => c.Id == customerId)).Nama);
        Assert.Equal(0, await cloud.SyncConflicts.CountAsync());
    }

    [Fact]
    public async Task PembayaranDenganIsiBerbedaTidakPernahDitimpa()
    {
        await _db.KosongkanAsync();
        await using var admin = _db.Admin();
        await using var cloud = _db.Cloud();
        var (adminId, serviceId, customerId) = await SisiSiapAsync(admin);
        var order = await BuatPesananAsync(admin, adminId, serviceId, customerId, 2m);
        await PembayaranService(admin).RecordCashAsync(
            new RecordCashPaymentRequest(order.Id, 20_000m, PaymentKind.Pelunasan, null, null),
            adminId, CancellationToken.None);
        await _db.Mesin(admin, cloud).JalankanSatuSiklusAsync(CancellationToken.None);

        var asli = await cloud.Payments.FirstAsync(p => p.OrderId == order.Id);

        // Event lain membawa id pembayaran yang sama dengan nominal berbeda → konflik, bukan timpa.
        var palsu = Event(asli) with { Amount = 1m, IdempotencyKey = asli.IdempotencyKey + "-beda" };
        await SusunInboxAsync(cloud, SyncAggregate.Payment, asli.Id, "bayar-palsu", palsu, 99);
        await _db.Mesin(admin, cloud).JalankanSatuSiklusAsync(CancellationToken.None);

        Assert.Equal(asli.Amount, (await cloud.Payments.FirstAsync(p => p.Id == asli.Id)).Amount);
        Assert.Equal(SyncEventStatus.Gagal, (await cloud.SyncInbox.FirstAsync(i => i.EventId == "bayar-palsu")).Status);
        var konflik = await cloud.SyncConflicts.SingleAsync(c => c.AggregateId == asli.Id);
        Assert.Equal("PerluRekonsiliasi", konflik.Resolution);
        Assert.Equal(SyncAggregate.Payment, konflik.AggregateType);
    }

    [Fact]
    public async Task LedgerBedaDeltaJugaDicatatSebagaiKonflik()
    {
        await _db.KosongkanAsync();
        await using var admin = _db.Admin();
        await using var cloud = _db.Cloud();
        var (adminId, serviceId, customerId) = await SisiSiapAsync(admin);
        var order = await BuatPesananAsync(admin, adminId, serviceId, customerId, 2m);
        await PembayaranService(admin).RecordCashAsync(
            new RecordCashPaymentRequest(order.Id, 25_000m, PaymentKind.Pelunasan, null, null),
            adminId, CancellationToken.None);
        await _db.Mesin(admin, cloud).JalankanSatuSiklusAsync(CancellationToken.None);

        var ledger = await cloud.CreditLedger.FirstAsync(e => e.OrderId == order.Id);
        var beda = Event(ledger) with { Delta = 999m, IdempotencyKey = ledger.IdempotencyKey + "-x" };
        await SusunInboxAsync(cloud, SyncAggregate.CreditLedger, ledger.Id, "ledger-palsu", beda, 98);
        await _db.Mesin(admin, cloud).JalankanSatuSiklusAsync(CancellationToken.None);

        Assert.Equal(ledger.Delta, (await cloud.CreditLedger.FirstAsync(e => e.Id == ledger.Id)).Delta);
        Assert.Equal(1, await cloud.SyncConflicts.CountAsync(c => c.AggregateId == ledger.Id));
    }

    [Fact]
    public async Task JamPengirimMajuDitolakTanpaMenghilangkanEvent()
    {
        await _db.KosongkanAsync();
        await using var admin = _db.Admin();
        await using var cloud = _db.Cloud();
        var (adminId, serviceId, customerId) = await SisiSiapAsync(admin);
        var order = await BuatPesananAsync(admin, adminId, serviceId, customerId, 2m);
        var masaDepanId = Guid.NewGuid();
        var payload = await SnapshotPesananAsync(admin, order.Id) with
        {
            OrderId = masaDepanId,
            OrderNumber = $"LR-MASADEPAN-{masaDepanId:N}"[..24],
            CreatedAtUtc = DateTime.UtcNow.AddHours(2),
        };

        await SusunInboxAsync(cloud, SyncAggregate.Order, masaDepanId, "order-masa-depan", payload, 5);
        await _db.Mesin(admin, cloud).JalankanSatuSiklusAsync(CancellationToken.None);

        Assert.Equal(0, await cloud.Orders.CountAsync(o => o.Id == masaDepanId));
        var row = await cloud.SyncInbox.FirstAsync(i => i.EventId == "order-masa-depan");
        Assert.Equal(SyncEventStatus.Menunggu, row.Status);
        Assert.Equal(1, row.RetryCount);
        Assert.Contains("jam", row.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LimaPercobaanLaluGagalTetapiAntreanLainTetapJalan()
    {
        await _db.KosongkanAsync();
        await using var admin = _db.Admin();
        await using var cloud = _db.Cloud();
        var (adminId, serviceId, customerId) = await SisiSiapAsync(admin);
        await BuatPesananAsync(admin, adminId, serviceId, customerId, 2m);

        var yatim = new PaymentEvent(Guid.NewGuid(), Guid.NewGuid(), customerId, 5_000m, PaymentMethod.Tunai,
            PaymentKind.Pelunasan, PaymentStatus.Lunas, null, "yatim-kunci", adminId, DateTime.UtcNow, null);
        await SusunInboxAsync(cloud, SyncAggregate.Payment, yatim.Id, "yatim", yatim, 500);
        await SusunInboxAsync(cloud, SyncAggregate.Customer, customerId, "tetangga",
            new CustomerEvent(customerId, admin.Users.First(u => u.Profile!.Id == customerId).Id,
                "Pelanggan Sinkron", "Alamat baru hasil event tetangga",
                admin.Users.First(u => u.Profile!.Id == customerId).NormalizedPhone,
                UserRole.Pelanggan, true, DateTime.UtcNow), 501);

        var mesin = _db.Mesin(admin, cloud);
        for (var i = 0; i < SyncConfig.MaksimumRetry; i++)
        {
            await LepaskanBackoffAsync(cloud);
            var hasil = await mesin.JalankanSatuSiklusAsync(CancellationToken.None);
            // Kegagalan selalu terhitung, dan pada siklus pertama event sehat di antrean
            // yang sama tetap ikut diterapkan (antrean tidak berhenti di baris gagal).
            Assert.Equal(1, hasil[SyncDirection.AdminKeCloud].Gagal);
            if (i == 0) Assert.True(hasil[SyncDirection.AdminKeCloud].Diterapkan >= 2);
        }

        var gagal = await cloud.SyncInbox.FirstAsync(i => i.EventId == "yatim");
        Assert.Equal(SyncEventStatus.Gagal, gagal.Status);
        Assert.Equal(SyncConfig.MaksimumRetry, gagal.RetryCount);
        Assert.Null(gagal.NextRetryAtUtc);
        Assert.NotNull(gagal.LastError);

        // Event sehat di antrean yang sama tetap diterapkan.
        Assert.Equal(1, await cloud.CustomerProfiles.CountAsync(c => c.Id == customerId));
        Assert.Equal("Alamat baru hasil event tetangga",
            (await cloud.CustomerProfiles.FirstAsync(c => c.Id == customerId)).Alamat);
        Assert.Equal(SyncEventStatus.Diproses,
            (await cloud.SyncInbox.FirstAsync(i => i.EventId == "tetangga")).Status);

        var terlihat = await mesin.AntreanGagalAsync(SyncDirection.AdminKeCloud, CancellationToken.None);
        Assert.Contains("yatim", terlihat.Select(r => r.EventId));

        // Retry manual mengantrekan ulang, bukan menghapus jejak.
        var jumlah = await mesin.CobaUlangGagalAsync(CancellationToken.None);
        Assert.True(jumlah >= 1);
        Assert.Equal(SyncEventStatus.Menunggu, (await cloud.SyncInbox.FirstAsync(i => i.EventId == "yatim")).Status);
    }

    [Fact]
    public async Task AdminOfflinePesananCloudTetapAntreLaluMasukSaatOnline()
    {
        await _db.KosongkanAsync();
        await using var cloud = _db.Cloud();
        var (adminId, serviceId, customerId) = await SisiSiapAsync(cloud);

        // Aplikasi pelanggan membuat order di sisi cloud.
        var order = await BuatPesananAsync(cloud, adminId, serviceId, customerId, 3m, dariCloud: true);
        Assert.Equal("cloud", (await cloud.Orders.FirstAsync(o => o.Id == order.Id)).Source);
        Assert.Equal(OrderStatus.MenungguKonfirmasi, order.Status);

        await using var adminMati = _db.AdminDengan(_db.ConnMati);
        var offline = _db.Mesin(adminMati, cloud);
        var hasilOffline = await offline.JalankanSatuSiklusAsync(CancellationToken.None);
        Assert.False(hasilOffline[SyncDirection.CloudKeAdmin].TargetTerhubung);
        Assert.Equal(0, await cloud.SyncOutbox.CountAsync(o => o.Status == SyncEventStatus.Dikirim));
        Assert.Equal(0, await cloud.SyncOutbox.CountAsync(o => o.Status == SyncEventStatus.Gagal));

        await using var admin = _db.Admin();
        var online = _db.Mesin(admin, cloud);
        await online.JalankanSatuSiklusAsync(CancellationToken.None);

        var masuk = await admin.Orders.IgnoreQueryFilters().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(30_000m, masuk.EstimatedTotal);
        Assert.Equal(OrderStatus.MenungguKonfirmasi, masuk.Status);
        Assert.Equal(1, await admin.OrderItems.CountAsync(i => i.OrderId == order.Id));
        Assert.Equal(1, await admin.CustomerProfiles.CountAsync(c => c.Id == customerId));
    }

    [Fact]
    public async Task StatusMenampilkanAntrianCheckpointDanDetak()
    {
        await _db.KosongkanAsync();
        await using var admin = _db.Admin();
        await using var cloud = _db.Cloud();
        var (adminId, serviceId, customerId) = await SisiSiapAsync(admin);
        await BuatPesananAsync(admin, adminId, serviceId, customerId, 2m);

        var mesin = _db.Mesin(admin, cloud);
        var sebelum = await mesin.StatusAsync(CancellationToken.None);
        Assert.True(sebelum.AntrianKeluarAdmin > 0);
        Assert.False(sebelum.PekerjaAktif);

        await mesin.JalankanSatuSiklusAsync(CancellationToken.None);
        var sesudah = await mesin.StatusAsync(CancellationToken.None);

        Assert.Equal(0, sesudah.AntrianKeluarAdmin);
        Assert.True(sesudah.CheckpointAdminKeCloud > 0);
        Assert.NotNull(sesudah.DetakTerakhirUtc);
        Assert.NotNull(sesudah.SinkronTerakhirBerhasilUtc);
        Assert.True(sesudah.PekerjaAktif);
        // Detak disimpan sebagai AppSetting, bukan tabel baru.
        Assert.Equal(1, await admin.AppSettings.CountAsync(a => a.Key == SyncEngine.HeartbeatKey));
    }

    [Fact]
    public async Task CrashSetengahJalanDilanjutkanMesinBaruTanpaDuplikasi()
    {
        await _db.KosongkanAsync();
        await using var admin = _db.Admin();
        await using var cloud = _db.Cloud();
        var (adminId, serviceId, customerId) = await SisiSiapAsync(admin);
        var order = await BuatPesananAsync(admin, adminId, serviceId, customerId, 2m);
        await PembayaranService(admin).RecordCashAsync(
            new RecordCashPaymentRequest(order.Id, 20_000m, PaymentKind.Pelunasan, null, null),
            adminId, CancellationToken.None);

        // Batch 1 = satu event per siklus, lalu "proses mati" (semua context dibuang).
        var parsial = _db.Mesin(admin, cloud, batch: 1);
        await parsial.JalankanSatuSiklusAsync(CancellationToken.None);
        await parsial.JalankanSatuSiklusAsync(CancellationToken.None);
        Assert.True(await admin.SyncOutbox.CountAsync(o => o.Status == SyncEventStatus.Menunggu) > 0,
            "masih harus ada antrean keluar yang belum terkirim");
        Assert.Equal(1, await cloud.Orders.CountAsync(o => o.Id == order.Id));

        await using var admin2 = _db.Admin();
        await using var cloud2 = _db.Cloud();
        var baru = _db.Mesin(admin2, cloud2);
        for (var i = 0; i < 12; i++)
        {
            await LepaskanBackoffAsync(admin2, cloud2);
            await baru.JalankanSatuSiklusAsync(CancellationToken.None);
        }

        Assert.Equal(1, await cloud2.Orders.CountAsync(o => o.Id == order.Id));
        Assert.Equal(1, await cloud2.Payments.CountAsync(p => p.OrderId == order.Id));
        Assert.Equal(0, await cloud2.SyncInbox.CountAsync(i => i.Status == SyncEventStatus.Menunggu));
        Assert.Equal(0, await admin2.SyncOutbox.CountAsync(o => o.Status == SyncEventStatus.Menunggu));
        Assert.Equal(0, await cloud2.SyncConflicts.CountAsync());
    }

    [Fact]
    public async Task NomorEventTidakKembarUntukBeberapaPerubahanSekaligus()
    {
        await _db.KosongkanAsync();
        await using var admin = _db.Admin();
        await using var cloud = _db.Cloud();
        var (adminId, serviceId, customerId) = await SisiSiapAsync(admin);
        var order = await BuatPesananAsync(admin, adminId, serviceId, customerId, 2m);
        await PembayaranService(admin).RecordCashAsync(
            new RecordCashPaymentRequest(order.Id, 20_000m, PaymentKind.Pelunasan, null, null),
            adminId, CancellationToken.None);

        var nomor = await admin.SyncOutbox.Select(s => s.Sequence).ToListAsync();
        Assert.Equal(4, nomor.Count);
        Assert.Equal(nomor.Count, nomor.Distinct().Count());

        // Nomor unik membuat induk selalu lebih dulu dari anak — tanpa itu urutan kirim jadi tebak-tebakan.
        var urut = await admin.SyncOutbox.OrderBy(s => s.Sequence)
            .Select(s => new { s.AggregateType, s.Operation }).ToListAsync();
        Assert.Equal(SyncAggregate.Customer, urut[0].AggregateType);
        Assert.Equal(SyncAggregate.Order, urut[1].AggregateType);
        Assert.Equal("created", urut[1].Operation);
        Assert.Equal(SyncAggregate.Payment, urut[2].AggregateType);
        Assert.Equal(SyncAggregate.Order, urut[3].AggregateType);
        Assert.Equal("pembayaran", urut[3].Operation);

        var mesin = _db.Mesin(admin, cloud, batch: 1);
        for (var i = 0; i < 4; i++)
        {
            await LepaskanBackoffAsync(admin, cloud);
            await mesin.JalankanSatuSiklusAsync(CancellationToken.None);
        }

        Assert.Equal(1, await cloud.Orders.CountAsync(o => o.Id == order.Id));
        Assert.Equal(1, await cloud.CustomerProfiles.CountAsync(p => p.Id == customerId));
        Assert.Equal(0, await admin.SyncOutbox.CountAsync(o => o.Status == SyncEventStatus.Menunggu));
    }
}
