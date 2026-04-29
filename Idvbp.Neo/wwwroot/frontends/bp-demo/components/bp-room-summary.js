(function () {
    window.IdvbpLayoutRuntime.register("bp-room-summary", {
        render(element, props) {
            element.innerHTML = `
                <section class="bp-room-summary">
                    <div>
                        <div class="summary-label">BP ROOM</div>
                        <div class="summary-name">${escapeHtml(props.roomName || "等待 BP 房间")}</div>
                    </div>
                    <div class="summary-stat">
                        <span>Phase</span>
                        <strong>${escapeHtml(props.phase || "Waiting")}</strong>
                    </div>
                    <div class="summary-stat">
                        <span>Round</span>
                        <strong>${escapeHtml(props.round ?? 1)}</strong>
                    </div>
                    <div class="summary-stat">
                        <span>Map</span>
                        <strong>${escapeHtml(props.mapName || "未选择")}</strong>
                    </div>
                    <div class="summary-score">
                        <strong>${escapeHtml(props.survivorScore ?? 0)}</strong>
                        <span>:</span>
                        <strong>${escapeHtml(props.hunterScore ?? 0)}</strong>
                    </div>
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
