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
    const hasil = pelanggan.tambah({
      nama: 'Uji Coba',
      no_hp: '081234567890',
      alamat: 'Jl. Tes No. 1',
    });
    pelanggan.ubah(hasil.id_pelanggan, {
      nama: 'Uji Coba Edit',
      no_hp: '081234567890',
      alamat: 'Jl. Tes No. 2',
    });
    const ketemu = pelanggan.getAll().some((p) => p.nama === 'Uji Coba Edit');
    pelanggan.hapus(hasil.id_pelanggan);
    const n = db.prepare('SELECT COUNT(*) AS n FROM layanan').get().n;
    console.log(`SMOKE_OK db="${db.name}" layanan=${n} pelangganCRUD=${ketemu}`);
    app.quit();
    return;
  }

  require('./ipc');
  buatWindow();
});
