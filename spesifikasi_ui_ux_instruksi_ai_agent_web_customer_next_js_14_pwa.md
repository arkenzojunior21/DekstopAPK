# Spesifikasi UI/UX & Instruksi AI Agent: Laundry Customer Web (Next.js 14 PWA)

Dokumen ini menjadi panduan lengkap dan instruksi siap eksekusi (*Full Prompt* + *Alert/Guardrail*) untuk **AI Coding Agent** dalam membangun dan merapikan aplikasi web pelanggan berbasis **Next.js 14 (App Router, TypeScript)** di `frontend/Laundry.Web/`.

Aplikasi web ini terhubung dengan *backend* C# yang sama dengan aplikasi desktop **Laundry Desk (C# WPF)**, sehingga alur data, autentikasi, isolasi *tenant*, SignalR, dan notifikasi perubahan harus saling sinkron dengan nyaman bagi pelanggan.

---

## Bagian 1: Gaya Visual yang Diambil (Style Only, Tanpa Aksen Korea & Tanpa Meniru Fitur)

Dari referensi `https://chingu-laundry.com/#layanan`, kita hanya mengambil **karakter visual dan gaya komunikasi**, **bukan** fitur pemesanan/reservasi, aturan minimal kg, atau alur WhatsApp mereka:

1. **Bahasa Hangat, Ramah, & *Conversational* (Tanpa Aksen Korea):**
   * Gunakan sapaan dan bahasa Indonesia yang natural, bersahabat, ringkas, dan menenangkan pelanggan (misalnya: *"Cucianmu sedang dirapikan"*, *"Pilih cabang terdekat"*, *"Tagihan siap dibayar"*).
   * **Dilarang menggunakan aksen Korea** (tidak memakai tulisan Hangul, romanisasi Korea, atau sapaan/istilah Korea).
2. **Tampilan Bersih Berbasis *Light Mode* & Kartu Terstruktur:**
   * Gunakan *Light Mode* sebagai tampilan utama agar terasa bersih, terang, dan mudah dibaca di layar *smartphone* maupun saat terkena cahaya luar.
   * Susun katalog layanan dan informasi paket dalam *card* yang rapi dengan *badge* durasi/kecepatan (misalnya *Reguler*, *Cepat / Express*) dan harga yang jelas per satuan (`kg` / `pcs`), lengkap dengan *filter* kategori/kecepatan yang mudah ditekan jempol (*mobile-first*).
3. **Anti-"AI Slop" untuk Pelanggan:**
   * Jangan tampilkan istilah teknis sistem (*JWT*, *payload*, *UTC*, *state machine*, *worker*, *cache* mentah) kepada pelanggan.
   * Jangan biarkan teks harga, status, tombol, atau *badge* terpotong tidak karuan di layar sempit.

---

## Bagian 2: Solusi UX & Arsitektur Teknis Kritis

### 1. Menu Notifikasi di Pojok Kanan Atas (Header Global)
* Pada layout utama `(main)/layout.tsx`, tampilkan ikon **Lonceng Notifikasi** di sisi kanan atas *header* bersama **Tenant Switcher**.
* Tampilkan *badge* angka notifikasi yang belum dibaca (`unreadCount`). Sembunyikan *badge* bila jumlahnya `0`.
* Saat diklik, buka *popover/drawer* notifikasi yang mencatat perubahan penting untuk pelanggan (misalnya: status cucian berubah, pesanan selesai ditimbang/siap bayar, pembayaran terkonfirmasi, atau balasan baru pada *chat* komplain), lengkap dengan waktu relatif, tombol **"Tandai semua dibaca"**, dan navigasi langsung ke halaman detail pesanan atau ruang *chat* komplain saat item diklik.

### 2. Isolasi Multi-Tenant (`X-Tenant-Id` & Cache)
* Saat pelanggan memilih atau mengganti cabang (*tenant*) melalui *Tenant Switcher*, pastikan `tenantId` aktif konsisten pada *header* `X-Tenant-Id`, *query key* TanStack Query (`['services', tenantId]`, `['orders', tenantId]`, dll.), dan *state* keranjang.
* Jika keranjang masih berisi item dari cabang sebelumnya saat pelanggan mengganti *tenant*, minta konfirmasi sebelum mengosongkan keranjang agar tidak terjadi salah kirim pesanan ke cabang lain.

### 3. Alur Timbang & Pembayaran yang Realistis
* Untuk layanan berbasis berat (`kg`) atau penjemputan, beri kejelasan antara **estimasi awal** dan **tagihan resmi setelah penimbangan di toko**.
* Saat kasir di WPF menginput berat aktual dan memperbarui status/tagihan lewat *backend*, *event* SignalR harus memperbarui tampilan *tracking* pesanan pelanggan secara *real-time* (dengan *fallback* polling 30 detik dan *re-sync* saat tab/aplikasi kembali aktif via `visibilitychange`).

### 4. PWA Offline Queue yang Aman & Kompresi Foto Komplain
* Batasi antrean tulis *offline* (IndexedDB + Background Sync) hanya untuk aksi yang aman saat tidak ada koneksi internet (misalnya menyusun/mengantrekan draf pesanan *non-Midtrans* atau pesan/draf yang didukung *backend*), dan beri tahu pelanggan dengan bahasa yang jelas apabila pembayaran digital (*Midtrans*) memerlukan koneksi internet aktif.
* Kompres foto bukti komplain di sisi klien sebelum diunggah ke `/api/upload` (`multipart/form-data`) agar hemat kuota dan tidak membebani memori ponsel.

---

## Bagian 3: Alert / Guardrail Singkat untuk AI Agent

Letakkan blok berikut di bagian paling atas saat memulai sesi kerja *AI agent* pada `frontend/Laundry.Web/`:

> **ALERT STYLE, LANGUAGE & SCOPE (WAJIB DIPATUHI):**
> 1. Tiru **hanya gaya visual** dari `https://chingu-laundry.com/#layanan` (ramah, santai, *mobile-first*, *card* dan *pill* bersih, *badge* kecepatan/durasi yang jelas, dan *micro-copy* hangat).
> 2. **DILARANG MENGGUNAKAN AKSEN KOREA** dalam bentuk apa pun (tidak ada tulisan Hangul, romanisasi Korea, atau istilah Korea).
> 3. **DILARANG MENIRU FITUR** atau alur bisnis Chingu Laundry. Semua fitur, alur, dan kontrak API harus mengikuti spesifikasi Next.js 14 PWA dan *backend* C# yang sudah ada.
> 4. **WAJIB ADA** menu **Lonceng Notifikasi** di pojok kanan atas *header* yang menampilkan *badge* belum dibaca dan mencatat perubahan status/pesanan/pembayaran/komplain secara *real-time*.
> 5. **HINDARI AI SLOP:** jangan tampilkan istilah teknis *backend* ke layar pelanggan, dan pastikan semua *layout* responsif di layar HP tanpa teks terpotong merusak makna.

---

## Bagian 4: Full Prompt / Task untuk AI Agent (Next.js 14 PWA)

Salin seluruh instruksi di bawah ini ke *AI agent* kamu untuk membangun atau merapikan `frontend/Laundry.Web/`:

```markdown
Bangun dan rapikan aplikasi web pelanggan **Next.js 14 (App Router, TypeScript)** di `frontend/Laundry.Web/` dengan mengadopsi **gaya visual** dari `https://chingu-laundry.com/#layanan` (gaya saja, tanpa fitur/alur bisnisnya dan **tanpa aksen Korea sama sekali**), sambil mengintegrasikan seluruh fitur dan kontrak API backend C# yang sudah ada.

### 1. Gaya Visual & Anti-AI-Slop (Referensi Style: chingu-laundry.com/#layanan)
- **Tema & Nuansa:** Gunakan *Light Mode* sebagai tema default yang bersih, terang, dan ramah (*mobile-first*), dengan tetap menyediakan opsi *Dark Mode* (`Tailwind dark: class`, tersimpan di `localStorage`) pada halaman Profil/Pengaturan.
- **Tanpa Aksen Korea:** Jangan memakai karakter Hangul, romanisasi bahasa Korea, atau nuansa Korea apa pun. Gunakan bahasa Indonesia yang ramah, jelas, dan bersahabat.
- **Komponen & Hierarki:** Gunakan *card* bersih dengan *border* halus, *rounded corner* konsisten, *pill/chip* interaktif untuk filter kategori & kecepatan layanan, serta *badge* informasi harga/estimasi waktu yang mudah dibaca.
- **Anti-AI-Slop:** Jangan tampilkan istilah teknis sistem (*JWT*, *UTC*, *payload*, *state machine*, *worker*) kepada pelanggan. Gunakan *skeleton loading*, *error boundary*, dan *toast* (`sonner`) yang ramah pengguna. Pastikan semua teks, *badge*, dan tombol rapi tanpa *overflow* atau pemotongan teks yang mengganggu di layar mobile.

### 2. Header Global + Menu Notifikasi di Pojok Kanan Atas
- Pada `(main)/layout.tsx`, bangun *header* mobile-first dengan:
  - **Tenant Switcher** di sisi kiri/tengah (menampilkan nama cabang aktif, jarak/logo, dan menu ganti cabang yang aman agar *cache* TanStack Query dan keranjang tidak tertukar antar *tenant*).
  - **Tombol Lonceng Notifikasi** di sisi kanan atas dengan *badge* jumlah notifikasi belum dibaca (sembunyikan *badge* jika `0`).
- Klik tombol lonceng membuka *popover/drawer* notifikasi yang mencatat perubahan penting untuk pelanggan (perubahan status cucian, pembaruan berat/tagihan siap bayar, status pembayaran, atau balasan komplain), lengkap dengan waktu relatif, tombol **"Tandai semua dibaca"**, dan klik item langsung mengarahkan ke halaman terkait (`/pesanan/[id]` atau `/komplain/chat/[id]`).
- Hubungkan daftar notifikasi dengan *event* SignalR serta perubahan data pesanan/pembayaran/komplain agar otomatis bertambah dan memperbarui *badge* secara *real-time*.

### 3. Fitur Utama Customer Journey
1. **Auth & Onboarding:**
   - Login (phone + OTP via `/api/auth/send-otp` & `/api/auth/verify-otp`) dan Register (nama, phone, alamat).
   - **Tenant Selection** (`/select-tenant`): ambil lokasi GPS browser, hitung jarak Haversine ke *tenant* aktif yang memiliki koordinat, urutkan dari terdekat, tampilkan *card* (nama, alamat, jarak, rating, jam buka, logo), dan sediakan *fallback* daftar/pencarian manual jika izin GPS ditolak. Simpan `tenantId` secara konsisten untuk *header* `X-Tenant-Id` dan isolasi *query key*.
2. **Katalog & Keranjang:**
   - Tampilkan layanan dari *tenant* terpilih (`/api/tenants/{id}/services`) dengan nama, satuan (`kg`/`pcs`), harga, estimasi durasi, dan *express surcharge*.
   - *Toggle* Express dengan *update* harga *real-time*, pilihan *pickup/delivery* + slot/alamat, dan rincian estimasi total sebelum *checkout*.
3. **Buat Pesanan & Pembayaran:**
   - `POST /api/orders` -> kembalikan `orderNumber`, `estimatedFinishUtc`, dan `paymentUrl` (jika bayar via Midtrans).
   - Dukung metode pembayaran Cash, Midtrans (VA/QRIS/E-Wallet), atau Kredit saldo (jika tersedia), serta tampilkan struk digital setelah pembayaran. Tangani secara jelas alur layanan kiloan yang nominal resminya mengikuti hasil penimbangan toko.
4. **Tracking Pesanan Real-time:**
   - Detail pesanan (`/pesanan/[id]`): *status badge*, *timeline* vertikal dengan *timestamp* dan petugas, *update* *real-time* lewat SignalR (dengan *fallback* polling 30s dan *re-sync* saat `visibilitychange`), serta tombol **"Komplain"**.
5. **Komplain (Chat):**
   - *Thread* *chat* per pesanan (`/komplain/chat/[id]`) antara pelanggan dan admin toko secara *real-time* lewat SignalR, status komplain, dan kirim foto bukti (kompres gambar sebelum *upload* via `/api/upload` dengan `multipart/form-data`).
6. **Profil & Riwayat:**
   - Riwayat pesanan (filter status dan rentang tanggal), saldo kredit + *top-up* via Midtrans, CRUD alamat tersimpan (`/api/customers/me`), serta *logout* atau ganti *tenant*.

### 4. PWA & Integrasi Backend C#
- Konfigurasikan *manifest*, *service worker*, *custom install prompt banner* (`beforeinstallprompt`), *offline read cache* (katalog, riwayat pesanan, profil), *offline write queue* di IndexedDB dengan Background Sync khusus aksi yang aman saat *offline*, serta *web push notification* (FCM VAPID).
- Gunakan Next.js 14 App Router + TypeScript, Tailwind CSS + `shadcn/ui`, TanStack Query, Zustand, React Hook Form + Zod, dan tipe DTO yang selaras dengan *backend* C#.
- Sertakan ringkasan file yang dibuat/diubah, cara kerja lonceng notifikasi di kanan atas, dan penyesuaian *style* yang diterapkan.
```

---

## Bagian 5: Urutan Eksekusi Task yang Disarankan untuk AI Agent

Agar hasil kode lebih rapi dan mudah diuji langkah demi langkah, kamu bisa menjalankan *prompt* di atas secara bertahap:

1. **Task 1 — Theme, Header & Lonceng Notifikasi Kanan Atas:** Siapkan *Light Mode* default + *dark mode toggle*, *style* *card/pill* yang ramah tanpa aksen Korea, *Tenant Switcher*, *Bottom Nav* (`Beranda | Pesanan | Komplain | Profil`), dan komponen lonceng notifikasi + *badge* + *popover/drawer* di kanan atas.
2. **Task 2 — Auth, Tenant Selection (GPS + Fallback) & Katalog/Keranjang:** Bangun alur *login/register*, pemilihan cabang dengan Haversine dan *fallback* manual, isolasi `tenantId` pada *query key* & *cart*, serta katalog + *toggle* Express + *checkout* (`POST /api/orders` & pembayaran).
3. **Task 3 — Real-Time Tracking, Chat Komplain & PWA:** Sambungkan SignalR (`visibilitychange` re-sync + polling *fallback*), *chat* komplain dengan *upload* foto terkompresi, halaman profil/riwayat/alamat/saldo, serta konfigurasi *manifest*, *service worker*, *offline queue*, dan *push notification*.