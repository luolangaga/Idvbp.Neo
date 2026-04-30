(function () {
    window.IdvbpLayoutRuntime.register("team-card", {
        render(element, props) {
            const players = Array.isArray(props.players) ? props.players : [];
            const picks = props.characterPicks || {};
            const currentSide = String(props.currentSide || props.side || "").toLowerCase();
            const sideSlots = currentSide.includes("hunter")
                ? [readPick(picks, "hunter")]
                : [readPick(picks, "survivor1"), readPick(picks, "survivor2"), readPick(picks, "survivor3"), readPick(picks, "survivor4")];
            const rows = [0, 1, 2, 3, 4].map(index => {
                const player = players[index] || {};
                const pick = findPickForPlayer(player, sideSlots, index);
                const characterId = pick.characterId || pick.CharacterId || player.characterId || player.CharacterId || "-";
                return `
                    <li>
                        <span>${index + 1}</span>
                        <strong>${escapeHtml(player.name || "Empty")}</strong>
                        <em>${escapeHtml(characterId)}</em>
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

    function readPick(picks, key) {
        if (!picks) {
            return {};
        }

        const pascalKey = key.charAt(0).toUpperCase() + key.slice(1);
        return picks[key] || picks[pascalKey] || {};
    }

    function findPickForPlayer(player, picks, index) {
        const normalizedPlayerId = normalize(player.id);
        const normalizedName = normalize(player.name);
        const seatNumber = Number(player.seatNumber || player.SeatNumber || index + 1);

        return picks.find(pick => {
            if (!pick || !pick.characterId && !pick.CharacterId) {
                return false;
            }

            const pickId = normalize(pick.id || pick.Id);
            const pickName = normalize(pick.name || pick.Name);
            const pickSeat = Number(pick.seatNumber || pick.SeatNumber || 0);
            return (normalizedPlayerId && pickId && normalizedPlayerId === pickId)
                || (normalizedName && pickName && normalizedName === pickName)
                || (seatNumber > 0 && pickSeat > 0 && seatNumber === pickSeat);
        }) || {};
    }

    function normalize(value) {
        return String(value || "").trim().toLowerCase();
    }
})();
