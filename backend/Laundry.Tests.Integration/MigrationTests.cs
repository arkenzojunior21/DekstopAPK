using Laundry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Laundry.Tests.Integration;

/// <summary>
/// Gate Fase 2: migration harus bisa diterapkan pada database PostgreSQL baru.
/// Butuh instance admin lokal (scripts/db.ps1 start).
/// </summary>
public class MigrationTests
{
    private static string BaseConn =>
        EnvironmentConfig.Get("CONN_ADMIN",
            "Host=localhost;Port=5433;Database=laundry_admin;Username=laundry;Password=laundry_dev_2026");

    private static string WithDatabase(string conn, string db) =>
        new NpgsqlConnectionStringBuilder(conn) { Database = db }.ConnectionString;

    [Fact]
    public async Task MigrationDapatDiterapkanPadaDatabaseBaru()
    {
        var dbName = $"laundry_migtest_{Guid.NewGuid():N}"[..40];
        var adminConn = WithDatabase(BaseConn, "postgres");

        await using (var admin = new NpgsqlConnection(adminConn))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {dbName}";
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            var options = new DbContextOptionsBuilder<LaundryDbContext>()
                .UseNpgsql(WithDatabase(BaseConn, dbName))
                .Options;

            await using (var db = new LaundryDbContext(options))
            {
                await db.Database.MigrateAsync();
                Assert.True(await db.OrderNumberExistsAsync());
            }

            await using (var check = new NpgsqlConnection(WithDatabase(BaseConn, dbName)))
            {
                await check.OpenAsync();
                await using var cmd = check.CreateCommand();
                cmd.CommandText =
                    "select count(*) from information_schema.tables where table_schema='public'";
                var count = (long)(await cmd.ExecuteScalarAsync())!;
                Assert.True(count >= 25, $"tabel terbentuk: {count}");
            }
        }
        finally
        {
            await using var admin = new NpgsqlConnection(adminConn);
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS {dbName} WITH (FORCE)";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task UniqueConstraintMencegahNomorOrderGanda()
    {
        var options = new DbContextOptionsBuilder<LaundryDbContext>()
            .UseNpgsql(BaseConn)
            .Options;
        await using var db = new LaundryDbContext(options);

        var user = new Laundry.Domain.User
        {
            Role = Laundry.Domain.UserRole.Pelanggan,
            NormalizedPhone = $"TEST{Guid.NewGuid():N}"[..20],
            PasswordHash = "x",
            Profile = new Laundry.Domain.CustomerProfile { Nama = "Pelanggan Tes" },
        };
        await db.AddAsync(user);
        await db.SaveChangesAsync();

        var nomor = $"TEST-{Guid.NewGuid():N}"[..20];
        db.Orders.Add(new Laundry.Domain.Order { OrderNumber = nomor, CustomerId = user.Profile!.Id });
        await db.SaveChangesAsync();
        try
        {
            db.Orders.Add(new Laundry.Domain.Order { OrderNumber = nomor, CustomerId = user.Profile!.Id });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally
        {
            db.Orders.RemoveRange(db.Orders.Local);
            db.CustomerProfiles.Remove(user.Profile);
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }
}

public static class TestDbContextExtensions
{
    public static Task<bool> OrderNumberExistsAsync(this LaundryDbContext db) =>
        db.Database.SqlQueryRaw<string>(
            "select 'ok' as value from information_schema.columns where table_name='Orders' and column_name='OrderNumber' limit 1")
        .AnyAsync();
}
