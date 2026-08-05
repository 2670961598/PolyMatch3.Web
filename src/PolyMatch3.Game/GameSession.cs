using System;
using System.Text;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Defs;
using PolyMatch3.Diagnostics;
using PolyMatch3.Matcher;
using PolyMatch3.Step;
using PolyMatch3.Tools;

namespace PolyMatch3.Game
{
    /// <summary>
    /// 【导读】一局游戏会话：对外三接口 + 事件出口，是 Web 桥接层唯一的打交道对象。
    ///
    /// 生命周期：
    ///   Create() 装配（棋盘 → 图案 → 匹配器 → 注册表 → 填充 → 校验 → 编排器）后，
    ///   Orchestrator 主循环在后台 Task 里跑，停在 PlayerSwap 等待输入；
    ///   表现层 OfferSwap(a,b) 喂输入 → 逻辑层跑完一轮连锁 → 事件逐个经 OnEventJson 推出（JSON）。
    ///
    /// 对应框架的三接口对接指南：
    ///   输入路径 = InputChannel（OfferSwap 是唯一合法输入口）；
    ///   事件出口 = IEventSink（本类实现，Orchestrator 每步边界驱动，事件带全局单调 Seq）；
    ///   确定性   = XorShift128PlusRandom 种子（同种子+同输入 ⇒ 事件流逐字节一致）。
    ///
    /// 工具箱面板（GameConfig）：拓扑/仲裁/生成裁决/炸弹范围/重力/计分/步数预算全部构造注入，
    /// 缺省 = 现状行为。炸弹模式：kinds 平行数组，快照 BoardJson 里带 kinds 供前端渲染 💣。
    /// </summary>
    public sealed class GameSession : IEventSink
    {
        private readonly GraphBoard _board;
        private readonly StepContext _ctx;
        private readonly InputChannel<(int a, int b)> _input;
        private readonly ClassicStepManager _manager;
        private readonly IStepManager _classicManager; // 可空：传统模式
        private readonly System.Func<bool> _waitingForInput;
        private readonly int _mode;
        private readonly Samples.KindLayer _kinds; // 可空：仅炸弹/传统模式
        private readonly IMatcher _matcher;  // GetHint 的合法手探测用
        private Task _runTask;

        /// <summary>事件出口：每个游戏事件一条 JSON（含全局 Seq）。</summary>
        public Action<string> OnEventJson;

        /// <summary>编排异常出口（如 maxSteps 保险丝熔断、步数用尽外的异常终局）。</summary>
        public Action<string> OnError;

        private GameSession(GraphBoard board, StepContext ctx,
            InputChannel<(int a, int b)> input, ClassicStepManager manager, int mode, Samples.KindLayer kinds,
            IMatcher matcher, IStepManager classicManager = null, System.Func<bool> waitingForInput = null)
        {
            _board = board;
            _ctx = ctx;
            _input = input;
            _manager = manager;
            _mode = mode;
            _kinds = kinds;
            _matcher = matcher;
            _classicManager = classicManager;
            _waitingForInput = waitingForInput ?? (() => manager.PlayerSwap.WaitingForInput);
        }

        /// <summary>旧签名开局（等价于全默认 GameConfig，行为与旧版一致）。</summary>
        public static GameSession Create(int mode, int width, int height, int colorCount, ulong seed, int[] pieces = null, bool bombs = false)
        {
            return Create(new GameConfig
            {
                Mode = mode,
                Width = width,
                Height = height,
                Colors = colorCount,
                Seed = seed.ToString(),
                Bombs = bombs,
                Pieces = pieces == null ? null : string.Join(",", pieces),
            });
        }

        /// <summary>
        /// 工具箱开局：cfg 的每个工具选项对应一个装配注入点（见 GameConfig 注释）。
        /// pieces 为 null 时按种子随机填充（初始匹配原样保留，不做检测）；
        /// 否则全指定（长度必须 = width×height，0=空，1~N=颜色）——正确性测试入口。
        /// </summary>
        public static GameSession Create(GameConfig cfg)
        {
            var board = BoardModes.CreateBoard(cfg.Mode, cfg.Width, cfg.Height, cfg.Topology);
            var patterns = BoardModes.CreatePatterns(cfg.Mode);
            // 强制串行：浏览器 WASM 无线程，小棋盘也在阈值以下，并行无意义
            var matcher = new FixedPatternMatcher(patterns, parallel: false);
            var rng = new XorShift128PlusRandom(ulong.Parse(cfg.Seed));
            var pieces = cfg.ParsePieces();
            var colorCount = cfg.Colors;

            // 棋子注册表：0=空硬约定，1..colorCount 按序注册
            var registry = new PieceRegistry();
            for (int i = 1; i <= colorCount; i++)
                registry.Register(new ColorPiece("颜色" + i));
            registry.Freeze();

            if (pieces != null)
                BoardInitializer.Fill(board, pieces);
            else
                BoardInitializer.FillRandom(board, rng, colorCount);

            BoardValidator.Validate(board, patterns);

            var ctx = new StepContext(rng);
            var input = new InputChannel<(int a, int b)>();

            // 传统三消模式（完整特殊子交互矩阵，仅矩形）：走 Samples.Classic 的 ClassicGameManager
            if (cfg.Bombs && cfg.Mode == BoardModes.Rect)
            {
                var classicPatterns = Samples.Classic.ClassicSetup.CreatePatterns();
                var classicMatcher = new FixedPatternMatcher(classicPatterns, parallel: false);
                BoardValidator.Validate(board, classicPatterns);
                var classicKinds = new Samples.KindLayer(board.CellCount);
                var classicManager = new Samples.Classic.ClassicGameManager(input, classicMatcher, colorCount,
                    cfg.Width, cfg.Height, classicKinds, registry, cfg.BuildArbiter(), cfg.BuildResolver());
                var classicSession = new GameSession(board, ctx, input, null, cfg.Mode, classicKinds,
                    classicMatcher, classicManager, () => classicManager.Input.WaitingForInput);
                classicSession.StartRun(classicManager);
                return classicSession;
            }

            var kinds = cfg.Bombs ? new Samples.KindLayer(board.CellCount) : null;
            // 声明式图编排：GameConfig → StepGraphDef → GraphStepManager（原手写 ClassicStepManager 的等价图，
            // 工具箱面板的每个开关 = 图里的节点/参数，见 GraphBuilder）
            var graph = GraphBuilder.Build(cfg, cfg.Bombs);
            var catalog = GraphCatalog.Create(kinds, cfg.BuildSelector());
            var buildCtx = new StepBuildContext
            {
                Board = board,
                Matcher = matcher,
                Pieces = registry,
                Input = input,
                Colors = colorCount,
            };
            var graphManager = new GraphStepManager(graph, catalog, buildCtx);
            bool WaitForAnyInput()
            {
                foreach (var kv in graphManager.NodeSteps)
                    if (kv.Value is PlayerSwapStep ps && ps.WaitingForInput) return true;
                return false;
            }
            var session = new GameSession(board, ctx, input, null, cfg.Mode, kinds, matcher,
                null, WaitForAnyInput);
            session.StartRun(graphManager);
            return session;
        }

        private void StartRun(IStepManager manager)
        {
            var orch = new Orchestrator(_board, manager, _ctx);
            orch.AddEventSink(this);
            _runTask = orch.RunAsync();
            _runTask.ContinueWith(t =>
            {
                OnError?.Invoke(t.Exception?.GetBaseException().Message ?? "未知错误");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>表现层 → 逻辑层的唯一输入路径。编排已死时抛异常。</summary>
        public void OfferSwap(int a, int b)
        {
            if (_runTask.IsFaulted)
                throw new InvalidOperationException("游戏流程已终止", _runTask.Exception?.GetBaseException());
            _input.Offer((a, b));
        }

        /// <summary>是否正在等待玩家输入（可否点击）。</summary>
        public bool WaitingForInput => _waitingForInput();

        /// <summary>累计消除数。</summary>
        public int Score
        {
            get
            {
                _ctx.Info.TryGet<int>(EliminateStep.EliminatedTotalKey, out var total);
                return total;
            }
        }

        /// <summary>结算管道总分（score.total，未开启计分时为 0）。</summary>
        public int Points
        {
            get
            {
                _ctx.Info.TryGet<int>(ScoreStep.TotalKey, out var total);
                return total;
            }
        }

        /// <summary>剩余步数（turn.ap，未开启步数预算时为 0）。</summary>
        public int MovesLeft
        {
            get
            {
                _ctx.Info.TryGet<int>(TurnKeys.Ap, out var ap);
                return ap;
            }
        }

        /// <summary>提示一手（HintStrategy：合法手枚举取第一手）。仅应在等输入时调用。</summary>
        public (int a, int b)? GetHint()
        {
            var pairs = LegalMoveProbe.EnumerateLegalSwaps(_board, _matcher);
            var legal = new System.Collections.Generic.List<SwapOperation>(pairs.Count);
            foreach (var p in pairs) legal.Add(new SwapOperation(p.a, p.b));
            var pick = new HintStrategy().ChooseMove(_board, legal, _ctx);
            return pick.HasValue ? (pick.Value.A, pick.Value.B) : ((int a, int b)?)null;
        }

        /// <summary>当前棋盘快照（长度 CellCount，行优先）。</summary>
        public int[] Snapshot()
        {
            return _board.PieceTypes.ToArray();
        }

        /// <summary>棋盘 JSON：{width, height, score, points, moves, cells:[...]}。</summary>
        public string BoardJson()
        {
            var cells = _board.PieceTypes;
            var sb = new StringBuilder(64 + cells.Length * 2);
            sb.Append("{\"mode\":").Append(_mode)
              .Append(",\"width\":").Append(_board.Width)
              .Append(",\"height\":").Append(_board.Height)
              .Append(",\"score\":").Append(Score)
              .Append(",\"points\":").Append(Points)
              .Append(",\"moves\":").Append(MovesLeft)
              .Append(",\"waiting\":").Append(WaitingForInput ? "true" : "false")
              .Append(",\"cells\":[");
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(cells[i]);
            }
            sb.Append(']');
            if (_kinds != null)
            {
                sb.Append(",\"kinds\":[");
                for (int i = 0; i < cells.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(_kinds.Get(i));
                }
                sb.Append(']');
            }
            return sb.Append("}").ToString();
        }

        // ---- IEventSink ----

        public void OnStepBegin(int stepIndex, string stepName) { }

        public void OnEvent(in GameEventEnvelope envelope)
        {
            OnEventJson?.Invoke(EventJson.Serialize(in envelope));
        }

        public void OnStepEnd(int stepIndex, string stepName) { }
    }
}
