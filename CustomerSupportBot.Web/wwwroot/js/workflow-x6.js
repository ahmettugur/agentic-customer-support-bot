// workflow-x6.js — X6 v2 tabanlı görsel workflow tasarımcısı
(function () {
    'use strict';

    // ── Sabitler ──────────────────────────────────────────────────────────────
    const NODE_W = 220;
    const NODE_H = 76;
    const BR_H   = 84;
    const H_GAP  = 300;
    const V_GAP  = 140;

    const TYPE_CFG = {
        Respond:     { color: '#3b82f6', border: '#2563eb', label: 'YANIT',    icon: '▶' },
        Lookup:      { color: '#10b981', border: '#059669', label: 'SORGULA',  icon: '⚡' },
        Branch:      { color: '#f59e0b', border: '#d97706', label: 'KOSUL',    icon: '⑃' },
        SetVariable: { color: '#8b5cf6', border: '#7c3aed', label: 'DEGISKEN', icon: '✎' }
    };

    const ALLOWED_TOOLS = [
        { value: 'product_inquiry_tool', label: 'product_inquiry_tool — Urun bilgisi' },
        { value: 'product_list_tool',    label: 'product_list_tool — Urun listesi'   },
        { value: 'order_status_tool',    label: 'order_status_tool — Siparis durumu' },
        { value: 'get_last_order_tool',  label: 'get_last_order_tool — Son siparis'  },
        { value: 'get_all_orders_tool',  label: 'get_all_orders_tool — Tum siparisler' }
    ];

    let graph     = null;
    let dotNetRef = null;

    // ── Node shape kaydı ─────────────────────────────────────────────────────
    // Sol renkli şerit + refX/refY ile node-local konumlandırma.
    // textWrap / foreignObject / absolut x-y koordinat kullanılmıyor.
    function registerShapes() {
        Object.entries(TYPE_CFG).forEach(([type, cfg]) => {
            const isBranch = type === 'Branch';
            const nodeH    = isBranch ? BR_H : NODE_H;

            X6.Graph.registerNode('wf-' + type.toLowerCase(), {
                inherit: 'rect',
                width:   NODE_W,
                height:  nodeH,
                markup: [
                    { tagName: 'rect', selector: 'body'      },
                    { tagName: 'rect', selector: 'stripe'    },
                    { tagName: 'text', selector: 'typeLabel' },
                    { tagName: 'text', selector: 'mainLabel' },
                ],
                attrs: {
                    // Ana kutu: beyaz, renkli border, yuvarlak
                    body: {
                        refWidth:    '100%',
                        refHeight:   '100%',
                        rx: 8, ry: 8,
                        fill:        '#ffffff',
                        stroke:      cfg.border,
                        strokeWidth: 2,
                        filter: 'drop-shadow(0 1px 4px rgba(0,0,0,0.07))'
                    },
                    // Sol renkli şerit — node tipini ayırt eder
                    stripe: {
                        refX:       0,
                        refY:       0,
                        width:      6,
                        refHeight:  '100%',
                        rx: 4, ry: 4,
                        fill:       cfg.color
                    },
                    // Tip etiketi — refX/refY: node'a göreceli, üst üçte bir
                    typeLabel: {
                        refX:               16,
                        refY:               '30%',
                        fill:               cfg.color,
                        fontSize:           10,
                        fontWeight:         700,
                        fontFamily:         'Inter, system-ui, sans-serif',
                        textAnchor:         'start',
                        textVerticalAnchor: 'middle',
                        letterSpacing:      '0.4'
                    },
                    // İçerik etiketi — node'un alt bölgesi (orta-alt)
                    mainLabel: {
                        refX:               16,
                        refY:               '72%',
                        fill:               '#334155',
                        fontSize:           11,
                        fontFamily:         'Inter, system-ui, sans-serif',
                        textAnchor:         'start',
                        textVerticalAnchor: 'middle'
                    }
                },
                ports: buildPortGroups(cfg, isBranch)
            }, true);
        });
    }

    function buildPortGroups(cfg, isBranch) {
        const mkPort = (color, labelText, isInput) => ({
            attrs: {
                circle: {
                    r:           6,
                    fill:        isInput ? '#fff' : color,
                    stroke:      color,
                    strokeWidth: 2,
                    magnet:      true,   // X6 v2: port'u sürüklenebilir yapar
                    cursor:      isInput ? 'default' : 'crosshair'
                }
            },
            label: labelText ? {
                position: { name: 'outside', args: { offset: 10 } },
                markup: [{ tagName: 'text', selector: 'label' }],
                attrs: { label: { text: labelText, fill: color, fontSize: 10, fontWeight: 600 } }
            } : undefined
        });

        const groups = {
            in:      { position: 'top',    ...mkPort('#94a3b8', null, true) },
            out:     { position: 'bottom', ...mkPort(cfg.border, null, false) },
            onTrue:  { position: 'right',  ...mkPort('#10b981', 'Dogru', false) },
            onFalse: { position: 'bottom', ...mkPort('#ef4444', 'Yanlis', false) }
        };

        const items = isBranch
            ? [{ id: 'in', group: 'in' }, { id: 'onTrue', group: 'onTrue' }, { id: 'onFalse', group: 'onFalse' }]
            : [{ id: 'in', group: 'in' }, { id: 'out', group: 'out' }];

        return { groups, items };
    }

    // Çıkış portları hover'da büyür — bağlantı başlatılabileceğini gösterir
    function bindPortHover() {
        graph.on('node:mouseenter', ({ node }) => {
            node.getPorts().forEach(port => {
                const isOut = port.group === 'out' || port.group === 'onTrue' || port.group === 'onFalse';
                if (isOut) {
                    node.setPortProp(port.id, 'attrs/circle/r', 10);
                    node.setPortProp(port.id, 'attrs/circle/strokeWidth', 3);
                }
            });
        });
        graph.on('node:mouseleave', ({ node }) => {
            node.getPorts().forEach(port => {
                node.setPortProp(port.id, 'attrs/circle/r', 6);
                node.setPortProp(port.id, 'attrs/circle/strokeWidth', 2);
            });
        });
    }

    // ── Graph init ────────────────────────────────────────────────────────────
    function initGraph(container) {
        graph = new X6.Graph({
            container,
            autoResize: true,
            background: { color: '#f8fafc' },
            grid: {
                visible: true,
                size: 20,
                type: 'doubleMesh',
                args: [
                    { color: '#e2e8f0', thickness: 1 },
                    { color: '#cbd5e1', thickness: 1, factor: 4 }
                ]
            },
            // History — X6 v2'de doğrudan config ile aktif edilir
            history: { enabled: true },
            snapline: { enabled: true, tolerance: 10 },
            selecting: {
                enabled: true,
                showNodeSelectionBox: true,
                rubberband: false,
                movable: true
            },
            connecting: {
                snap:         { radius: 40 },
                allowBlank:   false,
                allowLoop:    false,
                allowMulti:   false,
                highlight:    true,
                connector:    { name: 'rounded', args: { radius: 10 } },
                router:       { name: 'er', args: { direction: 'V', offset: 20 } },
                // Sadece çıkış portlarından (out, onTrue, onFalse) sürüklemeye izin ver
                validateMagnet({ magnet }) {
                    const pg = magnet.getAttribute('port-group');
                    return pg === 'out' || pg === 'onTrue' || pg === 'onFalse';
                },
                validateConnection({ sourceCell, targetCell, targetMagnet }) {
                    if (!targetMagnet) return false;
                    if (targetMagnet.getAttribute('port-group') !== 'in') return false;
                    if (sourceCell?.id === targetCell?.id) return false;
                    return true;
                },
                createEdge({ sourceMagnet }) {
                    const pg = sourceMagnet?.getAttribute('port-group') || 'out';
                    return makeEdge(pg);
                }
            },
            mousewheel: { enabled: true, zoomAtMousePosition: true, modifiers: null, factor: 1.1 },
            panning:    { enabled: true, eventTypes: ['rightMouseDown'] }
        });
    }

    function makeEdge(portGroup) {
        const color = portGroup === 'onTrue'  ? '#10b981'
                    : portGroup === 'onFalse' ? '#ef4444'
                    : '#94a3b8';
        return new X6.Shape.Edge({
            attrs: {
                line: {
                    stroke:       color,
                    strokeWidth:  2,
                    targetMarker: { name: 'classic', size: 8, fill: color }
                }
            },
            zIndex: 0
        });
    }

    // ── Events ────────────────────────────────────────────────────────────────
    function bindEvents() {
        graph.on('node:selected',   ({ node }) => notifyBlazor('OnNodeSelected', JSON.stringify(node.getData())));
        graph.on('node:unselected', ()          => notifyBlazor('OnNodeSelected', null));
        graph.on('blank:click',     ()          => { graph.cleanSelection(); notifyBlazor('OnNodeSelected', null); });
        graph.on('edge:connected',  ()          => notifyBlazor('OnGraphChanged', null));
        graph.on('edge:removed',    ()          => notifyBlazor('OnGraphChanged', null));
        graph.on('node:removed',    ()          => notifyBlazor('OnGraphChanged', null));
        bindPortHover();
    }

    function notifyBlazor(method, arg) {
        if (dotNetRef) {
            try { dotNetRef.invokeMethodAsync(method, arg); } catch (_) {}
        }
    }

    // ── Node yardımcıları ─────────────────────────────────────────────────────
    function truncate(str, max) {
        if (!str) return '';
        const s = str.replace(/\n/g, ' ').trim();
        return s.length > max ? s.substring(0, max - 1) + '…' : s;
    }

    function makeNodeLabel(data) {
        return data.label
            || data.template
            || data.condition
            || data.tool
            || (data.variableName ? data.variableName + ' = ' + (data.variableValue || '') : '')
            || '';
    }

    function applyNodeAttrs(node, data) {
        const cfg  = TYPE_CFG[data.type] || TYPE_CFG.Respond;
        node.setAttrByPath('typeLabel/text', cfg.icon + ' ' + cfg.label);
        node.setAttrByPath('mainLabel/text', truncate(makeNodeLabel(data), 28));
    }

    function addNode(data, x, y) {
        const isBranch  = data.type === 'Branch';
        const shapeName = 'wf-' + data.type.toLowerCase();

        const node = graph.addNode({
            id:    data.id,
            shape: shapeName,
            x, y,
            data:  { ...data }
        });

        applyNodeAttrs(node, data);
        return node;
    }

    function addEdge(sourceId, sourcePort, targetId) {
        if (!sourceId || !sourcePort || !targetId) return;
        if (!graph.getCellById(sourceId) || !graph.getCellById(targetId)) return;

        const dup = graph.getEdges().find(e =>
            e.getSourceCellId() === sourceId &&
            e.getSourcePortId() === sourcePort
        );
        if (dup) return; // her port'tan tek bağlantıya izin ver

        graph.addEdge({
            source: { cell: sourceId, port: sourcePort },
            target: { cell: targetId, port: 'in' },
            ...makeEdge(sourcePort).toJSON()
        });
    }

    // ── Auto-layout ───────────────────────────────────────────────────────────
    function computeLayout(steps, startStepId) {
        const stepMap  = Object.fromEntries(steps.map(s => [s.id, s]));
        const startId  = startStepId || steps[0]?.id;
        const pos      = {};
        const visited  = new Set();

        const queue = [{ id: startId, x: 0, y: 60 }];

        while (queue.length > 0) {
            const { id, x, y } = queue.shift();
            if (!id || visited.has(id)) continue;
            visited.add(id);

            pos[id] = { x, y };
            const step = stepMap[id];
            if (!step) continue;

            const ny = y + V_GAP;
            if (step.next    && !visited.has(step.next))
                queue.push({ id: step.next,    x,              y: ny });
            if (step.onTrue  && !visited.has(step.onTrue))
                queue.push({ id: step.onTrue,  x: x + H_GAP * 0.55, y: ny });
            if (step.onFalse && !visited.has(step.onFalse))
                queue.push({ id: step.onFalse, x: x - H_GAP * 0.55, y: ny });
        }

        // Bağlı olmayan node'ları sağa ekle
        let ox = Object.values(pos).length
            ? Math.max(...Object.values(pos).map(p => p.x)) + H_GAP
            : 0;
        steps.forEach(s => {
            if (!pos[s.id]) { pos[s.id] = { x: ox, y: 60 }; ox += H_GAP; }
        });

        // Tüm x pozisyonlarını ortalayarak canvas merkezine taşı
        const xs  = Object.values(pos).map(p => p.x);
        const mid = (Math.min(...xs) + Math.max(...xs)) / 2;
        const cx  = 500;
        Object.keys(pos).forEach(id => { pos[id].x += cx - mid; });

        return pos;
    }

    // ── Public API ────────────────────────────────────────────────────────────
    window.wfDesigner = {

        init(containerId, ref) {
            dotNetRef = ref;
            const el  = document.getElementById(containerId);
            if (!el) { console.error('[wfDesigner] container bulunamadi:', containerId); return; }
            registerShapes();
            initGraph(el);
            bindEvents();
        },

        loadGraph(jsonString) {
            if (!graph) return;
            graph.clearCells();

            let wf;
            try   { wf = typeof jsonString === 'string' ? JSON.parse(jsonString) : jsonString; }
            catch { console.error('[wfDesigner] gecersiz JSON'); return; }

            if (!wf?.steps?.length) return;

            const pos = computeLayout(wf.steps, wf.startStepId);

            // Node'lar
            wf.steps.forEach(step => {
                const p = pos[step.id] || { x: 100, y: 100 };
                addNode(step, p.x - NODE_W / 2, p.y);
            });

            // Edge'ler
            wf.steps.forEach(step => {
                if (step.next)    addEdge(step.id, 'out',     step.next);
                if (step.onTrue)  addEdge(step.id, 'onTrue',  step.onTrue);
                if (step.onFalse) addEdge(step.id, 'onFalse', step.onFalse);
            });

            setTimeout(() => graph.zoomToFit({ maxScale: 1, padding: 50 }), 80);
        },

        getGraphData() {
            if (!graph) return '{}';

            const edgeMap = {};
            graph.getEdges().forEach(e => {
                const src = e.getSourceCellId();
                const prt = e.getSourcePortId();
                const tgt = e.getTargetCellId();
                if (!edgeMap[src]) edgeMap[src] = {};
                edgeMap[src][prt] = tgt;
            });

            const steps = graph.getNodes().map(node => {
                const data    = node.getData() || {};
                const conns   = edgeMap[node.id] || {};
                const isBr    = data.type === 'Branch';
                return {
                    id:            node.id,
                    type:          data.type          || 'Respond',
                    label:         data.label         || null,
                    next:          isBr ? null         : (conns['out']     || null),
                    onTrue:        isBr ? (conns['onTrue']  || null) : null,
                    onFalse:       isBr ? (conns['onFalse'] || null) : null,
                    template:      data.template      || null,
                    tool:          data.tool          || null,
                    parameters:    data.parameters    || {},
                    storeAs:       data.storeAs       || null,
                    condition:     data.condition     || null,
                    variableName:  data.variableName  || null,
                    variableValue: data.variableValue || null,
                    _y: node.getPosition().y
                };
            });

            const start = steps.reduce((m, s) => (!m || s._y < m._y ? s : m), null);

            return JSON.stringify({
                startStepId: start?.id || null,
                steps: steps.map(({ _y, ...s }) => s)
            });
        },

        addStep(type) {
            if (!graph) return;
            const id  = 'step_' + Math.random().toString(36).substring(2, 8);
            const vp  = graph.getGraphArea();
            const cx  = vp ? vp.x + vp.width  / 2 : 400;
            const cy  = vp ? vp.y + vp.height / 2 : 300;
            addNode({ id, type }, cx - NODE_W / 2, cy - NODE_H / 2);
        },

        updateSelectedNode(dataJson) {
            if (!graph) return;
            const nodes = graph.getSelectedCells().filter(c => c.isNode());
            if (!nodes.length) return;
            let data;
            try { data = JSON.parse(dataJson); } catch { return; }
            const node = nodes[0];
            node.setData(data, { overwrite: true });
            applyNodeAttrs(node, data);
        },

        // History — X6 v2'de graph.undo/redo doğrudan veya history plugin üzerinden
        undo() {
            if (!graph) return;
            try {
                if (typeof graph.undo === 'function') { graph.undo(); return; }
                if (graph.history?.undo) graph.history.undo();
            } catch (e) { console.warn('[wfDesigner] undo:', e.message); }
        },

        redo() {
            if (!graph) return;
            try {
                if (typeof graph.redo === 'function') { graph.redo(); return; }
                if (graph.history?.redo) graph.history.redo();
            } catch (e) { console.warn('[wfDesigner] redo:', e.message); }
        },

        zoomIn()  { graph?.zoom(0.15); },
        zoomOut() { graph?.zoom(-0.15); },
        zoomFit() { graph?.zoomToFit({ maxScale: 1.2, padding: 50 }); },
        center()  { graph?.centerContent(); },

        deleteSelected() {
            if (!graph) return;
            const cells = graph.getSelectedCells();
            if (cells.length) graph.removeCells(cells);
        },

        getToolOptions() { return JSON.stringify(ALLOWED_TOOLS); },

        destroy() {
            try { graph?.dispose(); } catch (_) {}
            graph = null;
            dotNetRef = null;
        }
    };
})();
