using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Step;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 计数查询（"算结论"原语）：按谓词全场扫描，命中数写黑板。
    /// 用途：关卡目标（收集 N 个某色）、技能充能、修饰符条件。不发事件（结论只喂编排层）。
    /// Success = 命中数 &gt; 0。
    /// </summary>
    public sealed class CountStep : IStep
    {
        private readonly Func<GraphBoard, int, bool> _predicate;
        private readonly string _resultKey;

        public CountStep(string resultKey, Func<GraphBoard, int, bool> predicate)
        {
            if (string.IsNullOrEmpty(resultKey))
                throw new ArgumentException("resultKey 不能为空：计数结果写黑板该键，空键会让后续 Step 永远读不到", nameof(resultKey));
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
            _resultKey = resultKey;
        }

        /// <summary>便利构造：数某颜色（如 CountStep.Color("goal.red", 1)）。</summary>
        public static CountStep Color(string resultKey, int color)
        {
            return new CountStep(resultKey, (b, c) => b.GetPieceType(c) == color);
        }

        public string Name => "Count";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            int n = 0;
            for (int c = 0; c < board.CellCount; c++)
                if (_predicate(board, c)) n++;
            ctx.Info.Set(_resultKey, n);
            return Task.FromResult(new StepResult { Success = n > 0 });
        }
    }

    /// <summary>
    /// 合法手探测（静态工具）：枚举全部相邻对（所有边类型、无向去重），逐个试交换——
    /// 存在"交换后产生匹配"的对 ⇒ 有合法手。试算在颜色层上做完即还原，不改拓扑、不留痕迹。
    /// 含空格/同色的交换不算合法手（三消语义：交换两枚不同棋子）。
    /// </summary>
    public static class LegalMoveProbe
    {
        public static bool HasLegalSwap(GraphBoard board, IMatcher matcher)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (matcher == null) throw new ArgumentNullException(nameof(matcher));

            bool found = false;
            ScanPairs(board, matcher, pair =>
            {
                found = true;
                return false; // 早退：找到一手就够
            });
            return found;
        }

        /// <summary>枚举全部合法交换（Hint/AI 的候选集），按 (a, b) 升序返回（确定性）。</summary>
        public static List<(int a, int b)> EnumerateLegalSwaps(GraphBoard board, IMatcher matcher)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (matcher == null) throw new ArgumentNullException(nameof(matcher));

            var result = new List<(int a, int b)>();
            ScanPairs(board, matcher, pair =>
            {
                result.Add(pair);
                return true;
            });
            result.Sort();
            return result;
        }

        /// <summary>扫描全部相邻对并试交换；visitor 返回 false 即终止扫描。试算做完即还原。</summary>
        private static void ScanPairs(GraphBoard board, IMatcher matcher, Func<(int a, int b), bool> visitor)
        {
            for (int c = 0; c < board.CellCount; c++)
            {
                if (board.GetPieceType(c) == PieceRegistry.EmptyId) continue;
                for (int e = 0; e < board.EdgeTypeCount; e++)
                {
                    foreach (var n in board.NeighborsOf(c, e))
                    {
                        if (n <= c) continue; // 无向去重（每对只看一次）
                        if (board.GetPieceType(n) == PieceRegistry.EmptyId) continue;
                        if (board.GetPieceType(n) == board.GetPieceType(c)) continue; // 同色交换无意义

                        // 试交换（显式存取，做完即还原）
                        int vc = board.GetPieceType(c), vn = board.GetPieceType(n);
                        board.SetPieceType(c, vn);
                        board.SetPieceType(n, vc);
                        bool hit = matcher.Match(board).Count > 0;
                        board.SetPieceType(c, vc);
                        board.SetPieceType(n, vn);

                        if (hit && !visitor((c, n))) return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 死局检测（Step）：把"是否还有合法手"写黑板（默认键 hasLegalMove），Success = 有合法手。
    /// 编排层据此分支：有 → 回输入；无 → ShuffleStep 洗牌。不发事件。
    /// </summary>
    public sealed class DeadlockCheckStep : IStep
    {
        public const string DefaultKey = "hasLegalMove";

        private readonly IMatcher _matcher;
        private readonly string _resultKey;

        public DeadlockCheckStep(IMatcher matcher, string resultKey = DefaultKey)
        {
            _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
            if (string.IsNullOrEmpty(resultKey))
                throw new ArgumentException("resultKey 不能为空", nameof(resultKey));
            _resultKey = resultKey;
        }

        public string Name => "DeadlockCheck";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            bool has = LegalMoveProbe.HasLegalSwap(board, _matcher);
            ctx.Info.Set(_resultKey, has);
            return Task.FromResult(new StepResult { Success = has });
        }
    }
}
