# Pertanyaan Terbuka (tidak boleh ditebak)

Diangkat langsung dari Bagian 13 PRD. Sambil menunggu jawaban, fitur terkait memakai placeholder konfigurasi yang aman dan ditandai `OPEN QUESTION` di kode.

## Identitas usaha & struk
- Q1: Nama usaha, logo, alamat, nomor kontak, format identitas pada struk.

## Tarif & layanan
- Q2: Tarif aktual tiap layanan (kiloan, satuan, express, cuci+setrika, bed cover/selimut, paket, dry cleaning).
- Q3: Minimum berat per layanan.
- Q4: Durasi standar & durasi express; biaya tambahan express.
- Q5: Persentase DP default; batas minimum/maksimum DP.

## Paket langganan
- Q6: Kuota, masa berlaku, layanan yang termasuk, aturan pemakaian per transaksi, pembatalan, carry-over. (Sampai dijawab, modul langganan nonaktif.)

## Pembayaran
- Q7: Provider payment gateway + akun merchant; biaya admin gateway; kebijakan expiry QRIS/transfer.
- Q8: Aturan pajak/diskon/promo dan pembulatan rupiah.
- Q9: Detail provider refund dan target waktu pemrosesan.

## WhatsApp
- Q10: Provider WhatsApp Business API, template yang disetujui, opt-in, budget pesan.

## Antar-jemput
- Q11: Slot, jam operasional, area layanan, kapasitas slot, biaya per kondisi, siapa yang mengantar.
- Q12: Apakah pelanggan boleh mengedit/membatalkan slot setelah pesanan dikonfirmasi.

## Infrastruktur
- Q13: Hosting cloud, domain, TLS, backup cloud, monitoring, estimasi anggaran. Arah sementara: hanya jalur tanpa biaya. Kode sudah siap pindah (D-19: URI database cloud diterima apa adanya, port dari environment, `scripts/migrate.ps1` untuk membangun skema di database kosong — teruji pada PostgreSQL lokal). Yang masih kurang adalah keputusan + akun: nama host API dan connection string database cloud.
- Q14 (Q90 PRD): Konfigurasi — dokumen memakai rekomendasi hybrid lokal/cloud sampai dijawab resmi.

## Kepatuhan data
- Q15: Kebijakan penyimpanan data pribadi dan retensi transaksi sesuai ketentuan berlaku.

## Catatan implementasi saat ini
- Data contoh hanya dibuat kalau `SEED_DEV_CONTOH=1` di environment Development, nama layanannya berlabel `(CONTOH)`, dan tarifnya placeholder; produksi tidak punya nilai tebakan.
- Nama usaha di struk masih kosong sampai Q1 dijawab (`PUT /api/settings/business-name`); sistem tidak menebak.
- Q9 (refund): refund tunai di kasir sudah jalan dan append-only (lihat DECISIONS D-09); refund lewat QRIS/transfer menunggu provider di Q7/Q9.
- Sinkronisasi (D-11) baru diuji antara dua instance PostgreSQL lokal (`CONN_ADMIN` ⇄ `CONN_CLOUD`). Host/DSN cloud produksi belum ada (Q13), jadi jalur cloud→publik belum diverifikasi; yang dikirim hanya pelanggan, pesanan, pembayaran, refund, dan ledger kredit — katalog/tarif dan slot tidak direplikasi (sumber kebenaran tetap admin), dan hash kata sandi tidak pernah ikut menyeberang.
- Provider (D-12): yang jalan sekarang `mock-gateway` dan `mock-whatsapp`. Bentuk payload webhook (`paymentId`, `reference`, `amount`, `status`, `occurredAt`, `failureReason`) dan header tanda tangannya (`X-Mock-Signature: sha256=<hex>`) adalah kontrak lokal kita, bukan format vendor — begitu Q7/Q10 dijawab, adapter asli yang memetakan bentuk vendor tinggal dipasang tanpa mengubah aturan uang di `PaymentService`. Yang masih menunggu: nama provider + akun merchant (Q7), biaya admin gateway & masa berlaku QRIS (Q7), template WhatsApp yang disetujui + opt-in + nomor pengirim (Q10), dan nilai `WEBHOOK_SECRET` asli (saat ini wajib diisi manual di `.env`; kalau kosong/`CHANGE_ME` semua webhook ditolak 401).
- Backup & rilis (D-14): dump, verifikasi, retensi, dan pemulihan sudah jalan antar dua instance PostgreSQL lokal. Yang menunggu jawaban: `UPDATE_CHECK_URL` (manifest versi + installer di-hosting di mana, siapa yang menerbitkan sha256-nya) dan penyalinan arsip backup ke luar mesin (Q13, "backup cloud"). Selama keduanya kosong, `GET /api/app/update-check` menjawab `belum_dikonfigurasi` dan arsip hanya hidup di `BACKUP_DIR` lokal — tidak ada salinan ke tempat kedua dan tidak ada klaim fitur yang belum ada.
- Laporan & ekspor (D-15): yang dilaporkan hanya angka yang benar-benar ada di database, jadi Q2/Q3/Q4/Q8 tetap menentukan besar kecilnya omzet, bukan bentuk laporannya. Yang belum ada dan sengaja tidak ditebak: nama + logo usaha di kepala PDF/XLSX (Q1 — sekarang hanya judul laporan dan periode), aturan apakah diskon/pajak tampil sebagai kolom sendiri di laporan pendapatan (Q8), dan ambang "piutang macet" yang dianggap perlu ditagih (belum ada angkanya, jadi laporan piutang hanya menampilkan sisa tiap pesanan yang masih terbuka tanpa memberi vonis). Grafik deret harian sudah dirender di layar Laporan (bar per hari, otomatis digulir ke hari terbaru), jadi angka yang tampil di bar adalah angka dari `GET /api/reports/laporan` — bukan hitungan kedua di sisi UI.

