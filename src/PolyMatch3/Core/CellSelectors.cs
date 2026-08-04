using System;
using System.Collections.Generic;

namespace PolyMatch3.Core
{
    /// <summary>
    /// BFS 半径选择器：从锚点沿指定边类型（默认全部）扩散 radius 跳内的全部格子。
    /// 锚点第 0 位，其余按 BFS 首次命中序（CSR 顺序 ⇒ 完全确定）。
    /// 注意：矩形四邻接上 radius=1 是十字（不含对角）；要 3×3 请用 RectSquareSelector。
    /// </summary>
    public sealed class RadiusSelector : ICellSelector
    {
        private readonly int _radius;
        private readonly int[] _edgeIndices; // null = 全部边类型

        public RadiusSelector(int radius, params int[] edgeIndices)
        {
            if (radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius), radius, "半径必须 ≥ 1");
            _radius = radius;
            _edgeIndices = edgeIndices != null && edgeIndices.Length > 0 ? edgeIndices : null;
        }

        public string Id => _edgeIndices == null
            ? $"radius:{_radius}"
            : $"radius:{_radius}:{string.Join("+", _edgeIndices)}";

        public List<int> Select(GraphBoard board, int anchorCellId)
        {
            var result = new List<int> { anchorCellId };
            var dist = new Dictionary<int, int> { [anchorCellId] = 0 }; // 只做查找，不遍历 ⇒ 无字典序依赖

            for (int qi = 0; qi < result.Count; qi++)
            {
                int c = result[qi];
                int d = dist[c];
                if (d >= _radius) continue;

                if (_edgeIndices == null)
                {
                    for (int e = 0; e < board.EdgeTypeCount; e++)
                        EnqueueNeighbors(board, c, e, d, result, dist);
                }
                else
                {
                    foreach (var e in _edgeIndices)
                        EnqueueNeighbors(board, c, e, d, result, dist);
                }
            }
            return result;
        }

        private static void EnqueueNeighbors(GraphBoard board, int cell, int edge, int d,
            List<int> result, Dictionary<int, int> dist)
        {
            foreach (var n in board.NeighborsOf(cell, edge))
            {
                if (dist.ContainsKey(n)) continue;
                dist[n] = d + 1;
                result.Add(n);
            }
        }
    }

    /// <summary>
    /// 直线选择器：从锚点沿每条指定边类型（每条 = 一臂）逐格走到头。
    /// 锚点第 0 位，然后按臂序逐臂伸展；每臂取 CSR 槽内第一个邻居行走（矩形每方向槽恰一个邻居）。
    /// 保险丝：走到已选格即停（环状拓扑绕回锚点时不会死循环）。
    /// </summary>
    public sealed class LineSelector : ICellSelector
    {
        private readonly int[] _edgeIndices;

        public LineSelector(params int[] edgeIndices)
        {
            if (edgeIndices == null || edgeIndices.Length == 0)
                throw new ArgumentException("至少指定一条臂（边类型索引）", nameof(edgeIndices));
            _edgeIndices = edgeIndices;
        }

        public string Id => $"line:{string.Join("+", _edgeIndices)}";

        public List<int> Select(GraphBoard board, int anchorCellId)
        {
            var result = new List<int> { anchorCellId };
            var seen = new HashSet<int> { anchorCellId };

            foreach (var e in _edgeIndices)
            {
                int cur = anchorCellId;
                while (true)
                {
                    var ns = board.NeighborsOf(cur, e);
                    if (ns.Length == 0) break;
                    int next = ns[0];
                    if (!seen.Add(next)) break; // 环/汇合 ⇒ 停
                    result.Add(next);
                    cur = next;
                }
            }
            return result;
        }
    }

    /// <summary>
    /// 矩形方块选择器（**矩形专用**，坐标系切比雪夫半径，不沿边走）：
    /// radius=1 即经典 3×3。保留它的原因：炸弹 3×3 本就是矩形玩法概念，
    /// 且为 BombEliminateStep 的默认行为提供逐格兼容。
    /// 锚点第 0 位，其余按行优先序（越界裁剪）。
    /// </summary>
    public sealed class RectSquareSelector : ICellSelector
    {
        private readonly int _radius;

        public RectSquareSelector(int radius)
        {
            if (radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius), radius, "半径必须 ≥ 1");
            _radius = radius;
        }

        public string Id => $"rect-square:{_radius}";

        public List<int> Select(GraphBoard board, int anchorCellId)
        {
            var result = new List<int> { anchorCellId };
            int w = board.Width, cx = anchorCellId % w, cy = anchorCellId / w;
            for (int dy = -_radius; dy <= _radius; dy++)
            {
                for (int dx = -_radius; dx <= _radius; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= w || y < 0 || y >= board.Height) continue;
                    result.Add(y * w + x);
                }
            }
            return result;
        }
    }
}
