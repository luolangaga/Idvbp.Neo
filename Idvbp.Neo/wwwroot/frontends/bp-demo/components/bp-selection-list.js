(function () {
    window.IdvbpLayoutRuntime.register("bp-selection-list", {
        render(element, props) {
            const items = Array.isArray(props.items) ? props.items : [];
            const field = props.field || "name";
            const content = items.length === 0
                ? `<p>${escapeHtml(props.emptyText || "No data")}</p>`
                : `<ol>${items.map((item, index) => `
                    <li>
                        <span>${escapeHtml(item.order ?? index + 1)}</span>
                        <strong>${escapeHtml(item[field] || item.name || item.id || "-")}</strong>
                    </li>
                `).join("")}</ol>`;

            element.innerHTML = `
                <section class="bp-selection-list">
                    <h2>${escapeHtml(props.title || "Selection")}</h2>
                    ${content}
                </section>
            `;
        }
    });

    function escapeHtml(value) {
        return String(value)
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }
})();
