(function () {
    const container = document.getElementById('ingredients');
    const template = document.getElementById('ingredientTemplate');
    const addButton = document.getElementById('addIngredient');
    if (!container || !template || !addButton) return;

    function renumber() {
        container.querySelectorAll('.ingredient-row').forEach((row, index) => {
            row.querySelectorAll('[name^="Ingredients["]').forEach(el => {
                el.name = el.name.replace(/Ingredients\[\d+\]/, `Ingredients[${index}]`);
            });
        });
    }

    addButton.addEventListener('click', () => {
        const index = container.querySelectorAll('.ingredient-row').length;
        const html = template.innerHTML.replaceAll('__i__', index);
        container.insertAdjacentHTML('beforeend', html);
        window.initProductAutocomplete?.(container.lastElementChild);
        $(container.lastElementChild).find('select').select2?.('open');
    });

    container.addEventListener('click', e => {
        const button = e.target.closest('.remove-ingredient');
        if (!button) return;
        const rows = container.querySelectorAll('.ingredient-row');
        if (rows.length === 1) {
            $(rows[0].querySelector('select')).val(null).trigger('change');
            rows[0].querySelector('input').value = '';
            return;
        }
        button.closest('.ingredient-row').remove();
        renumber();
    });
})();
