(function () {
    const defaults = {
        background: {
            color: "#1a1a2e",
            image: "",
            fit: "cover",
            position: "center"
        },
        character: {
            imageBase: "",
            survivorFolder: "",
            hunterFolder: "",
            imageExtension: "png",
            fit: "contain",
            showPlaceholder: true
        },
        banList: {
            imageBase: "",
            survivorFolder: "",
            hunterFolder: "",
            imageExtension: "png",
            itemSize: 56,
            itemGap: 10,
            rowGap: 10,
            maxPerRow: 4
        },
        timer: {
            durationSeconds: 0,
            remainingSeconds: 0
        }
    };
    const resourceImageCache = new Map();

    register("asg-bp-background", {
        render(element, props, context) {
            const config = mergeConfig(defaults.background, props.config, context.config);
            const image = config.image || props.image || "";
            element.innerHTML = `<div class="asg-bp-background"></div>`;
            const bg = element.firstElementChild;
            bg.style.backgroundColor = config.color || "#1a1a2e";
            bg.style.backgroundImage = image ? `url("${resolveAsset(image, context)}")` : "";
            bg.style.backgroundSize = backgroundSize(config.fit || "cover");
            bg.style.backgroundPosition = config.position || "center";
        },
        contextMenu({ config }) {
            const current = mergeConfig(defaults.background, config);
            return [
                {
                    type: "file",
                    label: "背景图片",
                    accept: "image/*",
                    async onChange(file, helpers) {
                        const asset = await helpers.importAsset(file, "backgrounds");
                        await helpers.setConfig({
                            ...current,
                            image: asset.relativePath || asset.url || ""
                        });
                        helpers.close();
                    }
                },
                {
                    type: "select",
                    label: "填充方式",
                    value: current.fit || "cover",
                    options: [
                        { value: "cover", label: "铺满裁切" },
                        { value: "contain", label: "完整显示" },
                        { value: "stretch", label: "直接铺满" }
                    ],
                    async onChange(value, helpers) {
                        await helpers.setConfig({ ...helpers.config, fit: value });
                    }
                },
                {
                    type: "text",
                    label: "背景位置",
                    value: current.position || "center",
                    async onChange(value, helpers) {
                        await helpers.setConfig({ ...helpers.config, position: value || "center" });
                    }
                },
                {
                    type: "color",
                    label: "底色",
                    value: current.color || "#1a1a2e",
                    async onChange(value, helpers) {
                        await helpers.setConfig({ ...helpers.config, color: value || "#1a1a2e" });
                    }
                },
                {
                    type: "button",
                    label: "清除背景图",
                    async action(value, helpers) {
                        const next = { ...helpers.config };
                        delete next.image;
                        await helpers.setConfig(next);
                    }
                }
            ];
        }
    });

    const characterSlotComponent = {
        render(element, props, context) {
            const config = mergeConfig(defaults.character, props.config, context.config);
            const player = props.player || {};
            const role = String(props.role || "survivor").toLowerCase();
            const characterId = read(player, "characterId") || "";
            const playerName = read(player, "name") || "";
            const image = read(player, "avatarUrl") ||
                buildCharacterImage(characterId, role, config, context, config.variant || "full");
            const imageMarkup = image
                ? `<img src="${escapeAttribute(image)}" alt="${escapeAttribute(characterId || playerName || props.label || role)}">`
                : "";
            const placeholder = !image && config.showPlaceholder !== false
                ? `<div class="asg-character-placeholder">?</div>`
                : "";

            element.innerHTML = `
                <section class="asg-character-slot asg-role-${escapeAttribute(role)}">
                    ${imageMarkup}
                    ${placeholder}
                </section>`;
        },
        contextMenu({ config }) {
            const current = mergeConfig(defaults.character, config);
            return [
                {
                    type: "select",
                    label: "立绘类型",
                    value: current.variant || "full",
                    options: [
                        { value: "half", label: "半身立绘" },
                        { value: "full", label: "全身立绘" }
                    ],
                    async onChange(value, helpers) {
                        await helpers.setConfig({
                            ...mergeConfig({}, helpers.config),
                            variant: value || "full"
                        });
                    }
                }
            ];
        }
    };

    const textComponent = {
        render(element, props, context) {
            const config = mergeConfig({}, props.config, context.config);
            const value = firstText(props.value, props.fallbackValue, config.value, props.fallback, "");
            const align = config.align || props.align || "left";
            element.innerHTML = `<div class="asg-bp-text">${escapeHtml(value)}</div>`;
            const text = element.firstElementChild;
            text.style.textAlign = align;
            applyTextConfig(text, config);
        }
    };

    const imageComponent = {
        render(element, props, context) {
            const config = mergeConfig({}, props.config, context.config);
            const source = config.source || props.source || dataImage(props.fallbackData) ||
                resolveResourceImage(props.resourceType || config.resourceType, props.resourceId || config.resourceId, config.variant || "default", context) || "";
            const resolved = source ? resolveAsset(source, context) : "";
            element.innerHTML = `
                <section class="asg-bp-image">
                    ${resolved ? `<img src="${escapeAttribute(resolved)}" alt="${escapeAttribute(props.label || "")}">` : ""}
                </section>`;
            const image = element.querySelector("img");
            if (image) {
                image.style.objectFit = config.fit || props.fit || "contain";
            }
        }
    };

    const banListComponent = {
        render(element, props, context) {
            const config = mergeConfig(defaults.banList, props.config, context.config);
            const items = Array.isArray(props.items) ? props.items : [];
            const role = String(props.role || "survivor").toLowerCase();
            const classes = ["asg-bp-ban-list", props.global ? "is-global" : ""].filter(Boolean).join(" ");
            const content = items.map(item => {
                const characterId = read(item, "characterId") || read(item, "id") || read(item, "name") || "";
                const image = buildCharacterImage(characterId, role, config, context, config.variant || "header");
                const body = image
                    ? `<img src="${escapeAttribute(image)}" alt="${escapeAttribute(characterId)}">`
                    : `<span>${escapeHtml(characterId || "-")}</span>`;
                return `<li>${body}</li>`;
            }).join("");

            element.innerHTML = `<ol class="${classes}">${content}</ol>`;
            const list = element.firstElementChild;
            list.style.setProperty("--asg-ban-size", `${numberOr(config.itemSize, 56)}px`);
            list.style.setProperty("--asg-ban-gap", `${numberOr(config.itemGap, 10)}px`);
            list.style.setProperty("--asg-ban-row-gap", `${numberOr(config.rowGap, 10)}px`);
            list.style.gridTemplateColumns = `repeat(${Math.max(1, numberOr(config.maxPerRow, 4))}, var(--asg-ban-size))`;
        }
    };

    registerSurvivorSlot("asg-bp-survivor1-slot", 1);
    registerSurvivorSlot("asg-bp-survivor2-slot", 2);
    registerSurvivorSlot("asg-bp-survivor3-slot", 3);
    registerSurvivorSlot("asg-bp-survivor4-slot", 4);

    function registerSurvivorSlot(type, seatNumber) {
        register(type, {
            render(element, props, context) {
                characterSlotComponent.render(element, { ...props, role: "survivor", seatNumber }, context);
            },
            contextMenu: characterSlotComponent.contextMenu
        });
    }

    register("asg-bp-hunter-slot", {
        render(element, props, context) {
            characterSlotComponent.render(element, { ...props, role: "hunter" }, context);
        },
        contextMenu: characterSlotComponent.contextMenu
    });

    register("asg-bp-survivor1-name", textComponent);
    register("asg-bp-survivor2-name", textComponent);
    register("asg-bp-survivor3-name", textComponent);
    register("asg-bp-survivor4-name", textComponent);
    register("asg-bp-hunter-name", textComponent);
    register("asg-bp-team-name", textComponent);
    register("asg-bp-map-name", textComponent);
    register("asg-bp-phase-name", textComponent);
    register("asg-bp-team-logo", imageComponent);
    register("asg-bp-map-image", {
        render(element, props, context) {
            imageComponent.render(element, { ...props, resourceType: "map" }, context);
        }
    });
    register("asg-bp-local-ban-list", {
        render(element, props, context) {
            banListComponent.render(element, { ...props, global: false }, context);
        }
    });
    register("asg-bp-global-ban-list", {
        render(element, props, context) {
            banListComponent.render(element, { ...props, global: true }, context);
        }
    });

    register("asg-bp-timer", {
        render(element, props, context) {
            const state = readTimerState(props, context);
            const warning = state.remaining > 0 && state.remaining <= 10 ? " is-warning" : "";
            element.innerHTML = `<div class="asg-bp-timer${warning}">${escapeHtml(formatTime(state.remaining))}</div>`;
        },
        actions: {
            syncState(element, action, context, event) {
                context.update();
            }
        }
    });

    register("asg-bp-timer-progress", {
        render(element, props, context) {
            const state = readTimerState(props, context);
            const percent = state.duration > 0 ? Math.max(0, Math.min(100, (state.remaining / state.duration) * 100)) : 0;
            const indeterminate = state.duration <= 0 && state.remaining <= 0;
            const warning = state.remaining > 0 && state.remaining <= 10 ? " is-warning" : "";
            element.innerHTML = `
                <div class="asg-bp-progress">
                    <div class="asg-bp-progress-fill${warning}${indeterminate ? " is-indeterminate" : ""}" style="width:${percent}%"></div>
                </div>`;
        },
        actions: {
            syncState(element, action, context, event) {
                context.update();
            }
        }
    });

    function register(type, definition) {
        window.IdvbpLayoutRuntime.register(type, definition);
    }

    function mergeConfig(...values) {
        return values.reduce((result, value) => {
            const parsed = parseConfig(value);
            return parsed && typeof parsed === "object" && !Array.isArray(parsed)
                ? { ...result, ...parsed }
                : result;
        }, {});
    }

    function parseConfig(value) {
        if (!value) {
            return null;
        }
        if (typeof value === "object") {
            return value;
        }
        if (typeof value === "string") {
            try {
                return JSON.parse(value);
            } catch {
                return null;
            }
        }
        return null;
    }

    function read(source, name) {
        if (!source || typeof source !== "object") {
            return undefined;
        }
        if (Object.prototype.hasOwnProperty.call(source, name)) {
            return source[name];
        }
        const pascal = name.charAt(0).toUpperCase() + name.slice(1);
        if (Object.prototype.hasOwnProperty.call(source, pascal)) {
            return source[pascal];
        }
        const match = Object.keys(source).find(key => key.toLowerCase() === name.toLowerCase());
        return match ? source[match] : undefined;
    }

    function firstText(...values) {
        for (const value of values) {
            if (value !== undefined && value !== null && String(value).trim() !== "") {
                return value;
            }
        }
        return "";
    }

    function buildCharacterImage(characterId, role, config, context, variant) {
        if (!characterId) {
            return "";
        }
        const explicit = config.images && (config.images[characterId] || config.images[String(characterId).toLowerCase()]);
        if (explicit) {
            return resolveAsset(explicit, context);
        }
        const base = config.imageBase || "";
        const folder = role === "hunter" ? config.hunterFolder : config.survivorFolder;
        if (!base && !folder) {
            return resolveResourceImage("character", characterId, variant || (role === "hunter" ? "full" : "full"), context) || "";
        }
        const extension = String(config.imageExtension || "png").replace(/^\./, "");
        const path = [base, folder, `${characterId}.${extension}`]
            .filter(Boolean)
            .join("/")
            .replaceAll("\\", "/")
            .replace(/\/+/g, "/");
        return resolveAsset(path, context);
    }

    function resolveResourceImage(resourceType, resourceId, variant, context) {
        if (!resourceType || !resourceId) {
            return "";
        }
        const type = String(resourceType).toLowerCase();
        const id = String(resourceId);
        const normalizedVariant = String(variant || (type === "map" ? "default" : "full"));
        const key = `${type}:${id}:${normalizedVariant}`;
        if (resourceImageCache.has(key)) {
            return resourceImageCache.get(key) || "";
        }

        resourceImageCache.set(key, "");
        const endpoint = type === "map"
            ? `/api/resources/maps/${encodeURIComponent(id)}/images?variant=${encodeURIComponent(normalizedVariant)}`
            : `/api/resources/characters/${encodeURIComponent(id)}/images?variant=${encodeURIComponent(normalizedVariant)}`;

        context.fetchJson(endpoint)
            .then(images => {
                const list = Array.isArray(images) ? images : [];
                const primary = list.find(item => read(item, "isPrimary")) || list[0];
                const url = read(primary || {}, "url") || "";
                resourceImageCache.set(key, url);
                if (url) {
                    context.update();
                }
            })
            .catch(() => {
                resourceImageCache.set(key, "");
            });

        return "";
    }

    function dataImage(value) {
        if (!value) {
            return "";
        }
        if (typeof value === "string") {
            return value.startsWith("data:") ? value : `data:image/png;base64,${value}`;
        }
        if (Array.isArray(value)) {
            const binary = value.reduce((text, item) => text + String.fromCharCode(Number(item) || 0), "");
            return `data:image/png;base64,${btoa(binary)}`;
        }
        return "";
    }

    function resolveAsset(value, context) {
        if (!value) {
            return "";
        }
        const text = String(value).replaceAll("\\", "/");
        if (/^(data:|blob:|https?:\/\/|\/)/i.test(text)) {
            return text;
        }
        const base = context?.frontendBase || "";
        return `${base}/${text.replace(/^\.?\//, "")}`;
    }

    function backgroundSize(value) {
        const fit = String(value || "cover").toLowerCase();
        if (fit === "stretch" || fit === "fill") {
            return "100% 100%";
        }
        if (fit === "contain") {
            return "contain";
        }
        return "cover";
    }

    function applyTextConfig(element, config) {
        if (config.color) {
            element.style.color = config.color;
        }
        if (config.fontSize) {
            element.style.fontSize = `${numberOr(config.fontSize, 16)}px`;
        }
        if (config.fontWeight) {
            element.style.fontWeight = config.fontWeight;
        }
        if (config.fontFamily) {
            element.style.fontFamily = `"${config.fontFamily}", sans-serif`;
        }
    }

    function readTimerState(props, context) {
        const config = mergeConfig(defaults.timer, props.config, context.config);
        const payload = props.event?.payload || context.store?.event?.payload || {};
        const remaining = read(payload, "remainingSeconds") ?? read(payload, "remaining") ?? config.remainingSeconds;
        const duration = read(payload, "durationSeconds") ?? read(payload, "duration") ?? config.durationSeconds;
        return {
            remaining: Math.max(0, Math.floor(Number(remaining) || 0)),
            duration: Math.max(0, Math.floor(Number(duration) || 0))
        };
    }

    function formatTime(seconds) {
        const minutes = Math.floor(seconds / 60);
        const rest = seconds % 60;
        return `${minutes}:${String(rest).padStart(2, "0")}`;
    }

    function numberOr(value, fallback) {
        const number = Number(value);
        return Number.isFinite(number) ? number : fallback;
    }

    function escapeHtml(value) {
        return String(value)
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function escapeAttribute(value) {
        return escapeHtml(value);
    }
})();
