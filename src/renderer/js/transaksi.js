(() => {
  const { esc, rupiah, pesanError } = window.util;
  const el = (id) => document.getElementById(id);
  const api = window.api.transaksi;

  let pelanggan = [];
  let layanan = [];
  let riwayat = [];

  const fmtTgl = (d) =>
    new Date(d + 'T00:00:00').toLocaleDateString('id-ID', {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });

  function tanggalLokal(offsetHari = 0) {
    const d = new Date(Date.now() + offsetHari * 86400000);
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }

  async function muat() {
    [pelanggan, layanan] = await Promise.all([
      window.api.pelanggan.getAll(),
      window.api.layanan.getAll(),
    ]);
    el('tr-pelanggan').innerHTML =
      '<option value="">— pilih pelanggan —</option>' +
      pelanggan
        .map((p) => `<option value="${p.id_pelanggan}">${esc(p.nama)} — ${esc(p.no_hp)}</option>`)
        .join('');
    resetForm();
    await muatRiwayat();
  }

  async function muatRiwayat() {
    riwayat = await api.getAll();
    renderRiwayat();
  }

  function renderRiwayat() {
    el('tbody-transaksi').innerHTML = riwayat
      .map(
        (t) => `<tr>
          <td>#${t.id_transaksi}</td>
          <td>${esc(t.nama_pelanggan)}</td>
          <td>${fmtTgl(t.tanggal_masuk)}</td>
          <td>${fmtTgl(t.tanggal_estimasi)}</td>
          <td class="angka">${rupiah(t.total_bayar)}</td>
          <td class="angka">${rupiah(t.dp)}</td>
          <td class="angka">${rupiah(t.sisa_bayar)}</td>
          <td><span class="badge badge-${t.status.toLowerCase()}">${t.status}</span></td>
        </tr>`
      )
      .join('');
    el('kosong-transaksi').hidden = riwayat.length > 0;
  }

  function tambahBaris() {
    const row = document.createElement('div');
    row.className = 'item-baris';
    row.innerHTML = `
      <select class="item-layanan">
        ${layanan.map((l) => `<option value="${l.id_layanan}">${esc(l.nama_layanan)} (${esc(l.satuan)})</option>`).join('')}
      </select>
      <input type="number" class="item-jumlah" min="0.1" step="0.1" value="1" />
      <span class="item-subtotal">Rp0</span>
      <button type="button" class="btn kecil merah item-hapus" title="Hapus baris">×</button>`;
    el('tr-items').appendChild(row);
    hitungBaris(row);
    hitungTotal();
  }

  function hitungBaris(row) {
    const l = layanan.find((x) => x.id_layanan === Number(row.querySelector('.item-layanan').value));
    const jml = Number(row.querySelector('.item-jumlah').value) || 0;
    row.querySelector('.item-subtotal').textContent = l ? rupiah(Math.round(l.harga_persatuan * jml)) : 'Rp0';
  }

  function hitungTotal() {
    let total = 0;
    document.querySelectorAll('#tr-items .item-baris').forEach((row) => {
      const l = layanan.find(
        (x) => x.id_layanan === Number(row.querySelector('.item-layanan').value)
      );
      const jml = Number(row.querySelector('.item-jumlah').value) || 0;
      if (l && jml > 0) total += Math.round(l.harga_persatuan * jml);
    });
    const dp = Number(el('tr-dp').value) || 0;
    el('tr-total').textContent = rupiah(total);
    el('tr-sisa').textContent = rupiah(Math.max(total - dp, 0));
    return total;
  }

  function resetForm() {
    el('tr-pelanggan').value = '';
    el('tr-masuk').value = tanggalLokal(0);
    el('tr-estimasi').value = tanggalLokal(3);
    el('tr-dp').value = 0;
    el('tr-items').innerHTML = '';
    el('tr-error').textContent = '';
    tambahBaris();
  }

  el('tr-tambah').addEventListener('click', tambahBaris);

  el('tr-items').addEventListener('input', (e) => {
    if (e.target.matches('.item-layanan, .item-jumlah')) {
      hitungBaris(e.target.closest('.item-baris'));
      hitungTotal();
    }
  });

  el('tr-items').addEventListener('click', (e) => {
    if (e.target.matches('.item-hapus')) {
      e.target.closest('.item-baris').remove();
      hitungTotal();
    }
  });

  el('tr-dp').addEventListener('input', hitungTotal);

  el('form-transaksi').addEventListener('submit', async (e) => {
    e.preventDefault();
    el('tr-error').textContent = '';
    const items = [...document.querySelectorAll('#tr-items .item-baris')]
      .map((row) => ({
        id_layanan: Number(row.querySelector('.item-layanan').value),
        jumlah: Number(row.querySelector('.item-jumlah').value),
      }))
      .filter((it) => it.id_layanan > 0 && it.jumlah > 0);
    const data = {
      id_pelanggan: Number(el('tr-pelanggan').value),
      tanggal_masuk: el('tr-masuk').value,
      tanggal_estimasi: el('tr-estimasi').value,
      dp: Number(el('tr-dp').value) || 0,
      items,
    };
    try {
      const r = await api.buat(data);
      alert(`Transaksi #${r.id_transaksi} tersimpan.\nTotal: ${rupiah(r.total)}`);
      resetForm();
      await muatRiwayat();
    } catch (err) {
      el('tr-error').textContent = pesanError(err);
    }
  });

  muat();
})();
