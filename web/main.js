// 【导读】PolyMatch3 WASM 前端（Phaser 表现层版），全部工作只有三件事：
//   1. 加载 dotnet 运行时，拿到 GameBridge 的导出函数（NewGameWithConfig/OfferSwap/GetBoard/GetHint）；
//   2. 维护一份棋盘镜像（board.cells/kinds）：开局来自配置开局的 JSON，之后按事件增量更新，
//      每批事件播完再用 GetBoard() 权威快照校准一次——镜像只是动画道具，逻辑层永远为准；
//   3. 把事件流交给 board-renderer.js（Phaser）：棋子池化、相机裁剪、分档消除特效、
//      移动/出现/消失全套补间。大棋盘（>2000 格）自动切性能模式：相机平移缩放 + 动画降级。
import { dotnet } from './_framework/dotnet.js';
import { BoardRenderer } from './board-renderer.js';

const $ = (id) => document.getElementById(id);
const PALETTE = ['#23242c', '#ff6b6b', '#ffd166', '#6bcb77', '#4d96ff', '#c780fa', '#ff9f45', '#3fd6c8', '#f473b9', '#a3e635'];
const LOG_MAX_LINES = 500;

let api = null;          // JSExport 导出函数集
let board = null;        // {mode, width, height, score, points, moves, cells:[...]}
let renderer = null;     // Phaser 渲染器
let selected = -1;
let eventQueue = [];
let pumping = false;
let lastEliminated = []; // Score 飘字的定位用（上一步消除的格子）

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

// ---------- 事件 → 镜像 + 渲染器 ----------
async function playEvent(ev) {
  if (!board) return;
  const c = board.cells;
  switch (ev.type) {
    case 'Swap':
      [c[ev.cells[0]], c[ev.cells[1]]] = [c[ev.cells[1]], c[ev.cells[0]]];
      if (board.kinds) [board.kinds[ev.cells[0]], board.kinds[ev.cells[1]]] = [board.kinds[ev.cells[1]], board.kinds[ev.cells[0]]];
      await renderer.swap(ev.cells[0], ev.cells[1]);
      break;

    case 'BombSpawn':
    case 'SpecialSpawn':
    case 'Transform':
      // 锚点免消转弹 / 宝石联动：kind 落到镜像，脉冲提示
      if (!board.kinds) board.kinds = new Array(c.length).fill(0);
      for (const i of ev.cells) board.kinds[i] = ev.kind ?? 1;
      await renderer.flashKind(ev.cells);
      break;

    case 'Match':
      await renderer.highlight(ev.cells, ev.priority || 0);
      break;

    case 'Eliminate':
      lastEliminated = ev.cells.slice();
      await renderer.eliminate(ev.cells);
      for (const i of ev.cells) { c[i] = 0; if (board.kinds) board.kinds[i] = 0; }
      break;

    case 'Fall': {
      const old = c.slice();
      const oldKinds = board.kinds ? board.kinds.slice() : null;
      await renderer.fall(ev.fromTo);
      // 两遍法：先把所有出发格清零，再写目的格——链式下落（一格既是 to 又是 from）才不会互相覆盖
      for (let k = 0; k < ev.fromTo.length; k += 2) {
        c[ev.fromTo[k]] = 0;
        if (board.kinds) board.kinds[ev.fromTo[k]] = 0;
      }
      for (let k = 0; k < ev.fromTo.length; k += 2) {
        c[ev.fromTo[k + 1]] = old[ev.fromTo[k]];
        if (board.kinds) board.kinds[ev.fromTo[k + 1]] = oldKinds[ev.fromTo[k]];
      }
      break;
    }

    case 'Spawn':
      ev.cells.forEach((cell, i) => { c[cell] = ev.pieces[i]; });
      await renderer.spawn(ev.cells, ev.pieces);
      break;

    case 'Shuffle':
      // 值的全排列只有逻辑层知道：先拉权威快照换值，再播洗牌动画
      board.cells = JSON.parse(api.GetBoard()).cells;
      await renderer.shuffle();
      break;

    case 'Score':
      renderer.scoreFloat(lastEliminated, ev.delta || 0);
      break;

    default:
      break; // Log / Error 等只记日志
  }
}

// ---------- 交互 ----------
function onCellClick(i) {
  if (!board || pumping || !board.waiting) return;
  if (board.cells[i] === 0) return;
  if (selected < 0) { selected = i; renderer.select(i); return; }
  if (selected === i) { selected = -1; renderer.select(-1); return; }
  api.OfferSwap(selected, i);   // 不相邻/无匹配都会被逻辑层正确处理（丢弃或弹回）
  selected = -1;
  renderer.select(-1);
}

function syncBoard() {
  board = JSON.parse(api.GetBoard());
  renderer.sync(board);
  updateStats();
}

function updateStats() {
  $('score').textContent = board.score ?? 0;
  $('points').textContent = board.points ?? 0;
  $('movesLeft').textContent = board.moves ? board.moves : '—';
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
    lastEliminated = [];
    $('log').innerHTML = '';
    renderer.setBoard(board);
    updateStats();
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
    await renderer.highlight([h.a, h.b], 0);
  } catch (e) { setStatus('提示失败：' + e, true); }
}

// ---------- 启动 ----------
async function boot() {
  renderer = new BoardRenderer('phaser-board', PALETTE, onCellClick);
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
