// 【导读】PolyMatch3 WASM 前端，全部工作只有三件事：
//   1. 加载 dotnet 运行时，拿到 GameBridge 的导出函数（NewGame/OfferSwap/GetBoard/GetScore）；
//   2. 维护一份棋盘镜像（board.cells/kinds）：开局来自 NewGame 的 JSON，之后按事件增量更新，
//      每批事件播完再用 GetBoard() 权威快照校准一次——镜像只是动画道具，逻辑层永远为准；
//   3. 把事件流翻译成动画：Swap 对调 → Match 高亮 → BombSpawn 转弹 → Eliminate 淡出 →
//      Fall 飞行棋子位移 → Spawn 从上方落入。TIMING 控制每阶段停留，CSS 过渡负责平滑。
// 大棋盘（>2000 格）自动切虚拟渲染：滚动容器内只实例化可见格，动画降级为直接落定。
import { dotnet } from './_framework/dotnet.js';

const $ = (id) => document.getElementById(id);
const PALETTE = ['#23242c', '#ff6b6b', '#ffd166', '#6bcb77', '#4d96ff', '#c780fa', '#ff9f45', '#3fd6c8', '#f473b9', '#a3e635'];

// 动画节奏（毫秒）：每个阶段停留多久，让消除过程看得清
const TIMING = {
  swap: 240,       // 交换（含弹回）
  match: 450,      // 匹配高亮停留
  vanish: 300,     // 消除缩小淡出
  afterElim: 140,  // 消除后空档
  fall: 300,       // 下落位移动画
  spawn: 340,      // 补充从上方落入
  chainGap: 180,   // 连锁之间的额外停顿
};
const BIG_BOARD = 2000;       // 超过此格子数进入"大棋盘模式"（虚拟渲染 + 动画降级）
const BIG_TIME_SCALE = 0.15;  // 大棋盘动画节奏压缩
const LOG_MAX_LINES = 500;
const VPITCH = 18;            // 虚拟渲染格距（16px 格 + 2px 缝）

let api = null;          // JSExport 导出函数集
let board = null;        // {mode, width, height, score, cells:[...]}
let cellDivs = [];       // 格子 DOM（虚拟模式下为稀疏数组，仅可见格有元素）
let gridKey = '';
let virtual = false;
let inner = null;        // 虚拟模式的滚动内容层
let selected = -1;
let eventQueue = [];
let pumping = false;
let highlight = new Set();   // 匹配高亮中的格子
let popping = new Set();     // 正在弹入的格子

const big = () => !!board && board.cells.length > BIG_BOARD;
const T = (ms) => big() ? Math.max(25, Math.round(ms * BIG_TIME_SCALE)) : ms;
const repaint = () => { if (virtual) updateVirtual(); else paintCells(); };

// ---------- 事件入口（.NET 推送） ----------
window.__onGameEvent = (json) => {
  const ev = JSON.parse(json);
  eventQueue.push(ev);
  pump();
};

async function pump() {
  if (pumping) return;
  pumping = true;
  setStatus('结算中…', true);
  perfBatch = {};
  while (eventQueue.length > 0) {
    const ev = eventQueue.shift();
    logEvent(ev);
    if (ev.type === 'Log' && ev.tag === 'Perf') collectPerf(ev.message);
    await playEvent(ev);
  }
  renderPerf();
  // 队列清空后用权威快照校准一次（镜像只是动画，以逻辑层为准）
  syncBoard();
  pumping = false;
  setStatus('等待你的操作', false);
}

// ---------- 耗时统计（C# Orchestrator 的 [Perf] 日志） ----------
let perfBatch = {};   // { stepName: {total, count, max} }

function collectPerf(msg) {
  const m = msg.match(/(\S+) 耗时 ([\d.]+)ms/);
  if (!m) return;
  const s = perfBatch[m[1]] || (perfBatch[m[1]] = { total: 0, count: 0, max: 0 });
  const ms = parseFloat(m[2]);
  s.total += ms; s.count++; s.max = Math.max(s.max, ms);
}

function renderPerf() {
  const parts = [];
  let sum = 0;
  for (const [name, s] of Object.entries(perfBatch)) {
    sum += s.total;
    parts.push(s.count > 1 ? `${name} ${s.total.toFixed(2)}ms(×${s.count})` : `${name} ${s.total.toFixed(2)}ms`);
  }
  if (parts.length) parts.push(`合计 ${sum.toFixed(2)}ms`);
  $('perf').textContent = parts.join(' | ');
}

async function playEvent(ev) {
  if (!board) return;
  const c = board.cells;
  switch (ev.type) {
    case 'Swap':
      [c[ev.cells[0]], c[ev.cells[1]]] = [c[ev.cells[1]], c[ev.cells[0]]];
      if (board.kinds) [board.kinds[ev.cells[0]], board.kinds[ev.cells[1]]] = [board.kinds[ev.cells[1]], board.kinds[ev.cells[0]]];
      repaint();
      await sleep(T(TIMING.swap));
      break;

    case 'BombSpawn':
    case 'SpecialSpawn':
      // 高优匹配锚点免消转弹：标记 kind，高亮提示
      if (!board.kinds) board.kinds = new Array(c.length).fill(0);
      for (const i of ev.cells) { board.kinds[i] = ev.kind ?? 1; highlight.add(i); }
      repaint();
      await sleep(T(TIMING.match));
      highlight.clear();
      repaint();
      break;

    case 'Transform':
      // 宝石联动：一批棋子变为某种特殊棋子
      if (!board.kinds) board.kinds = new Array(c.length).fill(0);
      for (const i of ev.cells) { board.kinds[i] = ev.kind ?? 1; highlight.add(i); }
      repaint();
      await sleep(T(TIMING.match));
      highlight.clear();
      repaint();
      break;

    case 'Match':
      for (const i of ev.cells) highlight.add(i);
      repaint();
      await sleep(T(TIMING.match));
      break;

    case 'Eliminate':
      if (big()) {
        for (const i of ev.cells) { c[i] = 0; if (board.kinds) board.kinds[i] = 0; }
        highlight.clear();
        repaint();
        await sleep(T(TIMING.vanish));
        break;
      }
      // 缩小淡出（持久化 DOM 上直接加 class，CSS transition 生效）
      for (const i of ev.cells) {
        const d = cellDivs[i];
        if (d) d.className = d._c + ' vanish'; // _c 是基础 class 缓存，repaint 时会被重新计算覆盖
      }
      await sleep(T(TIMING.vanish));
      for (const i of ev.cells) { c[i] = 0; if (board.kinds) board.kinds[i] = 0; }
      highlight.clear();
      repaint();
      await sleep(T(TIMING.afterElim));
      break;

    case 'Fall': {
      // 同一格可能既是目的地又是来源（链式下落）：拍快照 → 源格清空 → 飞行棋子做位移 → 落定
      const old = c.slice();
      const oldKinds = board.kinds ? board.kinds.slice() : null;
      const moves = [];
      for (let k = 0; k < ev.fromTo.length; k += 2)
        moves.push({ from: ev.fromTo[k], to: ev.fromTo[k + 1] });
      for (const m of moves) { c[m.from] = 0; if (board.kinds) board.kinds[m.from] = 0; }
      for (const m of moves) { c[m.to] = old[m.from]; if (board.kinds) board.kinds[m.to] = oldKinds[m.from]; }
      if (big()) {
        repaint();
        await sleep(T(TIMING.fall));
        break;
      }
      repaint();
      const flyers = moves.map(m => makeFlyer(old[m.from], m.from));
      await nextFrame();
      for (let i = 0; i < moves.length; i++) {
        const { dx, dy } = deltaPx(moves[i].from, moves[i].to);
        flyers[i].style.transform = `translate(${dx}px, ${dy}px)`;
      }
      await sleep(T(TIMING.fall));
      for (const m of moves) c[m.to] = old[m.from];
      for (const f of flyers) f.remove();
      repaint();
      break;
    }

    case 'Spawn': {
      ev.cells.forEach((cell, i) => { c[cell] = ev.pieces[i]; });
      if (big()) {
        repaint();
        await sleep(T(TIMING.spawn));
        break;
      }
      repaint();
      // 新棋子从棋盘上方落入目标格
      const flyers = ev.cells.map((cell, i) => {
        const f = makeFlyer(ev.pieces[i], cell);
        f.classList.add('spawn');
        f.style.transform = `translate(0, ${-(cellDivs[cell].offsetTop + pitch() + 8)}px)`;
        return f;
      });
      await nextFrame();
      for (const f of flyers) f.style.transform = 'translate(0, 0)';
      await sleep(T(TIMING.spawn));
      for (const f of flyers) f.remove();
      repaint();
      await sleep(T(TIMING.chainGap)); // 连锁之间的喘息，看得清下一次匹配
      break;
    }

    case 'Score':
      // 计分事件：日志面板展示明细，总分在批末快照校准时刷新
      break;

    case 'Shuffle':
      // 全盘重排：动画降级，直接拿权威快照重绘
      board = JSON.parse(api.GetBoard());
      render();
      await sleep(T(TIMING.fall));
      break;

    default:
      break; // Log / Error 等只记日志
  }
}

// ---------- 飞行棋子（表现层动画道具） ----------
function makeFlyer(v, atCell) {
  const host = cellDivs[atCell];
  const f = document.createElement('div');
  f.className = 'flyer';
  f.style.left = host.offsetLeft + 'px';
  f.style.top = host.offsetTop + 'px';
  f.style.width = host.offsetWidth + 'px';
  f.style.height = host.offsetHeight + 'px';
  f.style.background = PALETTE[v % PALETTE.length];
  f.textContent = v;
  $('board').appendChild(f);
  return f;
}

function deltaPx(from, to) {
  return {
    dx: cellDivs[to].offsetLeft - cellDivs[from].offsetLeft,
    dy: cellDivs[to].offsetTop - cellDivs[from].offsetTop,
  };
}

function pitch() {
  return cellDivs.length > 1 && board.width > 1 && cellDivs[1]
    ? cellDivs[1].offsetLeft - cellDivs[0].offsetLeft
    : cellDivs[0].offsetHeight + 4;
}

const nextFrame = () => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));

// ---------- 渲染 ----------
function newCell(i) {
  const d = document.createElement('div');
  d.onclick = () => onCellClick(i);
  return d;
}

function render() {
  const el = $('board');
  if (!board) { el.innerHTML = ''; cellDivs = []; gridKey = ''; return; }
  const key = `${board.mode}:${board.width}x${board.height}:${board.cells.length}`;
  if (key !== gridKey) buildBoardDom(el, key);
  repaint();
}

function buildBoardDom(el, key) {
  gridKey = key;
  el.innerHTML = '';
  el.style.cssText = '';      // 清掉上一局的内联样式，回到 #board 基础样式
  el.classList.remove('virtual');
  el.onscroll = null;
  inner = null;
  virtual = false;
  const mode = board.mode;
  const w = board.width;

  if (mode === 0 && board.cells.length > BIG_BOARD) {
    // 大棋盘：虚拟渲染（滚动容器 + 内容层，可见格才实例化）
    virtual = true;
    el.classList.add('virtual');
    inner = document.createElement('div');
    inner.className = 'board-inner';
    inner.style.width = w * VPITCH + 'px';
    inner.style.height = board.height * VPITCH + 'px';
    el.appendChild(inner);
    el.onscroll = () => updateVirtual();
    cellDivs = new Array(board.cells.length).fill(null);
    return;
  }

  if (mode === 0) {
    el.style.gridTemplateColumns = `repeat(${w}, var(--cell))`;
    cellDivs = board.cells.map((v, i) => {
      const d = newCell(i);
      el.appendChild(d);
      return d;
    });
  } else if (mode === 1) {
    // 三角形：base=52 高=45，△=(x+y) 偶数，同行左右错位半格
    const b = 52, t = 45;
    el.style.display = 'block';
    el.style.width = (b / 2 * (w + 1)) + 'px';
    el.style.height = (board.height * t) + 'px';
    cellDivs = board.cells.map((v, i) => {
      const x = i % w, y = (i / w) | 0;
      const d = newCell(i);
      d.style.position = 'absolute';
      d.style.left = (x * b / 2) + 'px';
      d.style.top = (y * t) + 'px';
      d.style.width = b + 'px';
      d.style.height = t + 'px';
      d.style.clipPath = ((x + y) & 1) === 0
        ? 'polygon(50% 0%, 100% 100%, 0% 100%)'   // △
        : 'polygon(0% 0%, 100% 0%, 50% 100%)';   // ▽
      el.appendChild(d);
      return d;
    });
  } else {
    // 六边形（平顶 odd-q）：宽 52 高 45，奇数列下移半格
    const s = 26, hw = 2 * s, hh = Math.round(Math.sqrt(3) * s);
    el.style.display = 'block';
    el.style.width = ((w - 1) * 1.5 * s + hw) + 'px';
    el.style.height = ((board.height - 1) * hh + hh + hh / 2) + 'px';
    cellDivs = board.cells.map((v, i) => {
      const x = i % w, y = (i / w) | 0;
      const d = newCell(i);
      d.style.position = 'absolute';
      d.style.left = (x * 1.5 * s) + 'px';
      d.style.top = (y * hh + ((x & 1) ? hh / 2 : 0)) + 'px';
      d.style.width = hw + 'px';
      d.style.height = hh + 'px';
      d.style.clipPath = 'polygon(25% 0%, 75% 0%, 100% 50%, 75% 100%, 25% 100%, 0% 50%)';
      el.appendChild(d);
      return d;
    });
  }
}

function paintCell(d, v, i) {
  const cls = 'cell'
    + (v === 0 ? ' empty' : '')
    + (i === selected ? ' selected' : '')
    + (highlight.has(i) ? ' match' : '')
    + (popping.has(i) ? ' pop' : '');
  // diff 缓存：值/class 没变就不碰 DOM（大棋盘性能关键）
  if (d._v !== v || d._k !== (board.kinds ? board.kinds[i] : 0)) {
    const kind = board.kinds ? board.kinds[i] : 0;
    d.style.background = PALETTE[v % PALETTE.length];
    d.textContent = v > 0 && !big() ? (kind > 0 ? ({ 1: '↔', 2: '↕', 3: '✳', 4: '💎' }[kind] || '💣') : v) : '';
    d._v = v;
    d._k = kind;
  }
  if (d._c !== cls) {
    d.className = cls;
    d._c = cls;
  }
}

function paintCells() {
  if (!board) return;
  for (let i = 0; i < board.cells.length; i++) {
    const d = cellDivs[i];
    if (d) paintCell(d, board.cells[i], i);
  }
  $('score').textContent = board.score ?? 0;
  $('points').textContent = board.points ?? 0;
  $('movesLeft').textContent = board.moves ? board.moves : '—';
}

// 虚拟渲染：只保证视口（±2 格余量）内的格子存在且内容最新
function updateVirtual() {
  if (!board || !inner) return;
  const el = $('board');
  const x0 = Math.max(0, Math.floor(el.scrollLeft / VPITCH) - 2);
  const x1 = Math.min(board.width - 1, Math.ceil((el.scrollLeft + el.clientWidth) / VPITCH) + 2);
  const y0 = Math.max(0, Math.floor(el.scrollTop / VPITCH) - 2);
  const y1 = Math.min(board.height - 1, Math.ceil((el.scrollTop + el.clientHeight) / VPITCH) + 2);
  const keep = new Set();
  for (let y = y0; y <= y1; y++) {
    for (let x = x0; x <= x1; x++) {
      const id = y * board.width + x;
      keep.add(id);
      let d = cellDivs[id];
      if (!d) {
        d = newCell(id);
        d.style.position = 'absolute';
        d.style.left = (x * VPITCH) + 'px';
        d.style.top = (y * VPITCH) + 'px';
        d.style.width = (VPITCH - 2) + 'px';
        d.style.height = (VPITCH - 2) + 'px';
        inner.appendChild(d);
        cellDivs[id] = d;
      }
      paintCell(d, board.cells[id], id);
    }
  }
  for (let i = 0; i < cellDivs.length; i++) {
    const d = cellDivs[i];
    if (d && !keep.has(i)) { d.remove(); cellDivs[i] = null; }
  }
  $('score').textContent = board.score ?? 0;
  $('points').textContent = board.points ?? 0;
  $('movesLeft').textContent = board.moves ? board.moves : '—';
}

function onCellClick(i) {
  if (!board || pumping) return;
  if (board.cells[i] === 0) return;
  if (selected < 0) { selected = i; repaint(); return; }
  if (selected === i) { selected = -1; repaint(); return; }
  api.OfferSwap(selected, i);   // 不相邻/无匹配都会被逻辑层正确处理（丢弃或弹回）
  selected = -1;
  repaint();
}

function syncBoard() {
  board = JSON.parse(api.GetBoard());
  highlight.clear(); popping.clear();
  render();
}

function setStatus(text, busy) {
  const s = $('status');
  s.textContent = text;
  s.className = busy ? 'busy' : '';
}

function logEvent(ev) {
  const isPerf = ev.type === 'Log' && ev.tag === 'Perf';
  const cls = isPerf ? 'ev-perf' : ({ Swap: 'ev-swap', Match: 'ev-match', Eliminate: 'ev-elim', Fall: 'ev-fall', Spawn: 'ev-spawn', BombSpawn: 'ev-bomb', SpecialSpawn: 'ev-bomb', Transform: 'ev-bomb', Score: 'ev-score', Shuffle: 'ev-shuffle', Error: 'ev-err', Log: 'ev-log' }[ev.type] || '');
  const line = document.createElement('div');
  line.className = cls;
  line.textContent = ev.type === 'Log'
    ? `[${ev.tag}] ${ev.message}`
    : ev.type === 'Score'
      ? `#${ev.seq} [${ev.step}] Score +${ev.delta} → 总分 ${ev.total}` +
        (ev.sources && ev.sources.length ? `（${ev.sources.map((s, i) => `${s} ${ev.deltas[i] >= 0 ? '+' : ''}${ev.deltas[i]}`).join('，')}）` : '')
      : `#${ev.seq} [${ev.step}] ${ev.type} ${JSON.stringify(ev.cells)}` +
        (ev.fromTo ? ` fromTo=${JSON.stringify(ev.fromTo)}` : '') +
        (ev.pieces ? ` pieces=${JSON.stringify(ev.pieces)}` : '') +
        (ev.priority !== undefined ? ` prio=${ev.priority}` : '') +
        (ev.kind !== undefined ? ` kind=${ev.kind}` : '') +
        (ev.message ? ` ${ev.message}` : '');
  const log = $('log');
  log.appendChild(line);
  while (log.childElementCount > LOG_MAX_LINES) log.removeChild(log.firstChild);
  log.scrollTop = log.scrollHeight;
}

const sleep = (ms) => new Promise(r => setTimeout(r, ms));

// ---------- 开局 ----------
// 工具箱面板 → config JSON（NewGameWithConfig 的契约，缺省 = 经典行为）
function collectConfig(custom) {
  const cfg = {
    mode: +$('mode').value,
    width: +$('w').value, height: +$('h').value,
    colors: +$('colors').value, seed: $('seed').value,
    bombs: $('bombs').checked,
    topology: $('topology').value,
    gravity: $('gravity').value,
    arbiter: $('arbiter').value,
    spawnResolver: $('spawnResolver').value,
    bombRange: $('bombRange').value,
    score: $('scoreOn').checked,
    moves: +$('moves').value,
  };
  if (custom) cfg.pieces = $('pieces').value;
  return cfg;
}

function savePanel(cfg) { try { localStorage.setItem('toolbox', JSON.stringify(cfg)); } catch { } }

function loadPanel() {
  try {
    const cfg = JSON.parse(localStorage.getItem('toolbox'));
    if (!cfg) return;
    if (cfg.mode !== undefined) $('mode').value = cfg.mode;
    if (cfg.width) $('w').value = cfg.width;
    if (cfg.height) $('h').value = cfg.height;
    if (cfg.colors) $('colors').value = cfg.colors;
    if (cfg.seed !== undefined) $('seed').value = cfg.seed;
    $('bombs').checked = !!cfg.bombs;
    if (cfg.topology) $('topology').value = cfg.topology;
    if (cfg.gravity) $('gravity').value = cfg.gravity;
    if (cfg.arbiter !== undefined) $('arbiter').value = cfg.arbiter;
    if (cfg.spawnResolver !== undefined) $('spawnResolver').value = cfg.spawnResolver;
    if (cfg.bombRange !== undefined) $('bombRange').value = cfg.bombRange;
    $('scoreOn').checked = !!cfg.score;
    if (cfg.moves !== undefined) $('moves').value = cfg.moves;
  } catch { }
}

function newGame(custom) {
  try {
    const cfg = collectConfig(custom);
    savePanel(cfg);
    const json = api.NewGameWithConfig(JSON.stringify(cfg));
    board = JSON.parse(json);
    selected = -1;
    eventQueue = [];
    highlight.clear(); popping.clear();
    gridKey = ''; // 强制重建格子 DOM
    $('log').innerHTML = '';
    render();
    setStatus('等待你的操作', false);
  } catch (e) {
    // 常见原因：指定棋盘长度不符、参数非法等
    setStatus('开局失败：' + e, true);
    logEvent({ seq: '!', step: '', type: 'Error', cells: [], message: String(e) });
  }
}

// 提示一手：高亮逻辑层选出的两格
async function hint() {
  if (!board || pumping || !board.waiting) return;
  try {
    const h = JSON.parse(api.GetHint());
    if (h.a === undefined) { setStatus('死局：没有合法手（等自动洗牌或重开）', true); return; }
    highlight.add(h.a); highlight.add(h.b);
    repaint();
    await sleep(1200);
    highlight.delete(h.a); highlight.delete(h.b);
    repaint();
  } catch (e) { setStatus('提示失败：' + e, true); }
}

// ---------- 启动 ----------
async function boot() {
  try {
    const { setModuleImports, getAssemblyExports, getConfig } = await dotnet.create();
    setModuleImports('gameBridge', { onGameEvent: (json) => window.__onGameEvent(json) });
    const exports = await getAssemblyExports(getConfig().mainAssemblyName);
    api = exports.PolyMatch3.Bridge.GameBridge;
    $('wasmStatus').textContent = 'WASM 就绪';
    loadPanel();
    $('btnRandom').onclick = () => newGame(false);
    $('btnCustom').onclick = () => newGame(true);
    $('hintBtn').onclick = () => hint();
    newGame(false);
  } catch (e) {
    $('wasmStatus').textContent = '加载失败：' + e;
    console.error(e);
  }
}
boot();

// PWA：注册 Service Worker（仅 HTTPS/localhost 生效，局域网 http 下静默跳过）
if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('./sw.js').catch(() => { /* 非安全上下文，忽略 */ });
}
