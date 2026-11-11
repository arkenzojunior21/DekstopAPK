using Laundry.Application.Abstractions;
using Laundry.Application.Core;
using Laundry.Infrastructure.Persistence;
using Laundry.Infrastructure.Services;
using Laundry.Infrastructure.Sync;
using Laundry.Infrastructure.Whatsapp;
using Laundry.Sync;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// Dua sisi database: admin lokal (sisi kerja kasir) dan cloud (sisi pelanggan/analitik).
builder.Services.AddDbContext<LaundryDbContext>(o => o.UseNpgsql(EnvironmentConfig.GetKoneksi("CONN_ADMIN")));
builder.Services.AddDbContext<CloudDbContext>(o => o.UseNpgsql(EnvironmentConfig.GetKoneksi("CONN_CLOUD")));
builder.Services.AddSingleton(SyncConfig.DariLingkungan());
builder.Services.AddScoped<SyncEngine>();

// Notifikasi ikut jalan di host yang sama: antreannya menunggu koneksi provider, bukan antrean data.
builder.Services.AddSingleton<IWhatsAppSender, MockWhatsAppSender>();
builder.Services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

builder.Services.AddHostedService<SyncWorker>();
builder.Services.AddHostedService<NotificationWorker>();

var host = builder.Build();
host.Run();
