(function () {
    const nav = document.getElementById('recipeNav');
    if (nav && window.bootstrap) {
        new bootstrap.ScrollSpy(document.body, { target: '#recipeNav', rootMargin: '0px 0px -60%' });
    }

    const canvas = document.getElementById('macroChart');
    if (!canvas || !window.Chart) return;
    const grams = ['protein', 'fat', 'carbs'].map(k => parseFloat(canvas.dataset[k]) || 0);
    const kcal = [grams[0] * 4, grams[1] * 9, grams[2] * 4];
    const total = kcal.reduce((a, b) => a + b, 0) || 1;

    new Chart(canvas, {
        type: 'doughnut',
        data: {
            labels: ['Білки', 'Жири', 'Вуглеводи'],
            datasets: [{ data: kcal.map(v => Math.round(v)), backgroundColor: ['#0d6efd', '#dc3545', '#198754'], borderWidth: 2 }],
        },
        options: {
            plugins: {
                legend: { position: 'bottom' },
                tooltip: { callbacks: { label: c => ` ${c.label}: ${c.raw} ккал (${Math.round(c.raw / total * 100)}%)` } },
            },
            cutout: '55%',
        },
    });
})();
