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
  win.loadFile(path.join(__dirname, '..', 'renderer', 'index.html'));
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

    const n = db.prepare('SELECT COUNT(*) AS n FROM layanan').get().n;
    console.log(
      `SMOKE_OK db="${db.name}" layanan=${n} pelangganCRUD=${pelangganOk} layananCRUD=${layananOk}`
    );
    app.quit();
    return;
  }

  require('./ipc');
  buatWindow();
});
