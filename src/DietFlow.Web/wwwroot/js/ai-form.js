document.querySelectorAll('form.ai-form').forEach(form => {
    form.addEventListener('submit', e => {
        if (window.jQuery && jQuery(form).valid && !jQuery(form).valid()) return;

        const clicked = e.submitter;
        form.querySelectorAll('button[type=submit]').forEach(b => b.disabled = true);

        if (clicked?.name) {
            const hidden = document.createElement('input');
            hidden.type = 'hidden';
            hidden.name = clicked.name;
            hidden.value = clicked.value;
            form.appendChild(hidden);
        }

        const spinner = clicked?.querySelector('.spinner-border');
        if (spinner) {
            spinner.classList.remove('d-none');
            const label = clicked.querySelector('.label');
            if (label) label.textContent = 'Аналізую фото…';
        }
    });
});
