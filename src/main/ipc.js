const { ipcMain } = require('electron');
const pelanggan = require('./db/pelanggan');

ipcMain.handle('pelanggan:getAll', () => pelanggan.getAll());
ipcMain.handle('pelanggan:tambah', (_e, data) => pelanggan.tambah(data));
ipcMain.handle('pelanggan:ubah', (_e, id, data) => pelanggan.ubah(id, data));
ipcMain.handle('pelanggan:hapus', (_e, id) => pelanggan.hapus(id));
