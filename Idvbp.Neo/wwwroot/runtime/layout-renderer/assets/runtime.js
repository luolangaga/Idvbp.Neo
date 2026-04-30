(() => {
    const root = document.getElementById("runtime-root");
    const statusElement = document.getElementById("runtime-status");
    const registry = new Map();
    const nodeContexts = new Map();
    const store = { room: null, event: null };
    const roomEvents = ["room.snapshot", "room.info.updated", "match.created", "room.map.updated", "room.ban.updated", "room.global-ban.updated", "room.role.selected", "room.phase.updated"];
    let frontendBase = "";
    let stageElement = null;
    let signalRConnection = null;
    let currentRoomId = "";
    let currentLayout = null;
    let currentLayoutPath = "";
    let currentOptions = null;
    let editorLayer = null;
    let editorPanel = null;
    let selectedNodeId = "";

    const builtInComponents = {
        text: {
            render(element, props) {
                element.textContent = props.text ?? "";
            }
        },
        image: {
            render(element, props, context) {
                let img = element.querySelector("img");
                if (!img) {
                    img = document.createElement("img");
                    img.alt = "";
                    img.style.width = "100%";
                    img.style.height = "100%";
                    img.style.objectFit = props.fit ?? "cover";
                    element.appendChild(img);
                }
                img.src = resolveFrontendUrl(props.source ?? props.src ?? "", context.frontendBase);
            }
        }
    };

    for (const [type, definition] of Object.entries(builtInComponents)) {
        registry.set(type, definition);
    }

    window.IdvbpLayoutRuntime = {
        register(type, definition) {
            if (!type || !definition) {
                throw new Error("Component registration requires type and definition.");
            }
            registry.set(type, definition);
        },
        getStore: () => store,
        emitFrontendEvent
    };

    document.addEventListener("DOMContentLoaded", boot);

    async function boot() {
        try {
            const options = readOptions();
            currentOptions = options;
            frontendBase = `/frontends/${encodeURIComponent(options.frontend)}`;
            const manifest = await fetchJson(`${frontendBase}/manifest.json`);
            const layoutPath = resolveLayoutPath(manifest, options);
            currentLayoutPath = layoutPath;
            const layout = await fetchJson(`${frontendBase}/${layoutPath}`);
            currentLayout = layout;

            await loadComponents(manifest);
            await loadInitialRoom(options.roomId);
            renderLayout(layout);
            if (options.edit) {
                enableLayoutEditor(layout, options);
                setRuntimeStatus("warning", "Layout editor", "Drag components, resize from the bottom-right handle, then save.");
            } else {
                await connectSignalR(options.roomId, collectEventTypes(layout));
            }
        } catch (error) {
            renderError(error);
        }
    }

    function readOptions() {
        const params = new URLSearchParams(window.location.search);
        return {
            frontend: params.get("frontend") || "bp-demo",
            roomId: params.get("roomId") || params.get("room") || "",
            page: params.get("page") || "",
            layout: params.get("layout") || "",
            edit: params.get("edit") === "1" || params.get("mode") === "edit"
        };
    }

    function resolveLayoutPath(manifest, options) {
        if (options.layout) {
            return normalizeRelativePath(options.layout);
        }

        if (options.page && Array.isArray(manifest.pages)) {
            const page = manifest.pages.find(item =>
                String(item.id || "").toLowerCase() === options.page.toLowerCase());
            if (page?.layout) {
                return normalizeRelativePath(page.layout);
            }
        }

        return normalizeRelativePath(manifest.entryLayout || "layout.json");
    }

    function normalizeRelativePath(value) {
        return String(value || "layout.json").replaceAll("\\", "/").replace(/^\/+/, "");
    }

    async function fetchJson(url) {
        const response = await fetch(url, { cache: "no-store" });
        if (!response.ok) {
            throw new Error(`${url} returned ${response.status}`);
        }
        return response.json();
    }

    async function loadComponents(manifest) {
        const components = Array.isArray(manifest.components) ? manifest.components : [];
        for (const component of components) {
            if (component.style) {
                appendStylesheet(`${frontendBase}/${component.style}`);
            }
            if (component.script) {
                await appendScript(`${frontendBase}/${component.script}`);
            }
        }
    }

    function appendStylesheet(href) {
        const link = document.createElement("link");
        link.rel = "stylesheet";
        link.href = href;
        document.head.appendChild(link);
    }

    function appendScript(src) {
        return new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = src;
            script.onload = resolve;
            script.onerror = () => reject(new Error(`Failed to load component script: ${src}`));
            document.head.appendChild(script);
        });
    }

    async function loadInitialRoom(roomId) {
        if (roomId) {
            store.room = await fetchJson(`/api/rooms/${encodeURIComponent(roomId)}`);
            currentRoomId = getRoomId(store.room) || roomId;
            return;
        }

        const rooms = normalizeRoomsResponse(await fetchJson("/api/rooms"));
        store.room = rooms.length > 0 ? rooms[0] : createEmptyRoom();
        currentRoomId = getRoomId(store.room);
    }

    function createEmptyRoom() {
        return {
            roomId: "",
            roomName: "等待 BP 房间",
            currentPhase: "Waiting",
            currentRound: 1,
            teamA: { name: "Team A", members: [], currentSide: "Survivor" },
            teamB: { name: "Team B", members: [], currentSide: "Hunter" },
            mapSelection: { bannedMaps: [], pickedMap: null },
            bans: { survivorBans: [], hunterBans: [] },
            globalBans: { survivorBans: [], hunterBans: [] },
            characterPicks: {},
            matchScore: { survivorMatchScore: 0, hunterMatchScore: 0, totalRounds: 0 }
        };
    }

    function renderLayout(layout) {
        nodeContexts.clear();
        root.textContent = "";

        const canvas = layout.canvas || {};
        stageElement = document.createElement("section");
        stageElement.className = "runtime-stage";
        stageElement.style.setProperty("--canvas-width", `${canvas.width || 1920}px`);
        stageElement.style.setProperty("--canvas-height", `${canvas.height || 1080}px`);
        if (canvas.background) {
            stageElement.style.backgroundImage = `url("${resolveFrontendUrl(canvas.background, frontendBase)}")`;
        }
        root.appendChild(stageElement);
        fitStage(canvas.width || 1920, canvas.height || 1080);
        window.addEventListener("resize", () => fitStage(canvas.width || 1920, canvas.height || 1080));

        for (const node of layout.nodes || []) {
            renderNode(node, stageElement);
        }
    }

    function fitStage(width, height) {
        if (!stageElement) {
            return;
        }
        const scale = Math.min(window.innerWidth / width, window.innerHeight / height);
        stageElement.style.transform = `scale(${scale})`;
    }

    function renderNode(node, parentElement) {
        const definition = registry.get(node.type);
        if (!definition) {
            throw new Error(`Component type '${node.type}' is not registered.`);
        }

        const wrapper = document.createElement("div");
        wrapper.className = "runtime-node";
        wrapper.dataset.nodeId = node.id;
        applyNodeStyle(wrapper, node.style || {});
        parentElement.appendChild(wrapper);

        if (node.css) {
            appendScopedCss(node.id, node.css);
        }

        const context = {
            node,
            element: wrapper,
            frontendBase,
            store,
            update: () => updateNode(node.id),
            emit: emitFrontendEvent
        };
        nodeContexts.set(node.id, { definition, context, props: {} });

        updateNode(node.id);

        for (const child of node.children || []) {
            renderNode(child, wrapper);
        }
    }

    function applyNodeStyle(element, style) {
        const numeric = ["left", "top", "width", "height"];
        for (const key of numeric) {
            if (style[key] !== undefined) {
                element.style[key] = `${style[key]}px`;
            }
        }
        if (style.zIndex !== undefined) {
            element.style.zIndex = style.zIndex;
        }
        if (style.opacity !== undefined) {
            element.style.opacity = style.opacity;
        }
        if (style.rotate !== undefined) {
            element.style.rotate = `${style.rotate}deg`;
        }
    }

    function updateNode(nodeId) {
        const entry = nodeContexts.get(nodeId);
        if (!entry) {
            return;
        }

        entry.props = resolveProps(entry.context.node.props || {});
        entry.definition.render?.(entry.context.element, entry.props, entry.context);
    }

    function updateAllNodes() {
        for (const nodeId of nodeContexts.keys()) {
            updateNode(nodeId);
        }
    }

    function enableLayoutEditor(layout, options) {
        document.body.classList.add("is-layout-editing");
        editorLayer = document.createElement("div");
        editorLayer.className = "layout-editor-layer";
        stageElement.appendChild(editorLayer);

        for (const node of layout.nodes || []) {
            createEditorBox(node);
        }

        editorPanel = document.createElement("aside");
        editorPanel.className = "layout-editor-panel";
        editorPanel.innerHTML = `
            <div class="layout-editor-title">Layout Editor</div>
            <label>Node<input data-field="id" readonly></label>
            <label>Left<input data-field="left" type="number"></label>
            <label>Top<input data-field="top" type="number"></label>
            <label>Width<input data-field="width" type="number"></label>
            <label>Height<input data-field="height" type="number"></label>
            <label>Z Index<input data-field="zIndex" type="number"></label>
            <div class="layout-editor-actions">
                <button type="button" data-action="save">Save layout</button>
                <button type="button" data-action="reload">Reload</button>
            </div>
            <p data-role="status"></p>`;
        document.body.appendChild(editorPanel);

        editorPanel.addEventListener("input", event => {
            const input = event.target.closest("input[data-field]");
            if (!input || !selectedNodeId || input.dataset.field === "id") {
                return;
            }

            const node = findLayoutNode(selectedNodeId);
            if (!node) {
                return;
            }

            node.style = node.style || {};
            node.style[input.dataset.field] = Number(input.value) || 0;
            syncNodeFromLayout(node);
        });

        editorPanel.querySelector('[data-action="save"]').addEventListener("click", () => saveEditedLayout(options));
        editorPanel.querySelector('[data-action="reload"]').addEventListener("click", () => window.location.reload());

        const firstNode = layout.nodes?.[0];
        if (firstNode) {
            selectEditorNode(firstNode.id);
        }
    }

    function createEditorBox(node) {
        const box = document.createElement("div");
        box.className = "layout-editor-box";
        box.dataset.nodeId = node.id;
        box.innerHTML = `<span>${escapeHtml(node.id)}</span><button type="button" title="Resize"></button>`;
        editorLayer.appendChild(box);
        syncEditorBox(node);

        box.addEventListener("pointerdown", event => {
            event.preventDefault();
            event.stopPropagation();
            selectEditorNode(node.id);
            const mode = event.target.tagName === "BUTTON" ? "resize" : "move";
            startEditorPointerInteraction(event, node, mode);
        });
    }

    function startEditorPointerInteraction(event, node, mode) {
        const pointerId = event.pointerId;
        const start = stagePointer(event);
        const style = node.style = node.style || {};
        const origin = {
            left: Number(style.left) || 0,
            top: Number(style.top) || 0,
            width: Number(style.width) || 100,
            height: Number(style.height) || 100
        };

        const move = moveEvent => {
            const point = stagePointer(moveEvent);
            const dx = point.x - start.x;
            const dy = point.y - start.y;
            if (mode === "resize") {
                style.width = Math.max(20, Math.round(origin.width + dx));
                style.height = Math.max(20, Math.round(origin.height + dy));
            } else {
                style.left = Math.round(origin.left + dx);
                style.top = Math.round(origin.top + dy);
            }
            syncNodeFromLayout(node);
        };

        const up = () => {
            window.removeEventListener("pointermove", move);
            window.removeEventListener("pointerup", up);
            window.removeEventListener("pointercancel", up);
            document.querySelector(`.layout-editor-box[data-node-id="${CSS.escape(node.id)}"]`)?.releasePointerCapture?.(pointerId);
        };

        event.currentTarget.setPointerCapture?.(pointerId);
        window.addEventListener("pointermove", move);
        window.addEventListener("pointerup", up);
        window.addEventListener("pointercancel", up);
    }

    function stagePointer(event) {
        const rect = stageElement.getBoundingClientRect();
        const scale = rect.width / (Number.parseFloat(getComputedStyle(stageElement).width) || rect.width || 1);
        return {
            x: (event.clientX - rect.left) / scale,
            y: (event.clientY - rect.top) / scale
        };
    }

    function selectEditorNode(nodeId) {
        selectedNodeId = nodeId;
        for (const box of editorLayer.querySelectorAll(".layout-editor-box")) {
            box.classList.toggle("is-selected", box.dataset.nodeId === nodeId);
        }
        refreshEditorPanel();
    }

    function refreshEditorPanel() {
        if (!editorPanel || !selectedNodeId) {
            return;
        }

        const node = findLayoutNode(selectedNodeId);
        if (!node) {
            return;
        }

        const style = node.style || {};
        for (const input of editorPanel.querySelectorAll("input[data-field]")) {
            const field = input.dataset.field;
            input.value = field === "id" ? node.id : style[field] ?? "";
        }
    }

    function findLayoutNode(nodeId) {
        const visit = nodes => {
            for (const node of nodes || []) {
                if (node.id === nodeId) {
                    return node;
                }
                const child = visit(node.children);
                if (child) {
                    return child;
                }
            }
            return null;
        };
        return visit(currentLayout?.nodes);
    }

    function syncNodeFromLayout(node) {
        const context = nodeContexts.get(node.id)?.context;
        if (context) {
            applyNodeStyle(context.element, node.style || {});
        }
        syncEditorBox(node);
        refreshEditorPanel();
    }

    function syncEditorBox(node) {
        const box = editorLayer?.querySelector(`.layout-editor-box[data-node-id="${CSS.escape(node.id)}"]`);
        if (!box) {
            return;
        }

        const style = node.style || {};
        box.style.left = `${Number(style.left) || 0}px`;
        box.style.top = `${Number(style.top) || 0}px`;
        box.style.width = `${Number(style.width) || 100}px`;
        box.style.height = `${Number(style.height) || 100}px`;
        box.style.zIndex = `${(Number(style.zIndex) || 0) + 10000}`;
    }

    async function saveEditedLayout(options) {
        const status = editorPanel?.querySelector('[data-role="status"]');
        try {
            if (status) {
                status.textContent = "Saving...";
            }
            const response = await fetch(`/api/frontends/${encodeURIComponent(options.frontend)}/layout?path=${encodeURIComponent(currentLayoutPath)}`, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(currentLayout)
            });
            if (!response.ok) {
                const text = await response.text();
                throw new Error(text || `Save failed: ${response.status}`);
            }
            if (status) {
                status.textContent = "Saved.";
            }
            setRuntimeStatus("ok", "Layout saved", currentLayoutPath);
        } catch (error) {
            if (status) {
                status.textContent = error.message || String(error);
            }
            setRuntimeStatus("error", "Layout save failed", error.message || String(error));
        }
    }

    function resolveProps(props) {
        if (Array.isArray(props)) {
            return props.map(resolveProps);
        }
        if (props && typeof props === "object") {
            if (typeof props.bind === "string") {
                return getByPath(store, props.bind);
            }
            const result = {};
            for (const [key, value] of Object.entries(props)) {
                result[key] = resolveProps(value);
            }
            return result;
        }
        return props;
    }

    function getByPath(source, path) {
        return path.split(".").reduce((current, segment) => {
            if (current === undefined || current === null) {
                return undefined;
            }
            if (Object.prototype.hasOwnProperty.call(current, segment)) {
                return current[segment];
            }

            const pascalSegment = segment.charAt(0).toUpperCase() + segment.slice(1);
            if (Object.prototype.hasOwnProperty.call(current, pascalSegment)) {
                return current[pascalSegment];
            }

            const matchedKey = Object.keys(current).find(key => key.toLowerCase() === segment.toLowerCase());
            return matchedKey ? current[matchedKey] : undefined;
        }, source);
    }

    function getRoomId(room) {
        return room?.roomId || room?.RoomId || "";
    }

    function appendScopedCss(nodeId, css) {
        const style = document.createElement("style");
        const scope = `[data-node-id="${CSS.escape(nodeId)}"]`;
        style.textContent = css
            .split("}")
            .map(rule => {
                const parts = rule.split("{");
                if (parts.length < 2) {
                    return "";
                }
                const selector = parts[0].trim();
                const body = parts.slice(1).join("{");
                const scopedSelector = selector
                    .split(",")
                    .map(item => {
                        const trimmed = item.trim();
                        return trimmed === ":host" ? scope : `${scope} ${trimmed.replaceAll(":host", scope)}`;
                    })
                    .join(", ");
                return `${scopedSelector} {${body}}`;
            })
            .join("\n");
        document.head.appendChild(style);
    }

    async function connectSignalR(roomId, eventTypes) {
        const effectiveRoomId = roomId || getRoomId(store.room) || currentRoomId;
        if (!effectiveRoomId) {
            setRuntimeStatus("warning", "SignalR waiting", "没有可订阅的 roomId。");
            return;
        }

        currentRoomId = effectiveRoomId;

        if (!window.signalR) {
            setRuntimeStatus("error", "SignalR unavailable", "本地 SignalR 客户端脚本未加载。");
            return;
        }

        signalRConnection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/game")
            .withAutomaticReconnect()
            .build();

        signalRConnection.on("RoomEvent", envelope => handleRoomEvent(envelope));
        signalRConnection.onreconnecting(error => {
            setRuntimeStatus("warning", "SignalR reconnecting", error?.message || "连接中断，正在重连。");
        });
        signalRConnection.onreconnected(async () => {
            setRuntimeStatus("ok", "SignalR reconnected", `roomId=${currentRoomId}`);
            await subscribeSignalR(currentRoomId, eventTypes);
        });
        signalRConnection.onclose(error => {
            setRuntimeStatus("error", "SignalR closed", error?.message || "连接已关闭。");
        });

        try {
            setRuntimeStatus("warning", "SignalR connecting", `roomId=${effectiveRoomId}`);
            await signalRConnection.start();
            await subscribeSignalR(effectiveRoomId, eventTypes);
            setRuntimeStatus("ok", "SignalR connected", `roomId=${effectiveRoomId}，已订阅 ${eventTypes.length} 个事件。`);
        } catch (error) {
            setRuntimeStatus("error", "SignalR failed", error.message || String(error));
        }
    }

    async function subscribeSignalR(roomId, eventTypes) {
        await signalRConnection.invoke("JoinRoom", roomId);
        const supportedEvents = eventTypes.filter(eventType => roomEvents.includes(eventType));
        if (supportedEvents.length > 0) {
            await signalRConnection.invoke("ReplaceSubscriptions", roomId, supportedEvents);
        }
        await signalRConnection.invoke("RequestRoomSnapshot", roomId);
    }

    async function refreshRoomSnapshot() {
        const roomId = currentRoomId || getRoomId(store.room);
        if (roomId) {
            store.room = await fetchJson(`/api/rooms/${encodeURIComponent(roomId)}`);
            currentRoomId = getRoomId(store.room) || roomId;
            updateAllNodes();
            return;
        }

        const rooms = normalizeRoomsResponse(await fetchJson("/api/rooms"));
        if (rooms.length > 0) {
            store.room = rooms[0];
            currentRoomId = getRoomId(store.room);
            updateAllNodes();
        }
    }

    function setRuntimeStatus(level, title, detail) {
        if (!statusElement) {
            return;
        }
        statusElement.className = `runtime-status ${level === "ok" ? "" : `is-${level}`}`.trim();
        statusElement.innerHTML = `<strong>${escapeHtml(title)}</strong>${detail ? ` · ${escapeHtml(detail)}` : ""}`;
    }

    function escapeHtml(value) {
        return String(value)
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function collectEventTypes(layout) {
        const eventTypes = new Set(roomEvents);
        const visit = node => {
            for (const eventType of Object.keys(node.events || {})) {
                eventTypes.add(eventType);
            }
            for (const child of node.children || []) {
                visit(child);
            }
        };
        for (const node of layout.nodes || []) {
            visit(node);
        }
        return [...eventTypes];
    }

    function handleRoomEvent(envelope) {
        const eventType = envelope.eventType || envelope.EventType || envelope.type || envelope.Type;
        const payload = envelope.payload ?? envelope.Payload;
        store.event = {
            type: eventType,
            timestamp: envelope.occurredAtUtc || envelope.OccurredAtUtc || envelope.timestamp || envelope.Timestamp,
            payload
        };

        mergeRoomPayload(eventType, payload);

        updateAllNodes();
        dispatchLayoutEvent(eventType, store.event);
        setRuntimeStatus("ok", "SignalR connected", `roomId=${currentRoomId}，last=${eventType || "unknown"}`);
        refreshRoomSnapshot().catch(error => console.warn("Room refresh after SignalR event failed.", error));
    }

    function normalizeRoomsResponse(value) {
        if (Array.isArray(value)) {
            return value;
        }
        if (Array.isArray(value?.value)) {
            return value.value;
        }
        if (Array.isArray(value?.Value)) {
            return value.Value;
        }
        return [];
    }

    function mergeRoomPayload(eventType, payload) {
        if (!payload || typeof payload !== "object") {
            return;
        }

        if (eventType === "room.snapshot" ||
            eventType === "room.info.updated" ||
            eventType === "match.created") {
            store.room = payload;
            return;
        }

        if (!store.room) {
            store.room = createEmptyRoom();
        }

        switch (eventType) {
            case "room.map.updated":
                store.room.currentRound = getPayloadValue(payload, "currentRound") ?? store.room.currentRound;
                store.room.mapSelection = getPayloadValue(payload, "mapSelection") ?? store.room.mapSelection;
                break;
            case "room.ban.updated":
                store.room.currentRound = getPayloadValue(payload, "currentRound") ?? store.room.currentRound;
                store.room.bans = getPayloadValue(payload, "bans") ?? store.room.bans;
                break;
            case "room.global-ban.updated":
                store.room.globalBans = getPayloadValue(payload, "globalBans") ?? store.room.globalBans;
                break;
            case "room.role.selected":
                store.room.characterPicks = getPayloadValue(payload, "characterPicks") ?? store.room.characterPicks;
                break;
            case "room.phase.updated":
                store.room.currentPhase = getPayloadValue(payload, "phase") ?? store.room.currentPhase;
                break;
            default:
                store.room = { ...store.room, ...payload };
                break;
        }
    }

    function getPayloadValue(payload, name) {
        if (!payload || typeof payload !== "object") {
            return undefined;
        }

        if (Object.prototype.hasOwnProperty.call(payload, name)) {
            return payload[name];
        }

        const pascalName = name.charAt(0).toUpperCase() + name.slice(1);
        if (Object.prototype.hasOwnProperty.call(payload, pascalName)) {
            return payload[pascalName];
        }

        const matchedKey = Object.keys(payload).find(key => key.toLowerCase() === name.toLowerCase());
        return matchedKey ? payload[matchedKey] : undefined;
    }

    function dispatchLayoutEvent(eventType, event) {
        for (const entry of nodeContexts.values()) {
            const actions = entry.context.node.events?.[eventType] || [];
            for (const action of actions) {
                runAction(entry, action, event);
            }
        }
    }

    function runAction(entry, action, event) {
        const element = entry.context.element;
        const name = action.action;
        if (entry.definition.actions?.[name]) {
            entry.definition.actions[name](element, action, entry.context, event);
            return;
        }

        switch (name) {
            case "playAnimation":
                playAnimation(element, action.name);
                break;
            case "stopAnimation":
                element.classList.remove("is-entering", "is-leaving", "is-pulsing");
                break;
            case "setVisible":
                element.classList.toggle("is-hidden", action.value === false);
                break;
            case "setClass":
                element.classList.add(action.name);
                break;
            case "removeClass":
                element.classList.remove(action.name);
                break;
            case "toggleClass":
                element.classList.toggle(action.name, action.value);
                break;
            case "setProp":
                entry.context.node.props = { ...(entry.context.node.props || {}), [action.name]: action.value };
                updateNode(entry.context.node.id);
                break;
            case "emit":
                emitFrontendEvent(action.type, action.payload);
                break;
            default:
                console.warn(`Unsupported action '${name}' on node '${entry.context.node.id}'.`);
        }
    }

    function playAnimation(element, name) {
        const className = name === "leave" ? "is-leaving" : name === "pulse" ? "is-pulsing" : "is-entering";
        element.classList.remove("is-entering", "is-leaving", "is-pulsing");
        void element.offsetWidth;
        element.classList.add(className);
    }

    function emitFrontendEvent(type, payload) {
        dispatchLayoutEvent(type, {
            type,
            payload,
            timestamp: new Date().toISOString()
        });
    }

    function resolveFrontendUrl(value, base) {
        if (!value || /^(https?:)?\/\//i.test(value) || value.startsWith("/")) {
            return value;
        }
        return `${base}/${value.replace(/^\.?\//, "")}`;
    }

    function renderError(error) {
        console.error(error);
        root.innerHTML = "";
        const element = document.createElement("div");
        element.className = "runtime-error";
        element.textContent = `Layout runtime failed:\n${error.message || error}`;
        root.appendChild(element);
    }
})();
