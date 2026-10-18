(function () {
    const MAX_SIDE = 1280;          
    const JPEG_QUALITY = 0.82;
    const MAX_INPUT_BYTES = 25 * 1024 * 1024;
    const ACCEPTED = ['image/jpeg', 'image/png', 'image/webp', 'image/heic', 'image/heif'];

    const formatSize = bytes => bytes >= 1024 * 1024
        ? `${(bytes / 1024 / 1024).toFixed(1)} МБ`
        : `${Math.max(1, Math.round(bytes / 1024))} КБ`;

    function init(input) {
        const wrapper = document.createElement('div');
        wrapper.className = 'photo-picker';
        wrapper.innerHTML = `
            <div class="photo-drop border border-2 rounded p-3 text-center" tabindex="0" role="button"
                 aria-label="Оберіть або перетягніть фото">
                <img class="photo-preview img-fluid rounded mb-2 d-none" alt="Прев'ю фото" />
                <div class="photo-hint text-muted small">Перетягніть фото сюди або оберіть файл</div>
                <div class="photo-info small mt-1"></div>
            </div>
            <div class="d-flex flex-wrap gap-2 mt-2">
                <button type="button" class="btn btn-sm btn-outline-primary photo-choose">🖼️ Обрати файл</button>
                <button type="button" class="btn btn-sm btn-outline-primary photo-camera">📷 Зробити фото</button>
                <button type="button" class="btn btn-sm btn-link text-danger photo-clear d-none">Прибрати</button>
            </div>
            <div class="photo-error text-danger small mt-1"></div>`;
        input.insertAdjacentElement('afterend', wrapper);
        input.classList.add('d-none'); 

        const drop = wrapper.querySelector('.photo-drop');
        const preview = wrapper.querySelector('.photo-preview');
        const hint = wrapper.querySelector('.photo-hint');
        const info = wrapper.querySelector('.photo-info');
        const error = wrapper.querySelector('.photo-error');
        const clearBtn = wrapper.querySelector('.photo-clear');
        const cameraBtn = wrapper.querySelector('.photo-camera');
        let previewUrl = null;

        if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
            cameraBtn.disabled = true;
            cameraBtn.title = 'Камера доступна лише через HTTPS';
        }

        function showError(message) { error.textContent = message || ''; }

        function setPreview(blob) {
            if (previewUrl) URL.revokeObjectURL(previewUrl);
            previewUrl = blob ? URL.createObjectURL(blob) : null;
            preview.src = previewUrl || '';
            preview.classList.toggle('d-none', !blob);
            hint.classList.toggle('d-none', !!blob);
            clearBtn.classList.toggle('d-none', !blob);
        }

        function putIntoInput(file) {
            const dt = new DataTransfer();
            if (file) dt.items.add(file);
            input.files = dt.files;
            input.dispatchEvent(new Event('change', { bubbles: true, cancelable: true }));
        }

        async function compress(source) {
            const bitmap = await createImageBitmap(source, { imageOrientation: 'from-image' });
            const scale = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height));
            const canvas = document.createElement('canvas');
            canvas.width = Math.round(bitmap.width * scale);
            canvas.height = Math.round(bitmap.height * scale);
            canvas.getContext('2d').drawImage(bitmap, 0, 0, canvas.width, canvas.height);
            bitmap.close();
            const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', JPEG_QUALITY));
            if (!blob) throw new Error('toBlob failed');
            return blob;
        }

        let busy = false;
        async function handle(fileOrBlob, originalName) {
            showError('');
            if (busy) return;
            if (fileOrBlob.type && !ACCEPTED.includes(fileOrBlob.type)) {
                showError('Підтримуються фото JPEG, PNG, WebP або HEIC.');
                return;
            }
            if (fileOrBlob.size > MAX_INPUT_BYTES) {
                showError(`Файл завеликий (${formatSize(fileOrBlob.size)}). Максимум — ${formatSize(MAX_INPUT_BYTES)}.`);
                return;
            }
            busy = true;
            info.textContent = 'Обробка фото…';
            try {
                const compressed = await compress(fileOrBlob);
                const baseName = (originalName || 'photo').replace(/\.[^.]+$/, '');
                const file = new File([compressed], `${baseName}.jpg`, { type: 'image/jpeg', lastModified: Date.now() });
                putIntoInput(file);
                setPreview(file);
                info.textContent = file.size < fileOrBlob.size
                    ? `${formatSize(fileOrBlob.size)} → ${formatSize(file.size)} · без геоданих`
                    : `${formatSize(file.size)} · вже оптимальний розмір · без геоданих`;
            } catch {
                if (fileOrBlob instanceof File) {
                    putIntoInput(fileOrBlob);
                    setPreview(null);
                    info.textContent = `${fileOrBlob.name} (${formatSize(fileOrBlob.size)}) — без стиснення`;
                } else {
                    info.textContent = '';
                    showError('Не вдалося обробити фото. Спробуйте інший файл.');
                }
            } finally {
                busy = false;
            }
        }
        
        const chooser = document.createElement('input');
        chooser.type = 'file';
        chooser.accept = ACCEPTED.join(',');
        chooser.hidden = true;
        wrapper.appendChild(chooser);
        wrapper.querySelector('.photo-choose').addEventListener('click', () => chooser.click());
        drop.addEventListener('click', () => chooser.click());
        drop.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); chooser.click(); } });
        chooser.addEventListener('change', () => {
            const file = chooser.files?.[0];
            if (file) handle(file, file.name);
            chooser.value = ''; 
        });

        ['dragenter', 'dragover'].forEach(type => drop.addEventListener(type, e => {
            e.preventDefault();
            drop.classList.add('border-primary', 'bg-light');
        }));
        ['dragleave', 'drop'].forEach(type => drop.addEventListener(type, e => {
            e.preventDefault();
            drop.classList.remove('border-primary', 'bg-light');
        }));
        drop.addEventListener('drop', e => {
            const file = [...(e.dataTransfer?.files || [])].find(f => f.type.startsWith('image/'));
            if (file) handle(file, file.name); else showError('Перетягніть файл зображення.');
        });

        clearBtn.addEventListener('click', () => {
            putIntoInput(null);
            setPreview(null);
            info.textContent = '';
            showError('');
        });

        cameraBtn.addEventListener('click', () => openCamera(blob => handle(blob, `camera-${Date.now()}`), showError));
    }

    let modalEl = null;
    function getModal() {
        if (modalEl) return modalEl;
        modalEl = document.createElement('div');
        modalEl.className = 'modal fade';
        modalEl.tabIndex = -1;
        modalEl.setAttribute('aria-label', 'Камера');
        modalEl.innerHTML = `
            <div class="modal-dialog modal-dialog-centered modal-lg">
                <div class="modal-content">
                    <div class="modal-header">
                        <h2 class="modal-title h5">📷 Фото страви</h2>
                        <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Закрити"></button>
                    </div>
                    <div class="modal-body text-center">
                        <video class="w-100 rounded bg-dark" autoplay playsinline muted></video>
                        <div class="camera-status small text-muted mt-2"></div>
                    </div>
                    <div class="modal-footer">
                        <button type="button" class="btn btn-outline-secondary camera-switch d-none">🔄 Інша камера</button>
                        <button type="button" class="btn btn-primary camera-shot" disabled>Зняти</button>
                    </div>
                </div>
            </div>`;
        document.body.appendChild(modalEl);
        return modalEl;
    }

    function openCamera(onShot, onError) {
        const el = getModal();
        const modal = bootstrap.Modal.getOrCreateInstance(el);
        const video = el.querySelector('video');
        const status = el.querySelector('.camera-status');
        const shotBtn = el.querySelector('.camera-shot');
        const switchBtn = el.querySelector('.camera-switch');
        let stream = null;
        let facingMode = 'environment'; 

        const stop = () => {
            stream?.getTracks().forEach(t => t.stop());
            stream = null;
            video.srcObject = null;
        };

        const start = async () => {
            stop();
            shotBtn.disabled = true;
            status.textContent = 'Запит доступу до камери…';
            try {
                stream = await navigator.mediaDevices.getUserMedia({
                    video: { facingMode: { ideal: facingMode }, width: { ideal: 1920 }, height: { ideal: 1080 } },
                    audio: false,
                });
                video.srcObject = stream;
                await video.play();
                status.textContent = '';
                shotBtn.disabled = false;
                const cams = (await navigator.mediaDevices.enumerateDevices()).filter(d => d.kind === 'videoinput');
                switchBtn.classList.toggle('d-none', cams.length < 2);
            } catch (err) {
                const messages = {
                    NotAllowedError: 'Доступ до камери заборонено. Дозвольте його в налаштуваннях браузера або оберіть файл.',
                    NotFoundError: 'Камеру не знайдено. Оберіть фото з файлу.',
                    NotReadableError: 'Камера зайнята іншою програмою.',
                };
                modal.hide();
                onError(messages[err?.name] || 'Не вдалося увімкнути камеру.');
            }
        };

        shotBtn.onclick = () => {
            const canvas = document.createElement('canvas');
            canvas.width = video.videoWidth;
            canvas.height = video.videoHeight;
            canvas.getContext('2d').drawImage(video, 0, 0);
            canvas.toBlob(blob => {
                modal.hide();
                if (blob) onShot(blob);
            }, 'image/jpeg', 0.95);
        };
        switchBtn.onclick = () => {
            facingMode = facingMode === 'environment' ? 'user' : 'environment';
            start();
        };

        el.addEventListener('hidden.bs.modal', stop, { once: true });
        el.addEventListener('shown.bs.modal', start, { once: true });
        modal.show();
    }

    document.querySelectorAll('input[type=file][data-photo-picker]').forEach(init);
})();
