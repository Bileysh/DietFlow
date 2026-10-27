
(function () {
    const log = document.getElementById('chatLog');
    if (!log) return;

    const form = document.getElementById('chatForm');
    const input = document.getElementById('chatInput');
    const sendBtn = document.getElementById('chatSend');
    const empty = document.getElementById('chatEmpty');
    const photoBox = document.getElementById('chatPhoto');
    const photoInput = document.getElementById('chatPhotoInput');
    const useLocation = document.getElementById('chatUseLocation');
    const quotaUsed = document.getElementById('quotaUsed');

    const STORAGE_KEY = 'dietflow.agent.history';
    const MAX_HISTORY = 20;
    let history = load();
    let busy = false;

    function load() {
        try { return JSON.parse(localStorage.getItem(STORAGE_KEY)) || []; } catch { return []; }
    }
    function save() {
        history = history.slice(-MAX_HISTORY);
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(history)); } catch {}
    }

    function bubble(role) {
        empty?.remove();
        const wrap = document.createElement('div');
        wrap.className = `chat-msg chat-${role}`;
        const inner = document.createElement('div');
        inner.className = 'chat-bubble';
        wrap.appendChild(inner);
        log.appendChild(wrap);
        return inner;
    }

    function scrollDown() { log.scrollTop = log.scrollHeight; }

    function renderMarkdown(target, text) {
        target.replaceChildren();
        let list = null;
        for (const raw of text.split('\n')) {
            const line = raw.trimEnd();
            const item = line.match(/^\s*(?:[-*•]|\d+[.)])\s+(.*)$/);
            if (item) {
                if (!list) { list = document.createElement('ul'); target.appendChild(list); }
                const li = document.createElement('li');
                inline(li, item[1]);
                list.appendChild(li);
                continue;
            }
            list = null;
            if (!line.trim()) continue;
            const p = document.createElement('p');
            inline(p, line.replace(/^#{1,6}\s+/, ''));
            if (/^#{1,6}\s/.test(line)) p.classList.add('fw-semibold');
            target.appendChild(p);
        }
    }

    function inline(parent, text) {
        const re = /\*\*(.+?)\*\*|\[([^\]]+)\]\(([^)\s]+)\)/g;
        let last = 0, m;
        while ((m = re.exec(text))) {
            parent.appendChild(document.createTextNode(text.slice(last, m.index)));
            if (m[1] !== undefined) {
                const b = document.createElement('strong');
                inline(b, m[1]);
                parent.appendChild(b);
            } else if (/^(\/(?!\/)|https:\/\/)/.test(m[3])) {
                const a = document.createElement('a');
                a.textContent = m[2];
                a.href = m[3];
                if (m[3].startsWith('https://')) { a.target = '_blank'; a.rel = 'noopener'; }
                parent.appendChild(a);
            } else {
                parent.appendChild(document.createTextNode(m[2]));
            }
            last = re.lastIndex;
        }
        parent.appendChild(document.createTextNode(text.slice(last)));
    }

    function addUser(text, imageUrl) {
        const b = bubble('user');
        if (imageUrl) {
            const img = document.createElement('img');
            img.src = imageUrl;
            img.alt = 'Фото страви';
            img.className = 'chat-photo rounded mb-1';
            b.appendChild(img);
        }
        if (text) {
            const p = document.createElement('div');
            p.textContent = text;
            b.appendChild(p);
        }
        scrollDown();
    }

    function addAssistant(text) {
        const b = bubble('assistant');
        const steps = document.createElement('ul');
        steps.className = 'chat-steps list-unstyled small text-muted mb-1';
        const body = document.createElement('div');
        body.className = 'chat-text';
        b.append(steps, body);
        if (text) renderMarkdown(body, text);
        return { bubble: b, steps, body };
    }

    for (const h of history) h.role === 'user' ? addUser(h.text) : addAssistant(h.text);
    scrollDown();

    async function readPhoto() {
        const file = photoInput?.files?.[0];
        if (!file) return null;
        const dataUrl = await new Promise((resolve, reject) => {
            const r = new FileReader();
            r.onload = () => resolve(r.result);
            r.onerror = reject;
            r.readAsDataURL(file);
        });
        return { base64: String(dataUrl).split(',')[1], url: URL.createObjectURL(file) };
    }

    let position = null;
    function getPosition() {
        if (!useLocation.checked) return Promise.resolve(null);
        if (position) return Promise.resolve(position);
        return new Promise(resolve => {
            if (!navigator.geolocation) return resolve(null);
            navigator.geolocation.getCurrentPosition(
                p => resolve(position = { lat: p.coords.latitude, lng: p.coords.longitude }),
                () => { useLocation.checked = false; resolve(null); },
                { timeout: 10000, maximumAge: 300000 });
        });
    }

    async function send(text) {
        if (busy) return;
        const photo = await readPhoto();
        if (!text && !photo) return;

        busy = true;
        sendBtn.disabled = true;
        input.value = '';
        addUser(text, photo?.url);
        photoBox.querySelector('.photo-clear')?.click();
        photoBox.classList.add('d-none');

        const view = addAssistant('');
        const typing = document.createElement('div');
        typing.className = 'chat-typing text-muted small';
        typing.textContent = 'Думаю…';
        view.body.appendChild(typing);
        scrollDown();

        const pos = await getPosition();
        const requestHistory = history.slice(-MAX_HISTORY);
        history.push({ role: 'user', text: text || '[фото страви]' });
        save();

        let answer = '';
        try {
            const res = await fetch('/api/v1/agent/chat', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'Accept': 'text/event-stream' },
                body: JSON.stringify({ message: text, history: requestHistory, imageBase64: photo?.base64, lat: pos?.lat, lng: pos?.lng }),
            });
            if (res.status === 401) { location.href = '/Account/Login?returnUrl=/Assistant'; return; }
            if (!res.ok) {
                const problem = await res.json().catch(() => null);
                throw new Error(problem?.detail || Object.values(problem?.errors || {})[0]?.[0] || 'Не вдалося надіслати повідомлення.');
            }
            if (quotaUsed) quotaUsed.textContent = String(Number(quotaUsed.textContent) + 1);

            const reader = res.body.getReader();
            const decoder = new TextDecoder();
            let buffer = '';
            for (;;) {
                const { value, done } = await reader.read();
                if (done) break;
                buffer += decoder.decode(value, { stream: true });
                let sep;
                while ((sep = buffer.indexOf('\n\n')) >= 0) {
                    const block = buffer.slice(0, sep);
                    buffer = buffer.slice(sep + 2);
                    const data = block.split('\n').filter(l => l.startsWith('data:')).map(l => l.slice(5).trimStart()).join('\n');
                    if (!data) continue;
                    const e = JSON.parse(data);
                    if (e.type === 'step') {
                        const li = document.createElement('li');
                        li.dataset.tool = e.tool;
                        li.textContent = e.text + '…';
                        view.steps.appendChild(li);
                    } else if (e.type === 'result') {
                        const li = [...view.steps.querySelectorAll('li')].reverse().find(x => x.dataset.tool === e.tool && !x.dataset.done);
                        if (li) {
                            li.dataset.done = '1';
                            li.textContent = li.textContent.replace(/…$/, '') + (e.ok ? ' → ' : ' ⚠ ') + e.text;
                            if (!e.ok) li.classList.add('text-danger');
                        }
                    } else if (e.type === 'delta') {
                        answer += e.text;
                        renderMarkdown(view.body, answer);
                    } else if (e.type === 'error') {
                        throw new Error(e.text);
                    }
                    scrollDown();
                }
            }
            if (answer) {
                history.push({ role: 'assistant', text: answer });
                save();
            }
        } catch (err) {
            typing.remove();
            const p = document.createElement('div');
            p.className = 'text-danger small';
            p.textContent = '⚠ ' + (err.message || 'Помилка з\'єднання.');
            view.body.appendChild(p);
        } finally {
            typing.remove();
            if (!answer && !view.body.childElementCount) view.body.textContent = '—';
            busy = false;
            sendBtn.disabled = false;
            input.focus();
            scrollDown();
        }
    }

    form.addEventListener('submit', e => { e.preventDefault(); send(input.value.trim()); });
    input.addEventListener('keydown', e => {
        if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); form.requestSubmit(); }
    });
    document.querySelectorAll('.chat-suggestion').forEach(b => b.addEventListener('click', () => send(b.textContent.trim())));
    document.getElementById('chatPhotoToggle').addEventListener('click', () => photoBox.classList.toggle('d-none'));
    document.getElementById('chatClear').addEventListener('click', () => {
        history = [];
        save();
        log.replaceChildren();
        location.reload();
    });
})();
