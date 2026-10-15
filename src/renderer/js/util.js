window.util = {
  esc: (s) =>
    String(s).replace(/[&<>"']/g, (c) =>
      ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])),
  rupiah: (n) => 'Rp' + Number(n).toLocaleString('id-ID'),
  pesanError: (err) =>
    String(err.message || err).replace(/^Error invoking remote method '[^']+': (?:Error: )?/, ''),
};
