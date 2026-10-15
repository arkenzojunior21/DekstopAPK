const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('api', {
  pelanggan: {
    getAll: () => ipcRenderer.invoke('pelanggan:getAll'),
    tambah: (data) => ipcRenderer.invoke('pelanggan:tambah', data),
    ubah: (id, data) => ipcRenderer.invoke('pelanggan:ubah', id, data),
    hapus: (id) => ipcRenderer.invoke('pelanggan:hapus', id),
  },
  layanan: {
    getAll: () => ipcRenderer.invoke('layanan:getAll'),
    tambah: (data) => ipcRenderer.invoke('layanan:tambah', data),
    ubah: (id, data) => ipcRenderer.invoke('layanan:ubah', id, data),
    hapus: (id) => ipcRenderer.invoke('layanan:hapus', id),
  },
  transaksi: {
    buat: (data) => ipcRenderer.invoke('transaksi:buat', data),
    getAll: () => ipcRenderer.invoke('transaksi:getAll'),
    getById: (id) => ipcRenderer.invoke('transaksi:getById', id),
  },
});
