const db = require('./koneksi');

const TGL = /^\d{4}-\d{2}-\d{2}$/;

function validasi(data) {
  const idPelanggan = Number(data.id_pelanggan);
  if (!Number.isInteger(idPelanggan) || idPelanggan <= 0) {
    throw new Error('Pelanggan belum dipilih.');
  }
  if (!TGL.test(data.tanggal_masuk || '') || !TGL.test(data.tanggal_estimasi || '')) {
    throw new Error('Tanggal masuk dan estimasi wajib diisi.');
  }
  if (data.tanggal_estimasi < data.tanggal_masuk) {
    throw new Error('Tanggal estimasi tidak boleh sebelum tanggal masuk.');
  }
  if (!Array.isArray(data.items) || data.items.length === 0) {
    throw new Error('Minimal satu layanan harus dipilih.');
  }
  for (const it of data.items) {
    const idLayanan = Number(it.id_layanan);
    const jumlah = Number(it.jumlah);
    if (!Number.isInteger(idLayanan) || idLayanan <= 0) {
      throw new Error('Ada baris layanan yang belum dipilih.');
    }
    if (!(jumlah > 0)) {
      throw new Error('Jumlah setiap layanan harus lebih dari 0.');
    }
  }
  const dp = Number(data.dp ?? 0);
  if (!Number.isFinite(dp) || dp < 0) {
    throw new Error('DP tidak valid.');
  }
  return {
    id_pelanggan: idPelanggan,
    tanggal_masuk: data.tanggal_masuk,
    tanggal_estimasi: data.tanggal_estimasi,
    dp,
    items: data.items.map((it) => ({ id_layanan: Number(it.id_layanan), jumlah: Number(it.jumlah) })),
  };
}

const simpan = db.transaction((data) => {
  const pelanggan = db
    .prepare('SELECT id_pelanggan FROM pelanggan WHERE id_pelanggan = ?')
    .get(data.id_pelanggan);
  if (!pelanggan) throw new Error('Pelanggan tidak ditemukan.');

  const ambilLayanan = db.prepare('SELECT * FROM layanan WHERE id_layanan = ?');
  const items = data.items.map((it) => {
    const l = ambilLayanan.get(it.id_layanan);
    if (!l) throw new Error(`Layanan #${it.id_layanan} tidak ditemukan.`);
    const subtotal = Math.round(l.harga_persatuan * it.jumlah);
    return {
      id_layanan: l.id_layanan,
      nama: l.nama_layanan,
      harga: l.harga_persatuan,
      jumlah: it.jumlah,
      subtotal,
    };
  });
  const total = items.reduce((s, i) => s + i.subtotal, 0);
  if (data.dp > total) throw new Error('DP tidak boleh melebihi total bayar.');

  const info = db
    .prepare(
      `INSERT INTO transaksi
        (id_pelanggan, tanggal_masuk, tanggal_estimasi, total_bayar, dp, sisa_bayar, status)
       VALUES (@id_pelanggan, @tanggal_masuk, @tanggal_estimasi, @total, @dp, @sisa, 'Proses')`
    )
    .run({ ...data, total, sisa: total - data.dp });
  const id_transaksi = Number(info.lastInsertRowid);

  const detail = db.prepare(
    `INSERT INTO detail_transaksi
      (id_transaksi, id_layanan, jumlah, subtotal, nama_layanan_snapshot, harga_snapshot)
     VALUES (?, ?, ?, ?, ?, ?)`
  );
  for (const i of items) {
    detail.run(id_transaksi, i.id_layanan, i.jumlah, i.subtotal, i.nama, i.harga);
  }
  return { id_transaksi, total };
});

const TRANSISI = {
  Proses: ['Selesai'],
  Selesai: ['Diambil'],
  Diambil: [],
};

module.exports = {
  buat: (data) => simpan(validasi(data)),

  getAll: () =>
    db
      .prepare(
        `SELECT t.*, p.nama AS nama_pelanggan, p.no_hp
         FROM transaksi t JOIN pelanggan p ON p.id_pelanggan = t.id_pelanggan
         ORDER BY t.id_transaksi DESC`
      )
      .all(),

  getById: (id) => {
    const header = db
      .prepare(
        `SELECT t.*, p.nama AS nama_pelanggan, p.no_hp
         FROM transaksi t JOIN pelanggan p ON p.id_pelanggan = t.id_pelanggan
         WHERE t.id_transaksi = ?`
      )
      .get(id);
    if (!header) return null;
    header.items = db
      .prepare(
        `SELECT d.*, l.satuan
         FROM detail_transaksi d LEFT JOIN layanan l ON l.id_layanan = d.id_layanan
         WHERE d.id_transaksi = ?
         ORDER BY d.id_detail`
      )
      .all(id);
    return header;
  },

  ubahStatus: (id, statusBaru) => {
    if (!TRANSISI[statusBaru]) {
      throw new Error(`Status "${statusBaru}" tidak dikenal.`);
    }
    const t = db
      .prepare(
        `SELECT t.*, p.nama AS nama_pelanggan, p.no_hp
         FROM transaksi t JOIN pelanggan p ON p.id_pelanggan = t.id_pelanggan
         WHERE t.id_transaksi = ?`
      )
      .get(id);
    if (!t) throw new Error(`Transaksi #${id} tidak ditemukan.`);
    if (!TRANSISI[t.status].includes(statusBaru)) {
      throw new Error(`Transisi ${t.status} → ${statusBaru} tidak diizinkan.`);
    }
    db.prepare('UPDATE transaksi SET status = ? WHERE id_transaksi = ?').run(statusBaru, id);
    t.status = statusBaru;
    return t;
  },
};
