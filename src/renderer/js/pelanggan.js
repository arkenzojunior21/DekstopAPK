(() => {
  const { esc, pesanError } = window.util;
  const el = (id) => document.getElementById(id);
  const apiPelanggan = window.api.pelanggan;

  let daftar = [];
  let editingId = null;

  async function muat() {
    daftar = await apiPelanggan.getAll();
    render();
  }

  function render() {
    const q = el('cari').value.trim().toLowerCase();
    const tampil = daftar.filter(
      (p) => p.nama.toLowerCase().includes(q) || p.no_hp.toLowerCase().includes(q)
    );
    el('tbody').innerHTML = tampil
      .map(
        (p) => `<tr>
          <td>${p.id_pelanggan}</td>
          <td>${esc(p.nama)}</td>
          <td>${esc(p.no_hp)}</td>
          <td>${esc(p.alamat)}</td>
          <td class="aksi">
            <button class="btn kecil" data-ubah="${p.id_pelanggan}">Ubah</button>
            <button class="btn kecil merah" data-hapus="${p.id_pelanggan}">Hapus</button>
          </td>
        </tr>`
      )
      .join('');
    el('kosong').hidden = tampil.length > 0;
  }

  function bukaForm(p) {
    editingId = p ? p.id_pelanggan : null;
    el('form-judul').textContent = p ? `Ubah Pelanggan #${p.id_pelanggan}` : 'Pelanggan Baru';
    el('form').hidden = false;
    el('nama').value = p ? p.nama : '';
    el('no_hp').value = p ? p.no_hp : '';
    el('alamat').value = p ? p.alamat : '';
    el('nama').focus();
  }

  function tutupForm() {
    editingId = null;
    el('form').hidden = true;
    el('form-error').textContent = '';
  }

  el('btn-baru').addEventListener('click', () => bukaForm(null));
  el('btn-batal').addEventListener('click', tutupForm);

  el('form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const data = {
      nama: el('nama').value,
      no_hp: el('no_hp').value,
      alamat: el('alamat').value,
    };
    try {
      if (editingId) await apiPelanggan.ubah(editingId, data);
      else await apiPelanggan.tambah(data);
      tutupForm();
      await muat();
    } catch (err) {
      el('form-error').textContent = pesanError(err);
    }
  });

  el('tbody').addEventListener('click', async (e) => {
    const idUbah = e.target.dataset?.ubah;
    const idHapus = e.target.dataset?.hapus;
    if (idUbah) {
      bukaForm(daftar.find((p) => p.id_pelanggan === Number(idUbah)));
      return;
    }
    if (idHapus) {
      const p = daftar.find((x) => x.id_pelanggan === Number(idHapus));
      if (!confirm(`Hapus pelanggan "${p.nama}"?`)) return;
      try {
        await apiPelanggan.hapus(p.id_pelanggan);
        await muat();
      } catch (err) {
        const m = pesanError(err);
        alert(
          m.includes('FOREIGN KEY')
            ? 'Pelanggan ini punya transaksi tercatat, tidak bisa dihapus.'
            : m
        );
      }
    }
  });

  el('cari').addEventListener('input', render);

  muat();
})();
