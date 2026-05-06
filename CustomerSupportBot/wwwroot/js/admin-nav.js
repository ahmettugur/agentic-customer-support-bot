// js/admin-nav.js
// Tüm admin sayfalarında ortak üst navigasyon barı.
// İlgili sayfanın <body> en üstüne dinamik olarak enjekte edilir;
// Mevcut HTML yapısı bozulmaz. Kullanım:
//   <script src="js/admin-nav.js" defer></script>
// CSS'i de içeride enjekte edilir — ek dosyaya gerek yok.

(function () {
  const NAV_ITEMS = [
    { href: '/',                       label: '💬 Sohbet',         match: /^\/?(index\.html)?$/i },
    { href: '/admin.html',             label: '🛡️ HITL Panel',     match: /admin\.html$/i },
    { href: '/traces.html',            label: '🧠 Trace Dashboard', match: /traces\.html$/i },
    { href: '/replay.html',            label: '🎬 Replay',          match: /replay\.html$/i },
    { href: '/workflow-designer.html', label: '🔧 Workflow Designer', match: /workflow-designer\.html$/i },
    { href: '/sla.html',               label: '⏱️ SLA',             match: /sla\.html$/i }
  ];

  // ─── Stil ───
  const css = `
    .csb-topnav {
      display: flex; align-items: center; gap: 4px;
      background: #1f2937; color: #e5e7eb;
      padding: 6px 14px;
      font: 14px/1.4 system-ui, -apple-system, "Segoe UI", sans-serif;
      box-shadow: 0 1px 4px rgba(0,0,0,.1);
      position: sticky; top: 0; z-index: 1000;
    }
    .csb-topnav-brand {
      font-weight: 600; margin-right: 12px; color: #fff;
      letter-spacing: .3px;
    }
    .csb-topnav a {
      color: #d1d5db; text-decoration: none;
      padding: 6px 10px; border-radius: 4px;
      transition: background-color .15s, color .15s;
      white-space: nowrap;
    }
    .csb-topnav a:hover { background: #374151; color: #fff; }
    .csb-topnav a.active {
      background: #2563eb; color: #fff;
    }
    .csb-topnav-spacer { flex: 1; }
    .csb-topnav-user {
      color: #9ca3af; font-size: 12px; margin-right: 8px;
    }
    .csb-topnav-logout {
      background: transparent; border: 1px solid #4b5563;
      color: #d1d5db; padding: 4px 10px; border-radius: 4px;
      font-size: 12px; cursor: pointer;
    }
    .csb-topnav-logout:hover { background: #dc2626; border-color: #dc2626; color: #fff; }
    @media (max-width: 720px) {
      .csb-topnav { flex-wrap: wrap; }
      .csb-topnav-brand { width: 100%; }
    }
  `;
  const style = document.createElement('style');
  style.textContent = css;
  document.head.appendChild(style);

  // ─── HTML ───
  const path = location.pathname;
  const links = NAV_ITEMS.map(item => {
    const active = item.match.test(path) ? ' class="active"' : '';
    return `<a href="${item.href}"${active}>${item.label}</a>`;
  }).join('');

  const userName = (() => {
    try {
      // Birincil: window.Auth (auth.js yüklüyse)
      if (window.Auth && typeof window.Auth.getCurrent === 'function') {
        const cur = window.Auth.getCurrent();
        if (cur) return cur.username || cur.userName || cur.name || cur.email || null;
      }
      // İkincil: localStorage 'cs.auth' JSON
      const raw = localStorage.getItem('cs.auth');
      if (raw) {
        const a = JSON.parse(raw);
        return a.username || a.userName || a.name || a.email || null;
      }
    } catch (_) {}
    return null;
  })();
  const userBlock = userName
    ? `<span class="csb-topnav-user" title="Aktif kullanıcı">👤 ${userName}</span>`
    : '';

  const nav = document.createElement('nav');
  nav.className = 'csb-topnav';
  nav.innerHTML = `
    <span class="csb-topnav-brand">CSB Admin</span>
    ${links}
    <span class="csb-topnav-spacer"></span>
    ${userBlock}
    <button type="button" class="csb-topnav-logout" id="csbTopnavLogout">Çıkış</button>
  `;

  function inject() {
    if (document.getElementById('csb-topnav-injected')) return;
    nav.id = 'csb-topnav-injected';
    document.body.insertBefore(nav, document.body.firstChild);

    document.getElementById('csbTopnavLogout')?.addEventListener('click', async () => {
      try {
        if (window.Auth && typeof window.Auth.logout === 'function') {
          await window.Auth.logout();
          return;
        }
      } catch (_) {}
      try {
        localStorage.removeItem('cs.auth');
        localStorage.removeItem('csb_token');
        localStorage.removeItem('csb_user');
        localStorage.removeItem('token');
        localStorage.removeItem('user');
      } catch (_) {}
      location.href = '/login.html';
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', inject);
  } else {
    inject();
  }
})();
