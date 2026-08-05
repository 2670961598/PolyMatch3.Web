using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Step;
using PolyMatch3.Tools;

namespace PolyMatch3.Game
{
    /// <summary>
    /// 撤销交换：交换是自逆操作，再换一次即还原（交换后无匹配时回滚棋盘）。
    /// Step 名与 Swap 区分（SwapBack），表现层可据此播"弹回"动画。
    /// kind 层同样换回去（双层同步铁律）。
    /// </summary>
    public sealed class RevertSwapStep : IStep
    {
        private readonly int _a;
        private readonly int _b;
        private readonly Samples.KindLayer _kinds;
        private readonly bool _readFromBlackboard;

        public RevertSwapStep(int a, int b, Samples.KindLayer kinds = null)
        {
            _a = a;
            _b = b;
            _kinds = kinds;
        }

        /// <summary>图编排用构造：执行时从黑板读最近一次交换（PlayerSwapStep.LastSwapKey），
        /// 节点因此可以是静态实例（否则弹回格子只能在运行时临时构建）。</summary>
        public RevertSwapStep(Samples.KindLayer kinds = null)
        {
            _kinds = kinds;
            _readFromBlackboard = true;
        }

        public string Name => "SwapBack";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            int a = _a, b = _b;
            if (_readFromBlackboard)
            {
                if (!ctx.Info.TryGet<(int a, int b)>(PlayerSwapStep.LastSwapKey, out var pair))
                    return Task.FromResult(new StepResult { Success = false }); // 没有可弹回的交换（不应发生）
                a = pair.a;
                b = pair.b;
            }

            int t = board.GetPieceType(a);
            board.SetPieceType(a, board.GetPieceType(b));
            board.SetPieceType(b, t);
            _kinds?.Swap(a, b);

            return Task.FromResult(new StepResult
            {
                Success = true,
                Events = { new SwapEvent(a, b) }
            });
        }
    }
}
