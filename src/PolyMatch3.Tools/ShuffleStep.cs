using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Step;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 洗牌（死局重排）：把所有非空格的棋子值做 Fisher-Yates 重排（只走 ctx.Random，可复现），
    /// 空格位置不动、棋子 multiset 不变。给了 matcher 时会重试到"洗出合法手"为止
    /// （maxAttempts 保险丝，耗尽则保留最后一次结果且 Success=false）。
    /// 骨架固定 + 两个接缝：BeforeShuffle（平行层快照）/ AfterShuffled（按同一置换表同步平行层，
    /// 标准写法见 Samples 的 KindShuffleStep——炸弹/特殊子玩法洗牌时 kind 层不错位）。
    /// </summary>
    public class ShuffleStep : IStep
    {
        private readonly IMatcher _matcher; // 可空：给了就保证洗出合法手
        private readonly int _maxAttempts;

        public ShuffleStep(IMatcher matcher = null, int maxAttempts = 32)
        {
            if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "保险丝必须 ≥ 1");
            _matcher = matcher;
            _maxAttempts = maxAttempts;
        }

        public virtual string Name => "Shuffle";
        public virtual StepAttributes Attributes => new StepAttributes();

        public virtual Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            // 非空格及其棋子值快照（按格 id 升序收集，确定性）
            var cells = new List<int>();
            var values = new List<int>();
            for (int c = 0; c < board.CellCount; c++)
            {
                int p = board.GetPieceType(c);
                if (p == PieceRegistry.EmptyId) continue;
                cells.Add(c);
                values.Add(p);
            }
            if (cells.Count < 2)
                return Task.FromResult(new StepResult { Success = false });

            BeforeShuffle(board, cells, ctx);

            // 下标置换（而非就地洗值）：平行层才能用同一张置换表同步
            var order = new int[values.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;

            bool ok = false;
            for (int attempt = 0; attempt < _maxAttempts && !ok; attempt++)
            {
                // Fisher-Yates（只走 ctx.Random），在上一尝试的置换上继续洗
                for (int i = order.Length - 1; i > 0; i--)
                {
                    int j = ctx.Random.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
                for (int i = 0; i < cells.Count; i++)
                    board.SetPieceType(cells[i], values[order[i]]);
                AfterShuffled(board, cells, order, ctx);

                ok = _matcher == null || LegalMoveProbe.HasLegalSwap(board, _matcher);
            }

            return Task.FromResult(new StepResult
            {
                Success = ok,
                Events = { new ShuffleEvent(cells.ToArray()) }
            });
        }

        /// <summary>接缝①：洗牌前（平行层拍快照的挂点；默认空）。</summary>
        protected virtual void BeforeShuffle(GraphBoard board, List<int> cells, StepContext ctx) { }

        /// <summary>
        /// 接缝②：每次尝试落子完成后回调。语义：cells[i] 的新棋子来自快照中的 cells[order[i]]——
        /// 平行层按同一公式同步（默认空，即"只洗颜色层"）。
        /// </summary>
        protected virtual void AfterShuffled(GraphBoard board, List<int> cells, int[] order, StepContext ctx) { }
    }
}
