// js/auth.js
// Tarayıcı tarafı JWT auth helper'ı.
// - Token storage: localStorage anahtarı 'cs.auth'
// - window.Auth.fetch: Bearer otomatik ekler, 401'de access token'ı refresh ile yenilemeyi dener,
//   yine başarısızsa login.html'e yönlendirir.
// - window.Auth.eventSourceUrl(url): SSE EventSource için ?access_token=... ekler
//   (EventSource header gönderemediği için query param kullanıyoruz; backend
//    JwtBearer.OnMessageReceived bu query param'ı okur).
// - window.Auth.requireAuth(): sayfa yüklenirken token yoksa login.html'e atar.

(function () {
    const STORAGE_KEY = 'cs.auth';
    const LOGIN_PAGE = '/login.html';

    function read() {
        try {
            const raw = localStorage.getItem(STORAGE_KEY);
            return raw ? JSON.parse(raw) : null;
        } catch { return null; }
    }

    function write(data) {
        if (!data) {
            localStorage.removeItem(STORAGE_KEY);
        } else {
            localStorage.setItem(STORAGE_KEY, JSON.stringify(data));
        }
    }

    function redirectToLogin(returnTo) {
        write(null);
        const target = returnTo || (location.pathname + location.search);
        const url = `${LOGIN_PAGE}?return=${encodeURIComponent(target)}`;
        if (location.pathname !== LOGIN_PAGE) location.replace(url);
    }

    async function login(username, password) {
        const r = await fetch('/auth/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username, password })
        });
        if (!r.ok) {
            const text = await r.text().catch(() => '');
            throw new Error(text || `Login başarısız (HTTP ${r.status})`);
        }
        const auth = await r.json();
        write(auth);
        return auth;
    }

    async function logout() {
        const auth = read();
        if (auth?.refreshToken) {
            try {
                await fetch('/auth/logout', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'Authorization': `Bearer ${auth.accessToken}`
                    },
                    body: JSON.stringify({ refreshToken: auth.refreshToken })
                });
            } catch { /* ignore */ }
        }
        write(null);
        location.replace(LOGIN_PAGE);
    }

    let refreshPromise = null;
    async function tryRefresh() {
        const auth = read();
        if (!auth?.refreshToken) return null;
        if (refreshPromise) return refreshPromise;

        refreshPromise = (async () => {
            try {
                const r = await fetch('/auth/refresh', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ refreshToken: auth.refreshToken })
                });
                if (!r.ok) return null;
                const next = await r.json();
                write(next);
                return next;
            } catch {
                return null;
            } finally {
                refreshPromise = null;
            }
        })();
        return refreshPromise;
    }

    function getAccessToken() {
        return read()?.accessToken || null;
    }

    function getCurrent() {
        const a = read();
        if (!a) return null;
        return { username: a.username, role: a.role };
    }

    async function authFetch(input, init = {}) {
        const auth = read();
        if (!auth?.accessToken) {
            redirectToLogin();
            throw new Error('Unauthorized');
        }

        const headers = new Headers(init.headers || {});
        headers.set('Authorization', `Bearer ${auth.accessToken}`);
        const opts = { ...init, headers };

        let res = await fetch(input, opts);
        if (res.status !== 401) return res;

        // 401 — refresh dene
        const refreshed = await tryRefresh();
        if (!refreshed) {
            redirectToLogin();
            throw new Error('Unauthorized');
        }
        const headers2 = new Headers(init.headers || {});
        headers2.set('Authorization', `Bearer ${refreshed.accessToken}`);
        res = await fetch(input, { ...init, headers: headers2 });
        if (res.status === 401) {
            redirectToLogin();
            throw new Error('Unauthorized');
        }
        return res;
    }

    function eventSourceUrl(url) {
        const token = getAccessToken();
        if (!token) {
            redirectToLogin();
            return url;
        }
        const sep = url.includes('?') ? '&' : '?';
        return `${url}${sep}access_token=${encodeURIComponent(token)}`;
    }

    function requireAuth() {
        if (!getAccessToken()) {
            redirectToLogin();
            return false;
        }
        return true;
    }

    window.Auth = {
        login,
        logout,
        fetch: authFetch,
        eventSourceUrl,
        requireAuth,
        getAccessToken,
        getCurrent
    };
})();
