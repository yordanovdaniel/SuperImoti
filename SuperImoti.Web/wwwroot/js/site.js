// SuperImoti — small progressive enhancements. Every feature also works without JavaScript.
(function () {
    'use strict';

    // Selects whose options are URLs (sort order, page size) navigate on change.
    document.querySelectorAll('.js-nav-select').forEach(function (select) {
        select.addEventListener('change', function () {
            window.location.href = select.value;
        });
    });

    // GET search forms: leave out empty fields so the address stays short (?deal=Sale instead of ?q=&deal=Sale&type=…).
    document.querySelectorAll('form.js-clean-get').forEach(function (form) {
        form.addEventListener('submit', function () {
            form.querySelectorAll('input[name], select[name]').forEach(function (field) {
                var isChoice = field.type === 'checkbox' || field.type === 'radio';
                if ((!isChoice && field.value === '') || (field.type === 'radio' && field.checked && field.value === '')) {
                    field.disabled = true;
                }
            });
        });
    });

    // Re-enable those fields when the user comes back with the browser's Back button.
    window.addEventListener('pageshow', function () {
        document.querySelectorAll('form.js-clean-get [disabled]').forEach(function (field) {
            field.disabled = false;
        });
    });

    // Ask before destructive actions (e.g. deleting a comment).
    document.querySelectorAll('form.js-confirm').forEach(function (form) {
        form.addEventListener('submit', function (event) {
            if (!window.confirm(form.dataset.confirm || 'Сигурни ли сте?')) {
                event.preventDefault();
            }
        });
    });

    // Favourite buttons toggle without reloading the page; on any error the normal form post is used.
    document.querySelectorAll('form.js-favorite-form').forEach(function (form) {
        form.addEventListener('submit', async function (event) {
            event.preventDefault();
            var button = form.querySelector('button');
            button.disabled = true;
            try {
                var response = await fetch(form.action, {
                    method: 'POST',
                    body: new FormData(form),
                    headers: { Accept: 'application/json' }
                });
                if (!response.ok) {
                    throw new Error('HTTP ' + response.status);
                }
                var data = await response.json();
                showFavorite(button, data.isFavorite);
            } catch (error) {
                form.submit();
            } finally {
                button.disabled = false;
            }
        });
    });

    function showFavorite(button, isFavorite) {
        var label = isFavorite ? 'Премахни от любими' : 'Добави в любими';
        button.classList.toggle('is-favorite', isFavorite);
        button.title = label;
        button.setAttribute('aria-label', label);
        button.setAttribute('aria-pressed', isFavorite ? 'true' : 'false');
        button.querySelector('i').className = 'bi ' + (isFavorite ? 'bi-heart-fill' : 'bi-heart');
        var text = button.querySelector('.js-favorite-label');
        if (text) {
            text.textContent = isFavorite ? 'В любими' : 'Запази';
        }
    }

    // Listing gallery: photo counter and highlighted thumbnail.
    var gallery = document.getElementById('gallery');
    if (gallery) {
        gallery.addEventListener('slid.bs.carousel', function (event) {
            var counter = gallery.querySelector('.js-gallery-index');
            if (counter) {
                counter.textContent = event.to + 1;
            }
            document.querySelectorAll('.gallery-thumb').forEach(function (thumb, index) {
                thumb.classList.toggle('active', index === event.to);
                if (index === event.to) {
                    thumb.scrollIntoView({ block: 'nearest', inline: 'center', behavior: 'smooth' });
                }
            });
        });
    }

    // "Price on request" disables the price field (a disabled field is neither validated nor sent).
    document.querySelectorAll('.js-price-on-request').forEach(function (checkbox) {
        var price = document.querySelector('.js-price-input');
        function sync() {
            if (price) {
                price.disabled = checkbox.checked;
            }
        }
        checkbox.addEventListener('change', sync);
        sync();
    });

    // Instant filter for tables (districts page).
    document.querySelectorAll('.js-table-filter').forEach(function (input) {
        var table = document.querySelector(input.dataset.target);
        if (!table) {
            return;
        }
        input.addEventListener('input', function () {
            var term = input.value.trim().toLocaleLowerCase('bg');
            table.querySelectorAll('tbody tr').forEach(function (row) {
                row.hidden = term !== '' && (row.dataset.filterText || '').indexOf(term) === -1;
            });
        });
    });
})();
