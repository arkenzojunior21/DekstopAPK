const db = require('./koneksi');

function validasi(data) {
  const nama = data.nama_layanan?.trim();
  const satuan = data.satuan?.trim();
  const harga = Number(data.harga_persatuan);
  if (!nama || !satuan) {
    throw new Error('Nama layanan dan satuan wajib diisi.');
  }
  if (!Number.isInteger(harga) || harga < 0) {
    throw new Error('Harga harus angka bulat, minimal 0.');
  }
  return { nama_layanan: nama, satuan, harga_persatuan: harga };
}

module.exports = {
  getAll: () => db.prepare('SELECT * FROM layanan ORDER BY nama_layanan').all(),

  tambah: (data) => {
    const bersih = validasi(data);
    const info = db
      .prepare(
        'INSERT INTO layanan (nama_layanan, satuan, harga_persatuan) VALUES (@nama_layanan, @satuan, @harga_persatuan)'
      )
      .run(bersih);
    return { id_layanan: Number(info.lastInsertRowid) };
  },

  ubah: (id, data) => {
    const bersih = validasi(data);
    const info = db
      .prepare(
        'UPDATE layanan SET nama_layanan = @nama_layanan, satuan = @satuan, harga_persatuan = @harga_persatuan WHERE id_layanan = @id'
      )
      .run({ ...bersih, id: id });
    return { perubahan: info.changes };
  },

  hapus: (id) => {
    const info = db.prepare('DELETE FROM layanan WHERE id_layanan = ?').run(id);
    return { perubahan: info.changes };
  },
};
