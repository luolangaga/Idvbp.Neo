(function () {
    window.IdvbpLayoutRuntime.register("character-model-3d-frame", {
        render(element, props, context) {
            let host = element.querySelector(".character-model-3d-frame");
            let frame = element.querySelector("iframe");
            const source = props.source || "/overlay/character-model-3d.html";

            if (!host) {
                host = document.createElement("div");
                host.className = "character-model-3d-frame";
                frame = document.createElement("iframe");
                frame.title = "Character Model 3D";
                frame.allow = "camera; microphone; autoplay; fullscreen";
                host.appendChild(frame);
                element.textContent = "";
                element.appendChild(host);
            }

            if (frame.dataset.source !== source) {
                frame.dataset.source = source;
                frame.src = source;
                frame.addEventListener("load", () => syncFrame(frame, context.store.room, context.config), { once: false });
            }

            syncFrame(frame, props.room || context.store.room, context.config || parseConfig(props.config));
        },
        actions: {
            syncState(element, action, context) {
                const frame = element.querySelector("iframe");
                syncFrame(frame, context.store.room, context.config);
            },
            reload(element) {
                const frame = element.querySelector("iframe");
                if (frame?.src) {
                    frame.src = frame.src;
                }
            }
        }
    });

    function syncFrame(frame, room, config) {
        if (!frame?.contentWindow || !room) {
            return;
        }

        frame.contentWindow.postMessage({
            type: "asg:character-model-3d:set-state",
            state: toCharacterModelState(room, config)
        }, "*");
    }

    function toCharacterModelState(room, config) {
        const picks = read(room, "characterPicks") || {};
        const bans = read(room, "bans") || {};
        const globalBans = read(room, "globalBans") || {};
        return {
            roomId: read(room, "roomId") || "",
            survivors: [
                characterId(read(picks, "survivor1")),
                characterId(read(picks, "survivor2")),
                characterId(read(picks, "survivor3")),
                characterId(read(picks, "survivor4"))
            ],
            hunter: characterId(read(picks, "hunter")),
            hunterBannedSurvivors: characterIds(read(bans, "survivorBans")),
            survivorBannedHunters: characterIds(read(bans, "hunterBans")),
            globalBannedSurvivors: characterIds(read(globalBans, "survivorBans")),
            globalBannedHunters: characterIds(read(globalBans, "hunterBans")),
            characterModel3DLayout: config || null
        };
    }

    function characterIds(items) {
        return Array.isArray(items)
            ? items.map(characterId).filter(Boolean)
            : [];
    }

    function characterId(value) {
        if (!value) {
            return null;
        }
        if (typeof value === "string") {
            return value.trim() || null;
        }
        return read(value, "characterId") || read(value, "id") || read(value, "name") || null;
    }

    function read(source, key) {
        if (!source || typeof source !== "object") {
            return undefined;
        }
        if (Object.prototype.hasOwnProperty.call(source, key)) {
            return source[key];
        }
        const pascal = key.charAt(0).toUpperCase() + key.slice(1);
        if (Object.prototype.hasOwnProperty.call(source, pascal)) {
            return source[pascal];
        }
        const matched = Object.keys(source).find(item => item.toLowerCase() === key.toLowerCase());
        return matched ? source[matched] : undefined;
    }

    function parseConfig(value) {
        if (!value || typeof value !== "string") {
            return value || null;
        }
        try {
            return JSON.parse(value);
        } catch {
            return null;
        }
    }
})();
