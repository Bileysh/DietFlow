(function () {
    document.querySelectorAll('[data-store-map]').forEach(el => {
        if (!window.L) return;
        const lat = parseFloat(el.dataset.lat), lng = parseFloat(el.dataset.lng);
        const map = L.map(el).setView([lat, lng], 15);
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19, attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
        }).addTo(map);
        const editable = el.hasAttribute('data-editable');
        const marker = L.marker([lat, lng], { draggable: editable }).addTo(map);
        if (!editable) return;

        const latInput = document.getElementById('Latitude');
        const lngInput = document.getElementById('Longitude');
        const setInputs = ll => { latInput.value = ll.lat.toFixed(6); lngInput.value = ll.lng.toFixed(6); };

        map.on('click', e => { marker.setLatLng(e.latlng); setInputs(e.latlng); });
        marker.on('dragend', () => setInputs(marker.getLatLng()));
        [latInput, lngInput].forEach(input => input.addEventListener('change', () => {
            const ll = L.latLng(parseFloat(latInput.value), parseFloat(lngInput.value));
            if (!isNaN(ll.lat) && !isNaN(ll.lng)) { marker.setLatLng(ll); map.panTo(ll); }
        }));
    });
})();
