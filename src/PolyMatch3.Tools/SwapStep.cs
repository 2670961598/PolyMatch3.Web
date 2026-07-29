using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Step;

namespace PolyMatch3.Tools
{
    /// <summary>交换两个格子的棋子。交换是自逆操作：再交换一次即还原，无需快照。</summary>
    public class SwapStep : IStep
    {
        protected readonly int _a;
        protected readonly int _b;

        public SwapStep(int a, int b)
        {
            _a = a;
            _b = b;
        }

        public virtual string Name => "Swap";
        public virtual StepAttributes Attributes => new StepAttributes();

        public virtual Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            int ta = board.GetPieceType(_a);
            board.SetPieceType(_a, board.GetPieceType(_b));
            board.SetPieceType(_b, ta);

            return Task.FromResult(new StepResult
            {
                Success = true,
                Events = { new SwapEvent(_a, _b) }
            });
        }
    }
}
