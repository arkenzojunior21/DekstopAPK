document.querySelectorAll('.sidebar nav button').forEach((btn) => {
  btn.addEventListener('click', () => {
    document
      .querySelectorAll('.sidebar nav button')
      .forEach((b) => b.classList.remove('nav-aktif'));
    btn.classList.add('nav-aktif');
    const target = 'halaman-' + btn.dataset.halaman;
    document.querySelectorAll('.halaman').forEach((h) => {
      h.hidden = h.id !== target;
    });
  });
});
