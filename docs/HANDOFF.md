# Catatan serah-terima dua sisi

Kanal komunikasi antara sisi server (`backend/`) dan sisi tampilan (`frontend/`). Formatnya ada di `AGENTS.md` bagian 2. **Aturan utama: entri baru di paling atas, entri lama tidak pernah diedit atau dihapus.** Kalau tidak ada pekerjaan yang tersisa untuk sisi lain, tulis `tidak ada` — jangan menghapus barisnya.

## 2026-11-11 — sisi server — Pekerja antrean ikut hidup di dalam API (`SYNC_AUTO`, D-24)

- Berubah: `SyncWorker` dan `NotificationWorker` pindah kelas ke `backend/Laundry.Infrastructure/Sync/` (namespace `Laundry.Infrastructure.Sync`), `Laundry.Infrastructure.csproj` menambah `Microsoft.Extensions.Hosting.Abstractions`, `backend/Laundry.Api/Program.cs` mendaftarkan keduanya saat `SYNC_AUTO=1`, `backend/Laundry.Sync/Program.cs` hanya berubah `using`. **Bentuk JSON dan nama endpoint tidak berubah** — `GET /api/sync/status`, `POST /api/sync/run`, `GET /api/sync/failed`, `POST /api/sync/retry-failed` tetap sama, hanya `pekerjaAktif` sekarang benar-benar bisa bernilai `true` di host yang tidak menjalankan `Laundry.Sync`.
- Berkas bersama yang kusentuh (umumkan di sini sesuai `AGENTS.md` bagian 1.4): `render.yaml` menambah `SYNC_AUTO=1`, `.env.example` menambah dokumentasi flag yang sama. Tidak ada rahasia di dalamnya dan tidak ada perubahan `Laundry.sln`/`Directory.Build.props`/`Dockerfile`.
- Perbaikan sekalian: `Program.cs` API ternyata mendaftarkan `IReportService` tiga kali dengan tiga komentar berbeda (sisa suntingan lama). Sisakan satu; tidak ada perilaku yang berubah karena resolusi DI mengambil pendaftar terakhir.
- Gerbang: build 0 warning | unit 99/99 | integrasi 66/66 | tampilan 17/17 | `dotnet publish backend/Laundry.Api -c Release` tetap 4 DLL sisi server. Bukti runtime: dengan `SYNC_AUTO=1` terhadap cluster dev, log memuat "Pekerja sinkronisasi mulai", `admin_ke_cloud: terkirim 100, diterapkan 100, gagal 0`, dan heartbeat `sync_detak_utc` bergerak dari `2026-10-19 00:02` ke waktu sekarang; tanpa flag, keduanya tidak muncul dan heartbeat tetap di nilai lama.
- Untuk sisi tampilan: jangan pakai `pekerjaAktif == false` sebagai alasan menampilkan "worker mati" saat demo ke arah URL publik — di deployment nilai itu kini bergantung pada `SYNC_AUTO`, bukan pada ada/tidaknya proses lain. Koreksi penting untuk entri pemandu di atas: **OTP hanya tampil di layar bila API berjalan dengan `ENVIRONMENT=Development`**; di URL publik (`ENVIRONMENT=Production`) `devCode` sengaja `null` dan kodenya hanya muncul di log host, jadi jalur **Lupa sandi**/**Daftar** yang kubagi itu berlaku untuk API lokal, bukan untuk instance Render.
- Butuh keputusan: empat variabel rahasia di dashboard Render (lihat entri sebelumnya) — sekarang `SYNC_AUTO=1` ikut dibawa blueprint, jadi tinggal menambah yang belum ada bila service-nya dibuat manual.


## 2026-11-11 — sisi server — Pemandu demo & uji untuk sisi tampilan

- Berubah: berkas baru `docs/PANDUAN-UJI-FRONTEND.md` (yang perlu dipasang, tiga cara mendapat API yang hidup, cara masuk tanpa meminta sandi, jalur klik per layar, peta layar ke berkas, daftar yang memang belum selesai) + satu baris rujukan di `AGENTS.md` bagian 8. Tidak ada kode yang berubah.
- Gerbang: tidak menyentuh kode, jadi tidak ada angka baru. API development hidup di `http://localhost:5104` (`/health` → 200) dan desktop sudah bisa dijalankan dari jalur `dotnet run --project frontend/Laundry.Desktop`.
- Untuk sisi tampilan: baca bagian 3 pemandu itu sebelum demo — masuk admin lewat **Lupa sandi** (OTP tampil di layar), jangan minta kata sandi siapa pun; akun Pelanggan dibuat sendiri lewat tab **Daftar**.
- Butuh keputusan: alamat API untuk laptop temanmu. Jalus Render belum bisa ditunjuk karena empat rahasia di dashboard masih kosong; sementara itu pilihannya satu laptop dua jendela, atau membuka port 5104 di firewall (perlu hak administrator satu kali).

## 2026-11-11 — sisi server — Pemisahan folder jadi `backend/` + `frontend/` (D-23)

- Berubah: seluruh proyek pindah lewat `git mv` dari `src/` dan `tests/` ke `backend/` (Domain, Application, Infrastructure, Api, Sync, Tests.Unit, Tests.Integration) dan `frontend/` (Desktop, Tests.Desktop); `Laundry.sln` + jalur di `Dockerfile`, `.dockerignore`, `scripts/migrate.ps1`, `README.md`, `docs/DECISIONS.md` ikut disesuaikan. Bentuk JSON request/response dan nama endpoint **tidak berubah sama sekali**.
- Gerbang: build 0 warning | unit 99/99 | integrasi 66/66 | tampilan 17/17.
- Untuk sisi tampilan: (1) exe hasil build sekarang di `frontend\Laundry.Desktop\bin\...`, `tmp-verifikasi\verifikasi-ui.ps1` sudah kunyalakan ke sana; (2) referensi proyek desktop kini `..\..\backend\Laundry.Application` dan `..\..\backend\Laundry.Domain`, jangan ditulis ulang dengan `src\`; (3) `KunciResourceTerkunciTest.cs` mencari XAML di `frontend\Laundry.Desktop` — kalau nanti menata ulang folder tampilan, perbarui satu baris itu; (4) publish satu berkas sisi tampilan sudah terbukti jalan dari jalur baru (71,9 MB, `dotnet publish frontend/Laundry.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true ...`).
- Butuh keputusan: bersih-bersih data pengujian di database sisi admin (147/150 layanan dan 134/217 pelanggan adalah sisa uji) — belum kubuka jalur hapus apa pun.

## 2026-11-11 — sisi server — Kartu SERVER di jendela login bisa disunting

- Berubah: `frontend/Laundry.Desktop/ViewModels/LoginViewModel.cs` (properti `Server` jadi bisa disunting + perintah `SimpanServer`) dan `frontend/Laundry.Desktop/Views/JendelaLogin.xaml` (TextBox + tombol Simpan/Periksa). Alamat yang disimpan masuk ke `%LOCALAPPDATA%\LaundryDesk\preferensi.json`, dan token tetap hanya hidup di memori proses.
- Catatan wilayah: perubahan ini menyentuh folder sisi tampilan, dan itu memang kubuka sebelum pemisahan wilayah berlaku. Mulai sekarang perbaikan rasa pada layar itu tugas sisi tampilan; sisi server tidak menyentuh XAML.
- Gerbang: build 0 warning | 182 hijau. Sudah dibuktikan nyata di aplikasi berjalan: alamat tidak valid ditolak dengan pesan Indonesia, dan alamat valid membuat aplikasi terhubung ke API yang dituju.
- Untuk sisi tampilan: tidak ada.
