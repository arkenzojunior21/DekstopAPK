# Rencana Implementasi — Aplikasi Laundry Dua Role

Baseline: `PRD.md`. Dokumen ini memecah pekerjaan menjadi fase berurutan sesuai PROMPT agent, lengkap dengan dependensi, risiko, dan acceptance criteria tiap fase.

## Kondisi lingkungan (hasil inspeksi Fase 0)

- Windows 11, repo `DekstopAPK` (branch `main`).
- .NET SDK 8 LTS dipasang level pengguna (tanpa admin) di `%LOCALAPPDATA%\Microsoft\dotnet`.
- PostgreSQL dipasang level pengguna dari distribusi binari resmi (initdb + pg_ctl, tanpa service Windows); instance lokal dipakai sebagai database admin, dan sebagai "cloud" pada lingkungan pengembangan (lihat DECISIONS D-02).
- Implementasi JavaScript lama (LaundryFlow Desk / Electron) dihapus dari working tree; riwayat lengkapnya ada pada commit `b722d54`.

## Fase dan dependensi

| # | Fase | Dependensi | Acceptance criteria ringkas |
|---|------|-----------|------------------------------|
| 1 | Solution foundation | lingkungan .NET | `dotnet build` solution lulus; struktur project + nullable/analyzers/DI/logging aktif; README prasyarat & cara run |
| 2 | Database & domain | 1 | Skema PostgreSQL (Bagian 7 PRD) sebagai migration EF Core; domain + unit tests kalkulasi lulus; constraint FK/unique/check berfungsi |
| 3 | Autentikasi & otorisasi | 2 | Bootstrap admin; registrasi OTP (dev: OTP tertulis di log/endpoint sandbox); lockout 5x/15 mnt; role & ownership divalidasi server; tests IDOR lulus |
| 4 | UI desktop dua role | 3 | Satu installer/app; login → dashboard Admin atau Pelanggan sesuai role; design system light/dark; loading/empty/error state |
| 5 | Fitur inti laundry | 2,4 | Pesanan walk-in & online; state machine status + history; berat aktual & hitung ulang + price snapshot; DP/pelunasan tunai; struk (snapshot + endpoint; render PDF dilakukan di UI fase 4/10); refund; aturan lunas-before-serahkan berlaku |
| 6 | Offline-first & sinkronisasi | 5 | Outbox/inbox durable, idempotency key, checkpoint; simulasi disconnect/crash/duplikat/out-of-order lulus tests; dashboard sync indicator berfungsi |
| 7 | Integrasi provider | 3,5 | Abstraksi WhatsApp & payment gateway + mock adapter dev; tidak ada kredensial fiktif di production path; webhook handler idempotent (diverifikasi terhadap mock signature) |
| 8 | Backup, restore, update, konfigurasi | 6 | Backup harian + retensi 7 hari + verifikasi; restore terkendali; pemeriksaan update dgn verifikasi hash; konfigurasi hybrid tanpa secret di repo |
| 9 | Laporan & audit | 5 | Grafik + filter tanggal; ekspor PDF/XLSX sesuai filter; audit append-only utk order/payment/refund/credit; tests peran lulus |
| 10 | Hardening & rilis | semua | Suite test lengkap hijau; installer desktop ter-build; dokumentasi operasi final; laporan akhir jujur soal mock & open question |

## Risiko utama & mitigasi

1. **Cakupan PRD sangat besar** (sync dua database + gateway + OTP). Mitigasi: urutan prioritas Bagian 8 prompt; provider eksternal = adapter + mock, fitur finansial eksternal ditandai `OPEN QUESTION`, bukan dikarang.
2. **PostgreSQL tanpa admin** — tidak ada service Windows. Mitigasi: pg_ctl manual + skrip start; DECISIONS D-03.
3. **WPF hanya bisa build di Windows** — sesuai (target Windows 10/11). Mitigasi: CI lokal via skrip build.
4. **Kredensial cloud/QRIS/WA Business tidak tersedia** — PRD melarang klaim palsu. Mitigasi: production adapter nonaktif sampai credential sah; sandbox/mock eksplisit.
5. **Deadline tugas sekolah** — memitigasi dengan demo berjalan pada Fase 5–6 meski Fase 7–8 masih mock.

## Definisi "selesai" per fase

Build lulus + unit test lulus + test integrasi relevan lulus + docs/README diperbarui. Tidak ada fitur yang dilaporkan selesai tanpa dijalankan.

## Progres tercatat

| Fase | Status backend (API) | Status UI desktop |
|---|---|---|
| 1 Solution foundation | selesai — 7 project, build 0 warning | — |
| 2 Database & domain | selesai — migration `InitialCreate` (+`OrderCancelFields`, `RefundOrderLink`) apply di instance admin & cloud; 58 unit test | — |
| 3 Auth & otorisasi | selesai — bootstrap/OTP/login/lockout/refresh rotation/logout/reset-revoke + rate limiter; 6 test integrasi | belum |
| 4 UI dua role | selesai — 15 endpoint dibaca klien desktop (`KlienApi` + JWT, refresh otomatis); frontend tidak pernah membuka koneksi database | selesai — WPF MVVM (CommunityToolkit.Mvvm), design system `Terang`/`Gelap`+`Kontrol` dengan kunci tema yang dipaksa identik oleh test, `ShellViewModel` 10 menu Admin & 4 menu Pelanggan sesuai peran dari server, `JalankanAsync` anti klik ganda, ComboBox/DatePicker di-restyle, `SelPotong` untuk teks panjang, 14 layar (Beranda Admin/Pelanggan, Pesanan, Buat Pesanan, Katalog, Pelanggan, Slot, Komplain, Laporan, Audit, Sistem, Pesanan saya, login, struk). Semua layar dibuka & diisi lewat automasi UI Automation terhadap API+PostgreSQL dev; pesanan dan komplain dari UI terkonfirmasi sampai baris database (lihat D-16/D-17). Batas yang dicatat: popup kalender `DatePicker` tetap terang di tema gelap |
| 5 Fitur inti | selesai — katalog, slot, pelanggan, pesanan (walk-in & online, state machine + history, berat aktual + price snapshot), pembayaran tunai/kredit + refund, komplain, struk snapshot, dashboard; 8 test integrasi | belum |
| 6 Sync offline-first | selesai — outbox/inbox dua arah + dedup EventId, dependency rank, checkpoint, detak, backoff 5x lalu `Gagal`, konflik finansial → `SyncConflict` (bukan timpa); worker `Laundry.Sync`; 4 endpoint `/api/sync/*`; 11 test integrasi (duplikat, urutan acak, crash, offline, jam simpang, retry, nomor event tidak kembar) — lihat juga D-13 soal urutan antrean | — |
| 7 Provider (WhatsApp/gateway) | selesai (mock) — interface `IPaymentGateway`/`IWhatsAppSender` + `MockPaymentGateway`/`MockWhatsAppSender` (`IsLive=false`, dilaporkan apa adanya di `/api/providers/status`); tagihan QRIS simulasi + webhook HMAC idempoten (401/400/422/200, nominal beda → audit + tolak, tidak menimpa uang); `NotificationDispatcher` di worker: `Terkirim`/`Gagal`/`DitundaPerluKoneksi` (tidak pernah pura-pura sukses); OTP tetap `MockOtpSender`; 4 endpoint `/api/payments/gateway/*` & `/api/providers/*`; 14 test integrasi (7 webhook/tagihan + 7 antrean notifikasi) | — |
| 8 Backup/update/konfigurasi | selesai — `BackupService`: pg_dump format custom + verifikasi `pg_restore --list` + riwayat `BackupHistories` + retensi yang hanya menghapus berkas terverifikasi non-terbaru; `BackupWorker` harian di API; 5 endpoint `/api/backup/*` (tanpa restore — sengaja); `UpdateChecker` + 3 endpoint `/api/app/*` (tanpa unduh/pasang otomatis, status jujur); `GET /api/config/status` (14 kunci + terisi, tanpa nilai); `scripts/pg-env.ps1`/`db.ps1`/`restore.ps1` sudah bebas password bawaan dan restore butuh konfirmasi diketik. Sudah dijalankan betulan: `db.ps1 status`/`backup` (2 arsip, 118 objek terverifikasi) dan tiga jalur penolakan `restore.ps1` (target asing, berkas tiada, konfirmasi batal). 8 test unit aturan murni + 7 test integrasi API/backup | — |
| 9 Laporan & audit | selesai — `LaporanAngka`/`RentangLaporan` di Domain (6 angka terpisah: aliran vs posisi, rentang 30 hari bawaan, potong dari sisi tua di 366 hari, deret harian berisi hari kosong); `ReportService` + `TabelLaporan` netral; ekspor tulisan sendiri PDF 1.4 (xref byte-akurat, lanskap otomatis, font dikecilkan sebelum potong) + XLSX ClosedXML (sel numerik) + CSV (`sep=;`, BOM, guard rumus); 4 endpoint `/api/reports/*` & `/api/audit` (hanya-baca, tanpa jalur ubah/hapus). 14 test unit matematika + 12 test dokumen (validasi byte PDF, 3 format jumlah baris identik) + 11 test integrasi API = 151 test hijau, build 0 warning; berkas hasil unduhan diverifikasi terbaca poppler (lihat D-15 soal render visual yang belum dicek) | — |
| 10 Hardening & rilis | selesai — gerbang final dijalankan: `dotnet build Laundry.sln` **0 warning / 0 error** (8 project) dan `dotnet test Laundry.sln` **169 test hijau, 0 gagal, 0 dilewati** (92 unit + 60 integrasi + 17 desktop). Publish diverifikasi dengan menjalankannya: `Laundry.Desktop.exe` one-file (929 KB, `PublishSingleFile`, `--self-contained false`) membuka jendela login, `dist/api` dan `dist/sync` menghasilkan berkas jalan — catatannya, pada mesin yang .NET-nya terpasang level pengguna, `.exe` apphost butuh `DOTNET_ROOT` atau jalankan lewat `dotnet <nama>.dll` (lihat README). Tidak ada installer NSIS/Inno; distribusi memakai folder hasil publish. | — |
