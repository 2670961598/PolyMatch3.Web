using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Step;
using PolyMatch3.Tools;

namespace PolyMatch3.Samples
{
    /// <summary>
    /// 双层同步的交换：颜色层交换（SwapStep 骨架）+ kind 层同步交换。
    /// </summary>
    public sealed class KindSwapStep : SwapStep
    {
        private readonly KindLayer _kinds;

        public KindSwapStep(KindLayer kinds, int a, int b) : base(a, b)
        {
            _kinds = kinds;
        }

        public override async Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            var result = await base.ExecuteAsync(board, ctx);
            _kinds.Swap(_a, _b);
            return result;
        }
    }

    /// <summary>
    /// 双层同步的重力：PathGravityStep 骨架 + OnPieceMoved 接缝同步 kind 层。
    /// </summary>
    public sealed class KindGravityStep : PathGravityStep
    {
        private readonly KindLayer _kinds;

        public KindGravityStep(KindLayer kinds, int cellCount, System.Collections.Generic.IEnumerable<(int from, int to)> edges)
            : base(cellCount, edges)
        {
            _kinds = kinds;
        }

        protected override void OnPieceMoved(GraphBoard board, int from, int to, StepContext ctx)
        {
            _kinds.Move(from, to);
        }
    }

    /// <summary>
    /// 双层同步的洗牌：ShuffleStep 骨架 + 接缝同步 kind 层。
    /// BeforeShuffle 拍 kind 快照，AfterShuffled 按同一置换表还原——炸弹被洗到哪，kind 跟到哪。
    /// </summary>
    public sealed class KindShuffleStep : ShuffleStep
    {
        private readonly KindLayer _kinds;
        private int[] _kindSnapshot; // 与 cells 列表同序（非 cellId 索引）

        public KindShuffleStep(KindLayer kinds, IMatcher matcher = null, int maxAttempts = 32)
            : base(matcher, maxAttempts)
        {
            _kinds = kinds;
        }

        protected override void BeforeShuffle(GraphBoard board, System.Collections.Generic.List<int> cells, StepContext ctx)
        {
            _kindSnapshot = new int[cells.Count];
            for (int i = 0; i < cells.Count; i++) _kindSnapshot[i] = _kinds.Get(cells[i]);
        }

        protected override void AfterShuffled(GraphBoard board, System.Collections.Generic.List<int> cells, int[] order, StepContext ctx)
        {
            // cells[i] 的新棋子来自快照中的 cells[order[i]]，kind 按同一公式走
            for (int i = 0; i < cells.Count; i++)
                _kinds.Set(cells[i], _kindSnapshot[order[i]]);
        }
    }
}
