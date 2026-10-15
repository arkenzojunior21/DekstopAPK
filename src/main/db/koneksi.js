const { app } = require('electron');
const path = require('path');
const Database = require('better-sqlite3');

const db = new Database(path.join(app.getPath('userData'), 'laundryflow.db'));

db.pragma('journal_mode = WAL');
db.pragma('foreign_keys = ON');

db.exec(`
  CREATE TABLE IF NOT EXISTS pelanggan (
    id_pelanggan INTEGER PRIMARY KEY AUTOINCREMENT,
    nama         TEXT NOT NULL,
    no_hp        TEXT NOT NULL,
    alamat       TEXT NOT NULL
  );

  CREATE TABLE IF NOT EXISTS layanan (
    id_layanan      INTEGER PRIMARY KEY AUTOINCREMENT,
    nama_layanan    TEXT NOT NULL,
    satuan          TEXT NOT NULL,
    harga_persatuan INTEGER NOT NULL
  );

  CREATE TABLE IF NOT EXISTS transaksi (
    id_transaksi     INTEGER PRIMARY KEY AUTOINCREMENT,
    id_pelanggan     INTEGER NOT NULL REFERENCES pelanggan(id_pelanggan),
    tanggal_masuk    TEXT NOT NULL,
    tanggal_estimasi TEXT NOT NULL,
    total_bayar      INTEGER NOT NULL,
    dp               INTEGER NOT NULL DEFAULT 0,
    sisa_bayar       INTEGER NOT NULL,
    status           TEXT NOT NULL DEFAULT 'Proses'
                      CHECK (status IN ('Proses', 'Selesai', 'Diambil'))
  );

  CREATE TABLE IF NOT EXISTS detail_transaksi (
    id_detail    INTEGER PRIMARY KEY AUTOINCREMENT,
    id_transaksi INTEGER NOT NULL REFERENCES transaksi(id_transaksi),
    id_layanan   INTEGER NOT NULL REFERENCES layanan(id_layanan),
    jumlah       REAL NOT NULL,
    subtotal     INTEGER NOT NULL
  );
`);

const jumlahLayanan = db.prepare('SELECT COUNT(*) AS n FROM layanan').get().n;
if (jumlahLayanan === 0) {
  const tambah = db.prepare(
    'INSERT INTO layanan (nama_layanan, satuan, harga_persatuan) VALUES (?, ?, ?)'
  );
  db.transaction(() => {
    tambah.run('Cuci Kering', 'kg', 7000);
    tambah.run('Cuci Kering Setrika', 'kg', 9000);
    tambah.run('Setrika Saja', 'kg', 5000);
    tambah.run('Cuci Sepatu', 'pasang', 15000);
  })();
}

module.exports = db;
