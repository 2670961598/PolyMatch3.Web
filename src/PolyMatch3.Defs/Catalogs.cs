using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Step;
using PolyMatch3.Tools;

namespace PolyMatch3.Defs
{
    /// <summary>
    /// Step 构建上下文：catalog 工厂构建 Step 实例时可取用的一切装配产物。
    /// 行为永远是代码，数据只通过 catalog 的稳定字符串 key 引用行为——
    /// 新玩法 = 新 Step 类 + 注册一个 key，数据层零改动。
    /// </summary>
    public sealed class StepBuildContext
    {
        /// <summary>本局棋盘（已冻结拓扑）。</summary>
        public GraphBoard Board;

        /// <summary>本局匹配器（由图案集构建，构造期已过全量校验）。</summary>
        public IMatcher Matcher;

        /// <summary>本局棋子注册表（已冻结）。</summary>
        public PieceRegistry Pieces;

        /// <summary>交换输入通道（输入型 Step 消费，表现层经 LevelSession.OfferSwap 投递）。</summary>
        public InputChannel<(int a, int b)> Input;

        /// <summary>关卡颜色数（refill 等 Step 的默认颜色数）。</summary>
        public int Colors;

        /// <summary>按名字解析边索引；未注册即抛含现有边名的可行动错误。</summary>
        public int ResolveEdge(string name, string where)
        {
            return BoardDef.ResolveEdgeName(Board.EdgeTypes, name, where);
        }
    }

    /// <summary>
    /// Step 行为注册表：稳定字符串 key → Step 工厂。
    /// Defs 内置注册 Tools 全部 Step（match/eliminate/gravity/pathGravity/refill/swap）；
    /// 玩法的特殊 Step（含输入型 playerSwap/revertSwap、炸弹系等）由游戏侧在自己的装配处注册进同一个 catalog。
    /// </summary>
    public sealed class StepCatalog
    {
        private readonly Dictionary<string, Func<StepBuildContext, JObject, IStep>> _factories
            = new Dictionary<string, Func<StepBuildContext, JObject, IStep>>();

        /// <summary>已注册 key 数。</summary>
        public int Count => _factories.Count;

        /// <summary>注册 Step 工厂。key 重复即抛（key 是数据层引用行为的唯一凭据，歧义不可接受）。</summary>
        public void Register(string key, Func<StepBuildContext, JObject, IStep> factory)
        {
            if (string.IsNullOrEmpty(key))
                throw new DefsException("StepCatalog.Register 的 key 为空：每个 Step 行为必须有稳定字符串 key。");
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (_factories.ContainsKey(key))
                throw new DefsException($"StepCatalog 的 key '{key}' 重复注册（现有：{ListKeys()}）。请改用新 key，或先确认是否为重复装配。");
            _factories.Add(key, factory);
        }

        public bool Contains(string key)
        {
            return key != null && _factories.ContainsKey(key);
        }

        /// <summary>按 key 构建 Step 实例。未知 key → 抛含已注册 key 列表的可行动错误。</summary>
        public IStep Build(string key, StepBuildContext ctx, JObject parameters, string nodeId)
        {
            if (string.IsNullOrEmpty(key) || !_factories.TryGetValue(key, out var factory))
                throw new DefsException(
                    $"StepGraph 节点 '{nodeId}' 引用了未注册的 step key '{key}'（catalog 已注册：{ListKeys()}）。请修正拼写，或在装配处注册该 key。");
            var step = factory(ctx, parameters);
            if (step == null)
                throw new DefsException($"step key '{key}' 的工厂返回了 null：请检查该工厂的注册代码。");
            return step;
        }

        private string ListKeys()
        {
            var keys = new List<string>(_factories.Keys);
            keys.Sort(StringComparer.Ordinal);
            return string.Join(", ", keys);
        }

        /// <summary>内置注册 Tools 全部 Step。玩法特殊 Step 在此返回值上继续 Register。</summary>
        public static StepCatalog CreateDefault()
        {
            var catalog = new StepCatalog();

            catalog.Register("match", (ctx, p) =>
            {
                // 兼容参数：arbitrate=true（默认）= Match + 覆盖去重仲裁的捆绑（旧内联仲裁语义）；
                // 需要别的仲裁策略时改用 "arbitrate" 节点在图里显式编排
                bool arbitrate = p?.Value<bool?>("arbitrate") ?? true;
                string resultKey = p?.Value<string>("resultKey") ?? MatchStep.DefaultKey;
                var match = new MatchStep(ctx.Matcher, resultKey);
                if (!arbitrate) return match;
                return new StepBundle("Match", match,
                    new ArbitrateStep(MatchArbiters.Containment, resultKey, resultKey));
            });

            catalog.Register("arbitrate", (ctx, p) =>
            {
                string arbiterId = p?.Value<string>("arbiter");
                var arbiter = string.IsNullOrEmpty(arbiterId) ? MatchArbiters.Containment : MatchArbiters.Get(arbiterId);
                string sourceKey = p?.Value<string>("sourceKey") ?? MatchStep.DefaultKey;
                string resultKey = p?.Value<string>("resultKey");
                return new ArbitrateStep(arbiter, sourceKey, resultKey);
            });

            catalog.Register("eliminate", (ctx, p) =>
            {
                string sourceKey = p?.Value<string>("sourceKey") ?? MatchStep.DefaultKey;
                return new EliminateStep(sourceKey, ctx.Pieces);
            });

            catalog.Register("gravity", (ctx, p) =>
            {
                string fallEdge = p?.Value<string>("fallEdge");
                if (string.IsNullOrEmpty(fallEdge))
                    throw new DefsException("gravity Step 缺少参数 fallEdge（边名，如 \"Down\"）：请在节点 params 中补充。");
                return new GravityStep(ctx.ResolveEdge(fallEdge, "gravity Step 参数 fallEdge"));
            });

            catalog.Register("pathGravity", (ctx, p) =>
            {
                var edgesToken = p?["edges"];
                if (edgesToken == null)
                    throw new DefsException("pathGravity Step 缺少参数 edges（有序有向边 [[from,to],...]）：请在节点 params 中补充。");
                var edges = new List<(int from, int to)>();
                foreach (var pair in edgesToken)
                {
                    var arr = pair as JArray;
                    if (arr == null || arr.Count != 2)
                        throw new DefsException($"pathGravity Step 参数 edges 含非法项 '{pair}'（应为 [from, to] 二元数组）。请修正。");
                    edges.Add((arr[0].Value<int>(), arr[1].Value<int>()));
                }
                return new PathGravityStep(ctx.Board.CellCount, edges);
            });

            catalog.Register("refill", (ctx, p) =>
            {
                int colorCount = p?.Value<int?>("colorCount") ?? ctx.Colors;
                return new RefillStep(colorCount, ctx.Pieces);
            });

            catalog.Register("swap", (ctx, p) =>
            {
                int? a = p?.Value<int?>("a");
                int? b = p?.Value<int?>("b");
                if (a == null || b == null)
                    throw new DefsException("swap Step 缺少参数 a/b（要交换的两个格子 id）：请在节点 params 中补充。输入驱动的交换请改用 playerSwap。");
                return new SwapStep(a.Value, b.Value);
            });

            catalog.Register("score", (ctx, p) =>
            {
                // 演示修饰符：flatBonus = 固定加分类分值；cascadeScale = 连锁×N
                var mods = new List<IScoreModifier>();
                int? flat = p?.Value<int?>("flatBonus");
                if (flat != null && flat.Value != 0)
                    mods.Add(new FlatBonusModifier($"flat:{flat.Value}", 50, flat.Value));
                if (p?.Value<bool?>("cascadeScale") ?? false)
                    mods.Add(new CascadeScaleModifier("cascade", 10));
                return new ScoreStep(mods.Count == 0 ? null : mods);
            });

            catalog.Register("beginTurn", (ctx, p) =>
            {
                int? ap = p?.Value<int?>("ap");
                if (ap == null)
                    throw new DefsException("beginTurn Step 缺少参数 ap（回合行动点上限）：请在节点 params 中补充。");
                return new BeginTurnStep(ap.Value);
            });

            catalog.Register("spendAp", (ctx, p) =>
            {
                int cost = p?.Value<int?>("cost") ?? 1;
                return new SpendApStep(cost);
            });

            catalog.Register("deadlockCheck", (ctx, p) =>
            {
                string resultKey = p?.Value<string>("resultKey") ?? DeadlockCheckStep.DefaultKey;
                return new DeadlockCheckStep(ctx.Matcher, resultKey);
            });

            catalog.Register("shuffle", (ctx, p) =>
            {
                // 默认带 matcher：重试到洗出合法手（保险丝 32 次）
                int maxAttempts = p?.Value<int?>("maxAttempts") ?? 32;
                return new ShuffleStep(ctx.Matcher, maxAttempts);
            });

            catalog.Register("count", (ctx, p) =>
            {
                string resultKey = p?.Value<string>("resultKey");
                if (string.IsNullOrEmpty(resultKey))
                    throw new DefsException("count Step 缺少参数 resultKey（计数结果写黑板的键）：请在节点 params 中补充。");
                int? color = p?.Value<int?>("color");
                if (color != null) return CountStep.Color(resultKey, color.Value);
                return new CountStep(resultKey, (b, c) => b.GetPieceType(c) != PieceRegistry.EmptyId);
            });

            catalog.Register("fieldGravity", (ctx, p) =>
            {
                // 汇点：默认底行；也可 params.sinks 显式给格子数组
                var sinksToken = p?["sinks"];
                if (sinksToken != null)
                    return new FieldGravityStep(sinksToken.ToObject<int[]>());
                var sinks = new int[ctx.Board.Width];
                for (int x = 0; x < ctx.Board.Width; x++)
                    sinks[x] = (ctx.Board.Height - 1) * ctx.Board.Width + x;
                return new FieldGravityStep(sinks);
            });

            return catalog;
        }
    }

    /// <summary>
    /// 棋子行为注册表：behavior key → IPiece 工厂（入参为棋子 key，即其 Id）。
    /// 内置 "color"（基础颜色棋子，无钩子）；玩法的 bomb/线弹等在自己程序集注册。
    /// </summary>
    public sealed class PieceCatalog
    {
        private readonly Dictionary<string, Func<string, IPiece>> _factories
            = new Dictionary<string, Func<string, IPiece>>();

        /// <summary>注册棋子行为工厂。key 重复即抛。</summary>
        public void Register(string behavior, Func<string, IPiece> factory)
        {
            if (string.IsNullOrEmpty(behavior))
                throw new DefsException("PieceCatalog.Register 的 behavior key 为空。");
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (_factories.ContainsKey(behavior))
                throw new DefsException($"PieceCatalog 的 behavior '{behavior}' 重复注册。请改用新 key，或先确认是否为重复装配。");
            _factories.Add(behavior, factory);
        }

        /// <summary>按 behavior 构建棋子。未知 behavior → 抛含已注册 key 列表的可行动错误。</summary>
        public IPiece Build(string behavior, string pieceKey)
        {
            if (string.IsNullOrEmpty(behavior) || !_factories.TryGetValue(behavior, out var factory))
                throw new DefsException(
                    $"棋子 '{pieceKey}' 引用了未注册的 behavior '{behavior}'（catalog 已注册：{ListKeys()}）。请修正拼写，或在装配处注册该行为。");
            var piece = factory(pieceKey);
            if (piece == null)
                throw new DefsException($"behavior '{behavior}' 的工厂返回了 null：请检查该工厂的注册代码。");
            return piece;
        }

        private string ListKeys()
        {
            var keys = new List<string>(_factories.Keys);
            keys.Sort(StringComparer.Ordinal);
            return string.Join(", ", keys);
        }

        /// <summary>内置注册：color（基础颜色棋子，无钩子）。</summary>
        public static PieceCatalog CreateDefault()
        {
            var catalog = new PieceCatalog();
            catalog.Register("color", key => new ColorPiece(key));
            return catalog;
        }
    }

    /// <summary>基础颜色棋子：只有身份（Id），无任何钩子。数据层的默认棋子行为。</summary>
    public sealed class ColorPiece : IPiece
    {
        public ColorPiece(string id)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
        }

        public string Id { get; }
    }

    /// <summary>
    /// 定名 Step 捆绑（CompositeStep 的最小具体类）：catalog 工厂需要"一个 key 产出
    /// 一串原子 Step"时使用（如 match 的 匹配+仲裁 兼容捆绑）。语义全部取基类默认
    /// （任一子 Step 失败即短路，全部成功才算成功，事件按序合并）。
    /// </summary>
    public sealed class StepBundle : CompositeStep
    {
        private readonly string _name;
        private readonly IStep[] _subSteps;

        public StepBundle(string name, params IStep[] subSteps)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _subSteps = subSteps ?? throw new ArgumentNullException(nameof(subSteps));
        }

        public override string Name => _name;

        public override IStep[] GetSubSteps() => _subSteps;
    }
}
