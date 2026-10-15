# LaundryFlow Desk

Aplikasi kasir & manajemen laundry berbasis desktop. Dibuat untuk proyek Rekayasa Perangkat Lunak (RPL).

Aplikasi berjalan sepenuhnya lokal — tidak butuh server, internet, maupun instalasi database terpisah. Data disimpan dalam satu file SQLite di folder pengguna (`%APPDATA%/laundryflow-desk/laundryflow.db` pada Windows), sehingga backup cukup dengan menyalin file tersebut.

## Fitur

- **Master data pelanggan** — CRUD lengkap (nama, no. HP, alamat) dengan pencarian.
- **Master data layanan** — CRUD layanan per satuan (kg / pasang / potong) beserta harganya; harga awal langsung tersedia 4 layanan contoh saat database pertama dibuat.
- **Transaksi penerimaan cucian** — pilih pelanggan, tambah baris layanan dinamis, total dan sisa bayar terhitung otomatis, DP disimpan bersama tanggal masuk & estimasi selesai. Rincian transaksi menyimpan *snapshot* nama & harga layanan, sehingga riwayat tidak berubah walau harga layanan diedit kemudian.
- **Alur status cucian** — Proses → Selesai → Diambil, dengan penjagaan transisi (lompat status ditolak).
- **Notifikasi WhatsApp** — saat status diubah ke "Selesai", aplikasi membuka WhatsApp dengan pesan siap kirim (nama, nomor transaksi, total, dan sisa bayar sudah terisi otomatis). Tidak memakai API berbayar, hanya tautan `wa.me`.
- **Ringkasan kerja** — strip statistik di halaman Transaksi: jumlah cucian masih diproses, siap diambil, sudah diambil, dan total sisa bayar aktif.
- **Filter & detail transaksi** — riwayat bisa disaring per status, dan tiap transaksi punya jendela detail berisi rincian layanan (dengan harga saat transaksi) plus total/DP/sisa.
- **Uji cepat & data demo** — tersedia skrip smoke test dan seed data demo (lihat Cara Menjalankan).

## Teknologi

| Bagian | Teknologi |
|---|---|
| Desktop shell | Electron.js (HTML, CSS, JavaScript) |
| Database | SQLite melalui `better-sqlite3` |
| Komunikasi UI ↔ database | IPC Electron (`ipcMain.handle` / `ipcRenderer.invoke` lewat `contextBridge`, `contextIsolation` aktif) |

Halaman UI (renderer) tidak pernah menyentuh database langsung — semua baca/tulis data lewat jembatan preload ke proses utama.

## Cara Menjalankan

Prasyarat: [Node.js](https://nodejs.org) versi LTS terpasang.

```bash
# 1. Install dependency
npm install

# 2. Rebuild modul native better-sqlite3 agar cocok dengan Node milik Electron
#    (wajib sekali setelah install; kalau dilewati aplikasi crash dengan
#     error "compiled against a different Node.js version")
npm install -D @electron/rebuild
npx @electron/rebuild -f -w better-sqlite3

# 3. Jalankan aplikasi
npm start
```

Perintah tambahan:

```bash
npm run smoke      # uji cepat semua lapisan data (CRUD, transaksi, status), hasil dicetak di terminal
npm run seed-demo  # isi data contoh: 2 pelanggan + 2 transaksi (dilewati jika sudah ada transaksi)
npm run dist       # build aplikasi jadi file .exe siap sebar (hasil di folder dist/)
```

Hasil `npm run dist`:
- `LaundryFlow Desk Setup x.x.x.exe` — installer biasa (ada shortcut di Start Menu).
- `LaundryFlow Desk x.x.x.exe` — versi portable, langsung klik dua kali tanpa install.

Catatan: data tersimpan per pengguna, jadi installer dan versi portable memakai database terpisah (`%APPDATA%\LaundryFlow Desk\` untuk versi ter-bundle, `%APPDATA%\laundryflow-desk\` saat jalan dari source).

## Struktur Proyek

```
build/
└─ icon.png            ikon aplikasi (dipakai electron-builder)
src/
├─ main/               proses utama (Electron main process)
│  ├─ main.js           pembuatan window, lifecycle aplikasi, skrip smoke/seed
│  ├─ ipc.js            pendaftaran seluruh channel ipcMain.handle
│  ├─ wa.js             pembuatan tautan wa.me + normalisasi nomor HP (08xx → 628xx)
│  └─ db/
│     ├─ koneksi.js     buka database, CREATE TABLE, seed layanan awal
│     ├─ pelanggan.js   query + validasi CRUD pelanggan
│     ├─ layanan.js     query + validasi CRUD layanan
│     └─ transaksi.js   query + validasi transaksi, detail, dan transisi status
├─ preload/
│  └─ preload.js        contextBridge — satu-satunya jembatan UI ke proses utama
└─ renderer/
   ├─ index.html        satu window, tiga halaman (Pelanggan / Layanan / Transaksi)
   ├─ css/style.css
   └─ js/
      ├─ app.js         navigasi antar halaman
      ├─ util.js        escape HTML, format rupiah, pembersih pesan error
      ├─ pelanggan.js
      ├─ layanan.js
      └─ transaksi.js
```

## Catatan

- Nomor HP pelanggan divalidasi 8–17 karakter angka; untuk notifikasi WA nomor otomatis dikonversi ke format 62.
- Penghapusan pelanggan/layanan yang sudah terpakai transaksi ditolak oleh foreign key — pesannya sudah diterjemahkan ke bahasa yang mudah dibaca di UI.
- Semua tampilan uang diformat rupiah, dan setiap input nilai (harga, DP, jumlah) divalidasi sebelum masuk database.
