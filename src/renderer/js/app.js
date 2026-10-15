(() => {
  const tombol = [...document.querySelectorAll('.sidebar nav button')];

  function aktifkan(btn) {
    tombol.forEach((b) => b.classList.remove('nav-aktif'));
    btn.classList.add('nav-aktif');
    const target = 'halaman-' + btn.dataset.halaman;
    document.querySelectorAll('.halaman').forEach((h) => {
      h.hidden = h.id !== target;
    });
  }

  tombol.forEach((btn) => btn.addEventListener('click', () => aktifkan(btn)));

  const awal = location.hash.replace('#', '');
  aktifkan(tombol.find((b) => b.dataset.halaman === awal) || tombol[0]);
})();
