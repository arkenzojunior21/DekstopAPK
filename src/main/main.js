const { app, BrowserWindow } = require('electron');
const path = require('path');

function buatWindow() {
  const win = new BrowserWindow({
    width: 1100,
    height: 720,
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, '..', 'preload', 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });
  const halaman = process.argv.find((a) => a.startsWith('--halaman='))?.split('=')[1];
  win.loadFile(path.join(__dirname, '..', 'renderer', 'index.html'), halaman ? { hash: halaman } : {});
}

app.whenReady().then(() => {
  const db = require('./db/koneksi');

  if (process.argv.includes('--smoke')) {
    const pelanggan = require('./db/pelanggan');
    const layanan = require('./db/layanan');

    const p = pelanggan.tambah({
      nama: 'Uji Coba',
      no_hp: '081234567890',
      alamat: 'Jl. Tes No. 1',
    });
    pelanggan.ubah(p.id_pelanggan, {
      nama: 'Uji Coba Edit',
      no_hp: '081234567890',
      alamat: 'Jl. Tes No. 2',
    });
    const pelangganOk = pelanggan.getAll().some((x) => x.nama === 'Uji Coba Edit');
    pelanggan.hapus(p.id_pelanggan);

    const l = layanan.tambah({
      nama_layanan: 'Uji Layanan',
      satuan: 'kg',
      harga_persatuan: 1000,
    });
    layanan.ubah(l.id_layanan, {
      nama_layanan: 'Uji Layanan Edit',
      satuan: 'item',
      harga_persatuan: 2000,
    });
    const layananOk = layanan.getAll().some((x) => x.nama_layanan === 'Uji Layanan Edit');
    layanan.hapus(l.id_layanan);

    const transaksi = require('./db/transaksi');
    const cuci = layanan.getAll().find((x) => x.nama_layanan === 'Cuci Kering');
    const sepatu = layanan.getAll().find((x) => x.nama_layanan === 'Cuci Sepatu');
    const pel = pelanggan.tambah({
      nama: 'Uji Transaksi',
      no_hp: '081299999999',
      alamat: 'Jl. Uji No. 1',
    });
    const t = transaksi.buat({
      id_pelanggan: pel.id_pelanggan,
      tanggal_masuk: '2026-10-15',
      tanggal_estimasi: '2026-10-18',
      dp: 10000,
      items: [
        { id_layanan: cuci.id_layanan, jumlah: 2 },
        { id_layanan: sepatu.id_layanan, jumlah: 1 },
      ],
    });
    const detail = transaksi.getById(t.id_transaksi);
    const transaksiOk =
      t.total === 29000 &&
      detail.items.length === 2 &&
      detail.sisa_bayar === 19000 &&
      detail.status === 'Proses';

    layanan.ubah(cuci.id_layanan, {
      nama_layanan: 'Cuci Kering',
      satuan: 'kg',
      harga_persatuan: 99000,
    });
    const snapshotOk =
      transaksi.getById(t.id_transaksi).items[0].harga_snapshot === 7000;

    db.prepare('DELETE FROM detail_transaksi WHERE id_transaksi = ?').run(t.id_transaksi);
    db.prepare('DELETE FROM transaksi WHERE id_transaksi = ?').run(t.id_transaksi);
    layanan.ubah(cuci.id_layanan, {
      nama_layanan: 'Cuci Kering',
      satuan: 'kg',
      harga_persatuan: 7000,
    });
    pelanggan.hapus(pel.id_pelanggan);

    const n = db.prepare('SELECT COUNT(*) AS n FROM layanan').get().n;
    console.log(
      `SMOKE_OK db="${db.name}" layanan=${n} pelangganCRUD=${pelangganOk} layananCRUD=${layananOk} transaksiOK=${transaksiOk} snapshotOK=${snapshotOk}`
    );
    app.quit();
    return;
  }

  require('./ipc');
  buatWindow();
});
