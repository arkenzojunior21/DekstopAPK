const { shell } = require('electron');

function normalisasiHp(noHp) {
  const angka = String(noHp || '').replace(/[^0-9]/g, '');
  if (angka.startsWith('0')) return '62' + angka.slice(1);
  if (angka.startsWith('62')) return angka;
  if (angka.startsWith('8')) return '62' + angka;
  return null;
}

function buatPesanSelesai(t) {
  return [
    `Halo ${t.nama_pelanggan},`,
    ``,
    `Cucian Anda (Transaksi #${t.id_transaksi}) sudah selesai diproses dan siap diambil di toko.`,
    `Estimasi selesai: ${t.tanggal_estimasi}.`,
    ``,
    `Total: Rp${Number(t.total_bayar).toLocaleString('id-ID')}`,
    `Sisa bayar: Rp${Number(t.sisa_bayar).toLocaleString('id-ID')}`,
    ``,
    `Terima kasih telah menggunakan jasa laundry kami!`,
  ].join('\n');
}

async function kirimWaSelesai(t) {
  const hp = normalisasiHp(t.no_hp);
  if (!hp || hp.length < 10) {
    return { dibuka: false, alasan: 'Nomor HP pelanggan tidak valid untuk WhatsApp.' };
  }
  const url = `https://wa.me/${hp}?text=${encodeURIComponent(buatPesanSelesai(t))}`;
  await shell.openExternal(url);
  return { dibuka: true, nomor: hp };
}

module.exports = { kirimWaSelesai };
