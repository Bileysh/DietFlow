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

    const knownColors = { 'АТБ': '#d6352b', 'Сільпо': '#f28c28', 'Novus': '#2e9e4f', 'Фора': '#7b3fb0', 'Varus': '#1f6fd1' };
    const chainColor = chain => knownColors[chain] ||
        `hsl(${[...chain].reduce((h, ch) => (h * 31 + ch.charCodeAt(0)) % 360, 7)}, 55%, 45%)`;

    const $ = id => document.getElementById(id);
    const status = $('mapStatus'), nearestInfo = $('nearestInfo'), chainBox = $('chainFilters');
    let mode = 'city';
    let stores = [];
    let prices = new Map();
    const hiddenChains = new Set();
    let userPos = null;
    let userLayer = null;

    async function fetchJson(url) {
        const response = await fetch(url, { headers: { Accept: 'application/json' } });
        if (!response.ok) throw new Error(`${response.status} ${url}`);
        return response.json();
    }

    async function loadCityStores() {
        let url = `/api/v1/stores?cityId=${encodeURIComponent(cityId)}&limit=100`;
        const all = [];
        while (url) {
            const page = await fetchJson(url);
            all.push(...page.items);
            url = page.nextLink;
        }
        return all;
    }

    const loadNearbyStores = () =>
        fetchJson(`/api/v1/stores/nearby?lat=${userPos.lat}&lng=${userPos.lng}&radiusKm=${$('radiusKm').value}`);

    function buildChainFilters() {
        const counts = new Map();
        stores.forEach(s => counts.set(s.chain, (counts.get(s.chain) || 0) + 1));
        chainBox.replaceChildren();
        [...counts.entries()].sort((a, b) => b[1] - a[1]).forEach(([chain, n]) => {
            const id = `chain-${[...chain].map(c => c.charCodeAt(0)).join('-')}`;
            const wrap = document.createElement('div');
            wrap.className = 'form-check';
            const input = document.createElement('input');
            Object.assign(input, { type: 'checkbox', className: 'form-check-input', id, checked: !hiddenChains.has(chain) });
            input.addEventListener('change', () => { input.checked ? hiddenChains.delete(chain) : hiddenChains.add(chain); render(); });
            const label = document.createElement('label');
            label.className = 'form-check-label d-flex align-items-center gap-2';
            label.htmlFor = id;
            const dot = document.createElement('span');
            dot.className = 'chain-dot';
            dot.style.background = chainColor(chain);
            label.append(dot, `${chain} (${n})`);
            wrap.append(input, label);
            chainBox.append(wrap);
        });
    }

    const priceColor = t => `hsl(${Math.round(120 * (1 - t))}, 70%, 42%)`;

    function pinIcon(store, entry, min, max) {
        const pin = document.createElement('div');
        pin.className = 'store-pin';
        if (!entry) {
            pin.style.background = chainColor(store.chain);
            pin.textContent = store.chain.slice(0, 2);
        } else {
            pin.style.background = priceColor(max > min ? (entry.price - min) / (max - min) : 0);
            pin.classList.add('store-pin-price');
            if (entry.source === 'Manual') pin.classList.add('store-pin-manual');
            pin.textContent = `${Math.round(entry.price)}₴`;
        }
        return L.divIcon({ html: pin, className: '', iconSize: null, iconAnchor: [18, 18], popupAnchor: [0, -16] });
    }

    function popupContent(store, entry) {
        const box = document.createElement('div');
        const title = document.createElement('a');
        title.href = `/Stores/Details/${store.id}`;
        title.className = 'fw-semibold';
        title.textContent = store.name;
        const meta = document.createElement('div');
        meta.className = 'small text-muted';
        meta.textContent = [store.chain, store.cityName, store.distanceKm !== undefined ? `${store.distanceKm} км від вас` : null]
            .filter(Boolean).join(' · ');
        box.append(title, meta);
        if (entry) {
            const p = document.createElement('div');
            p.className = 'mt-1';
            p.textContent = `Ціна: ${entry.price.toFixed(2)} грн/кг ` + ({ Manual: '✓ перевірено адміністратором', Stat: '📊 оцінка за Держстатом' }[entry.source] ?? '(демо)');
            box.append(p);
        }
        return box;
    }

    function visibleStores() {
        const onlyWithProduct = $('onlyWithProduct').checked && prices.size > 0;
        return stores.filter(s => !hiddenChains.has(s.chain) && (!onlyWithProduct || prices.has(s.id)));
    }

    function render() {
        cluster.clearLayers();
        const list = visibleStores();
        const values = list.map(s => prices.get(s.id)?.price).filter(v => v !== undefined);
        const min = Math.min(...values), max = Math.max(...values);

        for (const s of list) {
            const entry = prices.get(s.id);
            const marker = L.marker([s.latitude, s.longitude], { icon: pinIcon(s, entry, min, max), title: s.name });
            marker.bindPopup(() => popupContent(s, entry));
            cluster.addLayer(marker);
        }

        $('priceLegend').classList.toggle('d-none', values.length === 0);
        if (values.length) {
            $('priceMin').textContent = `${min.toFixed(2)} грн`;
            $('priceMax').textContent = `${max.toFixed(2)} грн`;
        }
        const where = mode === 'nearby' ? `у радіусі ${$('radiusKm').value} км` : 'у місті';
        status.textContent = `Показано ${list.length} з ${stores.length} магазинів ${where}` +
            (prices.size ? ` · продукт є в ${values.length}` : '');
    }

    async function loadPrices() {
        prices = new Map();
        const productId = $('productFilter').value;
        if (productId) {
            const scope = mode === 'city' ? `?cityId=${encodeURIComponent(cityId)}` : '';
            const details = await fetchJson(`/api/v1/products/${encodeURIComponent(productId)}${scope}`);
            details.prices.forEach(p => prices.set(p.storeId, { price: p.pricePerKg, source: p.source }));
        }
    }

    async function reload(fit) {
        status.textContent = 'Завантаження…';
        try {
            stores = mode === 'city' ? await loadCityStores() : await loadNearbyStores();
            await loadPrices();
        } catch {
            status.textContent = 'Не вдалося завантажити магазини.';
            return;
        }
        buildChainFilters();
        render();
        if (fit && stores.length) map.fitBounds(L.latLngBounds(stores.map(s => [s.latitude, s.longitude])), { padding: [40, 40], maxZoom: 15 });
    }

    function distanceKm(lat1, lng1, lat2, lng2) {
        const R = 6371, rad = d => d * Math.PI / 180;
        const dLat = rad(lat2 - lat1), dLng = rad(lng2 - lng1);
        const a = Math.sin(dLat / 2) ** 2 + Math.cos(rad(lat1)) * Math.cos(rad(lat2)) * Math.sin(dLng / 2) ** 2;
        return 2 * R * Math.asin(Math.sqrt(a));
    }

    function drawUser(extra = []) {
        if (userLayer) map.removeLayer(userLayer);
        const layers = [L.circleMarker([userPos.lat, userPos.lng], { radius: 8, color: '#0d6efd', fillOpacity: .9 }).bindTooltip('Ви тут')];
        if (mode === 'nearby') layers.push(L.circle([userPos.lat, userPos.lng], { radius: $('radiusKm').value * 1000, color: '#0d6efd', weight: 1, fillOpacity: .04 }));
        userLayer = L.layerGroup([...layers, ...extra]).addTo(map);
    }

    function locate() {
        return new Promise((resolve, reject) => {
            if (userPos) return resolve(userPos);
            if (!navigator.geolocation) return reject(new Error('unsupported'));
            navigator.geolocation.getCurrentPosition(
                pos => resolve(userPos = { lat: pos.coords.latitude, lng: pos.coords.longitude }),
                err => reject(err), { enableHighAccuracy: true, timeout: 10000 });
        });
    }

    function geoError(err) {
        return err?.code === 1 ? 'Доступ до геолокації заборонено. Дозвольте його в налаштуваннях браузера.'
            : err?.message === 'unsupported' ? 'Браузер не підтримує визначення місцезнаходження.'
            : 'Не вдалося визначити місцезнаходження.';
    }

    function showNearest() {
        const candidates = visibleStores().filter(s => prices.size === 0 || prices.has(s.id));
        if (candidates.length === 0) { nearestInfo.textContent = 'Немає магазинів за поточними фільтрами.'; return; }
        const ranked = candidates.map(s => ({ s, km: distanceKm(userPos.lat, userPos.lng, s.latitude, s.longitude) })).sort((a, b) => a.km - b.km);
        const nearest = ranked[0];

        drawUser([L.polyline([[userPos.lat, userPos.lng], [nearest.s.latitude, nearest.s.longitude]], { color: '#0d6efd', dashArray: '6 6' })]);
        map.fitBounds([[userPos.lat, userPos.lng], [nearest.s.latitude, nearest.s.longitude]], { padding: [60, 60], maxZoom: 16 });

        let text = `Найближчий: ${nearest.s.name} — ${nearest.km.toFixed(1)} км.`;
        if (prices.size) {
            const cheapest = ranked.reduce((a, b) => (prices.get(b.s.id).price < prices.get(a.s.id).price ? b : a));
            text += ` Найдешевший: ${cheapest.s.name} (${prices.get(cheapest.s.id).price.toFixed(2)} грн/кг, ${cheapest.km.toFixed(1)} км).`;
        }
        if (mode === 'city' && nearest.km > 50) text += ' Ви, схоже, в іншому місті — увімкніть «Поруч зі мною» або змініть місто.';
        nearestInfo.textContent = text;
    }

    async function setMode(newMode) {
        mode = newMode;
        $(mode === 'nearby' ? 'modeNearby' : 'modeCity').checked = true;
        $('radiusBox').classList.toggle('d-none', mode !== 'nearby');
        nearestInfo.textContent = '';
        if (mode === 'nearby') {
            try { await locate(); } catch (err) { nearestInfo.textContent = geoError(err); $('modeCity').checked = true; mode = 'city'; $('radiusBox').classList.add('d-none'); return; }
            await reload(true);
            drawUser();
        } else {
            if (userLayer) { map.removeLayer(userLayer); userLayer = null; }
            await reload(true);
        }
    }

    $('modeCity').addEventListener('change', () => setMode('city'));
    $('modeNearby').addEventListener('change', () => setMode('nearby'));
    $('radiusKm').addEventListener('change', () => mode === 'nearby' && reload(true).then(() => drawUser()));
    $('onlyWithProduct').addEventListener('change', render);
    window.jQuery?.('#productFilter').on('change', async () => { nearestInfo.textContent = ''; await loadPrices(); render(); });
    $('nearestBtn').addEventListener('click', async () => {
        nearestInfo.textContent = 'Визначаю місцезнаходження…';
        try { await locate(); showNearest(); } catch (err) { nearestInfo.textContent = geoError(err); }
    });

    window.dietflowMap = { map, setUserPosition: (lat, lng) => { userPos = { lat, lng }; }, setMode, showNearest };

    reload(true);
})();
