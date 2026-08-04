using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PolyMatch3.Core
{
    /// <summary>
    /// 图论棋盘，纯数据容器。
    /// 棋子类型：扁平 int[]（长度 CellCount）。
    /// 邻居：CSR（压缩稀疏行）双数组——_neighborOffsets（长度 CellCount × EdgeTypeCount + 1）
    /// 与 _neighbors（全部邻居紧凑排列）。每个 (格, 边类型) 槽位可持有 0..N 个邻居
    /// （同槽位保持加入顺序），天然支持单向边与多边。
    /// 拓扑构建两阶段：AddEdge 逐条加边 → FreezeTopology 压实定型（此后拓扑只读）。
    /// </summary>
    public class GraphBoard
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int CellCount;

        /// <summary>本棋盘的边词汇表（构造时冻结）。</summary>
        public readonly EdgeTypeRegistry EdgeTypes;
        public readonly int EdgeTypeCount;

        /// <summary>全棋盘棋子类型，一段连续内存。0=空，1~N=颜色。</summary>
        private readonly int[] _pieceTypes;

        // 拓扑构建期：每 (格,边) 槽的邻居列表；冻结后释放
        private List<int>[] _buildingEdges;

        // 冻结后的 CSR 拓扑
        private int[] _neighborOffsets;
        private int[] _neighbors;

        /// <summary>拓扑是否已冻结（冻结后方可匹配）。</summary>
        public bool IsTopologyFrozen { get; private set; }

        public GraphBoard(int width, int height, EdgeTypeRegistry edgeTypes)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), width, "宽度必须 ≥ 1");
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), height, "高度必须 ≥ 1");
            if (edgeTypes == null) throw new ArgumentNullException(nameof(edgeTypes));
            if (edgeTypes.Count == 0) throw new ArgumentException("注册表为空：棋盘至少需要一种边类型", nameof(edgeTypes));

            // 溢出守卫：int 乘法在 width×height 或 ×EdgeTypeCount 超过 int 上限时会回绕，
            // 静默产出与 Width/Height 不自洽的棋盘（如 int.MaxValue² 回绕成 1 格），必须启动期拦截
            long cellCountLong = (long)width * height;
            long slotsLong = cellCountLong * edgeTypes.Count;
            if (cellCountLong > int.MaxValue || slotsLong >= int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(width),
                    $"棋盘过大：{width}×{height}（{cellCountLong} 格 × {edgeTypes.Count} 种边）的 CSR 槽位数 {slotsLong} 超出 int 索引上限，请缩小棋盘尺寸或减少边类型数量");

            Width = width;
            Height = height;
            EdgeTypes = edgeTypes;
            EdgeTypeCount = edgeTypes.Count;
            CellCount = (int)cellCountLong;

            // 棋盘创建即定型边词汇表
            edgeTypes.Freeze();

            _pieceTypes = new int[CellCount];
            _buildingEdges = new List<int>[CellCount * EdgeTypeCount];
        }

        /// <summary>
        /// 逐条加边（拓扑构建期）。同一 (格, 边类型) 槽可多次调用形成多边，顺序保留。
        /// 单向边 = 只加 from→to 一侧；双向边 = 两侧各加一次。
        /// </summary>
        public void AddEdge(int fromCell, int edgeIndex, int toCell)
        {
            if (IsTopologyFrozen)
                throw new InvalidOperationException("拓扑已冻结，不能再加边");
            if ((uint)fromCell >= (uint)CellCount)
                throw new ArgumentOutOfRangeException(nameof(fromCell), fromCell, $"合法范围 [0, {CellCount})");
            if ((uint)edgeIndex >= (uint)EdgeTypeCount)
                throw new ArgumentOutOfRangeException(nameof(edgeIndex), edgeIndex, $"合法范围 [0, {EdgeTypeCount})");
            if ((uint)toCell >= (uint)CellCount)
                throw new ArgumentOutOfRangeException(nameof(toCell), toCell, $"合法范围 [0, {CellCount})");

            (_buildingEdges[fromCell * EdgeTypeCount + edgeIndex] ??= new List<int>()).Add(toCell);
        }

        /// <summary>
        /// 把构建期边表压实为 CSR 布局，此后拓扑只读。重复调用幂等。
        /// </summary>
        public void FreezeTopology()
        {
            if (IsTopologyFrozen) return;

            int slots = CellCount * EdgeTypeCount;
            _neighborOffsets = new int[slots + 1];
            int total = 0;
            for (int i = 0; i < slots; i++)
            {
                _neighborOffsets[i] = total;
                total += _buildingEdges[i]?.Count ?? 0;
            }
            _neighborOffsets[slots] = total;

            _neighbors = new int[total];
            for (int i = 0; i < slots; i++)
            {
                var list = _buildingEdges[i];
                if (list != null) list.CopyTo(_neighbors, _neighborOffsets[i]);
            }

            _buildingEdges = null;
            IsTopologyFrozen = true;
        }

        /// <summary>
        /// 建立矩形网格的双向邻居（每对相邻格各加两条反向边）。
        /// 默认索引与 <see cref="EdgeTypeRegistry.CreateRect"/> 的注册顺序一致；
        /// 使用自定义注册表时请显式传入四个方向的索引。
        /// </summary>
        public void BuildRectNeighbors(int up = 0, int down = 1, int left = 2, int right = 3)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int id = y * Width + x;
                    if (y > 0) AddEdge(id, up, id - Width);
                    if (y < Height - 1) AddEdge(id, down, id + Width);
                    if (x > 0) AddEdge(id, left, id - 1);
                    if (x < Width - 1) AddEdge(id, right, id + 1);
                }
            }
        }

        /// <summary>O(1) 棋子类型读取（热路径，内联友好）。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetPieceType(int cellId)
        {
            return _pieceTypes[cellId];
        }

        /// <summary>棋子类型写入。0=空，1~N=颜色。</summary>
        public void SetPieceType(int cellId, int pieceType)
        {
            _pieceTypes[cellId] = pieceType;
        }

        /// <summary>全棋盘棋子类型的只读连续视图（长度 CellCount）。写入走 SetPieceType。</summary>
        public ReadOnlySpan<int> PieceTypes => _pieceTypes;

        /// <summary>
        /// CSR 偏移表只读视图（长度 CellCount × EdgeTypeCount + 1）。
        /// 槽位 slot = cellId × EdgeTypeCount + edgeIndex 的邻居区间 = [offsets[slot], offsets[slot+1])。
        /// </summary>
        public ReadOnlySpan<int> NeighborOffsets => _neighborOffsets;

        /// <summary>CSR 邻居表只读视图（全部邻居紧凑排列，同槽位保持加入顺序）。</summary>
        public ReadOnlySpan<int> Neighbors => _neighbors;

        /// <summary>
        /// 指定 (格, 边类型) 槽的邻居只读视图（热路径，区域选择器/重力等沿边行走工具的入口）。
        /// 依赖 CSR 布局，拓扑未冻结即抛（与匹配器的冻结检查同一纪律）。
        /// </summary>
        public ReadOnlySpan<int> NeighborsOf(int cellId, int edgeIndex)
        {
            if (!IsTopologyFrozen)
                throw new InvalidOperationException("拓扑未冻结：NeighborsOf 依赖 CSR 布局，FreezeTopology 之后才可读取");
            int slot = cellId * EdgeTypeCount + edgeIndex;
            int begin = _neighborOffsets[slot];
            return _neighbors.AsSpan(begin, _neighborOffsets[slot + 1] - begin);
        }
    }
}
