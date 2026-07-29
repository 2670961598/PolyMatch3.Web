using System;
using PolyMatch3.Core;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 棋盘棋子初始化（开局前、FreezeTopology 之后调用）：填充颜色层的三种入口。
    /// ① 全指定（精心设计关卡）；② 种子随机（可复现）；③ 带约束的种子随机（重掷也走同一种子，仍可复现）。
    /// 铁规：框架不提供无种子的"纯随机"入口——正确姿势是"时钟播种 + 记录种子"。
    /// </summary>
    public static class BoardInitializer
    {
        /// <summary>
        /// ① 全指定填充：棋子值由外部给全（精心设计的关卡）。
        /// 值域约定（调用方保证，热路径零分支不拦）：合法值 0=空、1~N=颜色；
        /// 越界值（负数/超大）会被匹配器当普通颜色处理（幻影匹配）且逃避 Refill——请勿越界。
        /// </summary>
        public static void Fill(GraphBoard board, ReadOnlySpan<int> pieces)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (!board.IsTopologyFrozen)
                throw new InvalidOperationException("棋盘拓扑未冻结：加边完成后请先调用 FreezeTopology()");
            if (pieces.Length != board.CellCount)
                throw new ArgumentException($"棋子数组长度 {pieces.Length} 与棋盘格子数 {board.CellCount} 不一致", nameof(pieces));

            for (int i = 0; i < pieces.Length; i++)
                board.SetPieceType(i, pieces[i]);
        }

        /// <summary>
        /// ② 种子随机填充：同一随机源必然产出同一棋盘（可复现）。
        /// </summary>
        public static void FillRandom(GraphBoard board, IRandom rng, int colorCount)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (colorCount <= 0) throw new ArgumentOutOfRangeException(nameof(colorCount));
            if (!board.IsTopologyFrozen)
                throw new InvalidOperationException("棋盘拓扑未冻结：加边完成后请先调用 FreezeTopology()");

            for (int i = 0; i < board.CellCount; i++)
                board.SetPieceType(i, 1 + rng.Next(colorCount));
        }

        /// <summary>
        /// ③ 带约束的种子随机填充：填充后检查约束，不满足则用同一随机源整体重掷，直到满足。
        /// 重掷消耗的随机数同样来自种子，结果依然 100% 可复现。
        /// </summary>
        /// <param name="maxAttempts">重掷保险丝（约束过苛/棋盘不可行时快速失败）。</param>
        public static void FillRandom(GraphBoard board, IRandom rng, int colorCount, IBoardFillConstraint constraint, int maxAttempts = 100)
        {
            if (constraint == null) throw new ArgumentNullException(nameof(constraint));

            for (int attempt = 1; ; attempt++)
            {
                FillRandom(board, rng, colorCount);
                if (constraint.Accept(board)) return;
                if (attempt >= maxAttempts)
                    throw new InvalidOperationException(
                        $"约束填充重掷 {maxAttempts} 次仍未满足（{constraint.GetType().Name}）：请检查约束是否过苛或棋盘是否可行");
            }
        }
    }

    /// <summary>
    /// 棋盘填充约束：返回 false 则整体重掷。
    /// 实现接口即可自定义（无初始匹配、有可行步、密度限制……）。
    /// </summary>
    public interface IBoardFillConstraint
    {
        bool Accept(GraphBoard board);
    }
}
