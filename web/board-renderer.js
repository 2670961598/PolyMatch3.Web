// 【导读】Phaser 通用棋盘渲染器（纯表现层，零逻辑）：
//   - 贴图启动时一次性烘焙（每色圆角块/三角/六边 + 空槽），之后全走纹理；
//   - 棋子 = 池化 Container（底色块 + 文字），消失回池、生成取池，杜绝 GC 抖动；
//   - 大棋盘（>2000 格）进"性能模式"：相机拖拽平移 + 滚轮缩放，只实例化可见窗口（±2 格），
//     动画降级为直接落定；小棋盘全量实例化 + 全套补间动画；
//   - 分档消除：highlight() 记录本批组优先级，eliminate() 按档放效果
//     （三连缩放淡出 / 四连闪白爆散 / 五连光环粒子 + 屏震 / 炸弹橙闪大震）；
//   - main.js 只调本文件的公开方法，全部返回 Promise（动画完成才 resolve）。
export class BoardRenderer {
  /**
   * @param {string} parentId 容器 div id
   * @param {string[]} palette 颜色表（下标 = 棋子值）
   * @param {(i:number)=>void} onCellClick 格子点击回调
   */
  constructor(parentId, palette, onCellClick) {
    this.PALETTE = palette;
    this.onCellClick = onCellClick;
    this.board = null;        // 镜像引用（main.js 持有，本类只读）
    this.pieces = [];         // cellId → Container（性能模式仅可见格有值）
    this.pool = [];           // 回收的 Container
    this.matchTier = new Map(); // cellId → 本批最高优先级（消除分档用）
    this.selected = -1;
    this.virtual = false;
    this.scene = null;
    this.texturesReady = false;

    const self = this;
    this.game = new Phaser.Game({
      type: Phaser.AUTO,
      parent: parentId,
      backgroundColor: '#14151a',
      scale: {
        mode: Phaser.Scale.RESIZE,
        width: '100%',
        height: '100%',
      },
      scene: {
        create() { self._create(this); },
      },
    });
  }

  // ---------- 内部：场景启动 ----------

  _create(scene) {
    this.scene = scene;
    this._bakeTextures(scene);
    // 环爆贴图（五连/炸弹用）与粒子贴图
    this._bakeFxTextures(scene);
    // 相机交互（性能模式平移/缩放；普通模式也可缩放查看）
    this._setupCamera(scene);
  }

  _bakeTextures(scene) {
    const g = scene.add.graphics();
    const R = 44; // 基准格尺寸
    // 空槽
    g.fillStyle(0x23242c, 1).fillRoundedRect(0, 0, R, R, 8);
    g.generateTexture('cell-empty', R, R);
    // 每色圆角块
    this.PALETTE.forEach((hex, i) => {
      if (i === 0) return;
      g.clear();
      g.fillStyle(parseInt(hex.slice(1), 16), 1).fillRoundedRect(0, 0, R, R, 8);
      g.generateTexture('cell-' + i, R, R);
    });
    // 三角形（△/▽，base 52 高 45）
    g.clear();
    g.fillStyle(0x23242c, 1);
    g.fillTriangle(26, 0, 52, 45, 0, 45);
    g.generateTexture('tri-up-empty', 52, 45);
    g.clear();
    g.fillTriangle(0, 0, 52, 0, 26, 45);
    g.generateTexture('tri-down-empty', 52, 45);
    this.PALETTE.forEach((hex, i) => {
      if (i === 0) return;
      g.clear();
      g.fillStyle(parseInt(hex.slice(1), 16), 1).fillTriangle(26, 0, 52, 45, 0, 45);
      g.generateTexture('tri-up-' + i, 52, 45);
      g.clear();
      g.fillTriangle(0, 0, 52, 0, 26, 45);
      g.generateTexture('tri-down-' + i, 52, 45);
    });
    // 六边形（宽 52 高 45）
    const hexPts = [13, 0, 39, 0, 52, 22.5, 39, 45, 13, 45, 0, 22.5];
    const mkHex = (key, color) => {
      g.clear();
      g.fillStyle(color, 1);
      g.beginPath();
      g.moveTo(hexPts[0], hexPts[1]);
      for (let k = 2; k < hexPts.length; k += 2) g.lineTo(hexPts[k], hexPts[k + 1]);
      g.closePath();
      g.fillPath();
      g.generateTexture(key, 52, 45);
    };
    mkHex('hex-empty', 0x23242c);
    this.PALETTE.forEach((hex, i) => {
      if (i === 0) return;
      mkHex('hex-' + i, parseInt(hex.slice(1), 16));
    });
    g.destroy();
    this.texturesReady = true;
  }

  _bakeFxTextures(scene) {
    const g = scene.add.graphics();
    // 光环（圆环）
    g.lineStyle(4, 0xffffff, 1).strokeCircle(32, 32, 28);
    g.generateTexture('fx-ring', 64, 64);
    // 粒子点
    g.clear();
    g.fillStyle(0xffffff, 1).fillCircle(4, 4, 4);
    g.generateTexture('fx-dot', 8, 8);
    // 选中框
    g.clear();
    g.lineStyle(3, 0xffffff, 0.9).strokeRoundedRect(1, 1, 46, 46, 8);
    g.generateTexture('fx-select', 48, 48);
    g.destroy();
  }

  _setupCamera(scene) {
    const cam = scene.cameras.main;
    // 拖拽平移（仅性能模式有意义，普通棋盘 fit 后无需）
    scene.input.on('pointerdown', (p) => { this._drag = { x: p.x, y: p.y, sx: cam.scrollX, sy: cam.scrollY }; });
    scene.input.on('pointerup', () => { this._drag = null; });
    scene.input.on('pointermove', (p) => {
      if (!this._drag || !p.isDown || !this.virtual) return;
      cam.scrollX = this._drag.sx - (p.x - this._drag.x) / cam.zoom;
      cam.scrollY = this._drag.sy - (p.y - this._drag.y) / cam.zoom;
      this._materializeVisible();
    });
    scene.input.on('wheel', (p, objs, dx, dy) => {
      if (!this.virtual) return;
      const z = Phaser.Math.Clamp(cam.zoom * (dy > 0 ? 0.9 : 1.1), 0.2, 3);
      cam.setZoom(z);
      this._materializeVisible();
    });
  }

  // ---------- 布局 ----------

  /** 格子中心坐标 + 贴图前缀 + 格子尺寸（与旧 DOM 布局同公式）。 */
  _layout(i) {
    const w = this.board.width, mode = this.board.mode;
    const x = i % w, y = (i / w) | 0;
    if (mode === 1) { // 三角
      const up = ((x + y) & 1) === 0;
      return { px: x * 26 + 26, py: y * 45 + 22.5, key: up ? 'tri-up' : 'tri-down', cw: 52, ch: 45 };
    }
    if (mode === 2) { // 六边
      const odd = (x & 1) === 1;
      return { px: x * 39 + 26, py: y * 45 + (odd ? 22.5 : 0) + 22.5, key: 'hex', cw: 52, ch: 45 };
    }
    return { px: x * 48 + 24, py: y * 48 + 24, key: 'cell', cw: 44, ch: 44 };
  }

  _boardSize() {
    const w = this.board.width, h = this.board.height, mode = this.board.mode;
    if (mode === 1) return { bw: 26 * (w + 1), bh: h * 45 };
    if (mode === 2) return { bw: (w - 1) * 39 + 52, bh: (h - 1) * 45 + 45 + 22 };
    return { bw: w * 48, bh: h * 48 };
  }

  // ---------- 棋子池 ----------

  _take(scene, x, y) {
    const c = this.pool.pop() || this._makePiece(scene);
    c.setPosition(x, y).setVisible(true).setAlpha(1).setScale(1).setAngle(0);
    return c;
  }

  _makePiece(scene) {
    const img = scene.add.image(0, 0, 'cell-empty');
    const txt = scene.add.text(0, 0, '', {
      fontFamily: '"Segoe UI", "Microsoft YaHei", sans-serif',
      fontSize: '16px', fontStyle: 'bold', color: 'rgba(0,0,0,0.5)',
    }).setOrigin(0.5);
    const c = scene.add.container(0, 0, [img, txt]);
    c._img = img; c._txt = txt; c._cell = -1;
    // 输入挂在图片子对象上（Container 的 hitArea 有坑），且创建时只注册一次——
    // 池化复用时绝不再 on()，否则监听器累加，点一下触发 N 次（选中即取消，看似点不了）
    img.setInteractive();
    img.on('pointerdown', () => this.onCellClick(c._cell));
    return c;
  }

  _give(c) {
    if (!c) return;
    c.setVisible(false);
    c._cell = -1;
    this.pool.push(c);
  }

  /** 按镜像刷新一个棋子的贴图/文字（diff 更新）。 */
  _paint(c, i) {
    const v = this.board.cells[i];
    const kind = this.board.kinds ? this.board.kinds[i] : 0;
    const L = this._layout(i);
    const key = v === 0 ? L.key + '-empty' : L.key + '-' + (v % this.PALETTE.length);
    if (c._key !== key) { c._img.setTexture(key); c._key = key; }
    const glyph = kind > 0 ? ({ 1: '↔', 2: '↕', 3: '✳', 4: '💎' }[kind] || '💣') : (v > 0 ? String(v) : '');
    if (c._glyph !== glyph) { c._txt.setText(glyph); c._glyph = glyph; }
  }

  // ---------- 公开：建盘/同步 ----------

  /** 建/换棋盘。 */
  setBoard(board) {
    if (!this.scene) { this.board = board; return; }
    // 清旧盘
    for (const c of this.pieces) this._give(c);
    this.pieces = new Array(board.cells.length).fill(null);
    this.matchTier.clear();
    this.selected = -1;
    this.board = board;

    const scene = this.scene;
    this.virtual = board.cells.length > 2000;
    const { bw, bh } = this._boardSize();

    if (this.virtual) {
      scene.cameras.main.setBounds(0, 0, bw, bh).setZoom(1).centerOn(bw / 2, bh / 2);
      this._materializeVisible();
    } else {
      for (let i = 0; i < board.cells.length; i++) {
        const L = this._layout(i);
        const c = this._take(scene, L.px, L.py);
        c._cell = i;
        this._paint(c, i);
        this.pieces[i] = c;
      }
      // fit：棋盘整体缩放居中到画布
      const cw = scene.scale.width, ch = scene.scale.height;
      const z = Math.min(cw / (bw + 16), ch / (bh + 16), 1);
      scene.cameras.main.setBounds(-1000, -1000, bw + 2000, bh + 2000)
        .setZoom(z).centerOn(bw / 2, bh / 2);
    }
  }

  /** 批末权威校准：全量自愈——多出的回收、缺失的重建、坐标归位、按镜像重绘。 */
  sync(board) {
    this.board = board;
    if (this.virtual) { this._materializeVisible(); return; }
    for (let i = 0; i < board.cells.length; i++) {
      let c = this.pieces[i];
      if (board.cells[i] === 0) {
        if (c) { this._give(c); this.pieces[i] = null; }
        continue;
      }
      const L = this._layout(i);
      if (!c) {
        // 缺失（幽灵错位时被误收的格子）：从池里取一个补位
        c = this._take(this.scene, L.px, L.py);
        this.pieces[i] = c;
      }
      c.setPosition(L.px, L.py);
      c._cell = i;
      this._paint(c, i);
    }
  }

  /** 性能模式：只实例化可见窗口（±2 格）。 */
  _materializeVisible() {
    if (!this.virtual || !this.scene) return;
    const cam = this.scene.cameras.main;
    const view = cam.worldView;
    const w = this.board.width;
    const keep = new Set();
    for (let i = 0; i < this.board.cells.length; i++) {
      const L = this._layout(i);
      if (L.px > view.left - 100 && L.px < view.right + 100 && L.py > view.top - 100 && L.py < view.bottom + 100)
        keep.add(i);
    }
    for (let i = 0; i < this.pieces.length; i++) {
      if (keep.has(i)) {
        if (!this.pieces[i]) {
          const L = this._layout(i);
          const c = this._take(this.scene, L.px, L.py);
          c._cell = i;
          this._paint(c, i);
          this.pieces[i] = c;
        } else {
          this._paint(this.pieces[i], i);
        }
      } else if (this.pieces[i]) {
        this._give(this.pieces[i]);
        this.pieces[i] = null;
      }
    }
  }

  // ---------- 公开：动画（全部返回 Promise） ----------

  _tween(cfg) {
    return new Promise((resolve) => {
      this.scene.tweens.add({ ...cfg, onComplete: () => resolve() });
    });
  }

  async select(i) {
    if (this.virtual) return;
    if (this._selMarker) { this._selMarker.destroy(); this._selMarker = null; }
    this.selected = i;
    if (i < 0 || !this.pieces[i]) return;
    const L = this._layout(i);
    this._selMarker = this.scene.add.image(L.px, L.py, 'fx-select').setDepth(10);
  }

  async swap(a, b) {
    if (this.virtual) return;
    const ca = this.pieces[a], cb = this.pieces[b];
    if (!ca || !cb) return;
    const La = this._layout(a), Lb = this._layout(b);
    this.pieces[a] = cb; this.pieces[b] = ca;
    ca._cell = b; cb._cell = a; // 棋子换格，索引同步换
    await Promise.all([
      this._tween({ targets: ca, x: Lb.px, y: Lb.py, duration: 220, ease: 'Cubic.easeInOut' }),
      this._tween({ targets: cb, x: La.px, y: La.py, duration: 220, ease: 'Cubic.easeInOut' }),
    ]);
  }

  /** 匹配高亮（记录优先级档位供消除用）。 */
  async highlight(cells, priority) {
    for (const i of cells) {
      this.matchTier.set(i, Math.max(this.matchTier.get(i) || 0, priority || 0));
      const c = this.pieces[i];
      if (c) this.scene.tweens.add({ targets: c, scale: 1.12, duration: 160, yoyo: true, repeat: 1 });
    }
    if (!this.virtual) await new Promise((r) => this.scene.time.delayedCall(400, r));
  }

  /** 分档消除。 */
  async eliminate(cells) {
    if (this.virtual) {
      for (const i of cells) { this._give(this.pieces[i]); this.pieces[i] = null; }
      return;
    }
    let maxTier = 0, bomb = false;
    for (const i of cells) {
      maxTier = Math.max(maxTier, this.matchTier.get(i) || 0);
      if (this.board.kinds && this.board.kinds[i] > 0) bomb = true;
    }

    if (bomb || maxTier >= 95) {
      // 五连/炸弹：光环 + 粒子 + 屏震
      this.scene.cameras.main.shake(bomb ? 200 : 150, bomb ? 0.006 : 0.004);
      for (const i of cells.slice(0, 24)) {
        const L = this._layout(i);
        const ring = this.scene.add.image(L.px, L.py, 'fx-ring').setDepth(9);
        this.scene.tweens.add({ targets: ring, scale: 2.2, alpha: 0, duration: 380, onComplete: () => ring.destroy() });
        const burst = this.scene.add.particles(L.px, L.py, 'fx-dot', {
          speed: { min: 60, max: 180 }, lifespan: 380, quantity: 6, scale: { start: 1, end: 0 },
          tint: bomb ? 0xff9f45 : 0xffffff, emitting: false,
        }).setDepth(9);
        burst.explode(6);
        this.scene.time.delayedCall(450, () => burst.destroy());
      }
      await this._vanishAll(cells, 260);
    } else if (maxTier >= 80) {
      // 四连/T/十字：闪白 + 快速爆散 + 短屏震
      this.scene.cameras.main.shake(90, 0.0025);
      for (const i of cells) {
        const c = this.pieces[i];
        if (c) c._img.setTintFill(0xffffff);
      }
      await new Promise((r) => this.scene.time.delayedCall(120, r));
      for (const i of cells) {
        const c = this.pieces[i];
        if (c) c._img.clearTint();
      }
      await this._vanishAll(cells, 220, true);
    } else {
      await this._vanishAll(cells, 280);
    }

    for (const i of cells) { this._give(this.pieces[i]); this.pieces[i] = null; }
    for (const i of cells) this.matchTier.delete(i);
  }

  _vanishAll(cells, ms, scatter) {
    const tweens = [];
    for (const i of cells) {
      const c = this.pieces[i];
      if (!c) continue;
      tweens.push(this._tween({
        targets: c,
        scale: 0.1, alpha: 0,
        x: scatter ? c.x + Phaser.Math.Between(-30, 30) : c.x,
        y: scatter ? c.y + Phaser.Math.Between(-30, 30) : c.y,
        duration: ms, ease: 'Cubic.easeIn',
      }));
    }
    return Promise.all(tweens);
  }

  /** 下落/移动（fromTo 扁平的最终映射）。 */
  async fall(fromTo) {
    const moves = [];
    for (let k = 0; k < fromTo.length; k += 2) moves.push({ from: fromTo[k], to: fromTo[k + 1] });
    // 两遍法：先把移动中的棋子全部摘下来，再逐个放到目的格——
    // 链式下落（一格既是 to 又是 from）单循环会把刚落位的棋子再次搬走/覆盖
    const moving = [];
    for (const m of moves) {
      const c = this.pieces[m.from];
      if (c) { moving.push({ c, to: m.to }); this.pieces[m.from] = null; }
    }
    if (this.virtual) {
      for (const mv of moving) { mv.c._cell = mv.to; this.pieces[mv.to] = mv.c; }
      return;
    }
    const tweens = [];
    for (const mv of moving) {
      const L = this._layout(mv.to);
      mv.c._cell = mv.to; // 棋子落新格，索引同步落
      this.pieces[mv.to] = mv.c;
      tweens.push(this._tween({ targets: mv.c, x: L.px, y: L.py, duration: 280, ease: 'Quad.easeIn' }));
    }
    await Promise.all(tweens);
  }

  /** 生成（上方落入 + 过冲回弹）。 */
  async spawn(cells, values) {
    if (this.virtual) {
      cells.forEach((cell, i) => {
        const L = this._layout(cell);
        const c = this._take(this.scene, L.px, L.py);
        c._cell = cell;
        this._paint(c, cell);
        this.pieces[cell] = c;
      });
      return;
    }
    const tweens = [];
    cells.forEach((cell, i) => {
      const L = this._layout(cell);
      const c = this._take(this.scene, L.px, L.py - 120);
      c._cell = cell;
      c.setAlpha(0);
      this._paint(c, cell);
      this.pieces[cell] = c;
      tweens.push(this._tween({
        targets: c, x: L.px, y: L.py, alpha: 1,
        duration: 320, ease: 'Back.easeOut', delay: i * 20,
      }));
    });
    await Promise.all(tweens);
    await new Promise((r) => this.scene.time.delayedCall(140, r)); // 连锁喘息
  }

  /** 洗牌：全盘快速缩小→换值→放大。 */
  async shuffle() {
    if (this.virtual) return;
    const live = this.pieces.filter(Boolean);
    await Promise.all(live.map((c) => this._tween({ targets: c, scale: 0.2, duration: 160, ease: 'Quad.easeIn' })));
    live.forEach((c) => this._paint(c, c._cell));
    await Promise.all(live.map((c) => this._tween({ targets: c, scale: 1, duration: 200, ease: 'Back.easeOut' })));
  }

  /** 分数飘字（在消除重心处 +N 上浮淡出）。 */
  scoreFloat(cells, delta) {
    if (this.virtual || !cells.length) return;
    let sx = 0, sy = 0, n = 0;
    for (const i of cells) {
      const L = this._layout(i);
      sx += L.px; sy += L.py; n++;
    }
    const t = this.scene.add.text(sx / n, sy / n, '+' + delta, {
      fontFamily: '"Segoe UI", sans-serif', fontSize: '26px', fontStyle: 'bold', color: '#ffd166',
      stroke: '#000', strokeThickness: 3,
    }).setOrigin(0.5).setDepth(20);
    this.scene.tweens.add({
      targets: t, y: t.y - 46, alpha: 0, duration: 900, ease: 'Cubic.easeOut',
      onComplete: () => t.destroy(),
    });
  }

  /** 特殊生成/变身提示（BombSpawn/SpecialSpawn/Transform）：脉冲高亮。 */
  async flashKind(cells) {
    if (this.virtual) return;
    for (const i of cells) {
      const c = this.pieces[i];
      if (c) {
        this._paint(c, i);
        this.scene.tweens.add({ targets: c, scale: 1.25, duration: 180, yoyo: true, repeat: 1 });
      }
    }
    await new Promise((r) => this.scene.time.delayedCall(400, r));
  }

  destroy() {
    if (this.game) this.game.destroy(true);
  }
}
