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

        public RevertSwapStep(int a, int b, Samples.KindLayer kinds = null)
        {
            _a = a;
            _b = b;
            _kinds = kinds;
        }

        public string Name => "SwapBack";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            int t = board.GetPieceType(_a);
            board.SetPieceType(_a, board.GetPieceType(_b));
            board.SetPieceType(_b, t);
            _kinds?.Swap(_a, _b);

            return Task.FromResult(new StepResult
            {
                Success = true,
                Events = { new SwapEvent(_a, _b) }
            });
        }
    }
}
