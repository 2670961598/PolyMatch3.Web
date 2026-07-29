using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Step;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 显式路径重力：重力方向不由边类型决定，而由外部给定的**有序有向边列表**决定。
    /// 列表允许重复、允许汇聚、允许任意形状——经典"向下"只是边列表的一种生成方式，
    /// 向中间缩（每行两条链指向中央）、黑洞地形（一切指向黑洞格）都只是配置。
    /// 语义（完全确定）：每趟按格 id 升序扫描，非空格 u 沿其出边（保持给定顺序）找到第一个
    /// "目标为空"的 (u→v) 移动一步；迭代到不动点；趟数超 CellCount 即抛（边图含环）。
    /// 可重载：移动合法性 / 移动回调 / 事件构造 都是接缝（模板方法）。
    /// </summary>
    public class PathGravityStep : IStep
    {
        // 按 from 分桶的目标格列表（桶内保持给定顺序 = 先到先得）
        private readonly List<int>[] _outEdges;

        /// <param name="edges">有序有向边 (from, to)：同格多条出边时按给定顺序优先。越界/自环即抛。</param>
        public PathGravityStep(int cellCount, IEnumerable<(int from, int to)> edges)
        {
            if (cellCount <= 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
            if (edges == null) throw new ArgumentNullException(nameof(edges));

            _outEdges = new List<int>[cellCount];
            foreach (var (from, to) in edges)
            {
                if ((uint)from >= (uint)cellCount)
                    throw new ArgumentOutOfRangeException(nameof(edges), $"边起点 {from} 越界（合法范围 [0, {cellCount})）");
                if ((uint)to >= (uint)cellCount)
                    throw new ArgumentOutOfRangeException(nameof(edges), $"边终点 {to} 越界（合法范围 [0, {cellCount})）");
                if (from == to)
                    throw new ArgumentException($"重力边不允许自环：({from} → {to})", nameof(edges));
                (_outEdges[from] ??= new List<int>()).Add(to);
            }
        }

        public virtual string Name => "PathGravity";
        public virtual StepAttributes Attributes => new StepAttributes();

        public virtual Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            if (!board.IsTopologyFrozen)
                throw new InvalidOperationException("棋盘拓扑未冻结：加边完成后请先调用 FreezeTopology()");
            if (board.CellCount != _outEdges.Length)
                throw new InvalidOperationException($"重力边表覆盖 {_outEdges.Length} 格，与棋盘 {board.CellCount} 格不一致");

            int cellCount = board.CellCount;

            // origin[c]：当前位于 c 的棋子最初来自哪格
            var origin = new int[cellCount];
            for (int i = 0; i < cellCount; i++) origin[i] = i;

            bool anyMoved = false;
            bool moved = true;
            int passes = 0;
            while (moved)
            {
                if (++passes > cellCount)
                    throw new InvalidOperationException("PathGravityStep 超过趟数上限：重力边图疑似含环（棋子沿环永远追赶空格）");
                moved = false;
                for (int u = 0; u < cellCount; u++)
                {
                    if (board.GetPieceType(u) == PieceRegistry.EmptyId) continue;
                    var outs = _outEdges[u];
                    if (outs == null) continue;

                    for (int i = 0; i < outs.Count; i++)
                    {
                        int v = outs[i];
                        if (!CanMoveInto(board, u, v)) continue;

                        board.SetPieceType(v, board.GetPieceType(u));
                        board.SetPieceType(u, PieceRegistry.EmptyId);
                        origin[v] = origin[u];
                        origin[u] = u;
                        OnPieceMoved(board, u, v, ctx);
                        moved = true;
                        anyMoved = true;
                        break; // 每趟每子只走一步
                    }
                }
            }

            if (!anyMoved)
                return Task.FromResult(new StepResult { Success = false });

            var fromTo = new List<int>();
            var cells = new List<int>();
            for (int c = 0; c < cellCount; c++)
            {
                if (origin[c] != c && board.GetPieceType(c) != PieceRegistry.EmptyId)
                {
                    fromTo.Add(origin[c]);
                    fromTo.Add(c);
                    cells.Add(c);
                }
            }

            return Task.FromResult(new StepResult
            {
                Success = true,
                Events = { CreateFallEvent(fromTo.ToArray(), cells.ToArray()) }
            });
        }

        /// <summary>接缝①：一次移动是否合法（默认：目标格为空）。汇聚决胜 = 出边顺序 + 先到先得。</summary>
        protected virtual bool CanMoveInto(GraphBoard board, int from, int to)
        {
            return board.GetPieceType(to) == PieceRegistry.EmptyId;
        }

        /// <summary>接缝②：一次移动发生时回调（默认空；可在此同步玩法的平行数据层）。</summary>
        protected virtual void OnPieceMoved(GraphBoard board, int from, int to, StepContext ctx) { }

        /// <summary>接缝③：下落事件构造（默认 FallEvent，玩法可继承加料）。</summary>
        protected virtual GameEvent CreateFallEvent(int[] fromTo, int[] cells)
        {
            return new FallEvent(fromTo, cells);
        }

        /// <summary>矩形棋盘经典"向下"的边列表（每列自下而上指）。</summary>
        public static IEnumerable<(int from, int to)> BuildColumnEdges(int width, int height)
        {
            for (int y = 0; y < height - 1; y++)
                for (int x = 0; x < width; x++)
                    yield return (y * width + x, (y + 1) * width + x);
        }

        /// <summary>矩形棋盘"向水平中线汇聚"的边列表（左半向右、右半向左）。</summary>
        public static IEnumerable<(int from, int to)> BuildConvergeToCenterEdges(int width, int height)
        {
            int mid = width / 2;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < mid; x++) yield return (y * width + x, y * width + x + 1);
                for (int x = width - 1; x > mid; x--) yield return (y * width + x, y * width + x - 1);
            }
        }
    }
}
