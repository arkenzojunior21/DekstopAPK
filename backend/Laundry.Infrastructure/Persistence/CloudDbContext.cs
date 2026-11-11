using Microsoft.EntityFrameworkCore;

namespace Laundry.Infrastructure.Persistence;

/// <summary>
/// Konteks database cloud. Model dan migrationnya identik dengan <see cref="LaundryDbContext"/>;
/// yang berbeda hanya connection string-nya, sehingga worker sinkronisasi dapat memegang
/// kedua sisi dalam satu proses tanpa menduplikasi definisi entitas.
/// </summary>
public class CloudDbContext(DbContextOptions<CloudDbContext> options) : LaundryDbContext(options);
