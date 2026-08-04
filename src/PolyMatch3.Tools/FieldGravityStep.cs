using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Step;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 势场重力：以"汇点"（棋子流向的终点格）为原点 BFS 求距离场，每格棋子流向
    /// "距离严格更小且为空"的邻居（CSR 边序取第一个），多趟 compact 直到不动（CellCount 趟保险丝）。
    /// 重力流因此**必然无环**（沿严格递减的距离场）——闭合拓扑（环面等）上列重力不合法，
    /// 它是唯一的正确答案；矩形列重力是它的特例（汇点 = 底行，但允许绕流，等同沙堆语义）。
    /// 补充仍走 RefillStep（填满所有空格，与重力方向无关）。
    /// </summary>
    public sealed class FieldGravityStep : IStep
    {
        private readonly int[] _sinks;

        /// <param name="sinks">汇点格（距离 0，棋子流向它们；至少一个，越界即抛）。</param>
        public FieldGravityStep(params int[] sinks)
        {
            if (sinks == null || sinks.Length == 0)
                throw new ArgumentException("至少一个汇点（距离场的原点）", nameof(sinks));
            _sinks = sinks;
        }

        public string Name => "FieldGravity";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            var dist = ComputeField(board);

            // origin[c] = 当前占据 c 的棋子的出发格（初始为自己；移动时随棋子走）
            var origin = new int[board.CellCount];
            for (int c = 0; c < origin.Length; c++) origin[c] = c;

            bool anyMoved = false;
            for (int pass = 0; pass < board.CellCount; pass++) // 保险丝：每趟每子至多一步
            {
                bool moved = false;
                for (int c = 0; c < board.CellCount; c++)
                {
                    if (board.GetPieceType(c) == PieceRegistry.EmptyId) continue;
                    if (dist[c] == 0) continue; // 已在汇点

                    int target = FindFlowTarget(board, dist, c);
                    if (target < 0) continue;

                    board.SetPieceType(target, board.GetPieceType(c));
                    board.SetPieceType(c, PieceRegistry.EmptyId);
                    origin[target] = origin[c];
                    moved = true;
                }
                anyMoved |= moved;
                if (!moved) break;
            }

            if (!anyMoved)
                return Task.FromResult(new StepResult { Success = false });

            // FallEvent：FromTo = [出发, 终点]（终点 = 出发格的棋子最终落点），按出发格升序
            var pairs = new List<(int from, int to)>();
            for (int c = 0; c < board.CellCount; c++)
            {
                if (origin[c] != c && board.GetPieceType(c) != PieceRegistry.EmptyId)
                    pairs.Add((origin[c], c));
            }
            pairs.Sort((a, b) => a.from.CompareTo(b.from));
            var fromTo = new int[pairs.Count * 2];
            var movedCells = new int[pairs.Count];
            for (int i = 0; i < pairs.Count; i++)
            {
                fromTo[i * 2] = pairs[i].from;
                fromTo[i * 2 + 1] = pairs[i].to;
                movedCells[i] = pairs[i].from;
            }

            return Task.FromResult(new StepResult
            {
                Success = true,
                Events = { new FallEvent(fromTo, movedCells) }
            });
        }

        /// <summary>BFS 距离场（全部边类型、无向行走；不可达格 = int.MaxValue，永不移动）。</summary>
        private int[] ComputeField(GraphBoard board)
        {
            var dist = new int[board.CellCount];
            for (int i = 0; i < dist.Length; i++) dist[i] = int.MaxValue;

            var queue = new List<int>();
            foreach (var s in _sinks)
            {
                if ((uint)s >= (uint)board.CellCount)
                    throw new ArgumentOutOfRangeException(nameof(_sinks), s, $"汇点越界 [0, {board.CellCount})");
                if (dist[s] != 0) { dist[s] = 0; queue.Add(s); }
            }
            for (int qi = 0; qi < queue.Count; qi++)
            {
                int c = queue[qi];
                for (int e = 0; e < board.EdgeTypeCount; e++)
                {
                    foreach (var n in board.NeighborsOf(c, e))
                    {
                        if (dist[n] != int.MaxValue) continue;
                        dist[n] = dist[c] + 1;
                        queue.Add(n);
                    }
                }
            }
            return dist;
        }

        /// <summary>c 的流向目标：距离严格更小且为空的邻居（CSR 边序第一个）。无则 -1。</summary>
        private static int FindFlowTarget(GraphBoard board, int[] dist, int c)
        {
            for (int e = 0; e < board.EdgeTypeCount; e++)
            {
                foreach (var n in board.NeighborsOf(c, e))
                {
                    if (dist[n] < dist[c] && board.GetPieceType(n) == PieceRegistry.EmptyId)
                        return n;
                }
            }
            return -1;
        }
    }
}
