(async function () {
    const canvas = document.getElementById('weekChart');
    if (!canvas || !window.Chart) return;

    const response = await fetch('/Meals/WeekStats', { headers: { Accept: 'application/json' } });
    if (!response.ok) return;
    const { goal, days } = await response.json();

    new Chart(canvas, {
        data: {
            labels: days.map(d => d.label),
            datasets: [
                {
                    type: 'bar', label: 'Ккал', data: days.map(d => d.kcal), yAxisID: 'kcal',
                    backgroundColor: days.map(d => d.kcal > goal * 1.1 ? '#dc3545' : d.kcal >= goal * 0.9 ? '#ffc107' : '#198754'),
                    borderRadius: 4,
                },
                {
                    type: 'line', label: `Норма (${goal} ккал)`, data: days.map(() => goal), yAxisID: 'kcal',
                    borderColor: '#6c757d', borderDash: [6, 4], pointRadius: 0, borderWidth: 1.5,
                },
                {
                    type: 'line', label: 'Оцінка корисності (1–10)', data: days.map(d => d.avgScore), yAxisID: 'score',
                    borderColor: '#0d6efd', backgroundColor: '#0d6efd', tension: .3, spanGaps: true,
                },
            ],
        },
        options: {
            interaction: { mode: 'index', intersect: false },
            plugins: { legend: { position: 'bottom' } },
            scales: {
                kcal: { position: 'left', beginAtZero: true, title: { display: true, text: 'ккал' } },
                score: { position: 'right', min: 0, max: 10, grid: { drawOnChartArea: false }, title: { display: true, text: 'оцінка' } },
            },
        },
    });
})();
