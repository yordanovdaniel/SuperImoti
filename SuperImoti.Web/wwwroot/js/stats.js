// Statistics page: a small Vue 3 app that loads district statistics as JSON and sorts/filters them in the browser.
(function () {
    'use strict';

    var config = window.statsConfig;
    var numberFormat = new Intl.NumberFormat('bg-BG', { maximumFractionDigits: 0 });
    var { createApp, ref, computed, watch, onMounted } = Vue;

    createApp({
        setup() {
            const deal = ref('Sale');
            const category = ref('Apartment');
            const type = ref('');
            const search = ref('');
            const rows = ref([]);
            const loading = ref(false);
            const error = ref('');
            const sortKey = ref('avgPricePerSqm');
            const sortDescending = ref(true);

            const typesInCategory = computed(() =>
                config.propertyTypes.filter(t => !category.value || t.category === category.value));

            async function load() {
                loading.value = true;
                error.value = '';
                const params = new URLSearchParams({ deal: deal.value });
                if (type.value) {
                    params.set('type', type.value);
                } else if (category.value) {
                    params.set('category', category.value);
                }

                try {
                    const response = await fetch(`${config.dataUrl}?${params}`, { headers: { Accept: 'application/json' } });
                    if (!response.ok) {
                        throw new Error(`HTTP ${response.status}`);
                    }
                    rows.value = await response.json();
                } catch (e) {
                    rows.value = [];
                    error.value = 'Статистиката не можа да бъде заредена. Опитайте отново.';
                } finally {
                    loading.value = false;
                }
            }

            const filtered = computed(() => {
                const term = search.value.toLocaleLowerCase('bg');
                const direction = sortDescending.value ? -1 : 1;
                const key = sortKey.value;
                return rows.value
                    .filter(r => !term || r.district.toLocaleLowerCase('bg').includes(term))
                    .slice()
                    .sort((a, b) => typeof a[key] === 'string'
                        ? a[key].localeCompare(b[key], 'bg') * direction
                        : ((a[key] ?? 0) - (b[key] ?? 0)) * direction);
            });

            const totalCount = computed(() => filtered.value.reduce((sum, r) => sum + r.count, 0));

            // Weighted by the number of listings, so big districts count more.
            const averagePerSqm = computed(() => totalCount.value === 0
                ? null
                : filtered.value.reduce((sum, r) => sum + r.avgPricePerSqm * r.count, 0) / totalCount.value);

            const maxPerSqm = computed(() => Math.max(1, ...filtered.value.map(r => r.avgPricePerSqm || 0)));

            function sortBy(key) {
                if (sortKey.value === key) {
                    sortDescending.value = !sortDescending.value;
                } else {
                    sortKey.value = key;
                    sortDescending.value = key !== 'district';
                }
            }

            const arrow = key => sortKey.value === key ? (sortDescending.value ? '▼' : '▲') : '';
            const fmt = value => value == null ? '—' : numberFormat.format(value);
            const barWidth = row => `${Math.round((row.avgPricePerSqm || 0) / maxPerSqm.value * 100)}%`;

            function listingsLink(row) {
                const params = new URLSearchParams({ deal: deal.value, district: row.districtId });
                if (type.value) {
                    params.set('type', type.value);
                } else if (category.value) {
                    params.set('category', category.value);
                }
                return `${config.listingsUrl}?${params}`;
            }

            watch([deal, category, type], load);
            onMounted(load);

            return {
                deal, category, type, search, loading, error,
                categories: config.categories, typesInCategory, filtered, totalCount, averagePerSqm,
                sortBy, arrow, fmt, barWidth, listingsLink
            };
        }
    }).mount('#stats-app');
})();
