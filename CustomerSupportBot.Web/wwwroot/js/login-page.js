window.initLoginPage = function () {
    var params = new URLSearchParams(location.search);
    var returnTo = params.get('return') || '/admin';

    if (window.Auth && window.Auth.getAccessToken()) {
        location.replace(returnTo);
        return;
    }

    var form = document.getElementById('loginForm');
    var errEl = document.getElementById('loginError');
    var btn = document.getElementById('loginBtn');

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        errEl.classList.remove('visible');
        errEl.textContent = '';

        var username = document.getElementById('username').value.trim();
        var password = document.getElementById('password').value;
        if (!username || !password) return;

        btn.disabled = true;
        btn.textContent = 'Giriş yapılıyor\u2026';
        try {
            await window.Auth.login(username, password);
            location.replace(returnTo);
        } catch (err) {
            errEl.textContent = err.message && err.message.includes('401')
                ? 'Kullanıcı adı veya parola hatalı.'
                : (err.message || 'Giriş başarısız.');
            errEl.classList.add('visible');
            btn.disabled = false;
            btn.textContent = 'Giriş Yap';
        }
    });
};
