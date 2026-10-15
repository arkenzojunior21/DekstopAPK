const { app, BrowserWindow } = require('electron');
const path = require('path');

function buatWindow() {
  const win = new BrowserWindow({
    width: 1200,
    height: 760,
    minWidth: 980,
    minHeight: 640,
    backgroundColor: '#eef2f8',
    icon: path.join(__dirname, '..', '..', 'build', 'icon.png'),
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

  if (process.argv.includes('--seed-demo')) {
    const pelanggan = require('./db/pelanggan');
    const transaksi = require('./db/transaksi');
    const sudah = db.prepare('SELECT COUNT(*) AS n FROM transaksi').get().n;
    if (sudah > 0) {
      console.log(`SEED_LEWATI sudah ada ${sudah} transaksi.`);
      app.quit();
      return;
    }
    const hariIni = new Date();
    const tgl = (offset) => {
      const d = new Date(hariIni.getTime() + offset * 86400000);
      return d.toISOString().slice(0, 10);
    };
    const budi = pelanggan.tambah({ nama: 'Budi Santoso', no_hp: '081234567890', alamat: 'Jl. Merdeka No. 10' });
    const sari = pelanggan.tambah({ nama: 'Sari Dewi', no_hp: '082198765432', alamat: 'Jl. Kenanga No. 5' });
    const layanan = require('./db/layanan').getAll();
    const cuciSetrika = layanan.find((l) => l.nama_layanan === 'Cuci Kering Setrika');
    const sepatu = layanan.find((l) => l.nama_layanan === 'Cuci Sepatu');
    const t1 = transaksi.buat({
      id_pelanggan: budi.id_pelanggan,
      tanggal_masuk: tgl(-4),
      tanggal_estimasi: tgl(-1),
      dp: 20000,
      items: [{ id_layanan: cuciSetrika.id_layanan, jumlah: 3 }],
    });
    transaksi.ubahStatus(t1.id_transaksi, 'Selesai');
    transaksi.buat({
      id_pelanggan: sari.id_pelanggan,
      tanggal_masuk: tgl(0),
      tanggal_estimasi: tgl(3),
      dp: 0,
      items: [{ id_layanan: sepatu.id_layanan, jumlah: 2 }],
    });
    console.log('SEED_OK 2 pelanggan demo + 2 transaksi demo ditambahkan.');
    app.quit();
    return;
  }

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

    let tolakSalah = false;
    try {
      transaksi.ubahStatus(t.id_transaksi, 'Diambil');
    } catch {
      tolakSalah = true;
    }
    transaksi.ubahStatus(t.id_transaksi, 'Selesai');
    transaksi.ubahStatus(t.id_transaksi, 'Diambil');
    let tolakUlang = false;
    try {
      transaksi.ubahStatus(t.id_transaksi, 'Selesai');
    } catch {
      tolakUlang = true;
    }
    const statusOk =
      tolakSalah &&
      tolakUlang &&
      transaksi.getById(t.id_transaksi).status === 'Diambil';

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
      `SMOKE_OK db="${db.name}" layanan=${n} pelangganCRUD=${pelangganOk} layananCRUD=${layananOk} transaksiOK=${transaksiOk} snapshotOK=${snapshotOk} statusOK=${statusOk}`
    );
    app.quit();
    return;
  }

  require('./ipc');
  buatWindow();
});
