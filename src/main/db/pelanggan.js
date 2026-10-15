const db = require('./koneksi');

function validasi(data) {
  if (!data.nama?.trim() || !data.no_hp?.trim() || !data.alamat?.trim()) {
    throw new Error('Nama, No. HP, dan alamat wajib diisi.');
  }
  if (!/^[0-9+\-\s]{8,17}$/.test(data.no_hp.trim())) {
    throw new Error('Format No. HP tidak valid (8-17 karakter angka, boleh + dan spasi).');
  }
  return { nama: data.nama.trim(), no_hp: data.no_hp.trim(), alamat: data.alamat.trim() };
}

module.exports = {
  getAll: () => db.prepare('SELECT * FROM pelanggan ORDER BY nama').all(),

  tambah: (data) => {
    const bersih = validasi(data);
    const info = db
      .prepare('INSERT INTO pelanggan (nama, no_hp, alamat) VALUES (@nama, @no_hp, @alamat)')
      .run(bersih);
    return { id_pelanggan: Number(info.lastInsertRowid) };
  },

  ubah: (id, data) => {
    const bersih = validasi(data);
    const info = db
      .prepare(
        'UPDATE pelanggan SET nama = @nama, no_hp = @no_hp, alamat = @alamat WHERE id_pelanggan = @id'
      )
      .run({ ...bersih, id: id });
    return { perubahan: info.changes };
  },

  hapus: (id) => {
    const info = db.prepare('DELETE FROM pelanggan WHERE id_pelanggan = ?').run(id);
    return { perubahan: info.changes };
  },
};
