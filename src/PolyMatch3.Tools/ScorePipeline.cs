using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Step;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 结算上下文：一次结算（一步消除）的全部输入 + 运行中的分数。
    /// 修饰符按序读写本对象（可读 Board 但**禁写棋盘**——副作用必须走 Step）。
    /// </summary>
    public sealed class ScoreContext
    {
        /// <summary>棋盘（只读用途；写棋盘违反契约）。</summary>
        public GraphBoard Board;
        /// <summary>本次结算依据的匹配组（可为空列表，如交互路径强制消除）。</summary>
        public List<MatchGroup> Groups = new List<MatchGroup>();
        /// <summary>本次被清除的格子（EliminateStep.LastCellsKey）。</summary>
        public List<int> Cells = new List<int>();
        /// <summary>连锁层数（1 = 非连锁；由编排层在黑板 cascade.level 维护）。</summary>
        public int CascadeLevel = 1;
        /// <summary>运行中的分数（基础分起手，修饰符逐个变换）。</summary>
        public int Score;
        /// <summary>分明细（source 标签 → 增量），按修饰符应用顺序，随 ScoreEvent 发出。</summary>
        public readonly List<(string source, int delta)> Contributions = new List<(string, int)>();

        /// <summary>加算并记账。</summary>
        public void Add(string source, int delta)
        {
            Score += delta;
            Contributions.Add((source, delta));
        }

        /// <summary>乘算并记账（记录实际增量）。factor 为整数倍率。</summary>
        public void Scale(string source, int factor)
        {
            int delta = Score * (factor - 1);
            Score *= factor;
            Contributions.Add((source, delta));
        }
    }

    /// <summary>
    /// 触发器：修饰符的生效条件。显式按序求值（不做发布订阅总线——订阅顺序即行为，是确定性的天敌）。
    /// 输入 = 结算上下文 + Step 上下文（黑板可读：回合号、连锁计数等）。
    /// </summary>
    public interface ITrigger
    {
        string Id { get; }
        bool Test(ScoreContext score, StepContext ctx);
    }

    /// <summary>连锁层数达标触发（内置示例：cascade.level ≥ minLevel）。</summary>
    public sealed class MinCascadeTrigger : ITrigger
    {
        private readonly int _minLevel;
        public MinCascadeTrigger(int minLevel) { _minLevel = minLevel; }
        public string Id => $"min-cascade:{_minLevel}";
        public bool Test(ScoreContext score, StepContext ctx) => score.CascadeLevel >= _minLevel;
    }

    /// <summary>
    /// 计分修饰符（小丑牌式钩子的最小形态）：挂在结算管道上，按全序（Priority 降序，同级注册序）
    /// 逐个读写 ScoreContext。Trigger 为 null = 恒生效。可读棋盘，禁写棋盘。
    /// </summary>
    public interface IScoreModifier
    {
        string Id { get; }
        int Priority { get; }
        ITrigger Trigger { get; }
        void Apply(ScoreContext score, StepContext ctx);
    }

    /// <summary>固定加分类（如"每次消除 +50"）。</summary>
    public sealed class FlatBonusModifier : IScoreModifier
    {
        private readonly int _points;
        public FlatBonusModifier(string id, int priority, int points, ITrigger trigger = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Priority = priority;
            _points = points;
            Trigger = trigger;
        }
        public string Id { get; }
        public int Priority { get; }
        public ITrigger Trigger { get; }
        public void Apply(ScoreContext score, StepContext ctx) => score.Add(Id, _points);
    }

    /// <summary>按棋盘上某颜色现存数量加分类（如"每个红色 +5"——演示修饰符可读棋盘）。</summary>
    public sealed class PerColorCountModifier : IScoreModifier
    {
        private readonly int _color;
        private readonly int _pointsPer;
        public PerColorCountModifier(string id, int priority, int color, int pointsPer, ITrigger trigger = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Priority = priority;
            _color = color;
            _pointsPer = pointsPer;
            Trigger = trigger;
        }
        public string Id { get; }
        public int Priority { get; }
        public ITrigger Trigger { get; }
        public void Apply(ScoreContext score, StepContext ctx)
        {
            int n = 0;
            var types = score.Board.PieceTypes;
            for (int i = 0; i < types.Length; i++)
                if (types[i] == _color) n++;
            score.Add(Id, n * _pointsPer);
        }
    }

    /// <summary>连锁乘算类（分数 × 连锁层数，level 1 时增量为 0）。</summary>
    public sealed class CascadeScaleModifier : IScoreModifier
    {
        public CascadeScaleModifier(string id, int priority, ITrigger trigger = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Priority = priority;
            Trigger = trigger;
        }
        public string Id { get; }
        public int Priority { get; }
        public ITrigger Trigger { get; }
        public void Apply(ScoreContext score, StepContext ctx)
        {
            if (score.CascadeLevel > 1) score.Scale(Id, score.CascadeLevel);
        }
    }

    /// <summary>
    /// 结算管道（Step）：读本次消除（EliminateStep.LastCellsKey）→ 基础分（默认 = 消除格数，
    /// 可重载 ComputeBase）→ 修饰符链按全序逐个应用（Trigger 不过则跳过）→ 累计 score.total →
    /// 发单个 ScoreEvent（终值 + 分明细）。连锁层数读黑板 cascade.level（编排层维护，回输入时清零）。
    /// </summary>
    public class ScoreStep : IStep
    {
        /// <summary>累计总分的黑板键。</summary>
        public const string TotalKey = "score.total";
        /// <summary>连锁层数的黑板键（编排层写，缺省 = 1）。</summary>
        public const string CascadeLevelKey = "cascade.level";

        private readonly IScoreModifier[] _modifiers; // 构造时已按全序排好
        private readonly string _matchesKey;

        public ScoreStep(IReadOnlyList<IScoreModifier> modifiers = null, string matchesKey = MatchStep.DefaultKey)
        {
            _modifiers = modifiers == null ? new IScoreModifier[0] : new List<IScoreModifier>(modifiers).ToArray();
            foreach (var m in _modifiers)
                if (m == null) throw new ArgumentException("修饰符列表含 null", nameof(modifiers));
            // 全序：Priority 降序，同级保持注册序（下标升序）——完全确定
            var order = new int[_modifiers.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (x, y) =>
            {
                int c = _modifiers[y].Priority.CompareTo(_modifiers[x].Priority);
                return c != 0 ? c : x.CompareTo(y);
            });
            var sorted = new IScoreModifier[_modifiers.Length];
            for (int i = 0; i < order.Length; i++) sorted[i] = _modifiers[order[i]];
            _modifiers = sorted;
            _matchesKey = matchesKey;
        }

        public virtual string Name => "Score";
        public virtual StepAttributes Attributes => new StepAttributes();

        public virtual Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            if (!ctx.Info.TryGet<List<int>>(EliminateStep.LastCellsKey, out var cells) || cells.Count == 0)
                return Task.FromResult(new StepResult { Success = false });

            var sc = new ScoreContext { Board = board, Cells = cells };
            ctx.Info.TryGet<List<MatchGroup>>(_matchesKey, out var groups);
            if (groups != null) sc.Groups = groups;
            ctx.Info.TryGet<int>(CascadeLevelKey, out var level);
            sc.CascadeLevel = level > 0 ? level : 1;
            sc.Score = ComputeBase(sc);

            foreach (var m in _modifiers)
            {
                if (m.Trigger != null && !m.Trigger.Test(sc, ctx)) continue;
                m.Apply(sc, ctx);
            }

            ctx.Info.TryGet<int>(TotalKey, out var total);
            total += sc.Score;
            ctx.Info.Set(TotalKey, total);

            return Task.FromResult(new StepResult
            {
                Success = true,
                Events = { CreateEvent(sc, total) }
            });
        }

        /// <summary>接缝①：基础分（默认 = 本次消除格数）。</summary>
        protected virtual int ComputeBase(ScoreContext sc) => sc.Cells.Count;

        /// <summary>接缝②：结算事件构造。</summary>
        protected virtual GameEvent CreateEvent(ScoreContext sc, int total) => new ScoreEvent(sc.Score, total, sc.Contributions);
    }
}
