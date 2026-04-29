(function () {
    window.IdvbpLayoutRuntime.register("team-card", {
        render(element, props) {
            const players = Array.isArray(props.players) ? props.players : [];
            const rows = [0, 1, 2, 3, 4].map(index => {
                const player = players[index] || {};
                return `
                    <li>
                        <span>${index + 1}</span>
                        <strong>${escapeHtml(player.name || "Empty")}</strong>
                        <em>${escapeHtml(player.characterId || "-")}</em>
                    </li>
                `;
            }).join("");

            element.innerHTML = `
                <section class="team-card">
                    <header>
                        <div>
                            <span>${escapeHtml(props.label || props.side || "Team")}</span>
                            <h2>${escapeHtml(props.teamName || "未命名队伍")}</h2>
                        </div>
                        <strong>${escapeHtml(props.currentSide || "-")}</strong>
                    </header>
                    <ol>${rows}</ol>
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
