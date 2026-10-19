# Laundry — Aplikasi Desktop Dua Role (Admin & Pelanggan)

Aplikasi kasir & manajemen laundry untuk Windows 10/11. Satu aplikasi, dua role setelah login: **Admin** (operasional toko, dapat bekerja offline) dan **Pelanggan** (pesan online, lihat status & tagihan).

Stack sesuai spesifikasi (`PRD.md`): **C# WPF (MVVM) + ASP.NET Core Web API + PostgreSQL**, dengan sinkronisasi lokal↔cloud berbasis outbox/idempotency.

## Peta berkas

### Frontend (tampilan) — `src/Laundry.Desktop`
Seluruh UI berada di sini dan hanya di sini: window, View XAML, ViewModel (MVVM via CommunityToolkit.Mvvm), tema light/dark, style, dan aset gambar. Frontend **tidak pernah** membuka koneksi database — semua baca/tulis lewat `Laundry.Api`.

### Backend (logika & data)
| Project | Isi |
|---|---|
| `src/Laundry.Domain` | Entitas, enum status, aturan bisnis murni (tanpa dependensi UI/DB) |
| `src/Laundry.Application` | Use case, DTO request/response, validator, interface layanan (termasuk `IWhatsAppClient`, `IPaymentGateway`) |
| `src/Laundry.Api` | ASP.NET Core Web API: endpoint, autentikasi & otorisasi role, validasi server-side |
| `src/Laundry.Infrastructure` | EF Core + Npgsql, migration, repository, adapter mock provider |
| `src/Laundry.Sync` | Worker sinkronisasi: outbox/inbox, retry exponential backoff, checkpoint, idempotency |
| `tests/` | Unit & integration test |

## Prasyarat (Windows)

1. .NET SDK 8 LTS — jika belum ada, pasang level pengguna (tanpa admin):
   ```powershell
   Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1
   & $env:TEMP\dotnet-install.ps1 -Channel 8.0 -InstallDir $env:LOCALAPPDATA\Microsoft\dotnet
   ```
2. PostgreSQL 17 (binari zip resmi, tanpa installer) di `%LOCALAPPDATA%\pgsql17`, dengan dua instance data hasil `initdb` di `%LOCALAPPDATA%\laundry-pg\{admin,cloud}` (port 5433 = lokal admin, 5434 = cloud).
3. Berkas `.env` di folder ini (lihat `.env.example`) berisi connection string development. `.env` tidak ikut ke git.

## Menjalankan

```powershell
# nyalikan / matikan database
powershell -File scripts\db.ps1 start
powershell -File scripts\db.ps1 status

# build & test seluruh solution
dotnet build Laundry.sln
dotnet test Laundry.sln

# jalankan API (http://localhost:5104, swagger di /swagger) lalu desktop client
dotnet run --project src/Laundry.Api
dotnet run --project src/Laundry.Desktop

# jalankan worker sinkronisasi admin ⇄ cloud (proses terpisah dari API)
dotnet run --project src/Laundry.Sync
```

Worker membaca `SYNC_BATCH` (default 100, maks 500) dan `SYNC_INTERVAL_DETIK` (default 15) dari `.env`. Tanpa worker, semua operasi lokal tetap jalan dan perubahan hanya menumpuk di antrean keluar — tidak ada data yang hilang.

### Jalur uji cepat (empat terminal)

```powershell
powershell -File scripts\db.ps1 start                          # 1 database dev
dotnet run --project src/Laundry.Api                           # 2 http://localhost:5104
dotnet run --project src/Laundry.Sync                          # 3 opsional: pekerja sinkronisasi
dotnet run --project src/Laundry.Desktop                       # 4 aplikasinya
```

Akun pertama kali dibuat, bukan dibawa kode: `POST /api/auth/bootstrap` membuat admin (endpoint ini menutup diri begitu satu admin ada), dan akun Pelanggan dibuat dari tab **Daftar** di jendela login (`POST /api/auth/register/start` + `/register/complete`). Pada environment Development kode OTP tampil langsung di layar login dan di log API (`[MOCK WA] OTP ...`) — tidak ada pesan WhatsApp yang benar-benar terkirim. Tidak ada kata sandi bawaan di repo — kredensial dev hanya ada di `.env` dan database lokal.

Worker yang sama juga mengosongkan antrean notifikasi WhatsApp tiap siklus. Provider yang dipakai sekarang masih **mock** (`mock-gateway`, `mock-whatsapp`): tidak ada QRIS sungguhan, tidak ada pesan yang benar-benar terkirim, dan tidak ada dana yang berpindah. Antrean notifikasi tidak pernah pura-pura sukses — baris dengan nomor tujuan tidak sah atau adapter yang belum punya credential ditandai `DitundaPerluKoneksi` dan tetap terlihat di `GET /api/providers/status`.

`WEBHOOK_SECRET` dipakai untuk memverifikasi tanda tangan HMAC-SHA256 pada `POST /api/payments/gateway/webhook`. Nilainya kosong, pendek, atau masih `CHANGE_ME` berarti semua webhook ditolak 401 dan tagihan gateway tidak bisa lunas dari sisi API.

Butuh data contoh di database dev? Set `SEED_DEV_CONTOH=1` sebelum menjalankan API pada environment Development. Katalog yang dibuat diberi label `(CONTOH)` dan tarifnya hanya placeholder — tarif asli usaha tetap harus diisi lewat menu katalog.

`GET /health` tidak hanya menjawab "proses API hidup": pemeriksaan `database-admin` ikut dibuka (batas 3 detik), jadi `Healthy` berarti API dan PostgreSQL sisi admin sama-sama terjangkau. Kalau API jalan tapi database mati, `/health` mengembalikan 503 dan layar login menampilkan "Server di ... hidup, tapi database-nya belum bisa dihubungi" — permintaan lain yang menabrak koneksi database juga dibalas 503 dengan pesan Indonesia, bukan pesan mentah dari EF Core.

## Aplikasi desktop (dua role)

Setelah login, menu yang muncul ditentukan peran yang dikembalikan server — bukan pilihan pengguna:

| Role | Menu |
|---|---|
| Admin | Beranda, Pesanan, Buat Pesanan, Katalog, Pelanggan, Slot, Komplain, Laporan, Audit, Sistem |
| Pelanggan | Beranda, Pesan, Pesanan saya, Komplain |

Tema terang/gelap tersedia di semua layar dan perpindahan lewat tombol di bilah samping; preferensi (alamat server + tema) tersimpan di `%LOCALAPPDATA%\LaundryDesk\preferensi.json`. Token sesi hanya hidup di memori proses — menutup aplikasi benar-benar mengakhiri sesi. Struktur, style, dan batas yang diketahui didokumentasikan di `docs/DECISIONS.md` (D-16, D-17). Satu batas yang diketahui dan belum dibereskan: popup kalender `DatePicker` tetap bertema terang saat aplikasi memakai tema gelap (kontrol `Calendar` bawaan WPF butuh restyle menyeluruh).

## Membangun berkas rilis

```powershell
# desktop (one-file, tetap butuh .NET Desktop Runtime 8 di mesin target)
dotnet publish src/Laundry.Desktop -c Release -p:PublishSingleFile=true -r win-x64 --self-contained false -o dist/desk

# API dan worker sinkronisasi
dotnet publish src/Laundry.Api  -c Release -o dist/api
dotnet publish src/Laundry.Sync -c Release -o dist/sync
```

`dist/` tidak ikut ke git. Jalankan dengan berkas `.env` yang benar berada di folder proyek atau salah satu induk dari folder eksekusi (connection string, `JWT_KEY`, `WEBHOOK_SECRET` dibaca dari sana). Installer NSIS/Inno sengaja belum dipakai — distribusi untuk sekarang adalah folder hasil publish di atas.

Satu hal yang perlu diketahui saat menguji hasil publish: `Laundry.Api.exe` / `Laundry.Desktop.exe` adalah apphost yang mencari runtime .NET di lokasi terpasang sistem. Di mesin yang .NET-nya terpasang level pengguna (hasil `dotnet-install.ps1 -InstallDir`), runtime itu tidak terdaftar secara global, jadi `.exe` akan menjawab "You must install or update .NET". Solusinya salah satu: pasang .NET Desktop Runtime 8 secara normal di mesin target, set `DOTNET_ROOT` ke folder runtime tersebut, atau jalankan lewat `dotnet Laundry.Api.dll`. Kedua jalur terakhir sudah dites dan jalan.

## Naik ke database & hosting online

Skema database dibuat lewat migrasi EF, bukan berkas SQL tangan:

```powershell
powershell -File scripts/migrate.ps1          # kedua sisi (admin lalu cloud)
powershell -File scripts/migrate.ps1 cloud    # hanya sisi cloud
```

Connection string diambil dari environment, atau dari `.env` kalau environment kosong. Database cloud biasanya diserahkan sebagai URI — bentuk itu bisa ditempel apa adanya ke `CONN_CLOUD`, karena `EnvironmentConfig` mengubahnya sendiri jadi bentuk kunci=nilai yang dimau Npgsql (`Host=...;Port=...;Database=...;Username=...;Password=...;SSL Mode=...`, termasuk melepas kode persen pada password):

```powershell
CONN_CLOUD=postgresql://pengguna:sandi@ep-contoh.neon.tech:5432/laundry_cloud?sslmode=require&SearchPath=public
```

`SearchPath=public` bukan hiasan. Endpoint pooled Neon tertentu mengembalikan `search_path` kosong, dan EF Core mengirim nama tabel tanpa skema — akibatnya setiap query mati dengan `relation "Orders" does not exist` padahal tabelnya ada. Param itu dikenali Npgsql (bukan `psql`/libpq, jadi pemeriksaan manual dengan `psql` harus melepasnya dan menulis `public."Orders"`). Gejalanya menipu: `SELECT 1` pada `/health` tetap hijau karena tidak menyentuh tabel.

Sisi cloud sengaja dibangun memakai konteks migrasi `LaundryDbContext`; lewat `CloudDbContext`, EF Core tidak melihat satu pun migrasi (terbukti pada database kosong: riwayat migrasinya terbentuk tapi nol tabel). Skemanya memang identik, jadi yang membedakan hanya connection string-nya.

### Memindahkan sisi admin ke Neon (satu kali, pakai dump)

Sisi admin tidak dibangun ulang dari nol — datanya dipindah, karena `SyncOutbox` sisi admin adalah sumber jejak sinkronisasi. Yang dipakai `pg_dump`/`pg_restore` (di mesin ini ada di `C:\Program Files\PostgreSQL\18\bin`, tidak di PATH):

```powershell
& "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe" -h localhost -p 5433 -U laundry -d laundry_admin --no-owner --no-privileges -F c -f admin-lokal.dump
& "C:\Program Files\PostgreSQL\18\bin\pg_restore.exe" -d "<URI admin, tanpa &SearchPath>" --no-owner --no-privileges --exit-on-error admin-lokal.dump
```

Hasil pemindahan pertama (18 Oktober 2026): `--exit-on-error` tanpa satu pun galat, lalu hitungan per tabel **sama persis** di kedua sisi — 26 tabel, 3.938 baris, termasuk `Orders` 159, `Users` 217, `CustomerProfiles` 216, `Payments` 127, `Refunds` 28, `SyncOutbox` 942, `__EFMigrationsHistory` 5. Riwayat migrasi ikut berpindah, jadi `dotnet ef database update` di kemudian hari melihat skema sudah naik dan tidak mencoba membuat ulang. Bukti tulisan berjalan: `SyncCheckpoints` di Neon bertambah `LastSuccessAtUtc` beberapa detik setelah worker lokal berputar.

Dua hal yang harus dihentikan sebelum dump: matikan `Laundry.Api` dan `Laundry.Sync` (kalau tidak, antrean bergerak di tengah dump dan hitungannya tidak cocok). PostgreSQL lokal port 5433 jangan dihapus — dia satu-satunya salinan sebelum dump ini, dan `tmp-verifikasi/admin-lokal.dump` adalah jalur rollback-nya.

Setelah `CONN_CLOUD` menunjuk ke database online, `Laundry.Sync` worker membangun replikanya sendiri — tidak ada impor berkas. Dibuktikan pada replika pertama (database cloud kosong, sisi admin berisi 115 pesanan hasil pengujian): 674 event keluar semua dalam 7 siklus (100 event per siklus, jeda `SYNC_INTERVAL_DETIK=15`), nol konflik, dan jumlah baris di kedua sisi sama persis — 151 pelanggan, 115 pesanan, 115 baris pesanan, 266 riwayat status, 91 pembayaran, 20 refund, 20 ledger, total pembayaran lunas Rp1.270.000. Yang memang sengaja tidak direplikasi: `AuditLogs` (jejak audit hidup di sisi tempat kejadian) dan akun Admin (hash kata sandi tidak pernah berpindah database). Replikasi penuh ini butuh sekitar 12 menit karena worker bicara ke jaringan per event; pemakaian harian hanya mengirim beberapa event per siklus sehingga tidak terasa.

Kalau ada event yang tidak bisa diterapkan, worker tidak membuangnya: barisnya di `SyncConflicts` sisi tujuan ditandai `PerluRekonsiliasi` dan event-nya tetap ada di `SyncInbox` dengan status `Gagal`. `Laundry.Sync` punya jalur antrean gagal + coba ulang (`AntreanGagalAsync` / `CobaUlangGagalAsync`, muncul di layar Sistem aplikasi desktop) untuk mengembalikan antrean itu ke `Menunggu`.

Di hosting, API cukup dijalankan dari hasil publish (`dotnet dist/api/Laundry.Api.dll`). Kalau host memberi port lewat variabel `PORT` dan tidak menyetel `ASPNETCORE_URLS` — kebiasaan Render/Fly.io/Koyeb — aplikasi bind ke `0.0.0.0:$PORT` dengan sendirinya. `ASPNETCORE_URLS` tetap menang kalau diset, jadi development (5104 lewat `launchSettings.json`) tidak berubah.

Batas yang jujur untuk versi online:

- `JWT_KEY` (minimal 32 byte) dan `WEBHOOK_SECRET` wajib diisi sendiri; repo tidak membawa nilai asli, dan `WEBHOOK_SECRET` yang kosong/`CHANGE_ME` berarti semua webhook ditolak.
- Pekerja backup butuh `pg_dump`/`pg_restore` di mesin tempat API jalan. Di container hosting umum perkakas itu tidak ada, jadi matikan dengan `BACKUP_AUTO=0` dan jalankan backup dari mesin yang punya perkakasnya (`powershell -File scripts/db.ps1 backup`) — bukan mengandaikan backup otomatis ikut online.
- Database gratis biasanya tidur saat tidak dipakai (scale-to-zero), jadi permintaan pertama setelah nganggur bisa lambat beberapa detik.
- Kuota, region, dan batas gratis provider berubah sewaktu-waktu — angka pastinya tidak ditulis di sini; cek halaman harga provider sebelum menentukannya.

### Host API lewat Docker (jalur yang disiapkan: Render paket gratis)

Tiga berkas di akar repo mengurus ini: `Dockerfile` (hanya membangun sisi API — `Laundry.Desktop` WPF dan `Laundry.Sync` tidak ikut, jadi tidak ada proyek `net8.0-windows` di jalur Linux), `render.yaml` (blueprint satu web service, region Singapore supaya dekat Neon), dan `.dockerignore` yang menahan `.env` supaya connection string serta kunci tidak pernah membeku di dalam image.

```powershell
dotnet publish src/Laundry.Api -c Release -o dist/api   # bukti jalur build-nya jalan (4 proyek, tanpa tests/desktop)
```

Isi variabel ini di dashboard Render (nilainya tidak pernah masuk git): `CONN_ADMIN`, `CONN_CLOUD`, `JWT_KEY` (minimal 32 byte), `WEBHOOK_SECRET`. Keduanya sekarang URI Neon dan **wajib membawa `&SearchPath=public`** (alasan di atas). `ENVIRONMENT=Production` dan `BACKUP_AUTO=0` sudah dibawa `render.yaml`. Setelah deploy dapat alamat `https://...`, isi alamat itu di kolom **SERVER** jendela login atau di layar **Sistem** aplikasi desktop.

Kalau `.env` disunting lewat skrip, tulis tanpa BOM. `Set-Content -Encoding UTF8` di Windows PowerShell 5.1 menempel `EF BB BF` di awal berkas, dan karena `CONN_ADMIN` adalah baris pertama maka kunci itu tidak lagi dikenali oleh perkakas yang mencocokkan teks mentah (.NET sendiri menghapus BOM saat membaca, jadi gejalanya hanya muncul di `grep`/`sed`/skrip periksa).

Dua batas yang perlu diketahui sebelum disebut "online penuh":

- `Laundry.Sync` tidak ikut di-host. Worker itu harus jalan di mesin yang bisa menjangkau kedua database; selama ini laptop, karena kedua database sudah di Neon maka laptop tetap cocok — jalankan `dotnet run --project src/Laundry.Sync` saat ingin data di sisi admin naik ke sisi cloud (atau tombol **Sinkron sekarang** di layar Sistem).
- `Dockerfile` belum pernah dibangun di mesin ini (tidak ada Docker Desktop terpasang). Yang terbukti di sini hanya `dotnet publish src/Laundry.Api -c Release` berdiri sendiri tanpa solution/tests. Build image-nya baru dibuktikan pada deployment pertama di Render.

## Peta endpoint (v1)

| Group | Endpoint penting |
|---|---|
| Auth | `POST /api/auth/{register/start,register/complete,login,refresh,logout,reset/start,reset/complete,bootstrap}`, `GET /api/auth/me` |
| Katalog | `GET/POST /api/catalog`, `GET/PUT /api/catalog/delivery-fee` |
| Pengaturan | `GET/PUT /api/settings/business-name` |
| Slot | `GET/POST /api/slots` |
| Pelanggan | `GET/POST /api/customers`, `GET/PUT /api/customers/{id}`, `PUT /api/customers/{id}/active` |
| Pesanan | `POST /api/orders` (walk-in), `POST /api/orders/online`, `GET /api/orders`, `/mine`, `/dashboard/{admin,customer}`, `/orders/{id}`, `/orders/{id}/receipt`, `POST /orders/{id}/{transition,actuals,estimate,cancel-request,cancel-decision}`, `GET /orders/{id}/payments` |
| Pembayaran | `POST /api/payments/{cash,refund,credit}`, `GET /api/payments/credit-balance` |
| Komplain | `POST /api/complaints`, `GET /api/complaints`, `POST /api/complaints/{id}/reply`, `PUT /api/complaints/{id}/status` |
| Sinkronisasi (Admin) | `GET /api/sync/status`, `GET /api/sync/failed?arah=admin_ke_cloud\|cloud_ke_admin`, `POST /api/sync/retry-failed`, `POST /api/sync/run` |
| Tagihan gateway (Admin) | `POST /api/payments/gateway/quote` (buat tagihan QRIS simulasi), `POST /api/payments/gateway/webhook` (anonim, wajib tanda tangan HMAC `X-Mock-Signature`) |
| Provider & notifikasi (Admin) | `GET /api/providers/status` (isi antrean notifikasi + daftar adapter live/tidak), `POST /api/providers/notifications/drain` (mengosongkan antrean sekarang) |
| Backup (Admin) | `GET /api/backup/status`, `POST /api/backup/jalan` (`{"target":"admin"｜"cloud"}`), `POST /api/backup/{id}/verifikasi`, `POST /api/backup/retensi`, `PUT /api/backup/konfigurasi` (retensi 1..90 hari, backup harian) |
| Rilis & konfigurasi (Admin) | `GET /api/app/versi`, `GET /api/app/update-check`, `POST /api/app/update-verifikasi` (sha256 installer di `UPDATE_DIR`), `GET /api/config/status` (kunci yang dibaca + terisi/tidak, tanpa nilainya) |
| Laporan (Admin) | `GET /api/reports/laporan` (semua angka sekaligus), `GET /api/reports/tabel?jenis=...`, `GET /api/reports/export?jenis=...&format=pdf\|xlsx\|csv` (unduh berkas) — `jenis`: `ringkasan`, `transaksi`, `harian`, `layanan`, `metode`, `piutang`, `audit` |
| Audit (Admin) | `GET /api/audit?dari=&sampai=&cari=&entitas=&halaman=&ukuran=` — hanya baca; tidak ada jalur tulis/ubah/hapus |

## Laporan & ekspor

Semua filter menerima tanggal ISO (`dari=2026-10-01&sampai=2026-10-15`, selalu dibaca UTC), tanpa
tanggal = 30 hari terakhir, dan rentang dipotong maksimal 366 hari dari sisi tertua supaya data
terbaru tidak hilang. Enam angka dilaporkan terpisah dan tidak saling menggantikan: omzet,
penerimaan tunai, DP/pelunasan, refund, piutang (posisi — termasuk tagihan yang terbuka sebelum
periode), dan saldo kredit (posisi ledger akhir periode). Pesanan batal tidak menagih apa pun.

Satu model tabel netral (`TabelLaporan`) dipakai layar dan ketiga mesin ekspor, jadi PDF, XLSX, dan
CSV selalu berisi kolom dan angka yang sama dengan yang tampil di aplikasi — tidak ada jalur hitung
kedua yang bisa berbeda. PDF ditulis sendiri tanpa library (A4, otomatis lanskap kalau kolomnya
tidak muat, font per sel dikecilkan sebelum dipotong, xref byte-akurat); XLSX menyimpan angka sebagai
sel numerik berformat `Rp#,##0` (bukan teks) supaya bisa dijumlah di Excel; CSV memakai `sep=;` +
BOM UTF-8 dan mengutip teks yang mirip rumus (`=`, `+`, `@`) agar tidak dieksekusi Excel.

## Backup, retensi, dan pemulihan

`powershell -File scripts\db.ps1 backup` membuat arsip `pg_dump` format custom untuk kedua database
lalu membacanya kembali dengan `pg_restore --list`; tanpa binari atau `.env` yang benar skrip menolak
bekerja, bukan pura-pura sukses. Password dikirim lewat environment proses anak, tidak pernah lewat
argumen baris perintah dan tidak pernah ditulis di dalam skrip.

API menjalankan pekerjaan yang sama setiap hari: `BackupWorker` memeriksa tiap `BACKUP_CEK_MENIT`
(default 30 menit) apakah backup sukses+terverifikasi terakhir sudah lebih tua dari 26 jam, lalu
meng-dump kedua target dan menerapkan retensi. Matikan lewat `BACKUP_AUTO=0` di `.env` atau
`PUT /api/backup/konfigurasi {"harian":false}`. Riwayatnya di tabel `BackupHistories` dan isinya
dapat dilihat pada `GET /api/backup/status`.

Cara `scripts\restore.ps1` memulihkan sengaja harus diketik oleh manusia — restore adalah operasi
yang tidak bisa dibatalkan, jadi tidak punya endpoint HTTP:

```powershell
powershell -File scripts\restore.ps1 -File admin-20261016-003145-2688e337.dump -Target admin
```

Retensi hanya menghapus berkas yang (a) bukan yang paling baru untuk targetnya, (b) pernah sukses,
dan (c) lolos `pg_restore --list` saat diperiksa ulang. Berkas yang tidak lolos verifikasi dibiarkan
dan riwayatnya ditandai tidak terverifikasi — kehilangan arsip diam-diam lebih berbahaya daripada
satu berkas sampah yang terlihat.

Pemeriksaan versi (`GET /api/app/update-check`) membaca manifest JSON di `UPDATE_CHECK_URL` dan tidak
pernah mengunduh atau memasang apa pun. Tanpa manifest jawabannya `belum_dikonfigurasi`; kegagalan
jaringan dilaporkan sebagai `gagal_diperiksa`, bukan disimpulkan sebagai "sudah terbaru".
`POST /api/app/update-verifikasi` menghitung sha256 installer yang sudah diunduh manual di `UPDATE_DIR`
dan mencocokkannya dengan yang diumumkan manifest; nama berkas dengan path atau ekstensi selain
`.exe` ditolak sebelum sistem file disentuh.

## Status implementasi per fase

Lihat `docs/IMPLEMENTATION_PLAN.md` (fase 1–10), `docs/DECISIONS.md` (keputusan teknis), `docs/OPEN_QUESTIONS.md` (keputusan bisnis yang menunggu jawaban — tidak ditebak).

Gerbang terakhir (18 Oktober 2026): `dotnet build Laundry.sln` **0 warning / 0 error**, `dotnet test Laundry.sln` **182 test hijau** (99 unit + 66 integrasi + 17 test desktop) tanpa satu pun yang dilewati. Fase 1–9 selesai; fase 10 selesai kecuali installer (sengaja belum, lihat "Membangun berkas rilis"). Provider pembayaran dan WhatsApp masih mock, dan tarif, nama usaha, serta kebijakan bisnis lain masih menunggu jawaban di `docs/OPEN_QUESTIONS.md` — tidak ada nilai yang dikarang.

## Catatan riwayat

Versi prototipe berbasis Electron/JavaScript pernah dikembangkan lebih dulu dan kini diarsipkan pada commit `b722d54` di riwayat git repo ini.
