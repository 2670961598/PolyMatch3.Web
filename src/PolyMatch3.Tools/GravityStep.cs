using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Step;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 重力：棋子沿指定边类型向空格 compact。
    /// 广义图语义：非空格 u 的 fallEdge 邻居 v 为空 → u 的棋子移入 v；
    /// 按 CellId 升序逐趟扫描直到不动点（歧义先到先得，完全确定）。
    /// 矩形棋盘上即经典向下压实（顺序保持）。
    /// </summary>
    public class GravityStep : IStep
    {
        private readonly int _fallEdge;

        /// <param name="fallEdge">下落方向边索引（如矩形棋盘的 Down）。该边类型的子图必须无环。</param>
        public GravityStep(int fallEdge)
        {
            _fallEdge = fallEdge;
        }

        public virtual string Name => "Gravity";
        public virtual StepAttributes Attributes => new StepAttributes();

        public virtual Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            if (!board.IsTopologyFrozen)
                throw new InvalidOperationException("棋盘拓扑未冻结：加边完成后请先调用 FreezeTopology()");

            var offsets = board.NeighborOffsets;
            var neighbors = board.Neighbors;
            int stride = board.EdgeTypeCount;
            int cellCount = board.CellCount;

            // origin[c]：当前位于 c 的棋子最初来自哪格（-1 = 就是 c 自己）
            var origin = new int[cellCount];
            for (int i = 0; i < cellCount; i++) origin[i] = i;

            bool anyMoved = false;
            bool moved = true;
            int passes = 0;
            while (moved)
            {
                if (++passes > cellCount)
                    throw new InvalidOperationException("GravityStep 超过趟数上限：fallEdge 边类型的子图疑似含环（棋子沿环永远追赶空格）");
                moved = false;
                for (int u = 0; u < cellCount; u++)
                {
                    if (board.GetPieceType(u) == 0) continue;

                    int slot = u * stride + _fallEdge;
                    int start = offsets[slot];
                    int end = offsets[slot + 1];
                    for (int idx = start; idx < end; idx++)
                    {
                        int v = neighbors[idx];
                        if (board.GetPieceType(v) != 0) continue;

                        // u → v 移动一步
                        board.SetPieceType(v, board.GetPieceType(u));
                        board.SetPieceType(u, 0);
                        origin[v] = origin[u];
                        origin[u] = u;
                        moved = true;
                        anyMoved = true;
                        break; // 每趟每子只走一步（歧义按 id 升序先到先得）
                    }
                }
            }

            if (!anyMoved)
                return Task.FromResult(new StepResult { Success = false });

            // 汇总最终 from→to 映射
            var fromTo = new List<int>();
            var cells = new List<int>();
            for (int c = 0; c < cellCount; c++)
            {
                if (origin[c] != c && board.GetPieceType(c) != 0)
                {
                    fromTo.Add(origin[c]);
                    fromTo.Add(c);
                    cells.Add(c);
                }
            }

            return Task.FromResult(new StepResult
            {
                Success = true,
                Events = { new FallEvent(fromTo.ToArray(), cells.ToArray()) }
            });
        }
    }
}
