using System;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Step;

namespace PolyMatch3.Samples.Classic
{
    /// <summary>
    /// 输入型 Step：等待玩家输入一对相邻格并**记录**（不交换！）。
    /// 交换与否由 Manager 根据两格的 kind 分支决定：
    /// 普通对普通 → KindSwapStep；含特殊子 → 交互 Step（特殊子交换不挪位置，直接结算）。
    /// 非法输入（越界 / 同格 / 不相邻）直接丢弃，继续等待下一个。
    /// </summary>
    public sealed class SwapInputStep : IStep
    {
        /// <summary>最近输入的格子对（黑板键，Manager 分支与弹回都读它）。</summary>
        public const string PairKey = "swapPair";

        private readonly InputChannel<(int a, int b)> _input;

        /// <summary>是否正在等待玩家输入（表现层用它控制"可否点击"）。</summary>
        public volatile bool WaitingForInput;

        public SwapInputStep(InputChannel<(int a, int b)> input)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
        }

        public string Name => "SwapInput";
        public StepAttributes Attributes => new StepAttributes { IsBlocking = true, IsUserInput = true };

        public async Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            while (true)
            {
                WaitingForInput = true;
                var (a, b) = await _input.WaitAsync().ConfigureAwait(false);
                WaitingForInput = false;

                if (a == b) continue;
                if ((uint)a >= (uint)board.CellCount || (uint)b >= (uint)board.CellCount) continue;
                if (!AreAdjacent(board, a, b)) continue;

                ctx.Info.Set(PairKey, (a, b));
                return new StepResult { Success = true };
            }
        }

        /// <summary>b 是否在 a 的任意边类型邻居中（CSR 直查，同步方法以使用 span）。</summary>
        private static bool AreAdjacent(GraphBoard board, int a, int b)
        {
            ReadOnlySpan<int> offsets = board.NeighborOffsets;
            ReadOnlySpan<int> neighbors = board.Neighbors;
            int stride = board.EdgeTypeCount;
            for (int e = 0; e < stride; e++)
            {
                int slot = a * stride + e;
                for (int i = offsets[slot]; i < offsets[slot + 1]; i++)
                    if (neighbors[i] == b) return true;
            }
            return false;
        }
    }
}
