// Filters a <select data-locations> by the plant chosen in the select referenced by data-plant-source.
// Location <option>s carry data-plant="<plantId>"; options for other plants are hidden/disabled.
document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('select[data-plant-source]').forEach(loc => {
        const plant = document.querySelector(loc.dataset.plantSource);
        if (!plant) return;
        const apply = () => {
            let selectedStillValid = false;
            loc.querySelectorAll('option[data-plant]').forEach(o => {
                const ok = o.dataset.plant === plant.value;
                o.hidden = !ok; o.disabled = !ok;
                if (ok && o.selected) selectedStillValid = true;
            });
            if (!selectedStillValid) loc.value = '';
        };
        plant.addEventListener('change', apply);
        apply();
    });
});
