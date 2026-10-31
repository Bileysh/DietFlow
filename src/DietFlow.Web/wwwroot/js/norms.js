(function () {
    const form = document.getElementById('normsForm');
    if (!form) return;
    const out = document.getElementById('normsResult');
    const goalInput = document.getElementById('dailyGoal');

    form.addEventListener('submit', async e => {
        e.preventDefault();
        const data = Object.fromEntries(new FormData(form));
        out.textContent = 'Рахую…';
        try {
            const res = await fetch(form.dataset.url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ...data, age: Number(data.age), weightKg: Number(data.weightKg), heightCm: Number(data.heightCm) }),
            });
            const json = await res.json();
            if (!res.ok) throw new Error(json.detail || 'Помилка розрахунку');
            out.textContent = `Норма ${json.targetKcal} ккал (BMR ${json.bmr}, TDEE ${json.tdee}) · Б ${json.proteinG} г · Ж ${json.fatG} г · ` +
                `В ${json.carbsG} г · вода ${json.waterMl} мл · ІМТ ${json.bmi} (${json.bmiCategory}). Підставлено в поле — натисніть «Зберегти».`;
            goalInput.value = json.targetKcal;
        } catch (err) {
            out.textContent = '⚠ ' + (err.message || 'Сервіс розрахунку недоступний.');
        }
    });
})();
