// voice-orb.js
// Sesli görüşme ekranındaki kürenin kare başı animasyonu. Seviye realtime-client.js'in
// AnalyserNode'larından okunur ve #voiceOrb'a --level (0–1) olarak yazılır; Blazor yeniden
// çizilmez. Hangi seviyenin kullanılacağını kürenin durum sınıfı belirler (durumun tek kaynağı
// C# modeli): dinlerken mikrofon, konuşurken asistan sesi, diğer durumlarda 0.
// Hareket azaltma tercihinde döngü hiç başlamaz.

(function () {
    'use strict';

    const ATTACK = 0.5;    // yükselişte hızlı tepki
    const RELEASE = 0.08;  // düşüşte yavaş sönme — titremeyi önler

    let raf = 0;
    let level = 0;

    const reducedMotion = () =>
        !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);

    function start(getLevels) {
        stop();
        if (reducedMotion()) return;
        const tick = () => {
            const orb = document.getElementById('voiceOrb');
            if (orb) {
                let target = 0;
                try {
                    const levels = getLevels() || {};
                    if (orb.classList.contains('vc-orb--speaking')) target = levels.output || 0;
                    else if (orb.classList.contains('vc-orb--listening')) target = levels.input || 0;
                } catch { target = 0; }
                level += (target - level) * (target > level ? ATTACK : RELEASE);
                orb.style.setProperty('--level', level.toFixed(3));
            }
            raf = requestAnimationFrame(tick);
        };
        raf = requestAnimationFrame(tick);
    }

    function stop() {
        if (raf) cancelAnimationFrame(raf);
        raf = 0;
        level = 0;
    }

    window.voiceOrb = {
        start,
        stop,
        get running() { return raf !== 0; }
    };
})();
