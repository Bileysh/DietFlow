(function ($) {
    const messages = {
        inputTooShort: args => `Введіть ще ${args.minimum - args.input.length} симв.`,
        searching: () => 'Пошук…',
        noResults: () => 'Нічого не знайдено',
        loadingMore: () => 'Завантаження…',
        errorLoading: () => 'Не вдалося завантажити результати',
    };
    
    function renderItem(item) {
        if (item.loading || item.kcal === undefined) return item.text;
        return $('<div class="d-flex justify-content-between gap-2">')
            .append($('<span>').text(item.text))
            .append($('<small class="text-muted text-nowrap">').text(`${Math.round(item.kcal)} ккал${item.category ? ' · ' + item.category : ''}`));
    }

    function init(select) {
        const $select = $(select);
        if ($select.hasClass('select2-hidden-accessible')) return; 
        $select.select2({
            theme: 'bootstrap-5',
            width: '100%',
            placeholder: 'Почніть вводити назву продукту…',
            minimumInputLength: 3,
            language: messages,
            templateResult: renderItem,
            ajax: {
                url: '/api/v1/products/search',
                dataType: 'json',
                delay: 300,                                  
                data: params => ({ term: params.term, page: params.page || 1 }),
                processResults: data => ({ results: data.results, pagination: data.pagination }),
                cache: true,
            },
        });
    }

    window.initProductAutocomplete = root =>
        $(root || document).find('select[data-product-autocomplete]').addBack('select[data-product-autocomplete]').each((_, el) => init(el));

    $(() => window.initProductAutocomplete(document));
})(jQuery);
