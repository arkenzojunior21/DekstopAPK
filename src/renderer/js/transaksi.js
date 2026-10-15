(() => {
  const { esc, rupiah, pesanError } = window.util;
  const el = (id) => document.getElementById(id);
  const api = window.api.transaksi;

  let pelanggan = [];
  let layanan = [];
  let riwayat = [];
  let filterStatus = 'Semua';

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
    renderStatistik();
    renderRiwayat();
  }

  function renderStatistik() {
    const hitung = (s) => riwayat.filter((t) => t.status === s).length;
    el('stat-proses').textContent = hitung('Proses');
    el('stat-selesai').textContent = hitung('Selesai');
    el('stat-diambil').textContent = hitung('Diambil');
    const piutang = riwayat
      .filter((t) => t.status !== 'Diambil')
      .reduce((s, t) => s + t.sisa_bayar, 0);
    el('stat-piutang').textContent = rupiah(piutang);
  }

  const AKSI = {
    Proses: { label: 'Tandai Selesai', ke: 'Selesai' },
    Selesai: { label: 'Tandai Diambil', ke: 'Diambil' },
  };

  function renderRiwayat() {
    const tampil =
      filterStatus === 'Semua' ? riwayat : riwayat.filter((t) => t.status === filterStatus);
    el('tbody-transaksi').innerHTML = tampil
      .map((t) => {
        const aksi = AKSI[t.status];
        return `<tr>
          <td>#${t.id_transaksi}</td>
          <td>${esc(t.nama_pelanggan)}</td>
          <td>${fmtTgl(t.tanggal_masuk)}</td>
          <td>${fmtTgl(t.tanggal_estimasi)}</td>
          <td class="angka">${rupiah(t.total_bayar)}</td>
          <td class="angka">${rupiah(t.dp)}</td>
          <td class="angka">${rupiah(t.sisa_bayar)}</td>
          <td><span class="badge badge-${t.status.toLowerCase()}">${t.status}</span></td>
          <td class="aksi">
            <button class="btn kecil" data-detail="${t.id_transaksi}">Detail</button>${
              aksi
                ? ` <button class="btn kecil ${aksi.ke === 'Selesai' ? 'hijau' : ''}" data-status="${aksi.ke}" data-id="${t.id_transaksi}">${aksi.label}</button>`
                : ''
            }
          </td>
        </tr>`;
      })
      .join('');
    el('kosong-transaksi').hidden = tampil.length > 0;
  }

  el('chip-status').addEventListener('click', (e) => {
    const chip = e.target.closest('.chip');
    if (!chip) return;
    filterStatus = chip.dataset.filter;
    document.querySelectorAll('#chip-status .chip').forEach((c) => {
      c.classList.toggle('aktif', c === chip);
    });
    renderRiwayat();
  });

  async function bukaDetail(id) {
    const t = await api.getById(id);
    if (!t) return;
    el('modal-judul').textContent = `Transaksi #${t.id_transaksi} — ${t.nama_pelanggan}`;
    el('modal-isi').innerHTML = `
      <div class="info-grid">
        <div class="info"><div class="k">No. HP</div><div class="v">${esc(t.no_hp)}</div></div>
        <div class="info"><div class="k">Masuk</div><div class="v">${fmtTgl(t.tanggal_masuk)}</div></div>
        <div class="info"><div class="k">Estimasi Selesai</div><div class="v">${fmtTgl(t.tanggal_estimasi)}</div></div>
        <div class="info"><div class="k">Status</div><div class="v"><span class="badge badge-${t.status.toLowerCase()}">${t.status}</span></div></div>
      </div>
      <div class="tabel-wrap">
        <table>
          <thead><tr><th>Layanan</th><th>Jumlah</th><th>Harga</th><th>Subtotal</th></tr></thead>
          <tbody>${t.items
            .map(
              (i) => `<tr>
                <td>${esc(i.nama_layanan_snapshot)}</td>
                <td class="angka">${i.jumlah} ${esc(i.satuan || '')}</td>
                <td class="angka">${rupiah(i.harga_snapshot)}</td>
                <td class="angka">${rupiah(i.subtotal)}</td>
              </tr>`
            )
            .join('')}</tbody>
          <tfoot>
            <tr><td colspan="3">Total Bayar</td><td>${rupiah(t.total_bayar)}</td></tr>
            <tr><td colspan="3">DP</td><td>${rupiah(t.dp)}</td></tr>
            <tr><td colspan="3">Sisa Bayar</td><td>${rupiah(t.sisa_bayar)}</td></tr>
          </tfoot>
        </table>
      </div>`;
    el('modal-detail').hidden = false;
  }

  el('modal-tutup').addEventListener('click', () => {
    el('modal-detail').hidden = true;
  });

  el('modal-detail').addEventListener('click', (e) => {
    if (e.target === el('modal-detail')) el('modal-detail').hidden = true;
  });

  el('tbody-transaksi').addEventListener('click', async (e) => {
    const btn = e.target.closest('button');
    if (!btn) return;
    if (btn.dataset.detail) {
      try {
        await bukaDetail(Number(btn.dataset.detail));
      } catch (err) {
        alert(pesanError(err));
      }
      return;
    }
    const id = Number(btn.dataset?.id);
    const ke = btn.dataset?.status;
    if (!id || !ke) return;
    if (!confirm(`Ubah status transaksi #${id} menjadi "${ke}"?`)) return;
    try {
      const hasil = await api.ubahStatus(id, ke);
      if (hasil.wa && !hasil.wa.dibuka) {
        alert(`Status diubah ke "${ke}".\nNotifikasi WA tidak terkirim: ${hasil.wa.alasan}`);
      }
      await muatRiwayat();
    } catch (err) {
      alert(pesanError(err));
    }
  });

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
    const total = hitungTotal();
    if (data.dp > total) {
      el('tr-error').textContent = 'DP tidak boleh melebihi total bayar.';
      return;
    }
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
