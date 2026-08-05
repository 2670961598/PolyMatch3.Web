using System;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Diagnostics;
using PolyMatch3.Matcher;
using PolyMatch3.Step;
using PolyMatch3.Tools;

namespace PolyMatch3.Defs
{
    /// <summary>
    /// 输入型 Step 的可查询接口：会话层（表现层/测试）据此判断"当前是否可点击"。
    /// 由具体输入 Step（如 Samples 的 PlayerSwapStep）实现，Defs 不依赖任何具体实现。
    /// </summary>
    public interface IInputAwaiting
    {
        /// <summary>是否正在等待玩家输入。</summary>
        bool WaitingForInput { get; }
    }

    /// <summary>
    /// 一局 LevelDef 的装配产物。形状对齐 Web demo 的 GameSession：
    /// 事件出口 = Orchestrator.AddEventSink；输入入口 = OfferSwap；状态查询 = WaitingForInput / Snapshot。
    /// </summary>
    public sealed class LevelSession
    {
        private readonly GraphStepManager _manager;
        private readonly InputChannel<(int a, int b)> _input;

        internal LevelSession(LevelDef def, GraphBoard board, StepContext ctx, Orchestrator orchestrator,
            GraphStepManager manager, InputChannel<(int a, int b)> input)
        {
            Def = def;
            Board = board;
            Ctx = ctx;
            Orchestrator = orchestrator;
            _manager = manager;
            _input = input;
        }

        /// <summary>本局关卡定义。</summary>
        public LevelDef Def { get; }

        /// <summary>本局棋盘。</summary>
        public GraphBoard Board { get; }

        /// <summary>本局执行上下文（含随机源/黑板/历史）。</summary>
        public StepContext Ctx { get; }

        /// <summary>本局编排器（事件出口挂 AddEventSink）。</summary>
        public Orchestrator Orchestrator { get; }

        /// <summary>是否有输入型 Step 正在等待玩家输入（表现层"可否点击"、测试驱动点）。</summary>
        public bool WaitingForInput
        {
            get
            {
                foreach (var kv in _manager.NodeSteps)
                {
                    if (kv.Value is IInputAwaiting awaiting && awaiting.WaitingForInput)
                        return true;
                }
                return false;
            }
        }

        /// <summary>投递一次交换输入（一对相邻格 id）。非法输入由输入 Step 丢弃。</summary>
        public void OfferSwap(int a, int b)
        {
            _input.Offer((a, b));
        }

        /// <summary>棋盘棋子值快照（长度 CellCount 的拷贝，0 = 空）。回放/测试断言用。</summary>
        public int[] Snapshot()
        {
            return Board.PieceTypes.ToArray();
        }

        /// <summary>驱动主循环直到 StepManager 宣告结束（maxSteps 为死循环保险丝）。</summary>
        public Task<StepContext> RunAsync(int maxSteps = 10000)
        {
            return Orchestrator.RunAsync(maxSteps);
        }
    }

    /// <summary>
    /// 关卡装配器：LevelDef → LevelSession。
    /// 装配顺序（一切非法配置在此处启动期暴露，而非运行期崩溃）：
    /// 棋盘 → 图案集（边名解析 + FixedPatternMatcher 构造校验）→ 棋子集 →
    /// 初始化填充（与 Refill 共用同一种子随机源）→ BoardValidator 统一校验 → 编排图。
    /// </summary>
    public static class LevelLoader
    {
        /// <summary>装配一局。catalog 由调用方提供：内置 Tools Step 经 StepCatalog.CreateDefault()，玩法 Step 自行追加注册。</summary>
        public static LevelSession Load(LevelDef def, StepCatalog stepCatalog, PieceCatalog pieceCatalog)
        {
            if (def == null) throw new DefsException("LevelDef 为 null：请提供合法的关卡定义。");
            if (stepCatalog == null) throw new DefsException("LevelLoader.Load 需要 StepCatalog，传入了 null。");
            if (pieceCatalog == null) throw new DefsException("LevelLoader.Load 需要 PieceCatalog，传入了 null。");
            if (def.Board == null) throw new DefsException($"关卡 '{def.Name}' 缺少 board 定义。");
            if (def.Patterns == null) throw new DefsException($"关卡 '{def.Name}' 缺少 patterns 定义。");
            if (def.Pieces == null) throw new DefsException($"关卡 '{def.Name}' 缺少 pieces 定义。");
            if (def.StepGraph == null) throw new DefsException($"关卡 '{def.Name}' 缺少 stepGraph 定义。");
            if (def.Init == null) throw new DefsException($"关卡 '{def.Name}' 缺少 init 定义（kind：random / randomNoMatch / fixed）。");
            if (def.Colors <= 0)
                throw new DefsException($"关卡 '{def.Name}' 的 colors = {def.Colors} 非法（必须 ≥ 1）。请修正关卡 JSON 的 colors。");

            // 随机源：初始化填充与 Refill 共用同一实例——同种子整局可复现
            var rng = new XorShift128PlusRandom(def.Seed);

            var board = def.Board.ToGraphBoard();
            var patterns = def.Patterns.ToPatterns(board.EdgeTypes);
            var matcher = new FixedPatternMatcher(patterns);
            var pieces = def.Pieces.ToRegistry(pieceCatalog);

            if (def.Colors > pieces.Count)
                throw new DefsException(
                    $"关卡 '{def.Name}' 的 colors = {def.Colors} 超出棋子集已注册棋子数 {pieces.Count}（棋子 id 1..{def.Colors} 中会有注册表不认识的值）。请补充棋子定义，或减小 colors。");

            ApplyInit(def, board, rng, matcher);
            BoardValidator.Validate(board, patterns);

            var ctx = new StepContext(rng);
            var buildCtx = new StepBuildContext
            {
                Board = board,
                Matcher = matcher,
                Pieces = pieces,
                Input = new InputChannel<(int a, int b)>(),
                Colors = def.Colors,
            };
            var manager = new GraphStepManager(def.StepGraph, stepCatalog, buildCtx);
            var orchestrator = new Orchestrator(board, manager, ctx);
            return new LevelSession(def, board, ctx, orchestrator, manager, buildCtx.Input);
        }

        /// <summary>开局初始化填充（FreezeTopology 之后、开局之前）。</summary>
        private static void ApplyInit(LevelDef def, GraphBoard board, IRandom rng, IMatcher matcher)
        {
            switch (def.Init.Kind)
            {
                case "random":
                    BoardInitializer.FillRandom(board, rng, def.Colors);
                    break;
                case "randomNoMatch":
                    BoardInitializer.FillRandom(board, rng, def.Colors, new NoInitialMatchConstraint(matcher));
                    break;
                case "fixed":
                    if (def.Init.FixedPieces == null)
                        throw new DefsException($"关卡 '{def.Name}' 的 init.kind = \"fixed\" 但 fixedPieces 为 null：请提供全棋盘棋子值数组（长度 = {board.CellCount}）。");
                    if (def.Init.FixedPieces.Length != board.CellCount)
                        throw new DefsException(
                            $"关卡 '{def.Name}' 的 fixedPieces 长度 {def.Init.FixedPieces.Length} 与棋盘格子数 {board.CellCount}（{board.Width}×{board.Height}）不一致。请修正 fixedPieces。");
                    BoardInitializer.Fill(board, def.Init.FixedPieces);
                    break;
                default:
                    throw new DefsException(
                        $"关卡 '{def.Name}' 的 init.kind = '{def.Init.Kind}' 未知（合法值：random / randomNoMatch / fixed）。请修正关卡 JSON 的 init.kind。");
            }
        }
    }
}
