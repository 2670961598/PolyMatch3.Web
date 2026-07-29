# PolyMatch3.Web

把 Unity 工程里的 PolyMatch3 三消逻辑框架**复制**出来，接到浏览器（.NET WASM + 原生 JS 前端）。
原 Unity 工程（`Match3/`）为权威源码，定期同步过来。

## 代码阅读顺序（验证实现是否符合预期）

建议按"一局游戏是怎么跑起来的"这条线读，每个文件开头都有【导读】注释：

1. **`src/PolyMatch3.Game/GameSession.cs`** —— 装配总览：棋盘/图案/匹配器/注册表/编排器怎么拼成一局；
2. **`src/PolyMatch3.Game/BoardModes.cs`** —— 三种棋盘（矩形/三角/六边）的拓扑与图案配置，"代码即配置"的全部内容；
3. **`src/PolyMatch3.Game/ClassicStepManager.cs`** —— 主循环状态机：每一步之后该干什么（含炸弹模式只是"换两个 Step"的演示）；
4. **`src/PolyMatch3.Game/PlayerSwapStep.cs` / `RevertSwapStep.cs`** —— 输入侧：合法校验、非法丢弃、无匹配弹回、kind 层同步；
5. **`src/PolyMatch3.Bridge/GameBridge.cs`** —— JS 契约：开局/输入/查询三个出口 + 事件推送一个入口；
6. **`web/main.js`** —— 表现层：镜像模型 + 事件驱动动画 + 大棋盘虚拟渲染。

框架层（`src/PolyMatch3`、`src/PolyMatch3.Tools`、`src/PolyMatch3.Samples`）与 Unity 权威源码逐文件一致，
每个公开类型都有 XML 文档注释；工具/示例的设计意图看 `Match3/docs/architecture.md` §3.4~3.5。

## 结构

```
src/PolyMatch3/          【框架】Core（棋盘/边类型/棋子注册表/确定性随机）
                                 Matcher（图案/匹配引擎/优先级仲裁）
                                 Step（编排：IStep/Orchestrator/黑板/输入通道/事件，含最底层 MatchStep）
                                 Logging / Diagnostics（配置校验）
                         原则：只处理匹配相关 + 编排契约，无任何玩法工具
src/PolyMatch3.Tools/    【工具】常用玩法零件，不属于框架：Swap/Eliminate/Gravity/Refill、
                         棋盘填充（BoardInitializer）、开局约束、匹配基准测试
src/PolyMatch3.Game/     【业务层示例】经典矩形/三角/六边三消
                         BoardModes           三种棋盘模式的拓扑+图案配置
                         ClassicStepManager   主循环状态机（等输入→交换→匹配→消除→重力→补充→连锁）
                         PlayerSwapStep       输入型 Step（非法输入自动丢弃）
                         RevertSwapStep       交换无匹配时弹回（SwapBack）
                         GameSession          一局会话：开局（随机种子 / 指定棋盘）+ OfferSwap + 事件 JSON 出口
src/PolyMatch3.Bridge/   WASM 桥接层（browser-wasm）：JSExport 开局/输入/查询，JSImport 推送事件
web/                     纯 JS 前端（index.html + main.js + PWA manifest/sw/图标），构建时拷贝进 AppBundle
app/                     Capacitor 安卓离线壳（资源内置 APK）：build-apk.ps1 一键 sync + 出包
```

### 框架关键约定

- **棋子 = 纯逻辑、无状态**：棋盘上的 int（**0=空，硬约定**，`PieceRegistry.EmptyId`），
  注册表 `PieceRegistry` 按注册顺序分配 id（1..N），`IPiece` 只有身份；
  回调钩子（生成/消除）在 Step 层 `IPieceHooks`，由编排/工具层按格 id 升序调度。
- **匹配与仲裁独立**：`FixedPatternMatcher.Match()` 返回原始全量组；
  `MatchArbitrator.Arbitrate()` 是可选后处理（`MatchStep(matcher, arbitrate: false)` 可跳过），
  消哪些、怎么消由玩法决定。

## 构建与运行

```powershell
# 首次需要 wasm 工作负载（机器级，已装可跳过）
dotnet workload install wasm-tools

# 一键发布（编译 + 把 web/ 前端文件拷进 AppBundle）
.\publish-web.ps1

# 本地起服务试玩
cd src/PolyMatch3.Bridge/bin/Release/net9.0/browser-wasm/AppBundle
python -m http.server 8080
# 浏览器打开 http://localhost:8080
```

## JS 接口

`mode`：0=矩形（直线匹配）、1=三角形（三条边同型，路径匹配可拐弯）、2=六边形（三轴对边直线匹配）。
`bombs`：≠0 开启炸弹模式（四连锚点免消转 3×3 炸弹，命中即爆、可连环引爆）。

| JS 调用 | 说明 |
|---|---|
| `NewGame(mode, w, h, colors, seed, bombs)` | 随机开局（纯种子随机，初始匹配原样保留，直接等玩家输入），返回棋盘 JSON |
| `NewGameWithBoard(mode, w, h, colors, seed, csv, bombs)` | **指定棋盘开局**（正确性测试用），csv 为逗号分隔棋子值（0=空，行优先） |
| `OfferSwap(a, b)` | 交换两格（cellId = y×width+x）；不相邻自动丢弃，无匹配自动弹回 |
| `GetBoard()` | 权威棋盘快照 JSON（炸弹模式含 kinds 平行数组） |
| `GetScore()` | 累计消除数 |
| `onGameEvent(json)` | .NET → JS 推送：Swap / Match / Eliminate / Fall / Spawn / BombSpawn / Error / Log（含每步耗时，tag=Perf） |

## 正确性测试姿势

1. 前端"指定棋盘开局"：构造已知棋盘（比如直接埋一个五连、一个四连），验证匹配/仲裁/消除结果；
2. 同种子 + 同输入序列 ⇒ 事件流逐字节一致（框架确定性军规），可用种子复现比对；
3. 0=空的格子开局后第一步 Gravity/Refill 行为可测（Refill 走种子随机，同样可复现）。
