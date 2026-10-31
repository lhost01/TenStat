/*
 * Live2DPet — 页面侧 Live2D 渲染与交互
 *
 * 依赖（全部本地，app.local 虚拟主机）：
 *   vendor/pixi.min.js               pixi.js 6.5.10
 *   vendor/live2dcubismcore.min.js   Live2D Cubism Core
 *   vendor/cubism4.min.js            pixi-live2d-display 0.4.0 (Cubism 4 bundle)
 *
 * 模型设置来自 https://model.local/__model.json —— 由宿主进程拦截该虚拟地址、
 * 现场合成一份带 Motions / Expressions 的 model3.json 返回（模型目录只读，不改原文件）。
 */
(function () {
    'use strict';

    // ---------------------------------------------------------------- 宿主通道

    var host = (window.chrome && window.chrome.webview) ? window.chrome.webview : null;
    var hintEl = document.getElementById('hint');
    var stageEl = document.getElementById('stage');

    function post(obj) {
        if (!host) return;
        try { host.postMessage(obj); } catch (e) { /* 宿主已关闭 */ }
    }

    function log(text) {
        var s = String(text);
        post({ type: 'log', message: s });
        try { console.log('[pet] ' + s); } catch (e) { }
    }

    function reportError(text) {
        var s = String(text);
        post({ type: 'error', message: s });
        try { console.error('[pet] ' + s); } catch (e) { }
    }

    function showError(text) {
        if (!hintEl) return;
        hintEl.innerHTML = '';
        var box = document.createElement('div');
        box.className = 'err';
        box.textContent = text;
        hintEl.appendChild(box);
        hintEl.style.display = 'flex';
    }

    function hideError() {
        if (!hintEl) return;
        hintEl.style.display = 'none';
        hintEl.innerHTML = '';
    }

    // ---------------------------------------------------------------- 视线跟随

    // 宿主每 150ms 发一次全局鼠标位置（页面 CSS 像素坐标），换算成 -1..1 的方向。
    // 不用 model.focus()：它的参数写入时机在动作之后、但会被本模型的动作覆盖，
    // 而且这个模型没做头转绑定时靠 focus 完全看不出变化。
    var focusTarget = { x: 0, y: 0 };
    var focusX = null;
    var focusY = null;

    function focusAt(px, py) {
        var x = Number(px);
        var y = Number(py);
        if (!isFinite(x) || !isFinite(y)) return;

        focusX = x;
        focusY = y;

        var w = window.innerWidth || 420;
        var h = window.innerHeight || 760;

        focusTarget.x = Math.max(-1, Math.min(1, (x / w) * 2 - 1));
        focusTarget.y = Math.max(-1, Math.min(1, (y / h) * 2 - 1));
    }

    window.__petFocus = focusAt;

    // 在 coreModel.update() 之前写参数：这时动作、眨眼、物理都算完了，写进去的朝向不会被覆盖。
    // 参数不存在或范围是 0（模型没做这个绑定）就跳过。
    // 这个模型的 ParamAngleX/Y 有范围但被动作管线覆盖，光写参数看不出变化，
    // 所以再加一层「整只朝指针方向轻微偏移」，两个手段一起用。
    var lookRoot = null;
    var lookOffset = { x: 0, y: 0 };

    function ensureLookRoot() {
        if (lookRoot || !model || !app) return lookRoot;

        lookRoot = new PIXI.Container();
        app.stage.addChild(lookRoot);
        lookRoot.addChild(model);
        return lookRoot;
    }

    function applyLookOffset(target) {
        if (!ensureLookRoot()) return;

        var tx = target.x * 18;
        var ty = target.y * 10;

        lookOffset.x += (tx - lookOffset.x) * 0.18;
        lookOffset.y += (ty - lookOffset.y) * 0.18;

        lookRoot.x = lookOffset.x;
        lookRoot.y = lookOffset.y;
    }

    function applyFocusParams(target) {
        if (!model || !model.internalModel) return;
        var cm = model.internalModel.coreModel;
        if (!cm) return;

        // 不查 min/max：某些 Cubism Core 绑定下这两个方法返回值不可靠，
        // 直接写值，参数不存在时由 core 自己忽略。
        function set(id, value) {
            try { cm.setParameterValueById(id, value, 1); } catch (e) { }
        }

        set('ParamAngleX', target.x * 30);
        set('ParamAngleY', target.y * 22);
        set('ParamAngleZ', target.x * 8);
        set('ParamEyeBallX', target.x);
        set('ParamEyeBallY', target.y);
        set('ParamBodyAngleX', target.x * 8);

        applyLookOffset(target);
    }
    // 调试探针：看参数范围与读写是否可用
    window.__petParamProbe = function () {
        try {
            if (!model) return 'no model';
            var cm = model.internalModel && model.internalModel.coreModel;
            if (!cm) return 'no coreModel';

            var ids = ['ParamAngleX', 'ParamAngleY', 'ParamEyeBallX', 'ParamBodyAngleX'];

            var out = { methods: [], focus: { x: focusTarget.x, y: focusTarget.y }, ranges: {}, values: {} };
            try {
                var all = cm._parameterIds || [];
                out.angleLike = [];
                out.rawRanges = {};
                var mins = cm._parameterMinimumValues || [];
                var maxs = cm._parameterMaximumValues || [];
                for (var k = 0; k < all.length; k++) {
                    var pid = String(all[k]);
                    if (pid === 'ParamAngleX' || pid === 'ParamAngleY' || pid === 'ParamEyeBallX' || pid === 'ParamBodyAngleX') {
                        out.rawRanges[pid] = [mins[k], maxs[k]];
                    }
                }
                for (var i = 0; i < all.length; i++) {
                    var id = String(all[i]);
                    if (id.indexOf('Angle') >= 0 || id.indexOf('Eye') >= 0 || id.indexOf('Body') >= 0 || id.indexOf('Head') >= 0) {
                        out.angleLike.push(id);
                    }
                }
            } catch (e2) { out.angleLike = 'err'; }

            try { out.methods = Object.keys(cm).slice(0, 40); } catch (e) { }

            ids.forEach(function (id) {
                try { out.ranges[id] = [cm.getParameterMinimumValue(id), cm.getParameterMaximumValue(id)]; } catch (e) { out.ranges[id] = 'err'; }
                try { out.values[id] = cm.getParameterValueById(id); } catch (e) { out.values[id] = 'err'; }
            });

            return out;
        } catch (e) {
            return 'err: ' + (e && e.message);
        }
    };

    var bubbleEl = document.getElementById('bubble');
    var bubbleTimer = null;

    function showBubble(text) {
        if (!bubbleEl) return;

        var value = String(text || '').trim();
        if (!value) return;

        bubbleEl.textContent = value;
        bubbleEl.classList.add('show');

        if (bubbleTimer) {
            clearTimeout(bubbleTimer);
        }
        bubbleTimer = setTimeout(function () {
            bubbleEl.classList.remove('show');
        }, 5200);
    }

    // ---------------------------------------------------------------- 参数

    var query = new URLSearchParams(window.location.search);

    var modelRel = query.get('model') || '/__model.json';
    try { modelRel = decodeURIComponent(modelRel); } catch (e) { }
    if (modelRel.charAt(0) !== '/') modelRel = '/' + modelRel;

    var SETTINGS_URL = 'https://model.local' + modelRel;

    var userScale = parseFloat(query.get('scale') || '1');
    if (!isFinite(userScale) || userScale <= 0) userScale = 1;

    // ---------------------------------------------------------------- 底色

    // windowed WebView2 不支持逐像素透明，所以宿主把"卡片底色"传进来，
    // 页面自己也刷成同一个颜色 —— 视觉上就和卡片长在一起了。
    function applyBackdrop(value) {
        var v = String(value || '');
        if (!v) return;
        if (v.charAt(0) !== '#') v = '#' + v;
        try {
            document.documentElement.style.setProperty('--pet-bg', v);
            document.body.style.background = v;
            if (stageEl) stageEl.style.background = v;
        } catch (e) { /* 无所谓 */ }
    }

    window.__petSetBackdrop = applyBackdrop;

    var initialBackdrop = query.get('bg');
    if (initialBackdrop) applyBackdrop(initialBackdrop);

    // ---------------------------------------------------------------- 状态

    var app = null;
    var model = null;
    var ready = false;
    var loadTimer = null;
    var idleTimer = null;

    var modelSize = { w: 420, h: 760 };
    var motionGroups = {};
    var expressionNames = [];

    var PAD_RATIO = 0.04; // 四周边距 4%

    // ---------------------------------------------------------------- 渲染器

    function createRenderer() {
        app = new PIXI.Application({
            width: window.innerWidth || 420,
            height: window.innerHeight || 760,
            backgroundAlpha: 0,
            backgroundColor: 0x000000,
            transparent: true,
            antialias: true,
            autoStart: true,
            autoDensity: true,
            resolution: window.devicePixelRatio || 1,
            powerPreference: 'high-performance'
        });

        // 双保险：不同 pixi 版本对 backgroundAlpha 的落点不一样
        try { app.renderer.backgroundAlpha = 0; } catch (e) { }
        try { app.renderer.backgroundColor = 0x000000; } catch (e) { }

        var view = app.view;
        view.style.position = 'fixed';
        view.style.left = '0';
        view.style.top = '0';
        view.style.width = '100%';
        view.style.height = '100%';
        view.style.background = 'transparent';

        stageEl.appendChild(view);
    }

    // ---------------------------------------------------------------- 模型尺寸与布局

    function measure(m) {
        var candidates = [];
        var im = m && m.internalModel;

        if (im) {
            candidates.push([im.originalWidth, im.originalHeight]);
            candidates.push([im.width, im.height]);
        }
        candidates.push([m && m.width, m && m.height]);

        for (var i = 0; i < candidates.length; i++) {
            var w = candidates[i][0];
            var h = candidates[i][1];
            if (typeof w === 'number' && typeof h === 'number' && isFinite(w) && isFinite(h) && w > 1 && h > 1) {
                return { w: w, h: h };
            }
        }

        return { w: 420, h: 760 };
    }

    function layout() {
        var W = window.innerWidth || 420;
        var H = window.innerHeight || 760;

        if (app && app.renderer) {
            try { app.renderer.resize(W, H); } catch (e) { }
        }

        if (!model) return;

        var padX = Math.max(2, W * PAD_RATIO);
        var padY = Math.max(2, H * PAD_RATIO);
        var availW = Math.max(1, W - padX * 2);
        var availH = Math.max(1, H - padY * 2);

        var fit = Math.min(availW / modelSize.w, availH / modelSize.h);
        var s = fit * userScale;

        try { model.scale.set(s); } catch (e) {
            model.scale.x = s;
            model.scale.y = s;
        }

        // 底边对齐窗口底部、水平居中（anchor 固定为左上，直接算位置最稳）
        model.x = (W - modelSize.w * s) / 2;
        model.y = H - padY - modelSize.h * s;
    }

    // ---------------------------------------------------------------- 动作 / 表情

    function playMotion(group) {
        if (!model) return false;

        try {
            if (typeof model.motion === 'function') {
                if (group) return !!model.motion(group);
                return !!model.motion();
            }
        } catch (e) {
            log('播放动作 ' + group + ' 失败: ' + (e && e.message));
        }

        return false;
    }

    function playExpression(name) {
        if (!model || !name) return false;

        var im = model.internalModel;
        var mm = im && im.motionManager;
        var em = mm && mm.expressionManager;

        try {
            if (em && typeof em.setExpression === 'function') {
                em.setExpression(name);
                return true;
            }
        } catch (e) {
            log('设置表情 ' + name + ' 失败: ' + (e && e.message));
        }

        try {
            if (typeof model.expression === 'function') {
                model.expression(name);
                return true;
            }
        } catch (e2) {
            log('model.expression(' + name + ') 失败: ' + (e2 && e2.message));
        }

        return false;
    }

    function scheduleIdle() {
        clearTimeout(idleTimer);

        var delay = 9000 + Math.random() * 12000;
        idleTimer = setTimeout(function () {
            if (!ready) return;
            playMotion(motionGroups.Idle ? 'Idle' : null);
            scheduleIdle();
        }, delay);
    }

    function reactTap() {
        var played = false;

        if (motionGroups.TapBody) played = playMotion('TapBody');
        if (!played && motionGroups.Idle) playMotion('Idle');

        if (expressionNames.length > 0) {
            var name = expressionNames[Math.floor(Math.random() * expressionNames.length)];
            playExpression(name);
        }
    }

    // ---------------------------------------------------------------- 自检探针

    // 贴图是否真的上传成功（CORS 被挡时 pixi 只会留一张空贴图，页面看起来"白屏"却不报错）
    function probeTextures() {
        if (!model) return 0;

        var list = model.textures || (model.internalModel && model.internalModel.textures) || [];
        var ok = 0;

        for (var i = 0; i < list.length; i++) {
            var t = list[i];
            var bt = t && t.baseTexture;
            var w = (bt && bt.width) || 0;
            var h = (bt && bt.height) || 0;

            if (t && t.valid && bt && bt.valid && w > 1 && h > 1) ok++;

            log('贴图[' + i + '] valid=' + !!(t && t.valid) + ' baseValid=' + !!(bt && bt.valid) +
                ' size=' + w + 'x' + h + ' type=' + (bt && bt.resource ? (bt.resource.constructor.name || '?') : '?'));
        }

        return ok;
    }

    // 真渲染一次到 RenderTexture，数非透明像素 —— 证明"画出来了"，而不是只有对象树
    function probePixels() {
        try {
            var rt = PIXI.RenderTexture.create({ width: 210, height: 380 });
            app.renderer.render(app.stage, { renderTexture: rt });

            var px = app.renderer.extract.pixels(rt);
            var opaque = 0;
            var total = px.length / 4;

            for (var i = 3; i < px.length; i += 4) {
                if (px[i] > 8) opaque++;
            }

            rt.destroy(true);
            log('渲染探针：非透明像素 ' + opaque + ' / ' + total + '（' + (100 * opaque / total).toFixed(1) + '%）');

            return opaque;
        } catch (e) {
            log('渲染探针失败: ' + (e && e.message));
            return -1;
        }
    }

    // ---------------------------------------------------------------- 加载

    async function fetchSettings() {
        // 首选：宿主在文档创建前注入的副本。
        // 实测 WebView2 1.0.4314 不会为虚拟主机（app.local / model.local）的请求触发
        // WebResourceRequested，所以 https://model.local/__model.json 的拦截大概率不会发生 ——
        // 注入副本是可靠路径，拦截只是"万一它能触发"的额外通道。
        if (window.__PET_SETTINGS_JSON__) {
            try {
                var injected = JSON.parse(window.__PET_SETTINGS_JSON__);
                log('使用宿主注入的 model3.json');
                return injected;
            } catch (e) {
                log('注入 JSON 解析失败: ' + (e && e.message));
            }
        }

        // 兜底：直接问虚拟地址（若宿主拦截生效则由它应答）
        try {
            var resp = await fetch(SETTINGS_URL, { cache: 'no-store' });
            if (!resp.ok) throw new Error('HTTP ' + resp.status);

            var json = await resp.json();
            log('已取到动态 model3.json: ' + SETTINGS_URL);
            return json;
        } catch (e2) {
            log('fetch ' + SETTINGS_URL + ' 失败: ' + (e2 && e2.message));
            return null;
        }
    }

    function rememberSettings(json) {
        var fr = (json && json.FileReferences) || {};
        motionGroups = fr.Motions || {};

        var exps = fr.Expressions || [];
        expressionNames = exps
            .map(function (x) { return x && x.Name; })
            .filter(function (n) { return typeof n === 'string' && n.length > 0; });

        log('动作分组: [' + Object.keys(motionGroups).join(', ') + ']；表情 ' + expressionNames.length + ' 个');
    }

    async function loadModel() {
        if (!window.PIXI) throw new Error('PIXI 未加载（vendor/pixi.min.js）');
        if (!PIXI.live2d || !PIXI.live2d.Live2DModel) {
            throw new Error('PIXI.live2d.Live2DModel 不存在（vendor/cubism4.min.js 与 pixi 版本不匹配？）');
        }
        if (typeof Live2DCubismCore === 'undefined') {
            throw new Error('Live2DCubismCore 未加载（vendor/live2dcubismcore.min.js）');
        }

        var json = await fetchSettings();
        if (!json) {
            throw new Error('拿不到动态 model3.json（宿主注入缺失且 ' + SETTINGS_URL + ' 不可达）');
        }
        rememberSettings(json);

        var options = { autoInteract: false, autoUpdate: true };

        // 首选：拿到的 JSON + 设置 URL 当相对路径基准（不依赖虚拟地址拦截是否真的发生）
        // 注意：pixi-live2d-display 0.4.0 的 ModelSettings 要求 JSON 自带 url 字段，
        // 构造函数第二个参数不是 options —— 所以这里必须把 url 塞进 JSON 本身。
        if (json && PIXI.live2d.Cubism4ModelSettings) {
            try {
                if (typeof json.url !== 'string' || !json.url) json.url = SETTINGS_URL;

                var settings = new PIXI.live2d.Cubism4ModelSettings(json);
                return await PIXI.live2d.Live2DModel.from(settings, options);
            } catch (e) {
                log('Cubism4ModelSettings 路径失败: ' + (e && e.message) + '（回退到 URL 加载）');
            }
        }

        // 兜底：直接按 URL 加载（由宿主 WebResourceRequested 提供 JSON）
        return await PIXI.live2d.Live2DModel.from(SETTINGS_URL, options);
    }

    function onLoaded(m) {
        model = m;
        modelSize = measure(m);

        try {
            model.internalModel.on('beforeModelUpdate', function () {
                applyFocusParams(focusTarget);
            });
            log('视线跟随已启用（参数钩子）');
        } catch (e) {
            log('视线跟随不可用: ' + (e && e.message));
        }

        hideError();

        try { app.stage.addChild(m); } catch (e) { log('addChild 失败: ' + (e && e.message)); }

        // anchor 归零，位置完全由 layout() 计算
        try {
            if (m.anchor && typeof m.anchor.set === 'function') m.anchor.set(0, 0);
        } catch (e) { }

        layout();

        ready = true;
        clearTimeout(loadTimer);

        playMotion(motionGroups.Idle ? 'Idle' : null);
        scheduleIdle();

        log('模型就绪，原始尺寸 ' + modelSize.w + 'x' + modelSize.h);
        post({ type: 'ready', width: modelSize.w, height: modelSize.h, scale: userScale });

        // 只有带 probe=1（宿主开调试日志时会加）才做重活自检
        if (query.get('probe') === '1') {
            setTimeout(function () {
                var texOk = probeTextures();
                var opaque = probePixels();

                if (opaque === 0) {
                    reportError('自检：模型对象已加载但渲染结果全透明（贴图可能未上传 / CORS 受限）');
                } else if (opaque > 0) {
                    log('自检通过：贴图 ' + texOk + ' 张有效，画面有内容');
                }
            }, 2000);
        }
    }

    function boot() {
        try {
            createRenderer();
        } catch (e) {
            var msg = '渲染器初始化失败: ' + (e && e.message) + '\n' + SETTINGS_URL;
            showError(msg);
            reportError(msg);
            return;
        }

        // WebGL 可用性（诊断用，不影响后续流程）
        try {
            var probe = document.createElement('canvas');
            var gl = probe.getContext('webgl2') || probe.getContext('webgl') || probe.getContext('experimental-webgl');
            log(gl ? 'WebGL 可用' : 'WebGL 不可用（渲染可能失败）');
        } catch (e) { }

        loadTimer = setTimeout(function () {
            if (ready) return;
            var msg = '加载超时（8 秒）：' + SETTINGS_URL;
            showError(msg);
            reportError(msg);
        }, 8000);

        loadModel().then(onLoaded).catch(function (e) {
            clearTimeout(loadTimer);
            var msg = '加载 Live2D 模型失败\n' + (e && e.message ? e.message : String(e)) + '\n\n模型设置: ' + SETTINGS_URL;
            showError(msg);
            reportError(msg);
        });
    }

    // ---------------------------------------------------------------- 交互：拖动

    var drag = { active: false, x: 0, y: 0, moved: 0, pointerId: null };
    var lastTapTime = 0;

    function onPointerDown(e) {
        if (e.button !== undefined && e.button !== 0) return;

        drag.active = true;
        drag.x = e.screenX;
        drag.y = e.screenY;
        drag.moved = 0;
        drag.pointerId = e.pointerId;

        try { e.target.setPointerCapture(e.pointerId); } catch (err) { }

        post({ type: 'dragStart' });
        try { e.preventDefault(); } catch (err2) { }
    }

    function onPointerMove(e) {
        if (!drag.active) return;

        var dx = e.screenX - drag.x;
        var dy = e.screenY - drag.y;
        if (!dx && !dy) return;

        drag.x = e.screenX;
        drag.y = e.screenY;
        drag.moved += Math.abs(dx) + Math.abs(dy);

        // 宿主用光标屏幕坐标差分定位，dx/dy 只当"该移动了"的心跳
        post({ type: 'drag', dx: dx, dy: dy });
    }

    function onPointerUp(e) {
        if (!drag.active) return;

        drag.active = false;
        try { e.target.releasePointerCapture(drag.pointerId); } catch (err) { }

        post({ type: 'dragEnd' });

        if (drag.moved <= 5) {
            var now = Date.now();

            if (now - lastTapTime < 400) {
                lastTapTime = 0;
                post({ type: 'doubleclick' });
            } else {
                lastTapTime = now;
                reactTap();
            }
        }
    }

    function onPointerCancel() {
        if (!drag.active) return;

        drag.active = false;
        post({ type: 'dragEnd' });
    }

    window.addEventListener('pointerdown', onPointerDown, true);
    window.addEventListener('pointermove', onPointerMove, true);
    window.addEventListener('pointerup', onPointerUp, true);
    window.addEventListener('pointercancel', onPointerCancel, true);

    // ---------------------------------------------------------------- 交互：滚轮缩放

    window.addEventListener('wheel', function (e) {
        try { e.preventDefault(); } catch (err) { }
        post({ type: 'wheel', delta: (e.deltaY || 0) > 0 ? 1 : -1 });
    }, { passive: false });

    // ---------------------------------------------------------------- 宿主指令

    if (host) {
        host.addEventListener('message', function (ev) {
            var data = ev.data;

            if (typeof data === 'string') {
                try { data = JSON.parse(data); } catch (e) { return; }
            }
            if (!data || !data.cmd) return;

            switch (data.cmd) {
                case 'scale':
                    var v = Number(data.value);
                    if (isFinite(v) && v > 0) {
                        userScale = Math.max(0.25, Math.min(3, v));
                        layout();
                        log('缩放 → ' + userScale.toFixed(3));
                    }
                    break;
                case 'expression':
                    playExpression(data.name);
                    break;
                case 'motion':
                    playMotion(data.group);
                    break;
                case 'say':
                    showBubble(data.text);
                    // 说话时顺带眨个眼/换个表情，看起来更像在回应
                    if (data.mood !== false) {
                        playExpression(expressionNames.length
                            ? expressionNames[Math.floor(Math.random() * expressionNames.length)]
                            : null);
                    }
                    break;

                case 'focus':
                    // 宿主每 150ms 发一次全局鼠标位置（页面内 CSS 像素坐标），让模型盯着指针
                    focusAt(data.x, data.y);
                    break;
                case 'ping':
                    post({ type: 'pong' });
                    break;
                default:
                    break;
            }
        });
    }

    // ---------------------------------------------------------------- 杂项

    window.addEventListener('resize', layout);

    window.addEventListener('error', function (e) {
        reportError('页面脚本错误: ' + (e && e.message ? e.message : 'unknown'));
    });

    window.addEventListener('unhandledrejection', function (e) {
        var r = e && e.reason;
        reportError('未处理的 Promise 拒绝: ' + (r && r.message ? r.message : String(r)));
    });

    // 禁掉右键菜单与拖拽选中
    document.addEventListener('contextmenu', function (e) { e.preventDefault(); });
    document.addEventListener('dragstart', function (e) { e.preventDefault(); });

    boot();
})();
