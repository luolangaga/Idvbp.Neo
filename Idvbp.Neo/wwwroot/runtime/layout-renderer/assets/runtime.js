(() => {
    const root = document.getElementById("runtime-root");
    const statusElement = document.getElementById("runtime-status");
    const registry = new Map();
    const nodeContexts = new Map();
    const store = { room: null, event: null, configs: {} };
    const roomEvents = ["room.snapshot", "room.info.updated", "match.created", "room.map.updated", "room.ban.updated", "room.global-ban.updated", "room.role.selected", "room.phase.updated"];

    function rtLog(source, level, ...args) {
        const message = args.map(a => typeof a === "object" ? JSON.stringify(a) : String(a ?? "")).join(" ");
        console.log(`[${source}]`, ...args);
        try { navigator.sendBeacon("/api/runtime-logs", new Blob([JSON.stringify({ source, level, message })], { type: "application/json" })); } catch {}
    }
    let frontendBase = "";
    let stageElement = null;
    let signalRConnection = null;
    let currentRoomId = "";
    let currentLayout = null;
    let currentLayoutPath = "";
    let currentOptions = null;
    let editorLayer = null;
    let editorPanel = null;
    let editorContextMenu = null;
    let editorContextMenuItems = [];
    let selectedNodeId = "";
    let editorMode = "layout";
    let selectedAnimationRuleId = "";
    let availableFonts = [];
    let availableComponents = [];

    const animationPresets = [
        { id: "fade-in-up", label: "淡入上移", name: "idvbp-fade-in-up", duration: 520, easing: "cubic-bezier(.2,.8,.2,1)" },
        { id: "fade-out-up", label: "淡出上移", name: "idvbp-fade-out-up", duration: 420, easing: "ease" },
        { id: "pulse", label: "闪烁强调", name: "idvbp-pulse", duration: 560, easing: "ease" },
        { id: "pop", label: "弹出", name: "idvbp-pop", duration: 420, easing: "cubic-bezier(.2,.9,.2,1)" },
        { id: "shake", label: "抖动", name: "idvbp-shake", duration: 480, easing: "ease" },
        { id: "custom", label: "自定义 CSS", name: "idvbp-custom-animation", duration: 600, easing: "ease" }
    ];

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
        emitFrontendEvent,
        getNodeConfig,
        setNodeConfig,
        getNodeContext: nodeId => nodeContexts.get(nodeId)?.context || null,
        api: {
            fetchJson,
            endpoints: {
                rooms: "/api/rooms",
                currentRoom: "/api/rooms/current",
                signalR: "/hubs/game",
                localBpState: "/api/local-bp-state"
            },
            getRoom: () => store.room,
            getEvent: () => store.event,
            getConfig: getNodeConfig,
            setConfig: setNodeConfig
        }
    };

    document.addEventListener("DOMContentLoaded", boot);
    window.addEventListener("message", handleWindowMessage);

    async function boot() {
        rtLog("runtime", "info", "boot starting...");
        try {
            const options = readOptions();
            currentOptions = options;
            frontendBase = `/frontends/${encodeURIComponent(options.frontend)}`;
            rtLog("runtime", "info", "loading manifest frontend=", options.frontend, "page=", options.page);
            const manifest = await fetchJson(`${frontendBase}/manifest.json`);
            const layoutPath = resolveLayoutPath(manifest, options);
            currentLayoutPath = layoutPath;
            rtLog("runtime", "info", "loading layout=", layoutPath);
            const layout = await fetchJson(`${frontendBase}/${layoutPath}`);
            currentLayout = layout;
            rtLog("runtime", "info", "layout loaded nodes=", (layout.nodes || []).length);

            await loadComponents(manifest);
            rtLog("runtime", "info", "components loaded, registry size=", registry.size);
            await loadImplicitDesignerComponents(layout);
            rtLog("runtime", "info", "implicit designer components loaded, registry size=", registry.size);
            await loadInitialRoom(options.roomId);
            await loadComponentConfigs(options);
            renderLayout(layout);
            rtLog("runtime", "info", "layout rendered, nodeContexts=", nodeContexts.size);
            if (options.edit) {
                enableLayoutEditor(layout, options);
                await connectSignalR(options.roomId, collectEventTypes(layout));
                setRuntimeStatus("warning", "布局编辑器", "可编辑布局或动画规则，预览后保存。");
            } else {
                await connectSignalR(options.roomId, collectEventTypes(layout));
            }
            rtLog("runtime", "info", "boot complete");
        } catch (error) {
            rtLog("runtime", "error", "boot failed", error.message || String(error));
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

    async function loadImplicitDesignerComponents(layout) {
        const types = [...new Set(collectLayoutNodesFrom(layout.nodes || [])
            .map(node => node.type)
            .filter(type => type && !registry.has(type)))];
        for (const type of types) {
            const safeType = String(type).replace(/[^A-Za-z0-9_.-]/g, "");
            if (!safeType) {
                continue;
            }
            try {
                appendStylesheet(`${frontendBase}/components/designer/${safeType}.css`);
                await appendScript(`${frontendBase}/components/designer/${safeType}.js`);
            } catch {
                // Missing designer component files are rendered as placeholders later.
            }
        }
    }

    function collectLayoutNodesFrom(nodes) {
        const result = [];
        const visit = nodeList => {
            for (const node of nodeList || []) {
                result.push(node);
                visit(node.children);
            }
        };
        visit(nodes);
        return result;
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

        const current = await fetchJson("/api/rooms/current").catch(() => null);
        const currentRoom = readCurrentRoomPayload(current);
        if (currentRoom) {
            store.room = currentRoom;
            currentRoomId = getRoomId(currentRoom);
            return;
        }

        const rooms = normalizeRoomsResponse(await fetchJson("/api/rooms"));
        store.room = rooms.length > 0 ? rooms[0] : createEmptyRoom();
        currentRoomId = getRoomId(store.room);
    }

    async function loadComponentConfigs(options) {
        const pageId = getCurrentPageId();
        store.configs = {};
        try {
            const pageConfig = await fetchJson(`/api/frontends/${encodeURIComponent(options.frontend)}/pages/${encodeURIComponent(pageId)}/config`);
            store.pageConfig = pageConfig.value || "";
        } catch {
            store.pageConfig = "";
        }

        try {
            const response = await fetchJson(`/api/frontends/${encodeURIComponent(options.frontend)}/pages/${encodeURIComponent(pageId)}/components/config`);
            store.configs = response.values || {};
        } catch (error) {
            console.warn("Component configs failed to load.", error);
        }
    }

    function getCurrentPageId() {
        return currentOptions?.page || "main";
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
        fitStage(canvas);
        window.addEventListener("resize", () => fitStage(canvas));

        for (const node of layout.nodes || []) {
            renderNode(node, stageElement);
        }
    }

    function fitStage(canvas) {
        if (!stageElement) {
            return;
        }
        const width = canvas.width || 1920;
        const height = canvas.height || 1080;
        const scaleMode = String(canvas.scaleMode || canvas.fit || "contain").toLowerCase();
        if (scaleMode === "stretch") {
            const scaleX = window.innerWidth / width;
            const scaleY = window.innerHeight / height;
            stageElement.style.transform = `scale(${scaleX}, ${scaleY})`;
            return;
        }

        if (scaleMode === "cover") {
            const scale = Math.max(window.innerWidth / width, window.innerHeight / height);
            stageElement.style.transform = `scale(${scale})`;
            return;
        }

        const scale = Math.min(window.innerWidth / width, window.innerHeight / height);
        stageElement.style.transform = `scale(${scale})`;
    }

    function renderNode(node, parentElement) {
        const definition = registry.get(node.type) || createMissingComponentDefinition(node.type);
        ensureNodeRuntimeDefaults(node, definition);

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
            api: window.IdvbpLayoutRuntime.api,
            roomEvents: [...roomEvents],
            get config() {
                return parseConfigValue(getNodeConfig(node.id));
            },
            getConfig: getNodeConfig,
            setConfig: (value, targetNodeId = node.id) => setNodeConfig(targetNodeId, value),
            fetchJson,
            update: () => updateNode(node.id),
            emit: emitFrontendEvent
        };
        nodeContexts.set(node.id, { definition, context, props: {} });

        updateNode(node.id);

        for (const child of node.children || []) {
            renderNode(child, wrapper);
        }
    }

    function createMissingComponentDefinition(type) {
        return {
            render(element) {
                element.classList.add("runtime-missing-component");
                element.innerHTML = `
                    <strong>Component not registered</strong>
                    <span>${escapeHtml(type || "unknown")}</span>
                    <small>Check manifest.json and component script.</small>`;
            }
        };
    }

    function ensureNodeRuntimeDefaults(node, definition) {
        node.props = {
            room: { bind: "room" },
            event: { bind: "event" },
            config: { bind: `configs.${node.id}` },
            ...(node.props || {})
        };

        if (definition.actions?.syncState) {
            node.events = node.events || {};
            for (const eventType of roomEvents) {
                const actions = node.events[eventType] = Array.isArray(node.events[eventType])
                    ? node.events[eventType]
                    : [];
                if (!actions.some(action => action?.action === "syncState")) {
                    actions.push({ action: "syncState" });
                }
            }
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
        element.style.color = style.color || "";
        element.style.fontFamily = style.fontFamily ? `"${style.fontFamily}", sans-serif` : "";
        element.style.fontSize = style.fontSizeScale !== undefined ? `${Number(style.fontSizeScale) || 1}em` : "";
        element.classList.toggle("is-hidden", style.hidden === true);
        if (style.fontFamily && style.fontUrl) {
            ensureFontFace(style.fontFamily, style.fontUrl);
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
            <div class="layout-editor-title">布局编辑器</div>
            <div class="layout-editor-tabs">
                <button type="button" data-editor-mode="layout" class="is-active">布局</button>
                <button type="button" data-editor-mode="animation">动画编辑</button>
            </div>
            <section data-editor-section="layout">
                <label>控件<input data-field="id" readonly></label>
                <label>左侧<input data-field="left" type="number"></label>
                <label>顶部<input data-field="top" type="number"></label>
                <label>宽度<input data-field="width" type="number"></label>
                <label>高度<input data-field="height" type="number"></label>
                <label>层级<input data-field="zIndex" type="number"></label>
            </section>
            <section data-editor-section="animation" hidden>
                <label>规则<select data-animation-field="ruleId"></select></label>
                <div class="layout-editor-actions">
                    <button type="button" data-action="add-animation">新增规则</button>
                    <button type="button" data-action="delete-animation">删除</button>
                </div>
                <label>事件<select data-animation-field="eventType"></select></label>
                <label>目标<select data-animation-field="targetId"></select></label>
                <label>预设<select data-animation-field="preset"></select></label>
                <label>动画名<input data-animation-field="name"></label>
                <label>时长<input data-animation-field="duration" type="number" min="1"></label>
                <label>延迟<input data-animation-field="delay" type="number" min="0"></label>
                <label>重复<input data-animation-field="iterations"></label>
                <label>触发条件<textarea data-animation-field="condition" rows="3" placeholder="return true;"></textarea></label>
                <label>自定义 CSS<textarea data-animation-field="customCss" rows="6" placeholder="@keyframes my-animation { from { opacity: 0; } to { opacity: 1; } }"></textarea></label>
                <div class="layout-editor-actions">
                    <button type="button" data-action="preview-animation">预览动画</button>
                </div>
            </section>
            <section class="layout-editor-import">
                <label>组件库<select data-role="component-library"></select></label>
                <div class="layout-editor-actions">
                    <button type="button" data-action="import-component">导入组件</button>
                </div>
            </section>
            <div class="layout-editor-actions">
                <button type="button" data-action="save">保存布局</button>
                <button type="button" data-action="reload">重新加载</button>
            </div>
            <p data-role="status"></p>`;
        document.body.appendChild(editorPanel);
        createEditorContextMenu();
        document.addEventListener("click", hideEditorContextMenu);
        document.addEventListener("keydown", event => {
            if (event.key === "Escape") {
                hideEditorContextMenu();
            }
        });

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
        editorPanel.querySelector('[data-action="add-animation"]').addEventListener("click", addAnimationRule);
        editorPanel.querySelector('[data-action="delete-animation"]').addEventListener("click", deleteSelectedAnimationRule);
        editorPanel.querySelector('[data-action="preview-animation"]').addEventListener("click", previewSelectedAnimationRule);
        editorPanel.querySelector('[data-action="import-component"]').addEventListener("click", importSelectedComponent);
        editorPanel.addEventListener("click", event => {
            const button = event.target.closest("button[data-editor-mode]");
            if (button) {
                setEditorMode(button.dataset.editorMode);
            }
        });
        editorPanel.addEventListener("input", event => {
            const input = event.target.closest("[data-animation-field]");
            if (input) {
                updateSelectedAnimationRule(input);
            }
        });
        editorPanel.addEventListener("change", event => {
            const input = event.target.closest("[data-animation-field]");
            if (input) {
                updateSelectedAnimationRule(input);
            }
        });

        const firstNode = layout.nodes?.[0];
        if (firstNode) {
            selectEditorNode(firstNode.id);
        }
        normalizeAnimationRules();
        refreshAnimationEditor();
        loadEditorLibraries();
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
        box.addEventListener("contextmenu", event => {
            event.preventDefault();
            event.stopPropagation();
            selectEditorNode(node.id);
            showEditorContextMenu(event.clientX, event.clientY, node);
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

    function setEditorMode(mode) {
        editorMode = mode === "animation" ? "animation" : "layout";
        document.body.classList.toggle("is-animation-editing", editorMode === "animation");
        for (const button of editorPanel.querySelectorAll("button[data-editor-mode]")) {
            button.classList.toggle("is-active", button.dataset.editorMode === editorMode);
        }
        for (const section of editorPanel.querySelectorAll("[data-editor-section]")) {
            section.hidden = section.dataset.editorSection !== editorMode;
        }
        refreshAnimationEditor();
    }

    function normalizeAnimationRules() {
        currentLayout.animationRules = Array.isArray(currentLayout.animationRules)
            ? currentLayout.animationRules
            : [];
        for (const rule of currentLayout.animationRules) {
            rule.id = rule.id || createAnimationRuleId();
            rule.eventType = rule.eventType || roomEvents[0];
            rule.targetId = rule.targetId || currentLayout.nodes?.[0]?.id || "";
            rule.preset = rule.preset || "fade-in-up";
            const preset = getAnimationPreset(rule.preset);
            rule.name = rule.name || preset.name;
            rule.duration = Number(rule.duration) || preset.duration;
            rule.delay = Number(rule.delay) || 0;
            rule.easing = rule.easing || preset.easing;
            rule.iterations = rule.iterations || "1";
            rule.fillMode = rule.fillMode || "both";
            rule.condition = rule.condition || "";
            rule.customCss = rule.customCss || "";
        }
        selectedAnimationRuleId = selectedAnimationRuleId || currentLayout.animationRules[0]?.id || "";
    }

    function refreshAnimationEditor() {
        if (!editorPanel) {
            return;
        }

        normalizeAnimationRules();
        fillSelect(
            editorPanel.querySelector('[data-animation-field="eventType"]'),
            [...new Set([...roomEvents, ...collectAnimationEventTypes()])].map(value => ({ value, label: value })));
        fillSelect(
            editorPanel.querySelector('[data-animation-field="targetId"]'),
            collectLayoutNodes().map(node => ({ value: node.id, label: node.id })));
        fillSelect(
            editorPanel.querySelector('[data-animation-field="preset"]'),
            animationPresets.map(preset => ({ value: preset.id, label: preset.label })));
        fillSelect(
            editorPanel.querySelector('[data-animation-field="ruleId"]'),
            currentLayout.animationRules.map(rule => ({
                value: rule.id,
                label: `${rule.eventType || "event"} -> ${rule.targetId || "target"}`
            })));

        const rule = getSelectedAnimationRule();
        for (const input of editorPanel.querySelectorAll("[data-animation-field]")) {
            const field = input.dataset.animationField;
            if (field === "ruleId") {
                input.value = selectedAnimationRuleId;
            } else {
                input.value = rule ? rule[field] ?? "" : "";
            }
        }
    }

    function fillSelect(select, options) {
        if (!select) {
            return;
        }
        const current = select.value;
        select.textContent = "";
        for (const option of options) {
            const element = document.createElement("option");
            element.value = option.value;
            element.textContent = option.label;
            select.appendChild(element);
        }
        if (options.some(option => option.value === current)) {
            select.value = current;
        }
    }

    function collectAnimationEventTypes() {
        return (currentLayout.animationRules || [])
            .map(rule => rule.eventType)
            .filter(Boolean);
    }

    function collectLayoutNodes() {
        const result = [];
        const visit = nodes => {
            for (const node of nodes || []) {
                result.push(node);
                visit(node.children);
            }
        };
        visit(currentLayout?.nodes);
        return result;
    }

    function addAnimationRule() {
        normalizeAnimationRules();
        const targetId = selectedNodeId || currentLayout.nodes?.[0]?.id || "";
        const preset = getAnimationPreset("fade-in-up");
        const rule = {
            id: createAnimationRuleId(),
            eventType: roomEvents[0],
            targetId,
            preset: preset.id,
            name: preset.name,
            duration: preset.duration,
            delay: 0,
            easing: preset.easing,
            iterations: "1",
            fillMode: "both",
            condition: "",
            customCss: ""
        };
        currentLayout.animationRules.push(rule);
        selectedAnimationRuleId = rule.id;
        refreshAnimationEditor();
    }

    function deleteSelectedAnimationRule() {
        normalizeAnimationRules();
        currentLayout.animationRules = currentLayout.animationRules.filter(rule => rule.id !== selectedAnimationRuleId);
        selectedAnimationRuleId = currentLayout.animationRules[0]?.id || "";
        refreshAnimationEditor();
    }

    function updateSelectedAnimationRule(input) {
        normalizeAnimationRules();
        const field = input.dataset.animationField;
        if (field === "ruleId") {
            selectedAnimationRuleId = input.value;
            refreshAnimationEditor();
            return;
        }

        const rule = getSelectedAnimationRule();
        if (!rule) {
            return;
        }

        if (field === "duration" || field === "delay") {
            rule[field] = Number(input.value) || 0;
        } else {
            rule[field] = input.value;
        }

        if (field === "preset") {
            const preset = getAnimationPreset(rule.preset);
            rule.name = preset.name;
            rule.duration = preset.duration;
            rule.easing = preset.easing;
            if (preset.id === "custom" && !rule.customCss) {
                rule.customCss = "@keyframes idvbp-custom-animation {\n  from { opacity: 0; transform: scale(.96); }\n  to { opacity: 1; transform: scale(1); }\n}";
            }
            refreshAnimationEditor();
            return;
        }

        const ruleOption = editorPanel.querySelector(`[data-animation-field="ruleId"] option[value="${CSS.escape(rule.id)}"]`);
        if (ruleOption) {
            ruleOption.textContent = `${rule.eventType || "event"} -> ${rule.targetId || "target"}`;
        }
    }

    function previewSelectedAnimationRule() {
        const rule = getSelectedAnimationRule();
        if (!rule) {
            return;
        }
        const target = nodeContexts.get(rule.targetId)?.context.element;
        if (target) {
            playCssAnimation(target, rule);
        }
    }

    function getSelectedAnimationRule() {
        normalizeAnimationRules();
        return currentLayout.animationRules.find(rule => rule.id === selectedAnimationRuleId) || null;
    }

    function createAnimationRuleId() {
        return `animation-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 7)}`;
    }

    function getAnimationPreset(id) {
        return animationPresets.find(preset => preset.id === id) || animationPresets[0];
    }

    function createEditorContextMenu() {
        editorContextMenu = document.createElement("div");
        editorContextMenu.className = "layout-editor-menu";
        editorContextMenu.hidden = true;
        editorContextMenu.innerHTML = `
            <div class="layout-editor-menu-title">组件设置</div>
            <label>字体颜色<input type="color" data-menu-field="color"></label>
            <label>字体文件<select data-menu-field="fontFamily"></select></label>
            <label>字体倍率<input type="number" min="0.1" step="0.1" data-menu-field="fontSizeScale"></label>
            <label class="layout-editor-check"><input type="checkbox" data-menu-field="hidden"> 隐藏组件</label>
            <div class="layout-editor-actions">
                <button type="button" data-menu-action="upload-font">导入字体</button>
            </div>
            <div class="layout-editor-menu-custom" data-menu-role="custom-items"></div>
            <input type="file" data-menu-role="font-file" accept=".ttf,.otf,.woff,.woff2" hidden>`;
        document.body.appendChild(editorContextMenu);
        const deleteNodeButton = document.createElement("button");
        deleteNodeButton.type = "button";
        deleteNodeButton.dataset.menuAction = "delete-node";
        deleteNodeButton.textContent = "删除组件";
        editorContextMenu.querySelector(".layout-editor-actions")?.appendChild(deleteNodeButton);

        editorContextMenu.addEventListener("click", event => event.stopPropagation());
        editorContextMenu.addEventListener("input", updateNodeStyleFromMenu);
        editorContextMenu.addEventListener("change", updateNodeStyleFromMenu);
        editorContextMenu.addEventListener("click", handleCustomContextMenuClick);
        editorContextMenu.addEventListener("input", handleCustomContextMenuInput);
        editorContextMenu.addEventListener("change", handleCustomContextMenuInput);
        editorContextMenu.querySelector('[data-menu-action="upload-font"]').addEventListener("click", () => {
            editorContextMenu.querySelector('[data-menu-role="font-file"]').click();
        });
        editorContextMenu.querySelector('[data-menu-action="delete-node"]').addEventListener("click", deleteSelectedNode);
        editorContextMenu.querySelector('[data-menu-role="font-file"]').addEventListener("change", importFontFromMenu);
    }

    function showEditorContextMenu(x, y, node) {
        if (!editorContextMenu) {
            return;
        }

        refreshFontSelect();
        const style = node.style || {};
        editorContextMenu.querySelector('[data-menu-field="color"]').value = normalizeColorInput(style.color || "#f7f7f2");
        editorContextMenu.querySelector('[data-menu-field="fontFamily"]').value = style.fontFamily || "";
        editorContextMenu.querySelector('[data-menu-field="fontSizeScale"]').value = style.fontSizeScale ?? 1;
        editorContextMenu.querySelector('[data-menu-field="hidden"]').checked = style.hidden === true;
        renderCustomContextMenuItems(node);

        editorContextMenu.hidden = false;
        const rect = editorContextMenu.getBoundingClientRect();
        editorContextMenu.style.left = `${Math.min(x, window.innerWidth - rect.width - 8)}px`;
        editorContextMenu.style.top = `${Math.min(y, window.innerHeight - rect.height - 8)}px`;
    }

    function hideEditorContextMenu() {
        if (editorContextMenu) {
            editorContextMenu.hidden = true;
        }
    }

    function renderCustomContextMenuItems(node) {
        const container = editorContextMenu?.querySelector('[data-menu-role="custom-items"]');
        if (!container) {
            return;
        }

        editorContextMenuItems = [];
        container.textContent = "";
        const items = getCustomContextMenuItems(node);
        if (items.length === 0) {
            container.hidden = true;
            return;
        }

        container.hidden = false;
        const title = document.createElement("div");
        title.className = "layout-editor-menu-section-title";
        title.textContent = "组件菜单";
        container.appendChild(title);

        items.forEach((item, index) => {
            editorContextMenuItems[index] = item;
            container.appendChild(createCustomContextMenuElement(item, index));
        });
    }

    function getCustomContextMenuItems(node) {
        const entry = nodeContexts.get(node.id);
        const provider = entry?.definition?.contextMenu || entry?.definition?.editorContextMenu;
        if (!provider) {
            return [];
        }

        const helpers = createContextMenuHelpers(node, entry);
        try {
            const result = typeof provider === "function"
                ? provider({
                    node,
                    context: entry.context,
                    props: entry.props || {},
                    config: entry.context.config,
                    helpers
                })
                : provider;
            return Array.isArray(result) ? result.filter(Boolean) : [];
        } catch (error) {
            console.warn(`Context menu failed for node '${node.id}'.`, error);
            return [];
        }
    }

    function createCustomContextMenuElement(item, index) {
        if (item.type === "separator") {
            const separator = document.createElement("div");
            separator.className = "layout-editor-menu-separator";
            return separator;
        }

        if (item.type === "button") {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "layout-editor-menu-button";
            button.dataset.customMenuIndex = String(index);
            button.textContent = item.label || item.text || "Action";
            return button;
        }

        const label = document.createElement("label");
        label.textContent = item.label || item.name || "";
        const input = document.createElement(item.type === "select" ? "select" : "input");
        input.dataset.customMenuIndex = String(index);
        input.dataset.customMenuField = item.name || item.key || "";

        if (item.type === "file") {
            input.type = "file";
            input.accept = item.accept || "";
        } else if (item.type === "checkbox") {
            input.type = "checkbox";
            input.checked = item.checked === true || item.value === true;
            label.className = "layout-editor-check";
        } else if (item.type === "color") {
            input.type = "color";
            input.value = normalizeColorInput(item.value || "#f7f7f2");
        } else if (item.type === "number") {
            input.type = "number";
            if (item.min !== undefined) input.min = item.min;
            if (item.max !== undefined) input.max = item.max;
            if (item.step !== undefined) input.step = item.step;
            input.value = item.value ?? "";
        } else if (item.type === "select") {
            for (const option of item.options || []) {
                const optionElement = document.createElement("option");
                optionElement.value = option.value ?? option;
                optionElement.textContent = option.label ?? option.value ?? option;
                input.appendChild(optionElement);
            }
            input.value = item.value ?? "";
        } else {
            input.type = "text";
            input.value = item.value ?? "";
        }

        label.appendChild(input);
        return label;
    }

    async function handleCustomContextMenuClick(event) {
        const button = event.target.closest("button[data-custom-menu-index]");
        if (!button || !selectedNodeId) {
            return;
        }

        const item = editorContextMenuItems[Number(button.dataset.customMenuIndex)];
        if (item?.type !== "button") {
            return;
        }

        await runCustomContextMenuItem(item, null);
    }

    async function handleCustomContextMenuInput(event) {
        const input = event.target.closest("[data-custom-menu-index]");
        if (!input || !selectedNodeId) {
            return;
        }

        const item = editorContextMenuItems[Number(input.dataset.customMenuIndex)];
        if (!item || item.type === "button") {
            return;
        }

        let value = input.type === "checkbox" ? input.checked : input.value;
        if (input.type === "file") {
            value = input.files?.[0] || null;
            input.value = "";
            if (!value) {
                return;
            }
        }

        await runCustomContextMenuItem(item, value);
    }

    async function runCustomContextMenuItem(item, value) {
        const node = findLayoutNode(selectedNodeId);
        const entry = selectedNodeId ? nodeContexts.get(selectedNodeId) : null;
        if (!node || !entry) {
            return;
        }

        const helpers = createContextMenuHelpers(node, entry);
        try {
            if (typeof item.onSelect === "function") {
                await item.onSelect(value, helpers);
            } else if (typeof item.onChange === "function") {
                await item.onChange(value, helpers);
            } else if (typeof item.action === "function") {
                await item.action(value, helpers);
            }
            syncNodeFromLayout(node);
            renderCustomContextMenuItems(node);
        } catch (error) {
            setRuntimeStatus("error", "组件菜单执行失败", error.message || String(error));
        }
    }

    function createContextMenuHelpers(node, entry) {
        return {
            node,
            context: entry.context,
            props: entry.props || {},
            get config() {
                return entry.context.config || {};
            },
            setConfig: async value => {
                await setNodeConfig(node.id, value);
            },
            update: () => updateNode(node.id),
            close: hideEditorContextMenu,
            importAsset: (file, category) => importFrontendAsset(file, category)
        };
    }

    async function importFrontendAsset(file, category = "assets") {
        if (!currentOptions?.frontend || !file) {
            throw new Error("No frontend package or file selected.");
        }

        const form = new FormData();
        form.append("file", file);
        form.append("category", category);
        const response = await fetch(`/api/frontends/${encodeURIComponent(currentOptions.frontend)}/assets/import`, {
            method: "POST",
            body: form
        });
        if (!response.ok) {
            const text = await response.text();
            throw new Error(text || `Asset import failed: ${response.status}`);
        }
        return response.json();
    }

    function deleteSelectedNode() {
        if (!selectedNodeId || !currentLayout?.nodes) {
            return;
        }

        const node = findLayoutNode(selectedNodeId);
        if (!node) {
            return;
        }

        const removedIds = collectNodeIds(node);
        if (!removeLayoutNode(currentLayout.nodes, selectedNodeId)) {
            return;
        }

        for (const nodeId of removedIds) {
            nodeContexts.get(nodeId)?.context.element.remove();
            nodeContexts.delete(nodeId);
            editorLayer?.querySelector(`.layout-editor-box[data-node-id="${CSS.escape(nodeId)}"]`)?.remove();
        }

        if (Array.isArray(currentLayout.animationRules)) {
            currentLayout.animationRules = currentLayout.animationRules.filter(rule => !removedIds.includes(rule.targetId));
        }

        hideEditorContextMenu();
        selectedNodeId = currentLayout.nodes[0]?.id || "";
        if (selectedNodeId) {
            selectEditorNode(selectedNodeId);
        } else {
            refreshEditorPanel();
        }
        refreshAnimationEditor();
        setRuntimeStatus("ok", "组件已删除", node.id);
    }

    function collectNodeIds(node) {
        return [
            node.id,
            ...(node.children || []).flatMap(collectNodeIds)
        ].filter(Boolean);
    }

    function removeLayoutNode(nodes, nodeId) {
        const index = nodes.findIndex(node => node.id === nodeId);
        if (index >= 0) {
            nodes.splice(index, 1);
            return true;
        }

        for (const node of nodes) {
            if (removeLayoutNode(node.children || [], nodeId)) {
                return true;
            }
        }
        return false;
    }

    function updateNodeStyleFromMenu(event) {
        const input = event.target.closest("[data-menu-field]");
        if (!input || !selectedNodeId) {
            return;
        }

        const node = findLayoutNode(selectedNodeId);
        if (!node) {
            return;
        }

        const field = input.dataset.menuField;
        node.style = node.style || {};
        if (field === "hidden") {
            node.style.hidden = input.checked;
        } else if (field === "fontSizeScale") {
            node.style.fontSizeScale = Number(input.value) || 1;
        } else if (field === "fontFamily") {
            const font = availableFonts.find(item => item.family === input.value);
            node.style.fontFamily = font?.family || "";
            node.style.fontUrl = font?.url || "";
        } else {
            node.style[field] = input.value;
        }
        syncNodeFromLayout(node);
    }

    async function importFontFromMenu(event) {
        const file = event.target.files?.[0];
        event.target.value = "";
        if (!file) {
            return;
        }

        const form = new FormData();
        form.append("file", file);
        try {
            const response = await fetch("/api/frontends/fonts/import", {
                method: "POST",
                body: form
            });
            if (!response.ok) {
                const text = await response.text();
                throw new Error(text || `Font import failed: ${response.status}`);
            }
            const font = await response.json();
            availableFonts = [...availableFonts.filter(item => item.family !== font.family), font];
            refreshFontSelect();
            const select = editorContextMenu.querySelector('[data-menu-field="fontFamily"]');
            select.value = font.family;
            updateNodeStyleFromMenu({ target: select });
            setRuntimeStatus("ok", "字体已导入", font.family);
        } catch (error) {
            setRuntimeStatus("error", "字体导入失败", error.message || String(error));
        }
    }

    function refreshFontSelect() {
        const select = editorContextMenu?.querySelector('[data-menu-field="fontFamily"]');
        if (!select) {
            return;
        }
        fillSelect(select, [
            { value: "", label: "默认字体" },
            ...availableFonts.map(font => ({ value: font.family, label: font.family }))
        ]);
    }

    function normalizeColorInput(value) {
        return /^#[0-9a-f]{6}$/i.test(value) ? value : "#f7f7f2";
    }

    async function loadEditorLibraries() {
        try {
            const [fontsResponse, componentsResponse] = await Promise.all([
                fetch("/api/frontends/fonts", { cache: "no-store" }),
                fetch("/api/frontends/components", { cache: "no-store" })
            ]);
            availableFonts = fontsResponse.ok ? await fontsResponse.json() : [];
            availableComponents = componentsResponse.ok ? await componentsResponse.json() : [];
            for (const font of availableFonts) {
                ensureFontFace(font.family, font.url);
            }
            refreshFontSelect();
            refreshComponentLibrary();
        } catch (error) {
            console.warn("Editor libraries failed to load.", error);
        }
    }

    function refreshComponentLibrary() {
        const select = editorPanel?.querySelector('[data-role="component-library"]');
        if (!select) {
            return;
        }
        fillSelect(select, availableComponents.map(component => ({
            value: `${component.packageId}::${component.type}`,
            label: `${component.packageName} / ${component.type}`
        })));
    }

    async function importSelectedComponent() {
        const select = editorPanel?.querySelector('[data-role="component-library"]');
        const value = select?.value || "";
        const [sourcePackageId, type] = value.split("::");
        if (!sourcePackageId || !type) {
            return;
        }

        try {
            const response = await fetch(`/api/frontends/${encodeURIComponent(currentOptions.frontend)}/components/import`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ sourcePackageId, type })
            });
            if (!response.ok) {
                const text = await response.text();
                throw new Error(text || `Component import failed: ${response.status}`);
            }

            const component = await response.json();
            await loadImportedComponent(component);
            addImportedComponentNode(component.type);
            await refreshRoomSnapshot();
            await resubscribeCurrentLayoutEvents();
            setRuntimeStatus("ok", "组件已导入", component.type);
        } catch (error) {
            setRuntimeStatus("error", "组件导入失败", error.message || String(error));
        }
    }

    async function loadImportedComponent(component) {
        const base = `/frontends/${encodeURIComponent(currentOptions.frontend)}`;
        if (component.style) {
            appendStylesheet(`${base}/${component.style}`);
        }
        if (component.script) {
            await appendScript(`${base}/${component.script}`);
        }
    }

    function addImportedComponentNode(type) {
        const canvas = currentLayout.canvas || {};
        const idBase = `${type}-${Date.now().toString(36)}`;
        const node = {
            id: idBase,
            type,
            props: {
                room: { bind: "room" },
                event: { bind: "event" },
                config: { bind: `configs.${idBase}` }
            },
            events: Object.fromEntries(roomEvents.map(eventType => [
                eventType,
                [{ action: "syncState" }]
            ])),
            style: {
                left: Math.round((canvas.width || 1920) / 2 - 160),
                top: Math.round((canvas.height || 1080) / 2 - 90),
                width: 320,
                height: 180,
                zIndex: 10
            }
        };
        currentLayout.nodes = currentLayout.nodes || [];
        currentLayout.nodes.push(node);
        renderNode(node, stageElement);
        createEditorBox(node);
        selectEditorNode(node.id);
    }

    function ensureFontFace(family, url) {
        if (!family || !url) {
            return;
        }
        const styleId = `runtime-font-${CSS.escape(family)}`;
        if (document.getElementById(styleId)) {
            return;
        }
        const style = document.createElement("style");
        style.id = styleId;
        style.textContent = `@font-face { font-family: "${family.replaceAll('"', '\\"')}"; src: url("${url}"); font-display: swap; }`;
        document.head.appendChild(style);
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
                status.textContent = "正在保存...";
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
                status.textContent = "已保存。";
            }
            setRuntimeStatus("ok", "布局已保存", currentLayoutPath);
        } catch (error) {
            if (status) {
                status.textContent = error.message || String(error);
            }
            setRuntimeStatus("error", "布局保存失败", error.message || String(error));
        }
    }

    async function handleWindowMessage(event) {
        const data = event?.data;
        if (!data || typeof data !== "object") {
            return;
        }

        if (data.type !== "asg:frontend-page-config-dirty") {
            return;
        }

        const options = currentOptions;
        if (!options?.frontend) {
            return;
        }

        const pageId = getCurrentPageId();
        const value = typeof data.value === "string"
            ? data.value
            : JSON.stringify(data.value ?? {});
        const sourceNodeId = data.nodeId || findNodeIdByMessageSource(event.source);

        try {
            const url = sourceNodeId
                ? `/api/frontends/${encodeURIComponent(options.frontend)}/pages/${encodeURIComponent(pageId)}/components/${encodeURIComponent(sourceNodeId)}/config`
                : `/api/frontends/${encodeURIComponent(options.frontend)}/pages/${encodeURIComponent(pageId)}/config`;
            const response = await fetch(url, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ value })
            });
            if (!response.ok) {
                const text = await response.text();
                throw new Error(text || `Save failed: ${response.status}`);
            }
            if (sourceNodeId) {
                store.configs[sourceNodeId] = value;
                updateAllNodes();
            } else {
                store.pageConfig = value;
            }
            setRuntimeStatus("ok", sourceNodeId ? "Component config saved" : "Page config saved", sourceNodeId || `${options.frontend}/${pageId}`);
        } catch (error) {
            console.error("Frontend config save failed.", error);
            setRuntimeStatus("error", "Config save failed", error.message || String(error));
        }
    }

    function findNodeIdByMessageSource(source) {
        if (!source) {
            return "";
        }

        for (const [nodeId, entry] of nodeContexts.entries()) {
            const frames = entry.context.element.querySelectorAll("iframe");
            for (const frame of frames) {
                if (frame.contentWindow === source) {
                    return nodeId;
                }
            }
        }
        return "";
    }

    function getNodeConfig(nodeId) {
        return store.configs?.[nodeId] ?? "";
    }

    async function setNodeConfig(nodeId, value) {
        const serialized = typeof value === "string" ? value : JSON.stringify(value ?? {});
        const options = currentOptions;
        if (!options?.frontend || !nodeId) {
            return;
        }

        const pageId = getCurrentPageId();
        const response = await fetch(`/api/frontends/${encodeURIComponent(options.frontend)}/pages/${encodeURIComponent(pageId)}/components/${encodeURIComponent(nodeId)}/config`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ value: serialized })
        });
        if (!response.ok) {
            const text = await response.text();
            throw new Error(text || `Save failed: ${response.status}`);
        }
        store.configs[nodeId] = serialized;
        updateAllNodes();
    }

    function parseConfigValue(value) {
        if (!value || typeof value !== "string") {
            return value || null;
        }
        try {
            return JSON.parse(value);
        } catch {
            return value;
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

    function readCurrentRoomPayload(payload) {
        if (!payload || typeof payload !== "object") {
            return null;
        }

        const room = payload.room || payload.Room;
        if (room && typeof room === "object") {
            return room;
        }

        const roomId = payload.roomId || payload.RoomId || "";
        return roomId ? { ...createEmptyRoom(), roomId, roomName: payload.roomName || payload.RoomName || roomId } : null;
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
        currentRoomId = effectiveRoomId || "";

        if (!window.signalR) {
            setRuntimeStatus("error", "SignalR unavailable", "本地 SignalR 客户端脚本未加载。");
            return;
        }

        signalRConnection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/game")
            .withAutomaticReconnect()
            .build();

        signalRConnection.on("RoomEvent", envelope => handleRoomEvent(envelope));
        signalRConnection.on("CurrentRoomChanged", payload => handleCurrentRoomChanged(payload, eventTypes));
        signalRConnection.onreconnecting(error => {
            setRuntimeStatus("warning", "SignalR reconnecting", error?.message || "连接中断，正在重连。");
        });
        signalRConnection.onreconnected(async () => {
            setRuntimeStatus("ok", "SignalR reconnected", currentRoomId ? `roomId=${currentRoomId}` : "waiting for current room");
            if (currentRoomId) {
                await subscribeSignalR(currentRoomId, eventTypes);
            }
            await requestCurrentRoom(eventTypes);
        });
        signalRConnection.onclose(error => {
            setRuntimeStatus("error", "SignalR closed", error?.message || "连接已关闭。");
        });

        try {
            setRuntimeStatus("warning", "SignalR connecting", effectiveRoomId ? `roomId=${effectiveRoomId}` : "waiting for current room");
            await signalRConnection.start();
            if (effectiveRoomId) {
                await subscribeSignalR(effectiveRoomId, eventTypes);
                setRuntimeStatus("ok", "SignalR connected", `roomId=${effectiveRoomId}，已订阅 ${eventTypes.length} 个事件。`);
            } else {
                setRuntimeStatus("warning", "SignalR waiting", "等待当前房间选择。");
            }
            await requestCurrentRoom(eventTypes);
        } catch (error) {
            setRuntimeStatus("error", "SignalR failed", error.message || String(error));
        }
    }

    async function subscribeSignalR(roomId, eventTypes) {
        if (!roomId) {
            return;
        }

        await signalRConnection.invoke("JoinRoom", roomId);
        const supportedEvents = eventTypes.filter(eventType => roomEvents.includes(eventType));
        if (supportedEvents.length > 0) {
            await signalRConnection.invoke("ReplaceSubscriptions", roomId, supportedEvents);
        }
        await signalRConnection.invoke("RequestRoomSnapshot", roomId);
    }

    async function requestCurrentRoom(eventTypes) {
        if (!signalRConnection || signalRConnection.state !== "Connected") {
            return;
        }

        const payload = await signalRConnection.invoke("RequestCurrentRoom").catch(() => null);
        if (payload) {
            await handleCurrentRoomChanged(payload, eventTypes);
        }
    }

    async function handleCurrentRoomChanged(payload, eventTypes) {
        const nextRoom = readCurrentRoomPayload(payload);
        const nextRoomId = getRoomId(nextRoom) || payload?.roomId || payload?.RoomId || "";
        if (!nextRoomId || nextRoomId === currentRoomId) {
            return;
        }

        const previousRoomId = currentRoomId;
        currentRoomId = nextRoomId;
        store.room = nextRoom;
        updateAllNodes();

        if (signalRConnection?.state !== "Connected") {
            return;
        }

        if (previousRoomId) {
            await signalRConnection.invoke("LeaveRoom", previousRoomId).catch(() => {});
        }

        await subscribeSignalR(nextRoomId, eventTypes);
        setRuntimeStatus("ok", "Current room changed", `roomId=${nextRoomId}`);
    }

    async function resubscribeCurrentLayoutEvents() {
        if (!signalRConnection || !currentRoomId || signalRConnection.state !== "Connected") {
            return;
        }
        await subscribeSignalR(currentRoomId, collectEventTypes(currentLayout || {}));
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
        for (const rule of layout.animationRules || []) {
            if (rule.eventType) {
                eventTypes.add(rule.eventType);
            }
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
        rtLog("runtime", "info", "dispatchLayoutEvent type=", eventType, "nodes=", nodeContexts.size);
        for (const entry of nodeContexts.values()) {
            const actions = entry.context.node.events?.[eventType] || [];
            const hasSyncState = !!entry.definition.actions?.syncState;
            rtLog("runtime", "info", "node=", entry.context.node.id, "type=", entry.context.node.type, "actions=", actions.length, "hasSyncState=", hasSyncState);
            if (hasSyncState) {
                var cfg = entry.context.config;
                var cfgType = typeof cfg;
                var cfgRules = cfg && typeof cfg === "object" ? (Array.isArray(cfg.rules) ? cfg.rules.length : "no rules array") : "no config obj";
                rtLog("runtime", "info", "node=", entry.context.node.id, "configType=", cfgType, "configRules=", cfgRules, "configKeys=", cfg && typeof cfg === "object" ? Object.keys(cfg).join(",") : "N/A");
                if (Array.isArray(cfg?.rules)) {
                    for (var ri = 0; ri < cfg.rules.length; ri++) {
                        rtLog("runtime", "info", "rule[", ri, "] event=", cfg.rules[ri].event, "action=", cfg.rules[ri].action, "targetId=", cfg.rules[ri].targetId);
                    }
                }
            }
            for (const action of actions) {
                runAction(entry, action, event);
            }
            if (hasSyncState && !actions.some(a => a?.action === "syncState")) {
                rtLog("runtime", "info", "fallback syncState for", entry.context.node.id);
                entry.definition.actions.syncState(entry.context.element, { action: "syncState" }, entry.context, event);
            }
        }
        dispatchAnimationRules(eventType, event);
    }

    function dispatchAnimationRules(eventType, event) {
        for (const rule of currentLayout?.animationRules || []) {
            if (rule.eventType !== eventType || !evaluateAnimationCondition(rule, event)) {
                continue;
            }

            const target = nodeContexts.get(rule.targetId);
            if (target) {
                playCssAnimation(target.context.element, rule);
            }
        }
    }

    function runAction(entry, action, event) {
        const targetEntry = action.targetId ? nodeContexts.get(action.targetId) || entry : entry;
        const element = targetEntry.context.element;
        const name = action.action;
        if (targetEntry.definition.actions?.[name]) {
            targetEntry.definition.actions[name](element, action, targetEntry.context, event);
            return;
        }

        switch (name) {
            case "playAnimation":
                playAnimation(element, action.name);
                break;
            case "playCssAnimation":
                playCssAnimation(element, action);
                break;
            case "stopAnimation":
                element.classList.remove("is-entering", "is-leaving", "is-pulsing");
                element.style.animation = "";
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
                targetEntry.context.node.props = { ...(targetEntry.context.node.props || {}), [action.name]: action.value };
                updateNode(targetEntry.context.node.id);
                break;
            case "emit":
                emitFrontendEvent(action.type, action.payload);
                break;
            default:
                if (name === "syncState") {
                    return;
                }
                console.warn(`Unsupported action '${name}' on node '${targetEntry.context.node.id}'.`);
        }
    }

    function playAnimation(element, name) {
        const className = name === "leave" ? "is-leaving" : name === "pulse" ? "is-pulsing" : "is-entering";
        element.classList.remove("is-entering", "is-leaving", "is-pulsing");
        void element.offsetWidth;
        element.classList.add(className);
    }

    function playCssAnimation(element, action) {
        const preset = getAnimationPreset(action.preset);
        const animationName = action.name || preset.name;
        const duration = Math.max(1, Number(action.duration) || preset.duration);
        const delay = Math.max(0, Number(action.delay) || 0);
        const easing = action.easing || preset.easing || "ease";
        const iterations = action.iterations || "1";
        const fillMode = action.fillMode || "both";

        ensureCustomAnimationStyle(action);
        element.style.animation = "none";
        void element.offsetWidth;
        element.style.animation = `${animationName} ${duration}ms ${easing} ${delay}ms ${iterations} ${fillMode}`;
    }

    function ensureCustomAnimationStyle(action) {
        if (!action.customCss || !String(action.customCss).trim()) {
            return;
        }

        const styleId = `runtime-animation-${CSS.escape(action.id || action.name || "custom")}`;
        let style = document.getElementById(styleId);
        if (!style) {
            style = document.createElement("style");
            style.id = styleId;
            document.head.appendChild(style);
        }
        style.textContent = action.customCss;
    }

    function evaluateAnimationCondition(rule, event) {
        const source = String(rule.condition || "").trim();
        if (!source) {
            return true;
        }

        try {
            const body = /\breturn\b/.test(source) ? source : `return (${source});`;
            const target = nodeContexts.get(rule.targetId)?.context;
            const evaluator = new Function("store", "event", "payload", "target", "get", body);
            return evaluator(store, event, event?.payload, target, getByPath) !== false;
        } catch (error) {
            console.warn(`Animation condition failed for rule '${rule.id}'.`, error);
            return false;
        }
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
