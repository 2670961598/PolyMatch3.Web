using System;
using System.Collections.Generic;
using PolyMatch3.Core;
using PolyMatch3.Step;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 交换操作（数据型操作意图）：AI/Hint 枚举与选择的最小操作单元。
    /// 候选集由 LegalMoveProbe.EnumerateLegalSwaps 产出；后续操作类型（Tap/Path 等）
    /// 出现时再抽象 IOperation，现在不过度设计。
    /// </summary>
    public readonly struct SwapOperation : IEquatable<SwapOperation>
    {
        public readonly int A;
        public readonly int B;

        public SwapOperation(int a, int b) { A = a; B = b; }

        public bool Equals(SwapOperation other) => A == other.A && B == other.B;
        public override bool Equals(object obj) => obj is SwapOperation other && Equals(other);
        public override int GetHashCode() => A * 397 ^ B;
        public override string ToString() => $"swap({A},{B})";
    }

    /// <summary>
    /// 策略接口（Hint 与 AI 同源）：从合法操作集合中选一手。
    /// Hint = 浅打分取最优；AI = 同一个接口深度评估。无合法操作返回 null（调用方走洗牌）。
    /// 约束（确定性军规）：同输入必须同输出；打平按候选下标升序。
    /// </summary>
    public interface IStrategy
    {
        SwapOperation? ChooseMove(GraphBoard board, IReadOnlyList<SwapOperation> legal, StepContext ctx);
    }

    /// <summary>提示策略：打分委托取最高分（打平取下标升序）；无打分委托时取第一手。</summary>
    public sealed class HintStrategy : IStrategy
    {
        private readonly Func<GraphBoard, SwapOperation, int> _score;

        public HintStrategy(Func<GraphBoard, SwapOperation, int> score = null)
        {
            _score = score;
        }

        public SwapOperation? ChooseMove(GraphBoard board, IReadOnlyList<SwapOperation> legal, StepContext ctx)
        {
            if (legal == null || legal.Count == 0) return null;
            if (_score == null) return legal[0];

            int best = 0, bestScore = _score(board, legal[0]);
            for (int i = 1; i < legal.Count; i++)
            {
                int s = _score(board, legal[i]);
                if (s > bestScore) { bestScore = s; best = i; } // 严格大于才换 ⇒ 打平保留下标小者
            }
            return legal[best];
        }
    }
}
