using System.Threading.Tasks;
using PolyMatch3.Core;
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
}
