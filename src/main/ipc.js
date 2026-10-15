const { ipcMain } = require('electron');
const pelanggan = require('./db/pelanggan');
const layanan = require('./db/layanan');
const transaksi = require('./db/transaksi');

ipcMain.handle('pelanggan:getAll', () => pelanggan.getAll());
ipcMain.handle('pelanggan:tambah', (_e, data) => pelanggan.tambah(data));
ipcMain.handle('pelanggan:ubah', (_e, id, data) => pelanggan.ubah(id, data));
ipcMain.handle('pelanggan:hapus', (_e, id) => pelanggan.hapus(id));

ipcMain.handle('layanan:getAll', () => layanan.getAll());
ipcMain.handle('layanan:tambah', (_e, data) => layanan.tambah(data));
ipcMain.handle('layanan:ubah', (_e, id, data) => layanan.ubah(id, data));
ipcMain.handle('layanan:hapus', (_e, id) => layanan.hapus(id));

ipcMain.handle('transaksi:buat', (_e, data) => transaksi.buat(data));
ipcMain.handle('transaksi:getAll', () => transaksi.getAll());
ipcMain.handle('transaksi:getById', (_e, id) => transaksi.getById(id));
