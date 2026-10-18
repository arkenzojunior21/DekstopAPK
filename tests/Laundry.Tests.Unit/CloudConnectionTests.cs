using Laundry.Infrastructure.Persistence;
using Npgsql;

namespace Laundry.Tests.Unit;

/// <summary>
/// Database cloud menyerahkan connection string sebagai URI
/// (postgresql://pengguna:sandi@host:5432/basisdata?sslmode=require), dan Npgsql menolak bentuk
/// itu kalau langsung ditempel ke .env. <see cref="EnvironmentConfig.DariUri"/> yang mengubahnya
/// jadi kunci=nilai, termasuk melepas kode persen pada password dan memetakan parameter query.
/// Host di bawah contoh sintetis, bukan milik provider mana pun.
/// </summary>
public sealed class CloudConnectionTests
{
    [Fact]
    public void UriCloudTeruraiJadiKoneksiNpgsql()
    {
        var uri = "postgresql://laundry:SandiCloud123@ep-contoh-1234.us-east-2.aws.neon.tech:5432/laundry_cloud?sslmode=require";

        var csb = new NpgsqlConnectionStringBuilder(EnvironmentConfig.DariUri(uri));

        Assert.Equal("ep-contoh-1234.us-east-2.aws.neon.tech", csb.Host);
        Assert.Equal(5432, csb.Port);
        Assert.Equal("laundry_cloud", csb.Database);
        Assert.Equal("laundry", csb.Username);
        Assert.Equal("SandiCloud123", csb.Password);
        Assert.Equal(SslMode.Require, csb.SslMode);
    }

    [Fact]
    public void PasswordCloudBerkodePersenTetapUtuh()
    {
        var uri = "postgresql://laundry:abc%2Fdef%40ghi%3Bjkl@ep-contoh.neon.tech:5432/db?sslmode=require";

        var csb = new NpgsqlConnectionStringBuilder(EnvironmentConfig.DariUri(uri));

        Assert.Equal("abc/def@ghi;jkl", csb.Password);
    }

    [Fact]
    public void ParameterQueryDipelajariKeEjaanNpgsql()
    {
        var csb = new NpgsqlConnectionStringBuilder(EnvironmentConfig.DariUri(
            "postgres://laundry:sandi@ep-contoh.neon.tech/neondb?sslmode=require&connect_timeout=7&channel_binding=require"));

        Assert.Equal(SslMode.Require, csb.SslMode);
        Assert.Equal(7, csb.Timeout);
        Assert.Equal(ChannelBinding.Require, csb.ChannelBinding);
    }

    [Fact]
    public void ParameterAsingDiteruskanApaAdanya()
    {
        // Tidak dikenali = tetap ditulis apa adanya, supaya Npgsql menolak dengan pesan yang
        // terlihat alih-alih parameternya hilang diam-diam.
        var hasil = EnvironmentConfig.DariUri(
            "postgres://laundry:sandi@ep-contoh.neon.tech/neondb?sslrootcert=%2Fcerts%2Fbundle.pem");

        Assert.Contains("sslrootcert=/certs/bundle.pem", hasil);
    }

    [Fact]
    public void PortCloudYangTidakDitulisDibiarkanBawaanNpgsql()
    {
        var csb = new NpgsqlConnectionStringBuilder(
            EnvironmentConfig.DariUri("postgres://laundry:sandi@ep-contoh.neon.tech/neondb?sslmode=require"));

        Assert.Equal(5432, csb.Port);
    }

    [Fact]
    public void ConnectionStringKunciNilaiLewatTanpaDiubah()
    {
        const string dev = "Host=localhost;Port=5433;Database=laundry_admin;Username=laundry;Password=laundry_dev_2026";

        Assert.Equal(dev, EnvironmentConfig.DariUri(dev));
    }

    [Fact]
    public void NilaiKosongTidakDibuatJadiKoneksiAneh()
    {
        Assert.Equal(string.Empty, EnvironmentConfig.DariUri(string.Empty));
    }
}
