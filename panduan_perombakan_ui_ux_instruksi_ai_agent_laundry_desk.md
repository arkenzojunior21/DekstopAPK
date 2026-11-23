# Spesifikasi Audit & Perombakan UI/UX: Laundry Desk (C# WPF)

Dokumen ini berisi analisis lengkap mengenai masalah antarmuka (*UI/UX*) pada aplikasi desktop **Laundry Desk**, alasan teknis mengapa tampilan saat ini sulit digunakan oleh kasir maupun pemilik usaha, serta **instruksi terstruktur (*Full Prompt*)** yang siap diberikan kepada *AI Coding Agent* untuk melakukan perombakan menyeluruh.

---

## Bagian 1: Analisis Masalah & Kenapa Tampilan Saat Ini Bermasalah

### 1. Indikasi "AI Slop" (Desain Generatif Mentah)
Antarmuka saat ini terlihat seperti hasil *generate* AI mentah yang hanya menerjemahkan skema tabel *database* langsung menjadi kotak-kotak form tanpa mempertimbangkan psikologi dan alur kerja pengguna di dunia nyata:

* **Kebocoran Istilah Teknis ke Layar Kasir (*System Leakage*):**
  * Muncul status seperti `"pekerja sinkronisasi tidak berjalan"` di pojok kanan atas.
  * Muncul penjelasan sistem seperti `"Hanya langkah yang diizinkan mesin status yang muncul. Kalau server menolak, alasannya ditampilkan apa adanya"` dan `"Kosong berarti memang tidak ada perubahan, bukan gagal memuat"`.
  * **Kenapa ini salah:** Kasir laundry tidak butuh memahami arsitektur *state machine*, *background sync worker*, atau *database query*. Teks teknis semacam ini justru membuat kasir panik karena mengira aplikasi sedang *error*.
* **Hierarki Visual Datar (*Flat Visual Hierarchy*):**
  * Hampir seluruh komponen menggunakan kartu (*card*) dengan warna latar gelap yang seragam, *border* tipis yang sama, dan ukuran teks yang mirip.
  * **Kenapa ini salah:** Mata pengguna tidak dipandu ke mana harus melihat lebih dulu. Tombol aksi utama (*Primary Call-to-Action*), informasi pasif, dan kolom input menyatu tanpa kontras yang jelas.
* **Kebocoran Data *Dummy* / *Testing*:**
  * Terlihat label seperti `"Ditimbang Dan..."`, `"Kilo Laporan..."`, dan kode transaksi mentah `LR-20261018152618-9316`.
  * **Kenapa ini salah:** Data mentah dari *database* langsung di-*binding* ke XAML tanpa *value converter* atau pemformatan string yang ramah manusia.

---

### 2. Masalah Kegunaan (*Usability*) bagi Kasir & Pemilik Usaha

* **Alur Transaksi ("Buat Pesanan") Terbalik Secara Kognitif:**
  * **Kondisi saat ini:** Kasir diminta memilih layanan di kiri atas, mengisi data pelanggan walk-in di kanan atas, dan menekan tombol simpan di pojok kanan bawah. Terdapat pula istilah membingungkan seperti `"Perkiraan sendiri (belum ongkos)"`.
  * **Dampak:** Saat pelanggan antre, kasir harus menggerakkan kursor dan mata secara zig-zag. Alur kasir alami harus linear: **1. Identitas Pelanggan $\rightarrow$ 2. Pilih Layanan & Timbang Berat $\rightarrow$ 3. Ringkasan Biaya & Pembayaran**.
* **Pemotongan Teks (*Text Truncation*) yang Menghilangkan Informasi:**
  * Pada tabel **Pesanan** dan **Katalog**, teks terpotong menjadi `"Ditimbang Dan..."`, `"Kilo"`, `"Cuc"`, `"Laya"`, dan label persetujuan di kanan bawah terpotong menjadi `"Pelanggan menyetujui pembaya..."`.
  * **Penyebab teknis WPF:** Penggunaan `Grid` dengan `ColumnDefinition` berlebar tetap (`Width="60"`) tanpa `Star Sizing` (`Width="*"`) atau pembungkusan teks (`TextWrapping="Wrap"`).
* **Fragmentasi Menu Sidebar yang Terlalu Banyak:**
  * Terdapat 9 menu sejajar di sidebar (`Beranda`, `Pesanan`, `Buat Pesanan`, `Katalog`, `Pelanggan`, `Slot`, `Komplain`, `Laporan`, `Audit`).
  * **Dampak:** Beban kognitif tinggi. Fitur administratif/sistem seperti `Audit` seharusnya tidak sejajar dengan tombol operasional kasir harian.
* **Kontras Rendah (*Dark Mode* di Ruang Terang):**
  * Meja kasir laundry umumnya berada di ruangan terang dengan lampu neon atau cahaya matahari. Penggunaan *dark mode* pekat dengan teks abu-abu redup (`#94A3B8`) menurunkan keterbacaan angka nominal uang dan mempercepat kelelahan mata kasir.
* **Grafik Laporan Kaku (Kotak/Batang Datar):**
  * Pada halaman **Laporan**, grafik `"OMZET HARIAN"` berbentuk deretan garis kotak tipis di bagian bawah yang sulit dibaca tren kenaikan atau penurunannya oleh pemilik usaha.

---

## Bagian 2: Arsitektur Solusi

1. **Kasir-First Light Mode & Visual Hierarchy:**
   * Menggunakan tema terang bersih (*Light Mode*) sebagai *default* dengan kontras tinggi untuk keterbacaan di meja kasir, sambil tetap mempertahankan opsi *toggle* tema.
   * Membedakan secara tegas antara tombol aksi utama (*Solid Primary*), tombol sekunder (*Outline*), dan kartu informasi.
2. **Global Top Bar + Menu Notifikasi Real-Time di Pojok Kanan Atas:**
   * Menambahkan ikon lonceng notifikasi dengan *badge* jumlah notifikasi belum dibaca di pojok kanan atas pada seluruh layar utama.
   * Setiap perubahan data penting (pesanan baru dibuat, perubahan status cucian, pembayaran/DP/refund, perubahan katalog/tarif, penambahan pelanggan) otomatis tercatat ke dalam `NotificationService` dan muncul di *popup drawer* notifikasi.
3. **Alur Kasir 3-Langkah Linear (`Buat Pesanan`):**
   * Membagi layar transaksi menjadi alur kiri-ke-kanan yang logis tanpa teks membingungkan.
4. **Grafik Laporan Bergaya *Trading* (*Trading-Style Line & Area Chart*):**
   * Mengganti grafik kotak/batang dengan `LineSeries` menggunakan **LiveCharts2** (`LiveChartsCore.SkiaSharpView.WPF`) lengkap dengan efek gradasi area di bawah garis (*LinearGradientPaint*), *grid* horizontal minimalis, dan *interactive tooltip/crosshair* saat kursor diarahkan.

---

## Bagian 3: Full Instruksi (Prompt Siap Salin untuk AI Agent)

Salin seluruh teks di dalam blok kode di bawah ini dan berikan langsung kepada *AI coding agent* kamu:

```markdown
# TASK: Refactor UI/UX Aplikasi Desktop C# WPF "Laundry Desk"

Lakukan perombakan menyeluruh pada UI/UX aplikasi desktop C# WPF "Laundry Desk" agar lebih mudah dipahami oleh kasir dan pemilik usaha, menghilangkan kesan "AI slop" (teks teknis berlebih, hierarki datar, teks terpotong), menambahkan menu notifikasi perubahan di kanan atas, serta mengubah grafik laporan menjadi grafik garis interaktif bergaya trading online.

Terapkan seluruh spesifikasi berikut secara konsisten menggunakan pola MVVM (Model-View-ViewModel):

---

### 1. Sistem Tema & Kebersihan Antarmuka (Anti-AI Slop)
- **Default Light Mode (Kasir-Friendly):**
  - Gunakan Light Mode sebagai tema default agar nyaman dibaca di ruang kasir terang:
    - Window Background: `#F8FAFC`
    - Card / Surface Background: `#FFFFFF` dengan border `#E2E8F0` (`BorderThickness="1"`) dan shadow halus
    - Primary Text: `#0F172A`
    - Secondary Text: `#475569`
    - Primary Accent (Tombol Utama): `#2563EB` (Hover: `#1D4ED8`)
    - Status Sukses / Lunas: `#16A34A` (Background badge: `#DCFCE7`)
    - Status Proses / Peringatan: `#D97706` (Background badge: `#FEF3C7`)
    - Status Bahaya / Belum Lunas / Refund: `#DC2626` (Background badge: `#FEE2E2`)
  - Pertahankan fitur toggle tema (Terang / Gelap) di bagian bawah sidebar menggunakan `ResourceDictionary` dinamis.
- **Hapus Teks Teknis & Robotik:**
  - Hapus semua penjelasan sistem/developer dari layar operasional (contoh: hapus `"pekerja sinkronisasi tidak berjalan"`, `"Hanya langkah yang diizinkan mesin status yang muncul..."`, `"Kosong berarti memang tidak ada perubahan, bukan gagal memuat"`, `"Angka nol di sini hasil perhitungan, bukan data contoh"`).
  - Ganti indikator koneksi di top bar menjadi pill ringkas: titik hijau + `"Online"` atau titik kuning + `"Offline"`.
- **Perbaiki Text Truncation & Layout Responsif:**
  - Audit semua `DataGrid`, `ListView`, dan `Grid` di XAML. Pastikan tidak ada label, nama layanan, status pesanan, atau teks checkbox/tombol yang terpotong dengan `...`.
  - Gunakan `ColumnDefinition` dengan proporsi `Width="*"` dan `MinWidth` yang tepat, serta gunakan `TextWrapping="Wrap"` pada teks panjang.

---

### 2. Global Top Bar & Menu Notifikasi di Pojok Kanan Atas
- Buat komponen **Global Top Bar** yang tampil konsisten di bagian atas area konten utama setelah user login:
  - **Sisi Kiri:** Judul halaman aktif dan subjudul singkat yang jelas.
  - **Sisi Kanan:**
    1. Indikator status koneksi (`Online` / `Offline`).
    2. **Tombol Menu Notifikasi (Ikon Lonceng + Badge):**
       - Tampilkan ikon lonceng dengan badge merah bulat di sudut kanan atas ikon yang menampilkan jumlah notifikasi belum dibaca (`UnreadCount`). Sembunyikan badge jika `UnreadCount == 0`.
       - Saat tombol diklik, buka `Popup` / panel dropdown di bawah ikon lonceng (lebar ~380px, tinggi maksimal ~460px, scrollable).
- **Arsitektur NotificationService (MVVM):**
  - Buat `INotificationService` dan `NotificationService` (Singleton / DI) yang menyimpan `ObservableCollection<NotificationItem>` dengan properti: `Id`, `Title`, `Description`, `Category` (Pesanan, Pembayaran, Status, Katalog, Pelanggan, Sistem), `Timestamp`, `IsRead`, dan `TargetRoute` / `ReferenceId`.
  - Sediakan tombol **"Tandai semua dibaca"** dan **"Hapus semua"** di header popup notifikasi.
  - Hubungkan event pada ViewModel/Service transaksi, pesanan, pelanggan, dan katalog agar **setiap perubahan data/status otomatis memicu penambahan notifikasi baru** (misal: *"Pesanan Baru #LR-9316 dibuat"*, *"Status #LR-9316 berubah menjadi Selesai"*, *"Pembayaran Rp20.000 diterima"*, *"Tarif layanan Cuci Kiloan diperbarui"*).

---

### 3. Penyederhanaan Navigasi Sidebar & Alur Kasir ("Buat Pesanan")
- **Struktur Sidebar:**
  - Kelompokkan menu menjadi dua bagian agar tidak membingungkan kasir:
    - **OPERASIONAL:** `Beranda`, `Buat Pesanan` (beri highlight khusus sebagai aksi utama kasir), `Daftar Pesanan`, `Pelanggan`, `Jadwal Antar-Jemput`.
    - **MANAJEMEN:** `Layanan & Tarif`, `Komplain`, `Laporan`, `Riwayat Audit`.
- **Perombakan Alur "Buat Pesanan":**
  - Susun tata letak 3 kolom/tahap yang mengalir alami dari kiri ke kanan:
    - **Tahap 1 — Pelanggan:** Cari pelanggan lama (nama/no. HP) atau input pelanggan baru di satu kartu terpadu.
    - **Tahap 2 — Layanan & Berat/Qty:** Pilih layanan, input berat (kg) atau jumlah item, pilih opsi Express / Antar-Jemput, dan tampilkan tabel keranjang item yang jelas.
    - **Tahap 3 — Ringkasan & Pembayaran:** Tampilkan rincian subtotal, ongkos antar-jemput, total akhir dalam font besar dan tegas, pilihan metode bayar (Tunai / QRIS / Bayar Nanti), serta tombol utama penuh `"Simpan & Cetak Struk"`.

---

### 4. Grafik Laporan Bergaya Trading Online (Line Chart + Area Gradient)
- Pada halaman **Laporan**, ganti grafik kotak/batang datar menjadi **grafik garis interaktif bergaya trading / fintech** menggunakan library **LiveCharts2** (`LiveChartsCore.SkiaSharpView.WPF`). Jika project sudah memiliki library chart lain, migrasikan ke LiveCharts2 atau gunakan konfigurasi setara.
- **Spesifikasi Visual Grafik:**
  - Gunakan `LineSeries<double>` dengan `LineSmoothness = 0.25` (garis tegas namun tetap mulus).
  - Warna garis utama (`Stroke`): Hijau zamrud `#10B981` atau Biru `#2563EB` dengan ketebalan `3`.
  - Area di bawah garis (`Fill`): `LinearGradientPaint` dari warna garis utama (alpha `80`) memudar ke transparan (alpha `0`) secara vertikal dari atas ke bawah.
  - Titik data (`GeometrySize = 6`) dengan fill putih/gelap sesuai tema dan border warna garis utama.
  - **Sumbu X & Y:**
    - Sumbu X menampilkan tanggal terformat rapi (`dd MMM`, misal `25 Okt`) dengan `SeparatorsPaint = null` (hilangkan garis kotak-kotak vertikal).
    - Sumbu Y menampilkan format mata uang ringkas (`Rp 150rb` / `Rp 1,5jt`) dengan garis grid horizontal tipis (`StrokeThickness = 1`, warna `#E2E8F0` pada Light Mode).
  - **Interaktivitas:** Aktifkan tooltip/crosshair saat mouse diarahkan ke grafik sehingga pengguna bisa melihat tanggal pasti dan nominal omzet/penerimaan harian secara presisi, serta dukungan `ZoomMode="X"`.

---

### 5. Output yang Diharapkan
1. Implementasikan perubahan kode pada file XAML, ViewModel, Model, dan Service terkait tanpa merusak logika bisnis dan koneksi database yang sudah ada.
2. Pastikan project dapat di-build tanpa error (`dotnet build`).
3. Berikan ringkasan file apa saja yang dibuat/diubah beserta penjelasan singkat alur `NotificationService` dan binding data grafik laporan.
```

---

## Bagian 4: Panduan Eksekusi dengan AI Agent

1. **Jalankan secara bertahap jika proyek besar:** Jika *AI agent* kamu memiliki batas konteks per *turn*, minta ia mengerjakan secara berurutan:
   * **Langkah 1:** *Theme ResourceDictionary*, *Global Top Bar*, dan sistem *NotificationService* + *Popup* lonceng di kanan atas.
   * **Langkah 2:** Perombakan halaman *Buat Pesanan* (Kasir) dan perbaikan kolom tabel pada *Daftar Pesanan* & *Katalog*.
   * **Langkah 3:** Implementasi grafik garis bergaya *trading* pada halaman *Laporan*.
2. **Verifikasi Hasil:** Setelah kode selesai dibuat, pastikan kamu menguji alur menambah pesanan atau mengubah status cucian untuk melihat apakah *badge* lonceng di pojok kanan atas langsung bertambah secara *real-time*.