(() => {
    function buildDesignerRuntimeScript(type, defaults) {
        return `(function () {
  const defaults = ${JSON.stringify(defaults, null, 2)};

  function rtLog(source, level) {
    var args = Array.prototype.slice.call(arguments, 2);
    var message = args.map(function(a) { return typeof a === "object" ? JSON.stringify(a) : String(a == null ? "" : a); }).join(" ");
    console.log("[" + source + "]", args);
    try { navigator.sendBeacon("/api/runtime-logs", new Blob([JSON.stringify({ source: source, level: level, message: message })], { type: "application/json" })); } catch(e) {}
  }

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
    element.style.background = config.component.background || "transparent";
    element.style.borderRadius = (Number(config.component.radius) || 0) + "px";

    if (!element.__designerRendered) {
      element.__designerRendered = true;
      for (const item of config.elements || []) {
        const child = document.createElement("div");
        child.className = "${type}__element";
        child.dataset.elementId = item.id || "";
        applyElementStyle(child, item);
        child.appendChild(createElementContent(item, context));
        element.appendChild(child);
      }
      queueMicrotask(() => {
        if (!element.__designerReadyDispatched) {
          element.__designerReadyDispatched = true;
          void dispatchDesignerEvent(element, "designer.ready", { room: context.store.room }, context, context.store.event || {});
        }
      });
    } else {
      for (const item of config.elements || []) {
        const child = element.querySelector('[data-element-id="' + cssEscape(item.id || "") + '"]');
        if (child) applyElementStyle(child, item);
      }
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
    child.classList.toggle("is-hidden", !!item._hidden);
    if (item._style) { for (const k in item._style) child.style[k] = item._style[k]; }
  }

  async function applyRule(root, rule, context, event) {
    if (!rule || rule.event !== event.type) return;
    event.variables = event.variables || getVariables(root);
    const target = rule.targetId ? root.querySelector('[data-element-id="' + cssEscape(rule.targetId) + '"]') : root;
    const config = readConfig(context);
    let dirty = false;

    switch (rule.action) {
      case "if": {
        const branch = isTruthy(await resolveValue(rule.condition, config, context, event)) ? rule.then : rule.else;
        for (const childRule of branch || []) await applyRule(root, childRule, context, event);
        break;
      }
      case "timeout": {
        const delay = Math.max(0, Number(await resolveValue(rule.delay, config, context, event)) || 0);
        window.setTimeout(async () => {
          for (const childRule of rule.rules || []) await applyRule(root, childRule, context, event);
        }, delay);
        break;
      }
      case "interval": {
        const interval = Math.max(16, Number(await resolveValue(rule.interval, config, context, event)) || 1000);
        window.setInterval(async () => {
          for (const childRule of rule.rules || []) await applyRule(root, childRule, context, event);
        }, interval);
        break;
      }
      case "repeat": {
        const times = Math.max(0, Math.min(1000, Number(await resolveValue(rule.times, config, context, event)) || 0));
        for (let index = 0; index < times; index++) {
          const loopEvent = { ...event, loopIndex: index, loopItem: index, loopItemName: rule.itemName || "index" };
          for (const childRule of rule.rules || []) await applyRule(root, childRule, context, loopEvent);
        }
        break;
      }
      case "forEach": {
        const list = await resolveValue(rule.list, config, context, event);
        const items = Array.isArray(list) ? list : [];
        for (const [index, item] of items.slice(0, 1000).entries()) {
          const loopEvent = { ...event, loopIndex: index, loopItem: item, loopItemName: rule.itemName || "item" };
          for (const childRule of rule.rules || []) await applyRule(root, childRule, context, loopEvent);
        }
        break;
      }
      case "consoleLog":
        rtLog("designer", "info", await resolveValue(rule.value, config, context, event));
        break;
      case "fetch": {
        const url = stringify(await resolveValue(rule.url, config, context, event));
        if (url) fetch(url, { method: rule.method || "GET" }).catch(error => rtLog("designer", "error", error));
        break;
      }
      case "callApi": {
        try {
          const apiEvent = await callBackendApi(rule, config, context, event);
          if (rule.saveKey) setByPath(getVariables(root), rule.saveKey, apiEvent.apiResponse);
          for (const childRule of rule.rules || []) await applyRule(root, childRule, context, apiEvent);
        } catch (error) {
          rtLog("designer", "error", error);
        }
        break;
      }
      case "pulse":
        if (target) {
          target.classList.remove("is-pulsing");
          void target.offsetWidth;
          target.classList.add("is-pulsing");
        }
        break;
      case "setText": {
        const val = stringify(await resolveValue(rule.value, config, context, event));
        if (target) { target.textContent = val; }
        const el = findElement(config, rule.targetId);
        if (el) { el.text = val; dirty = true; }
        break;
      }
      case "setVisible": {
        const hidden = String(await resolveValue(rule.value, config, context, event)).toLowerCase() === "false";
        if (target) target.classList.toggle("is-hidden", hidden);
        const el = findElement(config, rule.targetId);
        if (el) { el._hidden = hidden; dirty = true; }
        break;
      }
      case "setSource": {
        const val = stringify(await resolveValue(rule.value, config, context, event));
        setMediaSource(target, val, context);
        const el = findElement(config, rule.targetId);
        if (el) { el.src = val; dirty = true; }
        break;
      }
      case "move": {
        const left = Number(rule.left) || 0, top = Number(rule.top) || 0;
        if (target) { target.style.left = left + "px"; target.style.top = top + "px"; }
        const el = findElement(config, rule.targetId);
        if (el) { el.left = left; el.top = top; dirty = true; }
        break;
      }
      case "resize": {
        const width = Math.max(1, Number(rule.width) || 100), height = Math.max(1, Number(rule.height) || 40);
        if (target) { target.style.width = width + "px"; target.style.height = height + "px"; }
        const el = findElement(config, rule.targetId);
        if (el) { el.width = width; el.height = height; dirty = true; }
        break;
      }
      case "setStyle": {
        const val = stringify(await resolveValue(rule.value, config, context, event));
        if (target && rule.property) target.style[rule.property] = val;
        const el = findElement(config, rule.targetId);
        if (el && rule.property) {
          if (rule.property === "color") el.color = val;
          else if (rule.property === "fontSize") el.fontSize = Number(val) || 24;
          else { if (!el._style) el._style = {}; el._style[rule.property] = val; }
          dirty = true;
        }
        break;
      }
      case "playAnimation":
        if (target) target.style.animation = rule.animation || "";
        break;
      case "setVariable":
        if (rule.key) setByPath(getVariables(root), rule.key, await resolveValue(rule.value, config, context, event));
        break;
      case "setConfig":
        if (rule.key) context.setConfig({ ...config, [rule.key]: await resolveValue(rule.value, config, context, event) });
        break;
      case "emit":
        if (rule.type) context.emit(rule.type, await resolveValue(rule.value, config, context, event));
        break;
    }
    if (dirty) { context.setConfig(config); }
  }

  function findElement(config, elementId) {
    if (!elementId || !config.elements) return null;
    for (let i = 0; i < config.elements.length; i++) {
      if (config.elements[i].id === elementId) return config.elements[i];
    }
    return null;
  }

  async function dispatchDesignerEvent(root, type, payload, context, sourceEvent) {
    const event = {
      type,
      payload,
      sourceEvent,
      timestamp: new Date().toISOString()
    };
    for (const rule of readConfig(context).rules || []) {
      await applyRule(root, rule, context, event);
    }
  }

  async function dispatchDerivedEvents(root, context, event) {
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
      if (phase.includes("ban")) await dispatchDesignerEvent(root, "designer.phase.ban.enter", { phase }, context, event);
      if (phase.includes("pick") || phase.includes("select")) await dispatchDesignerEvent(root, "designer.phase.pick.enter", { phase }, context, event);
      if (phase.includes("score") || phase.includes("result")) await dispatchDesignerEvent(root, "designer.phase.score.enter", { phase }, context, event);
    }

    if (eventType === "room.map.updated" || eventType === "room.snapshot") {
      const map = payload.pickedMap || payload.map || room.mapSelection?.pickedMap || null;
      if (map) await dispatchDesignerEvent(root, "designer.map.selected", { map }, context, event);
    }
  }

  function dispatchPickEvent(root, context, event, type, pick) {
    if (pick) {
      void dispatchDesignerEvent(root, type, { pick }, context, event);
    }
  }

  async function resolveValue(value, config, context, event) {
    if (!value || typeof value !== "object") return value;
    switch (value.source) {
      case "literal": return value.value ?? "";
      case "number": return Number(value.value) || 0;
      case "boolean": return !!value.value;
      case "event": return getByPath(event, value.path || "payload");
      case "room": return getByPath(context.store.room, value.path || "");
      case "config": return getByPath(config, value.path || "");
      case "variable": return getByPath(event.variables || {}, value.path || "");
      case "variableExists": return hasByPath(event.variables || {}, value.path || "");
      case "loopItem": return getByPath(event.loopItem, value.path || "");
      case "apiResponse": return getByPath(event.apiResponse, value.path || "");
      case "apiStatus": return event.apiStatus ?? 0;
      case "apiOk": return !!event.apiOk;
      case "apiRequest": {
        const apiEvent = await callBackendApi(value, config, context, event);
        event.apiUrl = apiEvent.apiUrl;
        event.apiStatus = apiEvent.apiStatus;
        event.apiOk = apiEvent.apiOk;
        event.apiResponse = apiEvent.apiResponse;
        return apiEvent.apiResponse;
      }
      case "arithmetic": return calculate(value.op, toNumber(await resolveValue(value.left, config, context, event)), toNumber(await resolveValue(value.right, config, context, event)));
      case "mathSingle": return calculateSingle(value.op, toNumber(await resolveValue(value.value, config, context, event)));
      case "compare": return compareValues(value.op, await resolveValue(value.left, config, context, event), await resolveValue(value.right, config, context, event));
      case "logic": return value.op === "OR"
        ? isTruthy(await resolveValue(value.left, config, context, event)) || isTruthy(await resolveValue(value.right, config, context, event))
        : isTruthy(await resolveValue(value.left, config, context, event)) && isTruthy(await resolveValue(value.right, config, context, event));
      case "logicNot": return !isTruthy(await resolveValue(value.value, config, context, event));
      case "textJoin": return (await Promise.all((value.parts || []).map(part => resolveValue(part, config, context, event)))).map(stringify).join("");
      case "textLength": return stringify(await resolveValue(value.value, config, context, event)).length;
      case "textContains": return stringify(await resolveValue(value.text, config, context, event)).includes(stringify(await resolveValue(value.search, config, context, event)));
      case "textReplace": return stringify(await resolveValue(value.text, config, context, event)).replaceAll(stringify(await resolveValue(value.from, config, context, event)), stringify(await resolveValue(value.to, config, context, event)));
      case "textSubstring": return stringify(await resolveValue(value.text, config, context, event)).slice(toNumber(await resolveValue(value.start, config, context, event)), toNumber(await resolveValue(value.end, config, context, event)));
      case "textCase": {
        const text = stringify(await resolveValue(value.value, config, context, event));
        return value.op === "toLowerCase" ? text.toLowerCase() : text.toUpperCase();
      }
      case "jsonParse": return parseJson(await resolveValue(value.value, config, context, event));
      case "jsonGet": return getByPath(await resolveValue(value.object, config, context, event), value.key || "");
      case "jsonStringify": return JSON.stringify(await resolveValue(value.value, config, context, event));
      case "jsonHas": {
        const target = await resolveValue(value.object, config, context, event);
        return target != null && Object.prototype.hasOwnProperty.call(Object(target), value.key || "");
      }
      case "typeof": return typeof await resolveValue(value.value, config, context, event);
      case "toNumber": return toNumber(await resolveValue(value.value, config, context, event));
      case "toString": return stringify(await resolveValue(value.value, config, context, event));
      default: return value.value ?? "";
    }
  }

  function calculate(op, left, right) {
    if (op === "-") return left - right;
    if (op === "*") return left * right;
    if (op === "/") return right === 0 ? 0 : left / right;
    if (op === "%") return right === 0 ? 0 : left % right;
    return left + right;
  }

  function calculateSingle(op, value) {
    if (op === "abs") return Math.abs(value);
    if (op === "floor") return Math.floor(value);
    if (op === "ceil") return Math.ceil(value);
    if (op === "sqrt") return Math.sqrt(Math.max(0, value));
    if (op === "random") return Math.floor(Math.random() * Math.max(0, value + 1));
    return Math.round(value);
  }

  function compareValues(op, left, right) {
    if (op === "!=") return left != right;
    if (op === ">") return left > right;
    if (op === "<") return left < right;
    if (op === ">=") return left >= right;
    if (op === "<=") return left <= right;
    return left == right;
  }

  function parseJson(value) {
    try { return typeof value === "string" ? JSON.parse(value) : value; } catch(e) { return null; }
  }

  async function callBackendApi(rule, config, context, event) {
    const url = normalizeBackendApiUrl(stringify(await resolveValue(rule.url, config, context, event)));
    const method = rule.method || "GET";
    const request = { method, headers: { "Accept": "application/json" } };
    if (method !== "GET" && method !== "HEAD") {
      const bodyValue = await resolveValue(rule.body, config, context, event);
      if (bodyValue !== "" && bodyValue != null) {
        request.headers["Content-Type"] = "application/json";
        request.body = typeof bodyValue === "string" ? bodyValue : JSON.stringify(bodyValue);
      }
    }

    const response = await fetch(url, request);
    const text = await response.text();
    const apiResponse = parseJson(text) ?? text;
    return {
      ...event,
      type: "designer.api.completed",
      apiUrl: url,
      apiStatus: response.status,
      apiOk: response.ok,
      apiResponse,
      payload: {
        ...(event.payload || {}),
        apiUrl: url,
        apiStatus: response.status,
        apiOk: response.ok,
        apiResponse
      }
    };
  }

  function normalizeBackendApiUrl(url) {
    if (!url) return "/api/health";
    if (/^https?:\\/\\//i.test(url)) return url;
    return url.startsWith("/") ? url : "/" + url;
  }

  function getByPath(source, path) {
    if (!path) return source;
    return String(path).split(".").reduce((current, segment) => current == null ? undefined : current[segment], source);
  }

  function hasByPath(source, path) {
    if (!path) return source != null;
    const segments = String(path).split(".");
    let current = source;
    for (const segment of segments) {
      if (current == null || !Object.prototype.hasOwnProperty.call(Object(current), segment)) return false;
      current = current[segment];
    }
    return true;
  }

  function setByPath(target, path, value) {
    const segments = String(path || "").split(".").filter(Boolean);
    if (segments.length === 0) return;
    let current = target;
    for (let index = 0; index < segments.length - 1; index++) {
      const segment = segments[index];
      if (current[segment] == null || typeof current[segment] !== "object") {
        current[segment] = {};
      }
      current = current[segment];
    }
    current[segments[segments.length - 1]] = value;
  }

  function getVariables(root) {
    root.__designerVars = root.__designerVars && typeof root.__designerVars === "object" ? root.__designerVars : {};
    return root.__designerVars;
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

  function toNumber(value) {
    const number = Number(value);
    return Number.isFinite(number) ? number : 0;
  }

  function isTruthy(value) {
    return value === true || value === 1 || String(value).toLowerCase() === "true" || (typeof value === "string" && value.length > 0);
  }

  function cssEscape(value) {
    return window.CSS?.escape ? CSS.escape(value) : String(value).replace(/["\\\\]/g, "");
  }

  window.IdvbpLayoutRuntime.register("${type}", {
    render,
    actions: {
      async syncState(element, action, context, event) {
        const currentEvent = event || context.store.event || {};
        for (const rule of readConfig(context).rules || []) {
          await applyRule(element, rule, context, currentEvent);
        }
        await dispatchDerivedEvents(element, context, currentEvent);
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

    window.IdvbpDesignerRuntimeCode = {
        buildDesignerRuntimeScript
    };
})();
