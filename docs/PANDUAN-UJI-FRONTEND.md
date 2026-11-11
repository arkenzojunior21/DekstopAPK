# Pemandu uji & demo untuk sisi tampilan

Untuk yang mengerjakan `frontend/`. Isinya: apa saja yang perlu dipasang, dapat API hidup dari mana, cara masuk tanpa meminta kata sandi siapa pun, jalur klik per layar beserta hasil yang benar, peta layar ke berkas, dan daftar hal yang memang belum selesai supaya tidak dilaporkan sebagai bug.

## 1. Yang kamu butuhkan — dan yang tidak

| Perlu | Tidak perlu |
|---|---|
| .NET SDK 8 (level pengguna, tanpa admin) | PostgreSQL sama sekali |
| Repo ini (`git clone`, lalu buka di folder apa pun) | Berkas `.env` |
| Satu alamat API yang bisa dijangkau | Kunci JWT, connection string Neon |

Aplikasi desktop tidak pernah membaca `.env` dan tidak pernah membuka koneksi database; ia hanya bicara HTTP ke `Laundry.Api`. Jadi sisi tampilan tidak punya utang konfigurasi ke sisi server selain satu alamat.

Pasang SDK (satu kali, tidak minta hak administrator):

```powershell
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1
& $env:TEMP\dotnet-install.ps1 -Channel 8.0 -InstallDir $env:LOCALAPPDATA\Microsoft\dotnet
$env:PATH = "$env:LOCALAPPDATA\Microsoft\dotnet;$env:USERPROFILE\.dotnet\tools;$env:PATH"
```

Lalu bangun dan jalankan:

```powershell
cd <folder repo>
taskkill /F /IM Laundry.Desktop.exe /T 2>$null
dotnet build Laundry.sln
dotnet run --project frontend/Laundry.Desktop
```

`dotnet build Laundry.sln` wajib 0 warning. Kalau muncul MSB3026/MSB3027 "file locked", itu aplikasi masih jalan — matikan dulu seperti baris `taskkill` di atas.

## 2. Dapat API hidup dari mana

| Cara | Status sekarang | Syarat |
|---|---|---|
| **A. Alamat publik di Render** | **Belum hidup.** Menunggu empat nilai rahasia diisi di dashboard oleh pemilik proyek. Jangan diperlihatkan ke dosen sebagai "sudah online" sebelum alamatnya benar-benar menjawab. | Cukup ketik alamatnya di kartu **SERVER** jendela login, tekan **Simpan**. |
| **B. API di laptop pengembangan ini, lewat WiFi** | Bisa, tapi perlu satu langkah yang minta hak administrator: aplikasi di mode development diikat ke `localhost` saja (lihat `launchSettings.json`), jadi komputer lain tidak bisa masuk sampai port 5104 dibuka di firewall. | Untuk demo berdua di satu ruangan: pemilik proyek menjalankan API-nya, teman satu WiFi mengetik `http://<alamat-ip-laptop>:5104`. |
| **C. Satu laptop, dua jendela** | Paling praktis dan tidak butuh apa pun. Aplikasi sudah berjalan di mesin ini; cukup buka `dotnet run --project frontend/Laundry.Desktop` dan API-nya sudah hidup di `http://localhost:5104`. | Tidak ada. |

Cara mengganti alamat tanpa membangun ulang: kartu **SERVER** di jendela login bisa disunting, tombol **Simpan** langsung menguji hasilnya dan alamat itu tersimpan di `%LOCALAPPDATA%\LaundryDesk\preferensi.json`.

## 3. Masuk aplikasi tanpa meminta sandi siapa pun

- **Admin.** Di database development hanya ada satu akun Admin. Jangan meminta kata sandinya: tekan **Lupa sandi** di jendela login, masukkan nomor akun itu, kode OTP-nya langsung tampil di layar (di environment Development server memang mengembalikan kodenya, tidak ada pesan yang benar-benar terkirim), lalu tentukan sandi baru sendiri. Ini sekaligus memperbaiki batas aman dari sandi development yang sempat terlihat.
- **Pelanggan.** Buat sendiri lewat tab **Daftar** di jendela login — alurnya sama, OTP tampil di layar, dan akun baru langsung masuk sebagai Pelanggan. Di development ada 366 akun pelanggan sisa pengujian, tapi sandinya tidak diketahui siapa pun, jadi jangan dipakai untuk demo.
- **Batas yang harus diketahui sebelum demo ke alamat publik.** Kode OTP di layar hanya ada selama API berjalan dengan `ENVIRONMENT=Development`. Di URL publik (Render menyetel `ENVIRONMENT=Production`) `devCode` sengaja `null` dan satu-satunya tempat kode itu adalah log host — tidak bisa dibaca dari aplikasi. Jadi jalur **Lupa sandi**/**Daftar** di atas berlaku untuk API lokal; kalau demonya lewat alamat publik, yang menyiapkan akunnya pemilik proyek lebih dulu.
- Setelah login, menu yang muncul ditentukan peran dari server, bukan pilihan pengguna.

## 4. Jalur demo per layar (klik berurutan, ini yang harus terlihat benar)

Sisi Admin:

| Layar | Klik | Hasil yang benar |
|---|---|---|
| Beranda | buka | angka hari ini muncul; tidak ada `0` untuk sesuatu yang datanya jelas ada |
| Buat Pesanan | isi pelanggan + layanan + jumlah → simpan | nomor pesanan muncul, struk bisa dibuka, status awal benar |
| Pesanan | filter status / tanggal, buka detail, ubah status | daftar menyaring dengan benar; riwayat status bertambah satu baris; status tidak bisa mundur |
| Katalog | ubah harga layanan | perubahan terlihat di pesanan berikutnya, dan pesanan lama tetap memakai harga saat itu (harga beku) |
| Pelanggan | cari, buka, tambah saldo kredit | mutasi saldo tercatat; saldo tidak bisa jadi negatif tanpa penolakan |
| Slot | tambah/ubah slot antar-jemput | slot yang penuh tidak menawarkan diri di layar Pelanggan |
| Komplain | buka satu, ubah status, balas | status berubah dan balasan tersimpan; ada pesan yang jelas kalau balasan kosong |
| Laporan | pilih rentang tanggal → ekspor PDF/XLSX | berkas terunduh dan isinya cocok dengan angka di layar |
| Audit | saring per pelaku dan aksi | setiap perubahan uang dan status ada jejaknya |
| Sistem | **Sinkron sekarang** + lihat status | tanpa internet: antrean menyebut "menunggu", aplikasi tetap bisa mencatat; dengan internet: antrean kosong dan checkpoint bergerak |

Sisi Pelanggan: **Beranda** (ringkasan), **Pesan** (layanan + antar-jemput/ambil sendiri + biaya antar), **Pesanan saya** (status + tagihan), **Komplain** (kirim + lihat balasan).

Dua jalur yang paling sering rusak dan wajib dicek ulang setiap kali menyentuh tampilan: **uang** (format rupiah konsisten, tidak ada selisih antara layar dan ekspor) dan **luring** (cabut kabel/WiFi, buat pesanan, colok lagi, data naik sendiri).

## 5. Peta layar ke berkas

| Berkas | Isi |
|---|---|
| `frontend/Laundry.Desktop/App.xaml` + `App.xaml.cs` | awal aplikasi, tema yang tersimpan, pemilihan alamat API |
| `Views/JendelaUtama.xaml` | kerangka + bilah samping tempat menu |
| `Views/JendelaLogin.xaml` | login, daftar, lupa sandi, kartu SERVER |
| `Views/*View.xaml` | satu layar per fitur (BerandaAdminView, PesananView, KatalogView, LaporanView, dst.) |
| `ViewModels/*ViewModel.cs` | data dan perintah tiap layar (MVVM, sumbernya di sini, bukan di code-behind) |
| `Themes/Kontrol.xaml`, `Themes/Terang.xaml`, `Themes/Gelap.xaml` | style, warna, kunci resource |
| `Services/KlienApi.cs` | satu-satunya pintu ke HTTP; alamat, token, dan penerjemahan galat ada di sini |
| `Services/Format.cs`, `Konverter.cs`, `Kartu.cs` | format rupiah/tanggal/relatif, konverter WPF, kartu statistik |
| `Services/Preferensi.cs`, `Sesi.cs` | preferensi mesin (alamat + tema) dan sesi di memori |
| `frontend/Laundry.Tests.Desktop/` | 17 uji tampilan yang menjaga kunci tema, konverter, dan keterbacaan kontras |

Kunci resource yang dipakai XAML tapi tidak ada di kamus akan membuat aplikasi meledak saat layar dibuka — `KunciResourceTerkunciTest` menangkap itu, jadi jalankan `dotnet test frontend/Laundry.Tests.Desktop` setiap mengubah `Themes/`.

## 6. Yang memang belum selesai — jangan dilaporkan sebagai cacat

- Provider pesan dan gateway pembayaran masih adapter mock; tidak ada pengiriman asli.
- Kode OTP dan kata sandi sementara tampil di layar/log pada environment Development. Sengaja, untuk kemudahan uji.
- Popup kalender `DatePicker` masih bertema terang ketika aplikasi memakai tema gelap. Perlu restyle menyeluruh kontrol `Calendar` bawaan WPF; ini batas yang diketahui dan tercatat di README.
- Data di database development masih banyak sisa pengujian (di antaranya 147 dari 150 layanan dan 134 dari 217 pelanggan). Angka demo akan terlihat aneh kalau tidak sadar ini.
- Aplikasi belum pernah dipasang di mesin yang benar-benar bersih; hasil publish baru dibuktikan di mesin pengembangan.

## 7. Cara mengirim hasil kerja

Buat cabang sendiri (`tampilan/<nama>`), jangan kerja di `main`; tambahkan satu entri di paling atas `docs/HANDOFF.md` dengan format di `AGENTS.md` bagian 2; `git add` menyebut berkas satu per satu. Commit dan push dikerjakan pemilik proyek, bukan oleh yang mengerjakan. Gerbang yang harus hijau sebelum Entri ditulis: `dotnet build Laundry.sln` 0 warning dan `dotnet test frontend/Laundry.Tests.Desktop` 17/17.

Kalau perubahanmu menyentuh bentuk data yang dikirim `Laundry.Api` (nama field, tipe, endpoint baru), itu mengubah kontrak: tulis di `docs/HANDOFF.md` dan tunggu sisi server mengiyakan sebelum dua-duanya mengubah kode.
