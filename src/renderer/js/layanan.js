(() => {
  const { esc, rupiah, pesanError } = window.util;
  const el = (id) => document.getElementById(id);
  const apiLayanan = window.api.layanan;

  let daftar = [];
  let editingId = null;

  async function muat() {
    daftar = await apiLayanan.getAll();
    render();
  }

  function render() {
    const q = el('cari-layanan').value.trim().toLowerCase();
    const tampil = daftar.filter((l) => l.nama_layanan.toLowerCase().includes(q));
    el('tbody-layanan').innerHTML = tampil
      .map(
        (l) => `<tr>
          <td>${l.id_layanan}</td>
          <td>${esc(l.nama_layanan)}</td>
          <td>${esc(l.satuan)}</td>
          <td class="angka">${rupiah(l.harga_persatuan)}</td>
          <td class="aksi">
            <button class="btn kecil" data-ubah="${l.id_layanan}">Ubah</button>
            <button class="btn kecil merah" data-hapus="${l.id_layanan}">Hapus</button>
          </td>
        </tr>`
      )
      .join('');
    el('kosong-layanan').hidden = tampil.length > 0;
  }

  function bukaForm(l) {
    editingId = l ? l.id_layanan : null;
    el('form-judul-layanan').textContent = l ? `Ubah Layanan #${l.id_layanan}` : 'Layanan Baru';
    el('form-layanan').hidden = false;
    el('nama_layanan').value = l ? l.nama_layanan : '';
    el('satuan').value = l ? l.satuan : '';
    el('harga').value = l ? l.harga_persatuan : '';
    el('nama_layanan').focus();
  }

  function tutupForm() {
    editingId = null;
    el('form-layanan').hidden = true;
    el('form-error-layanan').textContent = '';
  }

  el('btn-baru-layanan').addEventListener('click', () => bukaForm(null));
  el('btn-batal-layanan').addEventListener('click', tutupForm);

  el('form-layanan').addEventListener('submit', async (e) => {
    e.preventDefault();
    const data = {
      nama_layanan: el('nama_layanan').value,
      satuan: el('satuan').value,
      harga_persatuan: el('harga').value,
    };
    try {
      if (editingId) await apiLayanan.ubah(editingId, data);
      else await apiLayanan.tambah(data);
      tutupForm();
      await muat();
    } catch (err) {
      el('form-error-layanan').textContent = pesanError(err);
    }
  });

  el('tbody-layanan').addEventListener('click', async (e) => {
    const idUbah = e.target.dataset?.ubah;
    const idHapus = e.target.dataset?.hapus;
    if (idUbah) {
      bukaForm(daftar.find((l) => l.id_layanan === Number(idUbah)));
      return;
    }
    if (idHapus) {
      const l = daftar.find((x) => x.id_layanan === Number(idHapus));
      if (!confirm(`Hapus layanan "${l.nama_layanan}"?`)) return;
      try {
        await apiLayanan.hapus(l.id_layanan);
        await muat();
      } catch (err) {
        const m = pesanError(err);
        alert(
          m.includes('FOREIGN KEY')
            ? 'Layanan ini sudah dipakai di transaksi, tidak bisa dihapus.'
            : m
        );
      }
    }
  });

  el('cari-layanan').addEventListener('input', render);

  muat();
})();
