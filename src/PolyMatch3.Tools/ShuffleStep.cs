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
    /// </summary>
    public sealed class ShuffleStep : IStep
    {
        private readonly IMatcher _matcher; // 可空：给了就保证洗出合法手
        private readonly int _maxAttempts;

        public ShuffleStep(IMatcher matcher = null, int maxAttempts = 32)
        {
            if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "保险丝必须 ≥ 1");
            _matcher = matcher;
            _maxAttempts = maxAttempts;
        }

        public string Name => "Shuffle";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            // 非空格及其棋子值（按格 id 升序收集，确定性）
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

            bool ok = false;
            for (int attempt = 0; attempt < _maxAttempts && !ok; attempt++)
            {
                // Fisher-Yates（只走 ctx.Random）
                for (int i = values.Count - 1; i > 0; i--)
                {
                    int j = ctx.Random.Next(i + 1);
                    (values[i], values[j]) = (values[j], values[i]);
                }
                for (int i = 0; i < cells.Count; i++)
                    board.SetPieceType(cells[i], values[i]);

                ok = _matcher == null || LegalMoveProbe.HasLegalSwap(board, _matcher);
            }

            return Task.FromResult(new StepResult
            {
                Success = ok,
                Events = { new ShuffleEvent(cells.ToArray()) }
            });
        }
    }
}
