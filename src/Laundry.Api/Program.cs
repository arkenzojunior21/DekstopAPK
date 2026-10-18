using System.Security.Claims;
using Laundry.Application.Abstractions;
using Laundry.Application.Auth;
using Laundry.Application.Core;
using Laundry.Infrastructure.Persistence;
using Laundry.Infrastructure.Payments;
using Laundry.Infrastructure.Security;
using Laundry.Infrastructure.Services;
using Laundry.Infrastructure.Sync;
using Laundry.Infrastructure.Whatsapp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using Laundry.Api.Infrastructure;
using Laundry.Infrastructure.Backup;
using Laundry.Infrastructure.Update;

var builder = WebApplication.CreateBuilder(args);

// Hosting gratis (Render, Fly.io, Koyeb) memberitahu port lewat variabel PORT dan expects
// aplikasi bind ke 0.0.0.0. ASPNETCORE_URLS tetap menang kalau diset — termasuk oleh
// launchSettings.json saat `dotnet run` di development.
var portHosting = Environment.GetEnvironmentVariable("PORT");
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS"))
    && int.TryParse(portHosting, out var nomorPort) && nomorPort is > 0 and < 65536)
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{nomorPort}");
}

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database-admin");

builder.Services.AddDbContext<LaundryDbContext>(o =>
{
    o.UseNpgsql(EnvironmentConfig.GetKoneksi("CONN_ADMIN"));
    if (builder.Environment.IsDevelopment()) o.EnableSensitiveDataLogging();
});
// Database cloud untuk sisi sync; model dan migrasi sama, hanya connection string berbeda.
builder.Services.AddDbContext<CloudDbContext>(o => o.UseNpgsql(EnvironmentConfig.GetKoneksi("CONN_CLOUD")));
builder.Services.AddSingleton(SyncConfig.DariLingkungan());
builder.Services.AddScoped<SyncEngine>();

var jwtKey = EnvironmentConfig.Get("JWT_KEY", "dev-only-key-change-me-dev-only-key");
builder.Services.AddSingleton<ITokenService>(_ => new JwtTokenService(jwtKey));
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IOtpSender, MockOtpSender>();
// Adapter provider: hanya mock yang tersedia sekarang. Secret webhook dari environment —
// selama kosong, semua webhook ditolak (bukan diterima tanpa verifikasi).
builder.Services.AddSingleton<IWhatsAppSender, MockWhatsAppSender>();
builder.Services.AddSingleton<IPaymentGateway>(_ =>
    new MockPaymentGateway(EnvironmentConfig.Get("WEBHOOK_SECRET")));
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
builder.Services.AddScoped<IAuthService>(sp => new AuthService(
    sp.GetRequiredService<LaundryDbContext>(),
    sp.GetRequiredService<IPasswordHasher>(),
    sp.GetRequiredService<ITokenService>(),
    sp.GetRequiredService<IOtpSender>(),
    sp.GetRequiredService<EventWriter>(),
    exposeOtpInResponse: EnvironmentConfig.Get("ENVIRONMENT", builder.Environment.EnvironmentName) == "Development"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<AuditWriter>();
builder.Services.AddScoped<EventWriter>();
builder.Services.AddScoped<SettingsStore>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<IPaymentService>(sp => sp.GetRequiredService<PaymentService>());
builder.Services.AddScoped<IPaymentGatewayService>(sp => sp.GetRequiredService<PaymentService>());
builder.Services.AddScoped<IComplaintService, ComplaintService>();
builder.Services.AddScoped<ISlotService, SlotService>();

// Fase 9: laporan dan ekspor. Perhitungan angka hidup di Domain (LaporanAngka); service hanya memproyeksikan baris.
builder.Services.AddScoped<IReportService, ReportService>();

// Fase 9: laporan + ekspor. Query hanya memproyeksikan baris; semua rumus angka ada di Domain.
builder.Services.AddScoped<IReportService, ReportService>();

// Fase 9: laporan + ekspor. Perhitungan angka ada di Domain (LaporanAngka), service hanya memproyeksikan baris.
builder.Services.AddScoped<IReportService, ReportService>();

// Fase 8: backup pg_dump + pemeriksaan versi. BackupService scoped karena menulis riwayat ke database.
builder.Services.AddSingleton(BackupConfig.DariLingkungan());
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<IBackupService>(sp => sp.GetRequiredService<BackupService>());
builder.Services.AddHttpClient<IUpdateChecker, UpdateChecker>(c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHostedService<BackupWorker>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = JwtTokenService.Issuer,
            ValidateAudience = true,
            ValidAudience = JwtTokenService.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

// Rate limiting sisi server, bukan hanya UI (PRD 6.1).
// AUTH_RATE_LIMIT_PER_MENIT hanya untuk menurunkan batas di test integrasi.
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit = int.Parse(EnvironmentConfig.Get("AUTH_RATE_LIMIT_PER_MENIT", "30"));
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseExceptionHandler(errApp => errApp.Run(async context =>
{
    var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
    var ex = feature?.Error;
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Api");
    logger.LogError(ex, "Unhandled error on {Path}", context.Request.Path);
    context.Response.ContentType = "application/json";
    var (code, message) = ex switch
    {
        // Koneksi database mati bukan salah pengguna: tanpa kasus ini, pesan bahasa Inggris
        // dari EF Core ("transient failure") muncul di layar login dengan status 400.
        _ when DatabaseTidakTerhubung(ex) => (StatusCodes.Status503ServiceUnavailable,
            "Database belum dapat dihubungi. Pastikan server PostgreSQL menyala, lalu coba lagi."),
        KeyNotFoundException ke => (StatusCodes.Status404NotFound, ke.Message),
        UnauthorizedAccessException ua => (StatusCodes.Status403Forbidden, ua.Message),
        InvalidOperationException io => (StatusCodes.Status400BadRequest, io.Message),
        _ => (StatusCodes.Status500InternalServerError, builder.Environment.IsDevelopment()
            ? $"{ex?.GetType().Name}: {ex?.Message}" : "Terjadi kesalahan internal."),
    };
    context.Response.StatusCode = code;
    await context.Response.WriteAsJsonAsync(new { error = message });
}));

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// Data contoh dev bersifat opsional dan sengaja tidak pernah aktif otomatis:
// tarif asli usaha harus diisi sendiri, bukan ditebak sistem.
if (builder.Environment.IsDevelopment() && EnvironmentConfig.Get("SEED_DEV_CONTOH") == "1")
{
    using var scope = app.Services.CreateScope();
    await DevelopmentSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<LaundryDbContext>());
}

app.Run();

public partial class Program
{
    /// <summary>
    /// Membedakan "database tidak terjangkau" dari <see cref="InvalidOperationException"/> bisnis
    /// (yang pesannya sudah berbahasa Indonesia) lewat rantai inner exception.
    /// </summary>
    public static bool DatabaseTidakTerhubung(Exception? ex)
    {
        for (var jalan = ex; jalan is not null; jalan = jalan.InnerException)
        {
            if (jalan is Npgsql.NpgsqlException or Microsoft.EntityFrameworkCore.DbUpdateException)
                return true;
        }

        return false;
    }
}
