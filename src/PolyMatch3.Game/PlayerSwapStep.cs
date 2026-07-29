using System;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Step;
using PolyMatch3.Tools;

namespace PolyMatch3.Game
{
    /// <summary>
    /// 等待玩家输入一对格子并交换（输入型 Step，消费点即回放记录点）。
    /// 非法输入（越界 / 同格 / 不相邻）直接丢弃，继续等待下一个。
    /// </summary>
    public sealed class PlayerSwapStep : IStep
    {
        /// <summary>最近一次成功交换的格子对（黑板键，供 SwapBack 取回）。</summary>
        public const string LastSwapKey = "lastSwap";

        private readonly InputChannel<(int a, int b)> _input;
        private readonly Samples.KindLayer _kinds; // 可空：提供时交换同步 kind 层（双层同步铁律）

        /// <summary>是否正在等待玩家输入（表现层用它控制"可否点击"）。</summary>
        public volatile bool WaitingForInput;

        public PlayerSwapStep(InputChannel<(int a, int b)> input, Samples.KindLayer kinds = null)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _kinds = kinds;
        }

        public string Name => "PlayerSwap";
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

                int t = board.GetPieceType(a);
                board.SetPieceType(a, board.GetPieceType(b));
                board.SetPieceType(b, t);
                _kinds?.Swap(a, b);

                ctx.Info.Set(LastSwapKey, (a, b));
                return new StepResult { Success = true, Events = { new SwapEvent(a, b) } };
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
