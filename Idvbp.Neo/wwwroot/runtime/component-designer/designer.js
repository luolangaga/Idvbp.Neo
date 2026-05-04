(() => {
    const params = new URLSearchParams(window.location.search);
    const state = {
        frontend: params.get("frontend") || "",
        page: params.get("page") || "main",
        layout: params.get("layout") || "layout.json",
        workspace: null,
        selectedElementId: "title",
        loadedNodeId: "",
        assetMode: "image",
        elements: [
            {
                id: "title",
                type: "text",
                text: "New Component",
                left: 40,
                top: 40,
                width: 300,
                height: 72,
                zIndex: 1,
                color: "#f7f7f2",
                fontSize: 28,
                fit: "cover",
                src: "",
                url: ""
            }
        ]
    };

    const fields = {
        targetText: byId("targetText"),
        type: byId("typeInput"),
        nodeId: byId("nodeIdInput"),
        existingComponent: byId("existingComponentSelect"),
        componentWidth: byId("componentWidthInput"),
        componentHeight: byId("componentHeightInput"),
        componentBackground: byId("componentBackgroundInput"),
        componentRadius: byId("componentRadiusInput"),
        preview: byId("componentPreview"),
        elementId: byId("elementIdInput"),
        elementType: byId("elementTypeInput"),
        elementLeft: byId("elementLeftInput"),
        elementTop: byId("elementTopInput"),
        elementWidth: byId("elementWidthInput"),
        elementHeight: byId("elementHeightInput"),
        elementZ: byId("elementZInput"),
        elementText: byId("elementTextInput"),
        elementColor: byId("elementColorInput"),
        elementFontSize: byId("elementFontSizeInput"),
        elementFit: byId("elementFitInput"),
        asset: byId("assetInput"),
        code: byId("codeOutput"),
        status: byId("statusText"),
        toast: byId("toast"),
        toastTitle: byId("toastTitle"),
        toastDetail: byId("toastDetail")
    };

    fields.targetText.textContent = `${state.frontend || "unknown"} / ${state.page} / ${state.layout}`;
    let autoNodeId = fields.nodeId.value;
    let autoSaveTimer = 0;

    initBlockly();
    wireUi();
    renderDesigner();
    update();
    loadExistingComponents();

    function byId(id) {
        return document.getElementById(id);
    }

    function wireUi() {
        for (const input of [
            fields.type,
            fields.nodeId,
            fields.componentWidth,
            fields.componentHeight,
            fields.componentBackground,
            fields.componentRadius
        ]) {
            input.addEventListener("input", update);
            input.addEventListener("input", scheduleAutoSave);
        }
        fields.type.addEventListener("input", () => {
            if (!fields.nodeId.value || fields.nodeId.value === autoNodeId) {
                autoNodeId = sanitizeType(fields.type.value) || "designer-widget";
                fields.nodeId.value = autoNodeId;
            }
        });
        fields.nodeId.addEventListener("input", () => {
            autoNodeId = fields.nodeId.value;
        });
        fields.existingComponent.addEventListener("change", () => {
            const [type, nodeId] = String(fields.existingComponent.value || "").split("::");
            if (type && nodeId) {
                loadExistingDesignerComponent(type, nodeId);
            }
        });

        for (const input of [
            fields.elementId,
            fields.elementLeft,
            fields.elementTop,
            fields.elementWidth,
            fields.elementHeight,
            fields.elementZ,
            fields.elementText,
            fields.elementColor,
            fields.elementFontSize,
            fields.elementFit
        ]) {
            input.addEventListener("input", updateSelectedElementFromInputs);
            input.addEventListener("input", scheduleAutoSave);
        }

        byId("addTextButton").addEventListener("click", () => {
            addElement({ type: "text", text: "Text", width: 220, height: 64 });
        });
        byId("addImageButton").addEventListener("click", () => pickAssetForNewElement("image"));
        byId("addVideoButton").addEventListener("click", () => pickAssetForNewElement("video"));
        byId("replaceAssetButton").addEventListener("click", () => {
            const selected = getSelectedElement();
            if (!selected || selected.type === "text") {
                setStatus("Select an image or video element first.");
                return;
            }
            state.assetMode = "replace";
            fields.asset.accept = selected.type === "video" ? "video/*" : "image/*";
            fields.asset.click();
        });
        byId("deleteElementButton").addEventListener("click", deleteSelectedElement);
        byId("previewButton").addEventListener("click", previewFirstRule);
        byId("saveButton").addEventListener("click", saveComponent);
        byId("tutorialButton").addEventListener("click", openTutorial);
        byId("tutorialCloseButton").addEventListener("click", closeTutorial);
        fields.asset.addEventListener("change", importAsset);
    }

    function openTutorial() {
        const dialog = byId("tutorialDialog");
        if (dialog?.showModal) {
            dialog.showModal();
        }
    }

    function closeTutorial() {
        byId("tutorialDialog")?.close();
    }

    function initBlockly() {
        if (!window.Blockly) {
            setStatus("Blockly is not loaded; the block editor cannot be initialized.");
            return;
        }

        window.IdvbpDesignerBlockly.defineBlocks(elementOptions);
        state.workspace = Blockly.inject("blocklyDiv", {
            media: "/runtime/component-designer/media/",
            renderer: "zelos",
            trashcan: true,
            zoom: {
                controls: true,
                wheel: true,
                startScale: 0.82,
                maxScale: 1.5,
                minScale: 0.45,
                scaleSpeed: 1.12
            },
            toolbox: window.IdvbpDesignerBlockly.buildToolbox()
        });
        window.IdvbpDesignerBlockly.loadDefaultBlocks(state.workspace);
        state.workspace.addChangeListener(event => {
            if (!event.isUiEvent) {
                update();
                scheduleAutoSave();
            }
        });
    }

    function elementOptions() {
        const options = state.elements.map(element => [element.id, element.id]);
        return options.length > 0 ? options : [["none", "none"]];
    }

    function pickAssetForNewElement(type) {
        state.assetMode = type;
        fields.asset.accept = type === "video" ? "video/*" : "image/*";
        fields.asset.click();
    }

    async function importAsset(event) {
        const file = event.target.files?.[0];
        event.target.value = "";
        if (!file || !state.frontend) {
            return;
        }

        setStatus("姝ｅ湪瀵煎叆璧勬簮...");
        const form = new FormData();
        form.append("file", file);
        form.append("category", "designer");
        try {
            const response = await fetch(`/api/frontends/${encodeURIComponent(state.frontend)}/assets/import`, {
                method: "POST",
                body: form
            });
            if (!response.ok) {
                throw new Error(await response.text() || `HTTP ${response.status}`);
            }
            const asset = await response.json();
            if (state.assetMode === "replace") {
                const selected = getSelectedElement();
                if (selected) {
                    selected.src = asset.relativePath;
                    selected.url = asset.url;
                }
            } else {
                addElement({
                    type: state.assetMode,
                    src: asset.relativePath,
                    url: asset.url,
                    width: state.assetMode === "video" ? 360 : 260,
                    height: state.assetMode === "video" ? 200 : 180
                });
            }
            setStatus(`璧勬簮宸插鍏? ${asset.relativePath}`);
            renderDesigner();
            update();
            scheduleAutoSave();
        } catch (error) {
            setStatus(`璧勬簮瀵煎叆澶辫触: ${error.message || error}`);
        }
    }

    function addElement(partial) {
        const type = partial.type || "text";
        const id = uniqueElementId(type);
        const element = {
            id,
            type,
            text: partial.text || "",
            left: 60 + state.elements.length * 18,
            top: 60 + state.elements.length * 18,
            width: partial.width || 220,
            height: partial.height || 80,
            zIndex: state.elements.length + 1,
            color: "#f7f7f2",
            fontSize: 24,
            fit: "cover",
            src: partial.src || "",
            url: partial.url || ""
        };
        state.elements.push(element);
        state.selectedElementId = id;
        renderDesigner();
        update();
        scheduleAutoSave();
    }

    function uniqueElementId(prefix) {
        const safePrefix = sanitizeType(prefix) || "element";
        let index = 1;
        let id = `${safePrefix}-${index}`;
        while (state.elements.some(element => element.id === id)) {
            id = `${safePrefix}-${++index}`;
        }
        return id;
    }

    function renderDesigner() {
        fields.preview.textContent = "";
        fields.preview.style.width = `${positiveNumber(fields.componentWidth.value, 640)}px`;
        fields.preview.style.height = `${positiveNumber(fields.componentHeight.value, 360)}px`;
        fields.preview.style.background = fields.componentBackground.value;
        fields.preview.style.borderRadius = `${Math.max(0, Number(fields.componentRadius.value) || 0)}px`;

        for (const element of state.elements) {
            const node = document.createElement("div");
            node.className = "designer-element";
            node.dataset.elementId = element.id;
            node.classList.toggle("is-selected", element.id === state.selectedElementId);
            applyElementStyle(node, element);
            node.appendChild(createElementContent(element));
            const handle = document.createElement("div");
            handle.className = "resize-handle";
            node.appendChild(handle);
            node.addEventListener("pointerdown", pointerEvent => startElementDrag(pointerEvent, element, pointerEvent.target === handle));
            fields.preview.appendChild(node);
        }

        refreshElementInputs();
    }

    function createElementContent(element) {
        if (element.type === "image") {
            const img = document.createElement("img");
            img.alt = "";
            img.src = element.url || "";
            img.style.objectFit = element.fit || "cover";
            return img;
        }

        if (element.type === "video") {
            const video = document.createElement("video");
            video.src = element.url || "";
            video.muted = true;
            video.loop = true;
            video.autoplay = true;
            video.playsInline = true;
            video.style.objectFit = element.fit || "cover";
            return video;
        }

        const span = document.createElement("span");
        span.textContent = element.text || "";
        return span;
    }

    function applyElementStyle(node, element) {
        node.style.left = `${Number(element.left) || 0}px`;
        node.style.top = `${Number(element.top) || 0}px`;
        node.style.width = `${positiveNumber(element.width, 100)}px`;
        node.style.height = `${positiveNumber(element.height, 40)}px`;
        node.style.zIndex = Number(element.zIndex) || 1;
        node.style.color = element.color || "#f7f7f2";
        node.style.fontSize = `${positiveNumber(element.fontSize, 24)}px`;
    }

    function startElementDrag(event, element, resize) {
        event.preventDefault();
        event.stopPropagation();
        state.selectedElementId = element.id;
        renderDesigner();

        const start = { x: event.clientX, y: event.clientY };
        const origin = {
            left: Number(element.left) || 0,
            top: Number(element.top) || 0,
            width: Number(element.width) || 100,
            height: Number(element.height) || 40
        };

        const move = moveEvent => {
            const dx = moveEvent.clientX - start.x;
            const dy = moveEvent.clientY - start.y;
            if (resize) {
                element.width = Math.max(12, Math.round(origin.width + dx));
                element.height = Math.max(12, Math.round(origin.height + dy));
            } else {
                element.left = Math.round(origin.left + dx);
                element.top = Math.round(origin.top + dy);
            }
            renderDesigner();
            update();
            scheduleAutoSave();
        };

        const up = () => {
            window.removeEventListener("pointermove", move);
            window.removeEventListener("pointerup", up);
            window.removeEventListener("pointercancel", up);
        };

        window.addEventListener("pointermove", move);
        window.addEventListener("pointerup", up);
        window.addEventListener("pointercancel", up);
    }

    function updateSelectedElementFromInputs() {
        const selected = getSelectedElement();
        if (!selected) {
            return;
        }

        const nextId = sanitizeElementId(fields.elementId.value);
        if (nextId && nextId !== selected.id && !state.elements.some(element => element.id === nextId)) {
            selected.id = nextId;
            state.selectedElementId = nextId;
            if (state.workspace) {
                state.workspace.refreshToolboxSelection?.();
            }
        }

        selected.left = Number(fields.elementLeft.value) || 0;
        selected.top = Number(fields.elementTop.value) || 0;
        selected.width = positiveNumber(fields.elementWidth.value, selected.width);
        selected.height = positiveNumber(fields.elementHeight.value, selected.height);
        selected.zIndex = Number(fields.elementZ.value) || 1;
        selected.text = fields.elementText.value || "";
        selected.color = fields.elementColor.value || "#f7f7f2";
        selected.fontSize = positiveNumber(fields.elementFontSize.value, selected.fontSize);
        selected.fit = fields.elementFit.value || "cover";
        renderDesigner();
        update();
        scheduleAutoSave();
    }

    function refreshElementInputs() {
        const selected = getSelectedElement();
        const disabled = !selected;
        for (const input of [
            fields.elementId,
            fields.elementLeft,
            fields.elementTop,
            fields.elementWidth,
            fields.elementHeight,
            fields.elementZ,
            fields.elementText,
            fields.elementColor,
            fields.elementFontSize,
            fields.elementFit
        ]) {
            input.disabled = disabled;
        }

        if (!selected) {
            fields.elementId.value = "";
            fields.elementType.value = "";
            return;
        }

        fields.elementId.value = selected.id;
        fields.elementType.value = selected.type;
        fields.elementLeft.value = selected.left;
        fields.elementTop.value = selected.top;
        fields.elementWidth.value = selected.width;
        fields.elementHeight.value = selected.height;
        fields.elementZ.value = selected.zIndex;
        fields.elementText.value = selected.text || "";
        fields.elementColor.value = selected.color || "#f7f7f2";
        fields.elementFontSize.value = selected.fontSize || 24;
        fields.elementFit.value = selected.fit || "cover";
    }

    function deleteSelectedElement() {
        if (state.elements.length <= 1) {
            setStatus("Keep at least one element.");
            return;
        }
        state.elements = state.elements.filter(element => element.id !== state.selectedElementId);
        state.selectedElementId = state.elements[0]?.id || "";
        renderDesigner();
        update();
        scheduleAutoSave();
    }

    function getSelectedElement() {
        return state.elements.find(element => element.id === state.selectedElementId) || null;
    }

    function update() {
        fields.preview.style.width = `${positiveNumber(fields.componentWidth.value, 640)}px`;
        fields.preview.style.height = `${positiveNumber(fields.componentHeight.value, 360)}px`;
        fields.preview.style.background = fields.componentBackground.value;
        fields.preview.style.borderRadius = `${Math.max(0, Number(fields.componentRadius.value) || 0)}px`;
        fields.code.value = buildScript();
    }

    function previewFirstRule() {
        const eventType = getPreviewEventType();
        const rules = collectRules().filter(rule => rule.event === eventType);
        if (rules.length === 0) {
            setStatus(`No action blocks matched ${eventType}.`);
            return;
        }

        for (const rule of rules) {
            applyPreviewRule(rule);
        }
        setStatus(`宸查瑙堜簨浠? ${eventType}`);
    }
    async function saveComponent() {
        if (!state.frontend) {
            setStatus("Missing frontend parameter; cannot save.");
            return;
        }

        const type = sanitizeType(fields.type.value);
        const nodeId = sanitizeElementId(fields.nodeId.value || type);
        if (!type) {
            setStatus("Type ID cannot be empty.");
            return;
        }
        if (!nodeId) {
            setStatus("Node ID cannot be empty.");
            return;
        }

        setStatus("姝ｅ湪鐢熸垚缁勪欢...");
        try {
            const response = await fetch(`/api/frontends/${encodeURIComponent(state.frontend)}/components/designer`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    type,
                    script: fields.code.value || buildScript(type),
                    css: buildCss(type),
                    addToPage: true,
                    pageId: state.page,
                    layoutPath: state.layout,
                    nodeId,
                    width: positiveNumber(fields.componentWidth.value, 640),
                    height: positiveNumber(fields.componentHeight.value, 360),
                    left: 80,
                    top: 80,
                    zIndex: 20
                })
            });
            if (!response.ok) {
                throw new Error(await response.text() || `HTTP ${response.status}`);
            }
            const result = await response.json();
            const componentType = result.component?.type || type;
            const savedNodeId = result.nodeId || nodeId;
            const savedLayout = result.layoutPath || state.layout;
            const detail = `${state.frontend} / ${state.page} / ${savedLayout} / ${savedNodeId}`;
            setStatus(`宸茬敓鎴愬苟鎻掑叆椤甸潰: ${componentType} -> ${detail}\n${result.packagePath || ""}`);
            await saveDesignerState(savedNodeId);
            state.loadedNodeId = savedNodeId;
            await loadExistingComponents(savedNodeId);
            showToast("鐢熸垚鎴愬姛", `宸插啓鍏?${detail}`);
        } catch (error) {
            const message = error.message || String(error);
            setStatus(`鐢熸垚澶辫触: ${message}`);
            showToast("鐢熸垚澶辫触", message, true);
        }
    }

    function collectRules() {
        if (!state.workspace) {
            return [];
        }

        const rules = [];
        for (const block of state.workspace.getTopBlocks(false)) {
            if (block.type !== "idvbp_event") {
                continue;
            }

            const event = block.getFieldValue("EVENT");
            rules.push(...collectStatementRules(event, block.getInputTargetBlock("DO")));
        }
        return rules;
    }

    function collectStatementRules(event, actionBlock) {
        const rules = [];
        while (actionBlock) {
            const rule = blockToRule(event, actionBlock);
            if (rule) {
                rules.push(rule);
            }
            actionBlock = actionBlock.getNextBlock();
        }
        return rules;
    }

    async function loadExistingComponents(selectedNodeId = "") {
        if (!state.frontend || !state.page) {
            return;
        }

        try {
            const response = await fetch(`/api/frontends/${encodeURIComponent(state.frontend)}/pages/${encodeURIComponent(state.page)}/designer-components`, {
                cache: "no-store"
            });
            if (!response.ok) {
                return;
            }
            const components = await response.json();
            fields.existingComponent.textContent = "";
            const empty = document.createElement("option");
            empty.value = "";
            empty.textContent = "鏂板缓缁勪欢";
            fields.existingComponent.appendChild(empty);
            for (const item of components || []) {
                const option = document.createElement("option");
                option.value = `${item.type}::${item.nodeId}`;
                option.textContent = `${item.nodeId} (${item.type})`;
                fields.existingComponent.appendChild(option);
            }
            if (selectedNodeId) {
                const option = [...fields.existingComponent.options].find(item => item.value.endsWith(`::${selectedNodeId}`));
                if (option) {
                    fields.existingComponent.value = option.value;
                }
            }
        } catch (error) {
            console.warn("Designer component list failed to load.", error);
        }
    }

    async function loadExistingDesignerComponent(type, nodeId) {
        fields.type.value = type;
        fields.nodeId.value = nodeId;
        autoNodeId = nodeId;
        state.loadedNodeId = nodeId;

        try {
            const response = await fetch(`/api/frontends/${encodeURIComponent(state.frontend)}/pages/${encodeURIComponent(state.page)}/components/${encodeURIComponent(nodeId)}/config`, {
                cache: "no-store"
            });
            if (!response.ok) {
                throw new Error(await response.text() || `HTTP ${response.status}`);
            }
            const body = await response.json();
            const config = body.value ? JSON.parse(body.value) : null;
            if (!config) {
                const fallback = await loadDesignerStateFromScript(type);
                if (fallback) {
                    loadDesignerState(fallback);
                    await saveDesignerState(nodeId);
                    showToast("宸蹭粠缁勪欢鑴氭湰鎭㈠", `${state.frontend} / ${state.page} / ${nodeId}`);
                    return;
                }
                setStatus("The selected component has no editable config, and the designer could not recover it from the component script.");
                return;
            }
            loadDesignerState(config);
            showToast("Component loaded", `${state.frontend} / ${state.page} / ${nodeId}`);
        } catch (error) {
            setStatus(`杞藉叆鐜版湁缁勪欢澶辫触: ${error.message || error}`);
        }
    }

    async function loadDesignerStateFromScript(type) {
        const safeType = sanitizeType(type);
        if (!safeType) {
            return null;
        }

        try {
            const response = await fetch(`/frontends/${encodeURIComponent(state.frontend)}/components/designer/${encodeURIComponent(safeType)}.js`, {
                cache: "no-store"
            });
            if (!response.ok) {
                return null;
            }
            const source = await response.text();
            return extractDefaultsFromScript(source);
        } catch (error) {
            console.warn("Designer component script fallback failed.", error);
            return null;
        }
    }

    function extractDefaultsFromScript(source) {
        const marker = "const defaults = ";
        const start = source.indexOf(marker);
        if (start < 0) {
            return null;
        }

        const objectStart = source.indexOf("{", start + marker.length);
        if (objectStart < 0) {
            return null;
        }

        let depth = 0;
        let inString = false;
        let quote = "";
        let escaped = false;
        for (let index = objectStart; index < source.length; index++) {
            const ch = source[index];
            if (inString) {
                if (escaped) {
                    escaped = false;
                } else if (ch === "\\") {
                    escaped = true;
                } else if (ch === quote) {
                    inString = false;
                }
                continue;
            }

            if (ch === '"' || ch === "'") {
                inString = true;
                quote = ch;
                continue;
            }

            if (ch === "{") {
                depth++;
            } else if (ch === "}") {
                depth--;
                if (depth === 0) {
                    return JSON.parse(source.slice(objectStart, index + 1));
                }
            }
        }

        return null;
    }

    function loadDesignerState(config) {
        if (config.component) {
            fields.componentWidth.value = config.component.width ?? fields.componentWidth.value;
            fields.componentHeight.value = config.component.height ?? fields.componentHeight.value;
            fields.componentBackground.value = normalizeColor(config.component.background || fields.componentBackground.value);
            fields.componentRadius.value = config.component.radius ?? fields.componentRadius.value;
        }
        if (Array.isArray(config.elements) && config.elements.length > 0) {
            state.elements = config.elements.map(element => ({
                id: element.id || uniqueElementId(element.type || "element"),
                type: element.type || "text",
                text: element.text || "",
                left: Number(element.left) || 0,
                top: Number(element.top) || 0,
                width: positiveNumber(element.width, 100),
                height: positiveNumber(element.height, 40),
                zIndex: Number(element.zIndex) || 1,
                color: normalizeColor(element.color || "#f7f7f2"),
                fontSize: positiveNumber(element.fontSize, 24),
                fit: element.fit || "cover",
                src: element.src || "",
                url: element.url || resolveAssetUrl(element.src || "")
            }));
            state.selectedElementId = state.elements[0].id;
        }
        if (config.blocksXml && state.workspace) {
            state.workspace.clear();
            Blockly.Xml.domToWorkspace(Blockly.utils.xml.textToDom(config.blocksXml), state.workspace);
        }
        renderDesigner();
        update();
    }

    async function saveDesignerState(nodeId) {
        if (!nodeId) {
            return;
        }
        const config = {
            component: {
                width: positiveNumber(fields.componentWidth.value, 640),
                height: positiveNumber(fields.componentHeight.value, 360),
                background: fields.componentBackground.value,
                radius: Math.max(0, Number(fields.componentRadius.value) || 0)
            },
            elements: state.elements.map(element => ({ ...element })),
            rules: collectRules(),
            blocksXml: workspaceXml()
        };

        const response = await fetch(`/api/frontends/${encodeURIComponent(state.frontend)}/pages/${encodeURIComponent(state.page)}/components/${encodeURIComponent(nodeId)}/config`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ value: JSON.stringify(config) })
        });
        if (!response.ok) {
            throw new Error(await response.text() || `Config save failed: ${response.status}`);
        }
        state.loadedNodeId = nodeId;
    }

    function scheduleAutoSave() {
        if (!state.loadedNodeId) {
            return;
        }

        window.clearTimeout(autoSaveTimer);
        autoSaveTimer = window.setTimeout(async () => {
            try {
                await saveDesignerState(state.loadedNodeId);
                setStatus(`宸茶嚜鍔ㄤ繚瀛? ${state.loadedNodeId}`);
            } catch (error) {
                setStatus(`鑷姩淇濆瓨澶辫触: ${error.message || error}`);
            }
        }, 700);
    }

    function getPreviewEventType() {
        const eventBlock = state.workspace?.getTopBlocks(false).find(block => block.type === "idvbp_event");
        return eventBlock?.getFieldValue("EVENT") || "designer.ready";
    }

    function applyPreviewRule(rule) {
        const target = fields.preview.querySelector(`[data-element-id="${cssEscape(rule.targetId || state.selectedElementId)}"]`);
        switch (rule.action) {
            case "if": {
                const branch = previewTruthy(previewValue(rule.condition)) ? rule.then : rule.else;
                for (const childRule of branch || []) {
                    applyPreviewRule(childRule);
                }
                break;
            }
            case "timeout":
            case "interval":
            case "repeat":
            case "forEach":
            case "callApi":
                for (const childRule of rule.rules || []) {
                    applyPreviewRule(childRule);
                }
                break;
            case "pulse":
                if (target) {
                    target.classList.remove("is-pulsing");
                    void target.offsetWidth;
                    target.classList.add("is-pulsing");
                }
                break;
            case "setText":
                if (target) {
                    target.textContent = previewValue(rule.value);
                }
                break;
            case "setVisible":
                if (target) {
                    target.style.display = String(previewValue(rule.value)).toLowerCase() === "false" ? "none" : "";
                }
                break;
            case "move":
                if (target) {
                    target.style.left = `${Number(rule.left) || 0}px`;
                    target.style.top = `${Number(rule.top) || 0}px`;
                }
                break;
            case "resize":
                if (target) {
                    target.style.width = `${Number(rule.width) || 100}px`;
                    target.style.height = `${Number(rule.height) || 40}px`;
                }
                break;
            case "setStyle":
                if (target && rule.property) {
                    target.style[rule.property] = previewValue(rule.value);
                }
                break;
            case "playAnimation":
                if (target) {
                    target.style.animation = rule.animation || "";
                }
                break;
        }
    }

    function previewValue(value) {
        if (!value || typeof value !== "object") {
            return value ?? "";
        }
        switch (value.source) {
            case "literal":
                return value.value ?? "";
            case "number":
                return Number(value.value) || 0;
            case "boolean":
                return !!value.value;
            case "room":
                return `[room.${value.path || ""}]`;
            case "event":
                return `[event.${value.path || ""}]`;
            case "config":
                return `[config.${value.path || ""}]`;
            case "variable":
                return `[变量${value.path ? "." + value.path : ""}]`;
            case "variableExists":
                return `[变量存在${value.path ? "." + value.path : ""}]`;
            case "loopItem":
                return `[循环项${value.path ? "." + value.path : ""}]`;
            case "apiResponse":
                return `[API返回${value.path ? "." + value.path : ""}]`;
            case "apiStatus":
                return "[API状态码]";
            case "apiOk":
                return "[API是否成功]";
            case "apiRequest":
                return `[请求后端API ${value.method || "GET"} ${previewValue(value.url)}]`;
            case "arithmetic":
                return calculatePreview(value.op, Number(previewValue(value.left)) || 0, Number(previewValue(value.right)) || 0);
            case "mathSingle":
                return calculateSinglePreview(value.op, Number(previewValue(value.value)) || 0);
            case "compare":
                return comparePreview(value.op, previewValue(value.left), previewValue(value.right));
            case "logic":
                return value.op === "OR"
                    ? previewTruthy(previewValue(value.left)) || previewTruthy(previewValue(value.right))
                    : previewTruthy(previewValue(value.left)) && previewTruthy(previewValue(value.right));
            case "logicNot":
                return !previewTruthy(previewValue(value.value));
            case "textJoin":
                return (value.parts || []).map(part => String(previewValue(part) ?? "")).join("");
            case "textLength":
                return String(previewValue(value.value) ?? "").length;
            case "textContains":
                return String(previewValue(value.text) ?? "").includes(String(previewValue(value.search) ?? ""));
            case "textReplace":
                return String(previewValue(value.text) ?? "").replaceAll(String(previewValue(value.from) ?? ""), String(previewValue(value.to) ?? ""));
            case "textSubstring":
                return String(previewValue(value.text) ?? "").slice(Number(previewValue(value.start)) || 0, Number(previewValue(value.end)) || 0);
            case "textCase": {
                const text = String(previewValue(value.value) ?? "");
                return value.op === "toLowerCase" ? text.toLowerCase() : text.toUpperCase();
            }
            case "toNumber":
                return Number(previewValue(value.value)) || 0;
            case "jsonGet":
                return `[对象路径 ${value.key || ""}]`;
            case "toString":
            case "jsonStringify":
            case "typeof":
                return String(previewValue(value.value) ?? "");
            default:
                return "";
        }
    }

    function previewTruthy(value) {
        return value === true || value === 1 || String(value).toLowerCase() === "true" || (typeof value === "string" && value.length > 0);
    }

    function calculatePreview(op, left, right) {
        if (op === "-") return left - right;
        if (op === "*") return left * right;
        if (op === "/") return right === 0 ? 0 : left / right;
        if (op === "%") return right === 0 ? 0 : left % right;
        return left + right;
    }

    function calculateSinglePreview(op, value) {
        if (op === "abs") return Math.abs(value);
        if (op === "floor") return Math.floor(value);
        if (op === "ceil") return Math.ceil(value);
        if (op === "sqrt") return Math.sqrt(Math.max(0, value));
        if (op === "random") return Math.floor(Math.random() * Math.max(0, value + 1));
        return Math.round(value);
    }

    function comparePreview(op, left, right) {
        if (op === "!=") return left != right;
        if (op === ">") return left > right;
        if (op === "<") return left < right;
        if (op === ">=") return left >= right;
        if (op === "<=") return left <= right;
        return left == right;
    }

    function blockToRule(event, block) {
        const base = {
            event,
            targetId: block.getFieldValue("TARGET") || ""
        };

        switch (block.type) {
            case "idvbp_if":
                return {
                    event,
                    action: "if",
                    condition: blockValue(block, "CONDITION"),
                    then: collectStatementRules(event, block.getInputTargetBlock("THEN")),
                    else: collectStatementRules(event, block.getInputTargetBlock("ELSE"))
                };
            case "idvbp_set_timeout":
                return {
                    event,
                    action: "timeout",
                    delay: blockValue(block, "DELAY"),
                    rules: collectStatementRules(event, block.getInputTargetBlock("DO"))
                };
            case "idvbp_set_interval":
                return {
                    event,
                    action: "interval",
                    interval: blockValue(block, "INTERVAL"),
                    rules: collectStatementRules(event, block.getInputTargetBlock("DO"))
                };
            case "idvbp_repeat_times":
                return {
                    event,
                    action: "repeat",
                    times: blockValue(block, "TIMES"),
                    rules: collectStatementRules(event, block.getInputTargetBlock("DO"))
                };
            case "idvbp_for_each":
                return {
                    event,
                    action: "forEach",
                    list: blockValue(block, "LIST"),
                    itemName: block.getFieldValue("ITEM") || "item",
                    rules: collectStatementRules(event, block.getInputTargetBlock("DO"))
                };
            case "idvbp_console_log":
                return { event, action: "consoleLog", value: blockValue(block, "VALUE") };
            case "idvbp_fetch_url":
                return {
                    event,
                    action: "fetch",
                    method: block.getFieldValue("METHOD") || "GET",
                    url: blockValue(block, "URL")
                };
            case "idvbp_pulse":
                return { ...base, action: "pulse" };
            case "idvbp_set_text":
                return { ...base, action: "setText", value: blockValue(block, "VALUE") };
            case "idvbp_set_visible":
                return { ...base, action: "setVisible", value: { source: "literal", value: block.getFieldValue("VALUE") } };
            case "idvbp_set_source":
                return { ...base, action: "setSource", value: blockValue(block, "VALUE") };
            case "idvbp_move_element":
                return {
                    ...base,
                    action: "move",
                    left: Number(block.getFieldValue("LEFT")) || 0,
                    top: Number(block.getFieldValue("TOP")) || 0
                };
            case "idvbp_resize_element":
                return {
                    ...base,
                    action: "resize",
                    width: Math.max(1, Number(block.getFieldValue("WIDTH")) || 100),
                    height: Math.max(1, Number(block.getFieldValue("HEIGHT")) || 40)
                };
            case "idvbp_set_style":
                return { ...base, action: "setStyle", property: block.getFieldValue("PROP"), value: blockValue(block, "VALUE") };
            case "idvbp_play_animation":
                return { ...base, action: "playAnimation", animation: block.getFieldValue("ANIMATION") || "" };
            case "idvbp_set_variable":
                return { event, action: "setVariable", key: block.getFieldValue("KEY") || "", value: blockValue(block, "VALUE") };
            case "idvbp_set_config":
                return { event, action: "setConfig", key: block.getFieldValue("KEY") || "", value: blockValue(block, "VALUE") };
            case "idvbp_emit":
                return { event, action: "emit", type: block.getFieldValue("TYPE") || "", value: blockValue(block, "VALUE") };
            default:
                return null;
        }
    }

    function blockValue(block, inputName) {
        const valueBlock = block.getInputTargetBlock(inputName);
        if (!valueBlock) {
            return { source: "literal", value: "" };
        }
        return valueBlockToObj(valueBlock);
    }

    function valueBlockToObj(block) {
        switch (block.type) {
            case "idvbp_value_text":
                return { source: "literal", value: block.getFieldValue("VALUE") || "" };
            case "idvbp_value_number":
                return { source: "number", value: Number(block.getFieldValue("VALUE")) || 0 };
            case "idvbp_value_boolean":
                return { source: "boolean", value: block.getFieldValue("VALUE") === "true" };
            case "idvbp_value_event":
                return { source: "event", path: block.getFieldValue("PATH") || "payload" };
            case "idvbp_value_room":
                return { source: "room", path: block.getFieldValue("PATH") || "" };
            case "idvbp_value_config":
                return { source: "config", path: block.getFieldValue("PATH") || "" };
            case "idvbp_value_variable":
                return { source: "variable", path: block.getFieldValue("PATH") || "" };
            case "idvbp_variable_exists":
                return { source: "variableExists", path: block.getFieldValue("PATH") || "" };
            case "idvbp_value_loop_item":
                return { source: "loopItem", path: block.getFieldValue("PATH") || "" };
            case "idvbp_value_api_response":
                return { source: "apiResponse", path: block.getFieldValue("PATH") || "" };
            case "idvbp_value_api_status":
                return { source: "apiStatus" };
            case "idvbp_value_api_ok":
                return { source: "apiOk" };
            case "idvbp_api_request":
                return {
                    source: "apiRequest",
                    method: block.getFieldValue("METHOD") || "GET",
                    url: { source: "literal", value: block.getFieldValue("URL") || "/api/rooms" },
                    body: { source: "literal", value: "" }
                };
            case "idvbp_api_request_body":
                return {
                    source: "apiRequest",
                    method: block.getFieldValue("METHOD") || "POST",
                    url: { source: "literal", value: block.getFieldValue("URL") || "/api/rooms" },
                    body: blockValue(block, "BODY")
                };
            case "idvbp_arithmetic":
                return { source: "arithmetic", op: block.getFieldValue("OP") || "+", left: blockValue(block, "LEFT"), right: blockValue(block, "RIGHT") };
            case "idvbp_math_single":
                return { source: "mathSingle", op: block.getFieldValue("OP") || "round", value: blockValue(block, "VALUE") };
            case "idvbp_compare":
                return { source: "compare", op: block.getFieldValue("OP") || "==", left: blockValue(block, "LEFT"), right: blockValue(block, "RIGHT") };
            case "idvbp_logic_op":
                return { source: "logic", op: block.getFieldValue("OP") || "AND", left: blockValue(block, "LEFT"), right: blockValue(block, "RIGHT") };
            case "idvbp_logic_not":
                return { source: "logicNot", value: blockValue(block, "VALUE") };
            case "idvbp_text_join":
                return { source: "textJoin", parts: [blockValue(block, "A"), blockValue(block, "B")] };
            case "idvbp_text_length":
                return { source: "textLength", value: blockValue(block, "VALUE") };
            case "idvbp_text_contains":
                return { source: "textContains", text: blockValue(block, "TEXT"), search: blockValue(block, "SEARCH") };
            case "idvbp_text_replace":
                return { source: "textReplace", text: blockValue(block, "TEXT"), from: blockValue(block, "FROM"), to: blockValue(block, "TO") };
            case "idvbp_text_substring":
                return { source: "textSubstring", text: blockValue(block, "TEXT"), start: blockValue(block, "START"), end: blockValue(block, "END") };
            case "idvbp_text_case":
                return { source: "textCase", op: block.getFieldValue("OP") || "toUpperCase", value: blockValue(block, "VALUE") };
            case "idvbp_json_parse":
                return { source: "jsonParse", value: blockValue(block, "VALUE") };
            case "idvbp_json_get":
                return { source: "jsonGet", object: blockValue(block, "OBJECT"), key: block.getFieldValue("KEY") || "" };
            case "idvbp_json_path":
                return { source: "jsonGet", object: blockValue(block, "OBJECT"), key: block.getFieldValue("KEY") || "" };
            case "idvbp_json_stringify":
                return { source: "jsonStringify", value: blockValue(block, "VALUE") };
            case "idvbp_json_has":
                return { source: "jsonHas", object: blockValue(block, "OBJECT"), key: block.getFieldValue("KEY") || "" };
            case "idvbp_typeof":
                return { source: "typeof", value: blockValue(block, "VALUE") };
            case "idvbp_to_number":
                return { source: "toNumber", value: blockValue(block, "VALUE") };
            case "idvbp_to_string":
                return { source: "toString", value: blockValue(block, "VALUE") };
            default:
                return { source: "literal", value: block.getFieldValue?.("VALUE") || "" };
        }
    }

    function workspaceXml() {
        if (!state.workspace) {
            return "";
        }
        return Blockly.Xml.domToText(Blockly.Xml.workspaceToDom(state.workspace));
    }

    function buildScript(type = sanitizeType(fields.type.value) || "designer-widget") {
        const defaults = {
            component: {
                width: positiveNumber(fields.componentWidth.value, 640),
                height: positiveNumber(fields.componentHeight.value, 360),
                background: fields.componentBackground.value,
                radius: Math.max(0, Number(fields.componentRadius.value) || 0)
            },
            elements: state.elements.map(element => ({ ...element })),
            rules: collectRules(),
            blocksXml: workspaceXml()
        };

        if (!window.IdvbpDesignerRuntimeCode?.buildDesignerRuntimeScript) {
            throw new Error("Designer runtime code module is not loaded.");
        }

        return window.IdvbpDesignerRuntimeCode.buildDesignerRuntimeScript(type, defaults);
    }
    function buildCss(type = sanitizeType(fields.type.value) || "designer-widget") {
        return `.${type} {
  position: relative;
  overflow: hidden;
}

.${type}__element {
  position: absolute;
  display: grid;
  place-items: center;
}

.${type}__element img,
.${type}__element video {
  width: 100%;
  height: 100%;
  display: block;
}

.${type}__element span {
  max-width: 100%;
  overflow-wrap: anywhere;
  text-align: center;
}

.${type}__element.is-hidden {
  display: none;
}

.${type}__element.is-pulsing {
  animation: ${type}-pulse 520ms ease both;
}

@keyframes ${type}-pulse {
  0%, 100% { filter: brightness(1); transform: scale(1); }
  45% { filter: brightness(1.45); transform: scale(1.025); }
}`;
    }

    function sanitizeType(value) {
        return String(value || "")
            .trim()
            .toLowerCase()
            .replace(/[^a-z0-9_-]+/g, "-")
            .replace(/^-+|-+$/g, "");
    }

    function sanitizeElementId(value) {
        return String(value || "")
            .trim()
            .replace(/[^A-Za-z0-9_-]+/g, "-")
            .replace(/^-+|-+$/g, "");
    }

    function positiveNumber(value, fallback) {
        return Math.max(1, Number(value) || fallback);
    }

    function normalizeColor(value) {
        return /^#[0-9a-f]{6}$/i.test(String(value || "")) ? value : "#f7f7f2";
    }

    function resolveAssetUrl(value) {
        if (!value) {
            return "";
        }
        if (/^(https?:)?\/\//i.test(value) || String(value).startsWith("/")) {
            return value;
        }
        return `/frontends/${encodeURIComponent(state.frontend)}/${String(value).replace(/^\/+/, "")}`;
    }

    function cssEscape(value) {
        return window.CSS?.escape ? CSS.escape(value) : String(value).replace(/"/g, '\\"');
    }

    function setStatus(text) {
        fields.status.textContent = text;
    }

    function showToast(title, detail, isError = false) {
        fields.toastTitle.textContent = title;
        fields.toastDetail.textContent = detail;
        fields.toast.classList.toggle("is-error", isError);
        fields.toast.hidden = false;
        window.clearTimeout(showToast.timer);
        showToast.timer = window.setTimeout(() => {
            fields.toast.hidden = true;
        }, isError ? 7000 : 5000);
    }
})();
