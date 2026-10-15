const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('api', {
  pelanggan: {
    getAll: () => ipcRenderer.invoke('pelanggan:getAll'),
    tambah: (data) => ipcRenderer.invoke('pelanggan:tambah', data),
    ubah: (id, data) => ipcRenderer.invoke('pelanggan:ubah', id, data),
    hapus: (id) => ipcRenderer.invoke('pelanggan:hapus', id),
  },
});
