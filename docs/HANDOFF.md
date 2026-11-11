# Catatan serah-terima dua sisi

Kanal komunikasi antara sisi server (`backend/`) dan sisi tampilan (`frontend/`). Formatnya ada di `AGENTS.md` bagian 2. **Aturan utama: entri baru di paling atas, entri lama tidak pernah diedit atau dihapus.** Kalau tidak ada pekerjaan yang tersisa untuk sisi lain, tulis `tidak ada` — jangan menghapus barisnya.

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
