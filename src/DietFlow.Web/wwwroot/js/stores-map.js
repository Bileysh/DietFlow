(function () {
    const el = document.getElementById('storesMap');
    if (!el || !window.L) return;

    const cityId = el.dataset.cityId;
    const map = L.map(el).setView([+el.dataset.lat, +el.dataset.lng], 12);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
    }).addTo(map);

    const cluster = L.markerClusterGroup({ maxClusterRadius: 40 });
    map.addLayer(cluster);

    const chainColors = { 'АТБ': '#d6352b', 'Сільпо': '#f28c28', 'Novus': '#2e9e4f', 'Фора': '#7b3fb0', 'Varus': '#1f6fd1' };
    const chainColor = chain => chainColors[chain] || '#6c757d';
    document.querySelectorAll('.chain-dot').forEach(d => d.style.background = chainColor(d.dataset.chain));

    const status = document.getElementById('mapStatus');
    const nearestInfo = document.getElementById('nearestInfo');
    let stores = [];         
    let prices = new Map();   
    let userLayer = null;

    async function loadStores() {
        let url = `/api/v1/stores?cityId=${encodeURIComponent(cityId)}&limit=100`;
        const all = [];
        while (url) {
            const response = await fetch(url, { headers: { Accept: 'application/json' } });
            if (!response.ok) throw new Error(response.status);
            const page = await response.json();
            all.push(...page.items);
            url = page.nextLink; 
        }
        return all;
    }

    
    const priceColor = t => `hsl(${Math.round(120 * (1 - t))}, 70%, 42%)`;

    function pinIcon(store, price, min, max) {
        const pin = document.createElement('div');
        pin.className = 'store-pin';
        if (price === undefined) {
            pin.style.background = chainColor(store.chain);
            pin.textContent = store.chain.slice(0, 2);
        } else {
            const t = max > min ? (price - min) / (max - min) : 0;
            pin.style.background = priceColor(t);
            pin.classList.add('store-pin-price');
            pin.textContent = `${Math.round(price)}₴`;
        }
        return L.divIcon({ html: pin, className: '', iconSize: null, iconAnchor: [18, 18], popupAnchor: [0, -16] });
    }

    function popupContent(store, price) {
        const box = document.createElement('div');
        const title = document.createElement('a');
        title.href = `/Stores/Details/${store.id}`;
        title.className = 'fw-semibold';
        title.textContent = store.name;
        const meta = document.createElement('div');
        meta.className = 'small text-muted';
        meta.textContent = [store.chain, store.address].filter(Boolean).join(' · ');
        box.append(title, meta);
        if (price !== undefined) {
            const p = document.createElement('div');
            p.className = 'mt-1';
            p.textContent = `Ціна: ${price.toFixed(2)} грн/кг`;
            box.append(p);
        }
        return box;
    }

    function visibleStores() {
        const chains = new Set([...document.querySelectorAll('.chain-filter:checked')].map(c => c.value));
        const onlyWithProduct = document.getElementById('onlyWithProduct').checked && prices.size > 0;
        return stores.filter(s => chains.has(s.chain) && (!onlyWithProduct || prices.has(s.id)));
    }

    function render() {
        cluster.clearLayers();
        const list = visibleStores();
        const values = list.map(s => prices.get(s.id)).filter(v => v !== undefined);
        const min = Math.min(...values), max = Math.max(...values);

        for (const s of list) {
            const price = prices.get(s.id);
            const marker = L.marker([s.latitude, s.longitude], { icon: pinIcon(s, price, min, max), title: s.name });
            marker.bindPopup(() => popupContent(s, price));
            marker.storeId = s.id;
            cluster.addLayer(marker);
        }

        const legend = document.getElementById('priceLegend');
        legend.classList.toggle('d-none', values.length === 0);
        if (values.length) {
            document.getElementById('priceMin').textContent = `${min.toFixed(2)} грн`;
            document.getElementById('priceMax').textContent = `${max.toFixed(2)} грн`;
        }
        status.textContent = `Показано ${list.length} з ${stores.length} магазинів` +
            (prices.size ? ` · продукт є в ${values.length}` : '');
    }

    async function selectProduct(productId) {
        prices = new Map();
        if (productId) {
            const response = await fetch(`/api/v1/products/${encodeURIComponent(productId)}?cityId=${encodeURIComponent(cityId)}`);
            if (response.ok) {
                const details = await response.json();
                details.prices.forEach(p => prices.set(p.storeId, p.pricePerKg));
            }
        }
        nearestInfo.textContent = '';
        render();
    }

    function distanceKm(lat1, lng1, lat2, lng2) {
        const R = 6371, rad = d => d * Math.PI / 180;
        const dLat = rad(lat2 - lat1), dLng = rad(lng2 - lng1);
        const a = Math.sin(dLat / 2) ** 2 + Math.cos(rad(lat1)) * Math.cos(rad(lat2)) * Math.sin(dLng / 2) ** 2;
        return 2 * R * Math.asin(Math.sqrt(a));
    }

    function showNearest(lat, lng) {
        if (userLayer) map.removeLayer(userLayer);
        const candidates = visibleStores().filter(s => prices.size === 0 || prices.has(s.id));
        if (candidates.length === 0) { nearestInfo.textContent = 'Немає магазинів за поточними фільтрами.'; return; }

        const withDistance = candidates.map(s => ({ s, km: distanceKm(lat, lng, s.latitude, s.longitude) }))
            .sort((a, b) => a.km - b.km);
        const nearest = withDistance[0];

        userLayer = L.layerGroup([
            L.circleMarker([lat, lng], { radius: 8, color: '#0d6efd', fillOpacity: .9 }).bindTooltip('Ви тут'),
            L.polyline([[lat, lng], [nearest.s.latitude, nearest.s.longitude]], { color: '#0d6efd', dashArray: '6 6' }),
        ]).addTo(map);
        map.fitBounds([[lat, lng], [nearest.s.latitude, nearest.s.longitude]], { padding: [60, 60], maxZoom: 15 });

        let text = `Найближчий: ${nearest.s.name} — ${nearest.km.toFixed(1)} км.`;
        if (prices.size) {
            const cheapest = withDistance.reduce((a, b) => (prices.get(b.s.id) < prices.get(a.s.id) ? b : a));
            text += ` Найдешевший: ${cheapest.s.name} (${prices.get(cheapest.s.id).toFixed(2)} грн/кг, ${cheapest.km.toFixed(1)} км).`;
        }
        if (nearest.km > 50) text += ' Ви, схоже, в іншому місті — змініть місто в шапці сайту.';
        nearestInfo.textContent = text;
    }

    document.getElementById('nearestBtn').addEventListener('click', () => {
        if (!navigator.geolocation) { nearestInfo.textContent = 'Браузер не підтримує визначення місцезнаходження.'; return; }
        nearestInfo.textContent = 'Визначаю місцезнаходження…';
        navigator.geolocation.getCurrentPosition(
            pos => showNearest(pos.coords.latitude, pos.coords.longitude),
            err => nearestInfo.textContent = err.code === err.PERMISSION_DENIED
                ? 'Доступ до геолокації заборонено. Дозвольте його в налаштуваннях браузера.'
                : 'Не вдалося визначити місцезнаходження.',
            { enableHighAccuracy: true, timeout: 10000 });
    });

    document.querySelectorAll('.chain-filter').forEach(c => c.addEventListener('change', render));
    document.getElementById('onlyWithProduct').addEventListener('change', render);
    $('#productFilter').on('change', e => selectProduct(e.target.value));

    window.dietflowMap = { map, showNearest, selectProduct };

    loadStores()
        .then(list => { stores = list; return selectProduct(document.getElementById('productFilter').value); })
        .catch(() => status.textContent = 'Не вдалося завантажити магазини.');
})();
