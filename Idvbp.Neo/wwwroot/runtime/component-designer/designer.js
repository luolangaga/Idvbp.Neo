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

    const eventOptions = [
        ["页面加载完成 designer.ready", "designer.ready"],
        ["房间完整快照 room.snapshot", "room.snapshot"],
        ["房间信息更新 room.info.updated", "room.info.updated"],
        ["比赛创建 match.created", "match.created"],
        ["地图更新 room.map.updated", "room.map.updated"],
        ["本轮禁用更新 room.ban.updated", "room.ban.updated"],
        ["全局禁用更新 room.global-ban.updated", "room.global-ban.updated"],
        ["角色选择更新 room.role.selected", "room.role.selected"],
        ["求生者 1 选择 designer.survivor1.selected", "designer.survivor1.selected"],
        ["求生者 2 选择 designer.survivor2.selected", "designer.survivor2.selected"],
        ["求生者 3 选择 designer.survivor3.selected", "designer.survivor3.selected"],
        ["求生者 4 选择 designer.survivor4.selected", "designer.survivor4.selected"],
        ["监管者选择 designer.hunter.selected", "designer.hunter.selected"],
        ["阶段变化 room.phase.updated", "room.phase.updated"],
        ["进入禁用阶段 designer.phase.ban.enter", "designer.phase.ban.enter"],
        ["进入选择阶段 designer.phase.pick.enter", "designer.phase.pick.enter"],
        ["进入比分/结算阶段 designer.phase.score.enter", "designer.phase.score.enter"],
        ["地图选定 designer.map.selected", "designer.map.selected"],
        ["前台重置 frontend.reset", "frontend.reset"],
        ["停止全部动画 frontend.animation.stopAll", "frontend.animation.stopAll"]
    ];

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
                setStatus("请选择图片或视频元素。");
                return;
            }
            state.assetMode = "replace";
            fields.asset.accept = selected.type === "video" ? "video/*" : "image/*";
            fields.asset.click();
        });
        byId("deleteElementButton").addEventListener("click", deleteSelectedElement);
        byId("previewButton").addEventListener("click", previewFirstRule);
        byId("saveButton").addEventListener("click", saveComponent);
        fields.asset.addEventListener("change", importAsset);
    }

    function initBlockly() {
        if (!window.Blockly) {
            setStatus("Blockly 未加载，无法初始化积木编辑器。");
            return;
        }

        defineBlocks();
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
            toolbox: buildToolbox()
        });
        loadDefaultBlocks();
        state.workspace.addChangeListener(event => {
            if (!event.isUiEvent) {
                update();
                scheduleAutoSave();
            }
        });
    }

    function defineBlocks() {
        if (Blockly.Blocks.idvbp_event) {
            return;
        }

        Blockly.Blocks.idvbp_event = {
            init() {
                this.appendDummyInput()
                    .appendField("当事件")
                    .appendField(new Blockly.FieldDropdown(eventOptions), "EVENT");
                this.appendStatementInput("DO").appendField("执行");
                this.setColour(210);
            }
        };

        Blockly.Blocks.idvbp_value_text = {
            init() {
                this.appendDummyInput()
                    .appendField("文本")
                    .appendField(new Blockly.FieldTextInput("Ready"), "VALUE");
                this.setOutput(true, "String");
                this.setColour(160);
            }
        };

        Blockly.Blocks.idvbp_value_event = {
            init() {
                this.appendDummyInput()
                    .appendField("事件数据")
                    .appendField(new Blockly.FieldTextInput("payload"), "PATH");
                this.setOutput(true, "String");
                this.setColour(230);
            }
        };

        Blockly.Blocks.idvbp_value_room = {
            init() {
                this.appendDummyInput()
                    .appendField("房间数据")
                    .appendField(new Blockly.FieldTextInput("teamA.name"), "PATH");
                this.setOutput(true, "String");
                this.setColour(230);
            }
        };

        Blockly.Blocks.idvbp_value_config = {
            init() {
                this.appendDummyInput()
                    .appendField("组件配置")
                    .appendField(new Blockly.FieldTextInput("title"), "PATH");
                this.setOutput(true, "String");
                this.setColour(230);
            }
        };

        Blockly.Blocks.idvbp_pulse = {
            init() {
                this.appendDummyInput()
                    .appendField("让元素")
                    .appendField(new Blockly.FieldDropdown(elementOptions), "TARGET")
                    .appendField("强调动画");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(45);
            }
        };

        Blockly.Blocks.idvbp_set_text = {
            init() {
                this.appendValueInput("VALUE")
                    .appendField("设置元素")
                    .appendField(new Blockly.FieldDropdown(elementOptions), "TARGET")
                    .appendField("文字为");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(120);
            }
        };

        Blockly.Blocks.idvbp_set_visible = {
            init() {
                this.appendDummyInput()
                    .appendField("设置元素")
                    .appendField(new Blockly.FieldDropdown(elementOptions), "TARGET")
                    .appendField("可见")
                    .appendField(new Blockly.FieldDropdown([["显示", "true"], ["隐藏", "false"]]), "VALUE");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(120);
            }
        };

        Blockly.Blocks.idvbp_set_source = {
            init() {
                this.appendValueInput("VALUE")
                    .appendField("设置元素")
                    .appendField(new Blockly.FieldDropdown(elementOptions), "TARGET")
                    .appendField("资源为");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(120);
            }
        };

        Blockly.Blocks.idvbp_move_element = {
            init() {
                this.appendDummyInput()
                    .appendField("移动元素")
                    .appendField(new Blockly.FieldDropdown(elementOptions), "TARGET")
                    .appendField("到 X")
                    .appendField(new Blockly.FieldNumber(0), "LEFT")
                    .appendField("Y")
                    .appendField(new Blockly.FieldNumber(0), "TOP");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(65);
            }
        };

        Blockly.Blocks.idvbp_set_style = {
            init() {
                this.appendValueInput("VALUE")
                    .appendField("设置元素")
                    .appendField(new Blockly.FieldDropdown(elementOptions), "TARGET")
                    .appendField("样式")
                    .appendField(new Blockly.FieldDropdown([
                        ["文字色", "color"],
                        ["字号", "fontSize"],
                        ["背景", "background"],
                        ["透明度", "opacity"]
                    ]), "PROP")
                    .appendField("为");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(65);
            }
        };

        Blockly.Blocks.idvbp_set_config = {
            init() {
                this.appendValueInput("VALUE")
                    .appendField("写入组件配置")
                    .appendField(new Blockly.FieldTextInput("key"), "KEY")
                    .appendField("为");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(290);
            }
        };

        Blockly.Blocks.idvbp_emit = {
            init() {
                this.appendValueInput("VALUE")
                    .appendField("发出本地事件")
                    .appendField(new Blockly.FieldTextInput("custom.event"), "TYPE")
                    .appendField("数据");
                this.setPreviousStatement(true);
                this.setNextStatement(true);
                this.setColour(20);
            }
        };
    }

    function elementOptions() {
        const options = state.elements.map(element => [element.id, element.id]);
        return options.length > 0 ? options : [["none", "none"]];
    }

    function buildToolbox() {
        return {
            kind: "categoryToolbox",
            contents: [
                {
                    kind: "category",
                    name: "事件",
                    colour: "210",
                    contents: [{ kind: "block", type: "idvbp_event" }]
                },
                {
                    kind: "category",
                    name: "取数据",
                    colour: "230",
                    contents: [
                        { kind: "block", type: "idvbp_value_text" },
                        { kind: "block", type: "idvbp_value_event" },
                        { kind: "block", type: "idvbp_value_room" },
                        { kind: "block", type: "idvbp_value_config" }
                    ]
                },
                {
                    kind: "category",
                    name: "操作",
                    colour: "120",
                    contents: [
                        { kind: "block", type: "idvbp_set_text" },
                        { kind: "block", type: "idvbp_set_visible" },
                        { kind: "block", type: "idvbp_set_source" },
                        { kind: "block", type: "idvbp_move_element" },
                        { kind: "block", type: "idvbp_set_style" },
                        { kind: "block", type: "idvbp_pulse" },
                        { kind: "block", type: "idvbp_set_config" },
                        { kind: "block", type: "idvbp_emit" }
                    ]
                }
            ]
        };
    }

    function loadDefaultBlocks() {
        const xml = Blockly.utils.xml.textToDom(`
            <xml xmlns="https://developers.google.com/blockly/xml">
                <block type="idvbp_event" x="24" y="28">
                    <field name="EVENT">room.snapshot</field>
                    <statement name="DO">
                        <block type="idvbp_set_text">
                            <field name="TARGET">title</field>
                            <value name="VALUE">
                                <block type="idvbp_value_room">
                                    <field name="PATH">roomName</field>
                                </block>
                            </value>
                            <next>
                                <block type="idvbp_pulse">
                                    <field name="TARGET">title</field>
                                </block>
                            </next>
                        </block>
                    </statement>
                </block>
            </xml>`);
        Blockly.Xml.domToWorkspace(xml, state.workspace);
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

        setStatus("正在导入资源...");
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
            setStatus(`资源已导入: ${asset.relativePath}`);
            renderDesigner();
            update();
            scheduleAutoSave();
        } catch (error) {
            setStatus(`资源导入失败: ${error.message || error}`);
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
            setStatus("至少保留一个元素。");
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
            setStatus(`当前没有匹配 ${eventType} 的操作积木。`);
            return;
        }

        for (const rule of rules) {
            applyPreviewRule(rule);
        }
        setStatus(`已预览事件: ${eventType}`);
    }
    async function saveComponent() {
        if (!state.frontend) {
            setStatus("缺少 frontend 参数，无法保存。");
            return;
        }

        const type = sanitizeType(fields.type.value);
        const nodeId = sanitizeElementId(fields.nodeId.value || type);
        if (!type) {
            setStatus("类型 ID 不能为空。");
            return;
        }
        if (!nodeId) {
            setStatus("实例 ID 不能为空。");
            return;
        }

        setStatus("正在生成组件...");
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
            setStatus(`已生成并插入页面: ${componentType} -> ${detail}\n${result.packagePath || ""}`);
            await saveDesignerState(savedNodeId);
            state.loadedNodeId = savedNodeId;
            await loadExistingComponents(savedNodeId);
            showToast("生成成功", `已写入 ${detail}`);
        } catch (error) {
            const message = error.message || String(error);
            setStatus(`生成失败: ${message}`);
            showToast("生成失败", message, true);
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
            let actionBlock = block.getInputTargetBlock("DO");
            while (actionBlock) {
                const rule = blockToRule(event, actionBlock);
                if (rule) {
                    rules.push(rule);
                }
                actionBlock = actionBlock.getNextBlock();
            }
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
            empty.textContent = "新建组件";
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
                    showToast("已从组件脚本恢复", `${state.frontend} / ${state.page} / ${nodeId}`);
                    return;
                }
                setStatus("已选择现有组件，但它还没有可编辑配置，也无法从组件脚本恢复。");
                return;
            }
            loadDesignerState(config);
            showToast("已载入组件", `${state.frontend} / ${state.page} / ${nodeId}`);
        } catch (error) {
            setStatus(`载入现有组件失败: ${error.message || error}`);
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
                setStatus(`已自动保存: ${state.loadedNodeId}`);
            } catch (error) {
                setStatus(`自动保存失败: ${error.message || error}`);
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
            case "setStyle":
                if (target && rule.property) {
                    target.style[rule.property] = previewValue(rule.value);
                }
                break;
        }
    }

    function previewValue(value) {
        if (!value || typeof value !== "object") {
            return value ?? "";
        }
        if (value.source === "literal") {
            return value.value ?? "";
        }
        if (value.source === "room") {
            return `[room.${value.path || ""}]`;
        }
        if (value.source === "event") {
            return `[event.${value.path || ""}]`;
        }
        if (value.source === "config") {
            return `[config.${value.path || ""}]`;
        }
        return "";
    }

    function blockToRule(event, block) {
        const base = {
            event,
            targetId: block.getFieldValue("TARGET") || ""
        };

        switch (block.type) {
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
            case "idvbp_set_style":
                return { ...base, action: "setStyle", property: block.getFieldValue("PROP"), value: blockValue(block, "VALUE") };
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

        switch (valueBlock.type) {
            case "idvbp_value_event":
                return { source: "event", path: valueBlock.getFieldValue("PATH") || "payload" };
            case "idvbp_value_room":
                return { source: "room", path: valueBlock.getFieldValue("PATH") || "" };
            case "idvbp_value_config":
                return { source: "config", path: valueBlock.getFieldValue("PATH") || "" };
            case "idvbp_value_text":
            default:
                return { source: "literal", value: valueBlock.getFieldValue("VALUE") || "" };
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

        return `(function () {
  const defaults = ${JSON.stringify(defaults, null, 2)};

  function readConfig(context) {
    const config = context.config && typeof context.config === "object" ? context.config : {};
    return {
      ...defaults,
      ...config,
      component: { ...defaults.component, ...(config.component || {}) },
      elements: config.elements || defaults.elements,
      rules: config.rules || defaults.rules
    };
  }

  function render(element, props, context) {
    const config = readConfig(context);
    element.classList.add("${type}");
    element.__designerReadyDispatched = element.__designerReadyDispatched || false;
    element.style.background = config.component.background || "transparent";
    element.style.borderRadius = (Number(config.component.radius) || 0) + "px";
    element.innerHTML = "";

    for (const item of config.elements || []) {
      const child = document.createElement("div");
      child.className = "${type}__element";
      child.dataset.elementId = item.id || "";
      applyElementStyle(child, item);
      child.appendChild(createElementContent(item, context));
      element.appendChild(child);
    }

    if (!element.__designerReadyDispatched) {
      element.__designerReadyDispatched = true;
      queueMicrotask(() => dispatchDesignerEvent(element, "designer.ready", { room: context.store.room }, context, context.store.event || {}));
    }
  }

  function createElementContent(item, context) {
    if (item.type === "image") {
      const image = document.createElement("img");
      image.alt = "";
      image.src = resolveAsset(item.src, context);
      image.style.objectFit = item.fit || "cover";
      return image;
    }

    if (item.type === "video") {
      const video = document.createElement("video");
      video.src = resolveAsset(item.src, context);
      video.muted = true;
      video.loop = true;
      video.autoplay = true;
      video.playsInline = true;
      video.style.objectFit = item.fit || "cover";
      return video;
    }

    const span = document.createElement("span");
    span.textContent = item.text || "";
    return span;
  }

  function applyElementStyle(child, item) {
    child.style.left = (Number(item.left) || 0) + "px";
    child.style.top = (Number(item.top) || 0) + "px";
    child.style.width = (Number(item.width) || 100) + "px";
    child.style.height = (Number(item.height) || 40) + "px";
    child.style.zIndex = Number(item.zIndex) || 1;
    child.style.color = item.color || "";
    child.style.fontSize = (Number(item.fontSize) || 24) + "px";
  }

  function applyRule(root, rule, context, event) {
    if (!rule || rule.event !== event.type) return;
    const target = rule.targetId ? root.querySelector('[data-element-id="' + cssEscape(rule.targetId) + '"]') : root;
    const config = readConfig(context);

    switch (rule.action) {
      case "pulse":
        if (target) {
          target.classList.remove("is-pulsing");
          void target.offsetWidth;
          target.classList.add("is-pulsing");
        }
        break;
      case "setText":
        if (target) target.textContent = stringify(resolveValue(rule.value, config, context, event));
        break;
      case "setVisible":
        if (target) target.classList.toggle("is-hidden", String(resolveValue(rule.value, config, context, event)).toLowerCase() === "false");
        break;
      case "setSource":
        setMediaSource(target, resolveValue(rule.value, config, context, event), context);
        break;
      case "move":
        if (target) {
          target.style.left = (Number(rule.left) || 0) + "px";
          target.style.top = (Number(rule.top) || 0) + "px";
        }
        break;
      case "setStyle":
        if (target && rule.property) target.style[rule.property] = stringify(resolveValue(rule.value, config, context, event));
        break;
      case "setConfig":
        if (rule.key) context.setConfig({ ...config, [rule.key]: resolveValue(rule.value, config, context, event) });
        break;
      case "emit":
        if (rule.type) context.emit(rule.type, resolveValue(rule.value, config, context, event));
        break;
    }
  }

  function dispatchDesignerEvent(root, type, payload, context, sourceEvent) {
    const event = {
      type,
      payload,
      sourceEvent,
      timestamp: new Date().toISOString()
    };
    for (const rule of readConfig(context).rules || []) {
      applyRule(root, rule, context, event);
    }
  }

  function dispatchDerivedEvents(root, context, event) {
    const eventType = event?.type || "";
    const payload = event?.payload || {};
    const room = context.store.room || {};

    if (eventType === "room.role.selected" || eventType === "room.snapshot") {
      const picks = payload.characterPicks || room.characterPicks || {};
      dispatchPickEvent(root, context, event, "designer.survivor1.selected", picks.survivor1 || picks.Survivor1 || picks.survivorA || picks[0]);
      dispatchPickEvent(root, context, event, "designer.survivor2.selected", picks.survivor2 || picks.Survivor2 || picks.survivorB || picks[1]);
      dispatchPickEvent(root, context, event, "designer.survivor3.selected", picks.survivor3 || picks.Survivor3 || picks.survivorC || picks[2]);
      dispatchPickEvent(root, context, event, "designer.survivor4.selected", picks.survivor4 || picks.Survivor4 || picks.survivorD || picks[3]);
      dispatchPickEvent(root, context, event, "designer.hunter.selected", picks.hunter || picks.Hunter || picks.hunter1 || picks[4]);
    }

    if (eventType === "room.phase.updated" || eventType === "room.snapshot") {
      const phase = String(payload.phase || payload.currentPhase || room.currentPhase || "").toLowerCase();
      if (phase.includes("ban")) dispatchDesignerEvent(root, "designer.phase.ban.enter", { phase }, context, event);
      if (phase.includes("pick") || phase.includes("select")) dispatchDesignerEvent(root, "designer.phase.pick.enter", { phase }, context, event);
      if (phase.includes("score") || phase.includes("result")) dispatchDesignerEvent(root, "designer.phase.score.enter", { phase }, context, event);
    }

    if (eventType === "room.map.updated" || eventType === "room.snapshot") {
      const map = payload.pickedMap || payload.map || room.mapSelection?.pickedMap || null;
      if (map) dispatchDesignerEvent(root, "designer.map.selected", { map }, context, event);
    }
  }

  function dispatchPickEvent(root, context, event, type, pick) {
    if (pick) {
      dispatchDesignerEvent(root, type, { pick }, context, event);
    }
  }

  function resolveValue(value, config, context, event) {
    if (!value || typeof value !== "object") return value;
    if (value.source === "event") return getByPath(event, value.path || "payload");
    if (value.source === "room") return getByPath(context.store.room, value.path || "");
    if (value.source === "config") return getByPath(config, value.path || "");
    return value.value ?? "";
  }

  function getByPath(source, path) {
    if (!path) return source;
    return String(path).split(".").reduce((current, segment) => current == null ? undefined : current[segment], source);
  }

  function setMediaSource(target, value, context) {
    const media = target?.querySelector("img,video");
    if (media) media.src = resolveAsset(stringify(value), context);
  }

  function resolveAsset(value, context) {
    if (!value || /^(https?:)?\\/\\//i.test(value) || String(value).startsWith("/")) return value || "";
    return context.frontendBase + "/" + String(value).replace(/^\\/+/, "");
  }

  function stringify(value) {
    return value == null ? "" : typeof value === "string" ? value : JSON.stringify(value);
  }

  function cssEscape(value) {
    return window.CSS?.escape ? CSS.escape(value) : String(value).replace(/["\\\\]/g, "");
  }

  window.IdvbpLayoutRuntime.register("${type}", {
    render,
    actions: {
      syncState(element, action, context, event) {
        const currentEvent = event || context.store.event || {};
        for (const rule of readConfig(context).rules || []) {
          applyRule(element, rule, context, currentEvent);
        }
        dispatchDerivedEvents(element, context, currentEvent);
      }
    },
    contextMenu({ config, helpers }) {
      const current = config && typeof config === "object" ? config : readConfig(helpers.context);
      return [
        { type: "button", label: "刷新组件", action: () => helpers.update() },
        { type: "button", label: "保存当前配置", action: () => helpers.setConfig(current) }
      ];
    }
  });
})();`;
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
