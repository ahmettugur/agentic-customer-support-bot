// sla-bridge.js
// Called as: __slaSetup(dotNetRef)

window.__slaSetup = function (ref) {
    window.__slaRef = ref;
    document.addEventListener('visibilitychange', function () {
        if (window.__slaRef)
            window.__slaRef.invokeMethodAsync('OnVisibilityChange', !document.hidden).catch(function () { });
    });
};
