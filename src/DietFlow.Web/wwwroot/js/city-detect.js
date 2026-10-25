(function () {
    const button = document.getElementById('cityDetect');
    if (!button) return;
    if (!navigator.geolocation || !window.isSecureContext) { button.remove(); return; }

    button.addEventListener('click', () => {
        button.disabled = true;
        button.textContent = '⏳';
        navigator.geolocation.getCurrentPosition(async pos => {
            const form = button.closest('form');
            const body = new URLSearchParams({
                lat: pos.coords.latitude,
                lng: pos.coords.longitude,
                __RequestVerificationToken: form.querySelector('input[name="__RequestVerificationToken"]').value,
            });
            const response = await fetch('/Cities/Detect', { method: 'POST', body });
            if (response.ok) location.reload();
            else { button.textContent = '📍'; button.disabled = false; alert('Не вдалося визначити місто.'); }
        }, () => {
            button.textContent = '📍';
            button.disabled = false;
            alert('Доступ до геолокації заборонено — оберіть місто зі списку.');
        }, { timeout: 10000 });
    });
})();
