# Pegangan kerja dua sisi — LaundryFlow Desk

Berkas ini dibaca oleh setiap yang mengerjakan repo ini. Isinya aturan pembagian wilayah, cara saling memberi tahu, dan gerbang mutu yang tidak boleh dilewati. Tidak ada rahasia di sini: koneksi database, kunci JWT, dan kata sandi tetap di `.env` dan tidak pernah ditulis di berkas apa pun yang masuk git.

## 1. Pembagian wilayah

| Sisi | Punya folder | Jangan menyentuh |
|---|---|---|
| **Sisi server (backend)** | `backend/Laundry.Domain`, `backend/Laundry.Application`, `backend/Laundry.Infrastructure`, `backend/Laundry.Api`, `backend/Laundry.Sync` beserta dua proyek ujinya | seluruh `frontend/` |
| **Sisi tampilan (frontend)** | `frontend/Laundry.Desktop`, `frontend/Laundry.Tests.Desktop` | seluruh `backend/` |

Aturan dasar yang mengikat dua sisi:

1. **Tampilan tidak pernah membuka koneksi database.** Semua baca/tulis lewat HTTP ke `Laundry.Api`. Tidak boleh ada `Npgsql`, `DbContext`, atau connection string di proyek desktop.
2. **Validasi dua lapis.** Frontend memvalidasi untuk kenyamanan, backend memvalidasi karena itu satu-satunya yang bisa dipercaya. Frontend tidak boleh menganggap "sudah divalidasi di tampilan" sebagai alasan menghapus pemeriksaan di server, dan backend tidak boleh memindahkan validasi ke tampilan.
3. **Bahasa Indonesia di mana-mana**: komentar kode, nama metode lokal, pesan galat, dokumen, dan teks UI. Istilah teknis yang memang nama resmi (`IdempotencyKey`, `SearchPath`) tetap apa adanya.
4. Yang **tidak boleh diubah diam-diam** oleh sisi mana pun: `Laundry.sln`, `Directory.Build.props`, `Dockerfile`, `render.yaml`, `.dockerignore`, `scripts/`, `.env.example`, `docs/`. Berkas-berkas itu milik bersama; perubahannya wajib diumumkan lewat kanal di bagian 2 sebelum dikerjakan.
5. Kontrak antara dua sisi adalah **bentuk JSON yang dikirim `Laundry.Api`** dan **nama endpoint**. Bentuk itu hari ini tinggal di dua tempat: sebagian besar record request/response di `backend/Laundry.Api/Controllers/*.cs` (misalnya `LoginRequest`), dan DTO use case di `backend/Laundry.Application`. Mengubah nama field, tipe, atau bentuknya = mengubah kontrak, dan itu selalu pekerjaan dua sisi, bukan satu sisi saja.

## 2. Kanal komunikasi: `docs/HANDOFF.md`

Ini satu-satunya tempat dua sisi saling bicara. Bentuknya **append-only**: entri baru ditambahkan di paling atas, entri lama tidak pernah diedit atau dihapus. Satu entri per paket kerja, tidak perlu panjang.

Format baku:

```
## 2026-11-11 — sisi server — [judul satu baris]
- Berubah: <berkas/endpoint/DTO yang tersentuh>
- Gerbang: build 0 warning | unit 99/99 | integrasi 66/66
- Untuk sisi tampilan: <apa yang harus dikerjakan pihak lain, atau "tidak ada">
- Butuh keputusan: <hal yang menunggu jawaban pemilik proyek, atau "tidak ada">
```

Tiga keadaan yang **wajib** menulis entri sebelum pekerjaan lain dilanjutkan:

- kontrak berubah (nama/ tipe/ field baru di request atau response, endpoint baru, status code baru);
- ada pekerjaan yang sengaja dibiarkan setengah jalan dan sisi lain mungkin menyentuhnya;
- ada batas atau jebakan baru yang ditemukan di mesin ini (misalnya versi PostgreSQL, perilaku pooler Neon).

Kalau pesan untuk sisi lain belum dikerjakan, jangan dihapus — biarkan entri-nya berdiri dan tambahkan balasan baru di atas yang merujuk judulnya.

## 3. Aturan git (bagian paling sering merusak pekerjaan orang lain)

- Satu cabang per paket kerja: `server/<nama>` atau `tampilan/<nama>`, lalu masuk ke `main` lewat pull request. Bekerja langsung di `main` hanya untuk perbaikan satu berkas yang jelas.
- `git pull --rebase` di awal sesi, **bukan** di akhir, dan hanya setelah pohon kerja bersih.
- Dilarang tanpa izin pemilik proyek: `git reset --hard`, `git clean -fd`, `git stash` (apalagi menstash pekerjaan sisi lain), `git checkout -- <berkas>` milik orang lain, `push --force` ke `main`, dan `--no-verify`.
- `git add` selalu menyebut berkas secara eksplisit. `git add -A` berisiko ikut menyeret `.env`, `dist/`, dan `tmp-verifikasi/`.
- Commit atas nama pemilik repo (`git config user.name` sudah diatur), dan **commit serta push dikerjakan pemilik**, bukan oleh yang mengerjakan, supaya tiap masuk terverifikasi lebih dulu.
- Sebelum commit: `git status`, dan baca daftar berkas yang ikut. Kalau ada nama berkas yang mencurigakan bisa mengandung rahasia, berhenti dan tanya.

## 4. Mesin ini: hal yang harus diketahui supaya tidak mengulang kegagalan

- .NET SDK 8 terpasang **level pengguna**, tidak ada di PATH global. Setiap panggilan shell perlu:
  ```powershell
  $env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:USERPROFILE\.dotnet\tools;$env:PATH"
  $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
  ```
- Tidak ada Python dan tidak ada installer yang boleh minta hak administrator. Jangan sarankan jalur yang memakai keduanya.
- Sebelum `dotnet build`, matikan dulu proses yang mengunci berkas hasil build: `taskkill /F /IM Laundry.Desktop.exe /T` dan `taskkill /F /IM Laundry.Api.exe /T`. Kalau tidak, muncul MSB3026/MSB3027 "file locked".
- PostgreSQL dev **dua instance manual** di `%LOCALAPPDATA%\laundry-pg\{admin,cloud}` (port 5433 admin, 5434 cloud), dijalankan `powershell -File scripts\db.ps1 start`. Cluster itu di-`initdb` oleh **PostgreSQL 17**; binari 18 tidak bisa membukanya dan `pg_dump` akan menghasilkan berkas 0 byte.
- Di laptop ini juga ada **Windows service PostgreSQL pada port 5432** yang dipakai pekerjaan lain. Service itu tidak boleh dihentikan saat bersih-bersih proses.
- Database produksi ada di Neon (project `laundry-admin` dan `laundry-cloud`). URI pooled dari sisi admin butuh `&SearchPath=public` di akhir connection string, dan hanya Npgsql yang menerima parameter itu — `psql` menolaknya, dan itu bukan bukti koneksi aplikasi rusak. Jangan pernah menyentuh project Neon lain yang ada di akun.
- Berkas `.env` dicari dari folder ini ke atas; letaknya tetap di root repo dan tidak ikut ke git.
- Hasil `dotnet publish` berupa `.exe` apphost bisa gagal menemukan runtime di mesin ini; jalankan dengan `DOTNET_ROOT` diset ke runtime level pengguna, atau `dotnet <nama>.dll`.
- **Satu database hanya boleh punya satu penguras antrean.** `NotificationDispatcher.DrainAsync` memilih baris `Menunggu` tanpa klaim baris (`FOR UPDATE SKIP LOCKED` tidak dipakai), jadi `SYNC_AUTO=1` (pekerja sync + notifikasi jalan di dalam `Laundry.Api`) dan `dotnet run --project backend/Laundry.Sync` tidak boleh hidup bersamaan pada database yang sama. Di laptop: `SYNC_AUTO` kosong. Di hosting: `SYNC_AUTO=1` dan tidak ada worker lokal yang menyentuh database itu.
- Mengubah skema tidak otomatis berlaku di Neon. Yang terpasang di cloud hanya dibangun lewat `powershell -File scripts/migrate.ps1` dari laptop ini (URI pooled + `&SearchPath=public`); tidak ada `MigrateAsync` saat API menyala, jadi deploy berikutnya dengan migrasi baru akan membaca skema lama sampai skrip itu dijalankan.

## 5. Gerbang mutu

Tidak ada paket kerja yang dianggap selesai sebelum baris di sisi masing-masing hijau. Sesudah pemindahan folder (D-23), total seluruhnya 182.

| Sisi | Perintah wajib |
|---|---|
| dua-duanya | `dotnet build Laundry.sln` → **0 warning** |
| server | `dotnet test backend/Laundry.Tests.Unit` (99) lalu `dotnet test backend/Laundry.Tests.Integration` (66) |
| tampilan | `dotnet test frontend/Laundry.Tests.Desktop` (17) |

Test integrasi butuh kedua instance PostgreSQL dev menyala. Kalau gagal dengan `NpgsqlException` atau HTTP 503 "Database belum dapat dihubungi", nyalakan `scripts/db.ps1 start` lebih dulu — itu penyebab yang paling sering, bukan kerusakan kode.

Perubahan apa pun pada skema database juga wajib: `powershell -File scripts/migrate.ps1` untuk sisi yang berubah, dan catatan bahwa kedua sisi (lokal dan Neon) harus punya set migrasi yang identik.

## 6. Cara berpikir yang diharapkan

- **Jangan mengklaim yang belum dibuktikan.** Operasi ke layanan luar (hosting, provider pesan, gateway bayar, Neon) hanya boleh dilaporkan selesai setelah ada tanggapan nyata dari layanan itu. Kalau ternyata tadi melaporkan keberhasilan yang keliru, tarik kembali pernyataan itu dengan jelas, bukan diam-diam.
- **Jangan mengarang nilai bisnis**: nama provider, tarif, endpoint, nomor webhook, ambang retensi. Kalau nilainya belum ada, tulis `docs/OPEN_QUESTIONS.md` dan pakai placeholder yang kelihatan sebagai placeholder.
- **Perubahan terkecil yang menyelesaikan masalah.** Perbaikan bug tidak sekaligus jadi kesempatan merapikan folder lain; pekerjaan tampilan (rasa, warna, animasi) adalah wilayah sisi tampilan dan tidak boleh dikerjakan sambil lalu dari sisi server.
- **Aplikasi harus tetap bisa mencatat tanpa internet.** Sisi admin bekerja lokal dan antrean keluar (`SyncOutbox`) menahan perubahan sampai ada koneksi. Setiap fitur baru di sisi server harus tetap benar kalau desktop sedang luring.
- Kode yang sudah berjalan tidak dihapus sebelum ada penggantinya yang terbukti, dan tidak ada kompatibilitas pura-pura (berkas kosong, `// removed`) untuk jalur yang sudah pasti tidak dipakai.

## 7. Status yang diketahui sekarang, supaya tidak "diperbaiki" dua kali

- Provider pesan dan gateway pembayaran masih adapter mock: kredensial vendor yang benar belum ada, jadi pengiriman pesan hanya dicatat dan QRIS dibuat lewat jalur lokal. Ini pilihan, bukan bug.
- Kode OTP muncul di layar login dan di log API pada environment Development. Sengaja, supaya pengujian tidak butuh nomor asli.
- Popup kalender `DatePicker` masih bertema terang saat aplikasi memakai tema gelap (kontrol `Calendar` bawaan WPF perlu restyle menyeluruh). Batas ini sudah tercatat di README.
- Data pengujian masih banyak di database sisi admin (147 dari 150 layanan, 134 dari 217 pelanggan adalah sisa uji). Keputusan membersihkannya belum diambil pemilik; jangan dihapus sepihak.
- Deployment ke hosting gratis masih menunggu nilai rahasia diisi di dashboard Render; `render.yaml` sengaja tidak memuat nilai rahasia apa pun.
- Antrean sync dan notifikasi sudah punya penggerak di sisi host: `render.yaml` menyetel `SYNC_AUTO=1` sehingga pekerja berjalan di dalam proses API (D-24). Jangan "memperbaiki" ini dengan menambahkan service worker kedua di Render, dan jangan menyalakan `SYNC_AUTO` di laptop.

## 8. Rujukan

- `README.md` — cara menjalankan, struktur berkas, dan perintah publish.
- `docs/DECISIONS.md` — keputusan teknis D-1 sampai D-24 beserta alasan dan batasnya. Baca sebelum mengubah perilaku yang sudah ada.
- `docs/OPEN_QUESTIONS.md` — hal yang menunggu keputusan pemilik proyek.
- `docs/PANDUAN-UJI-FRONTEND.md` — pemandu sisi tampilan: yang perlu dipasang, cara masuk, jalur klik per layar, peta layar ke berkas.
- `docs/HANDOFF.md` — kanal komunikasi dua sisi (format di bagian 2).
