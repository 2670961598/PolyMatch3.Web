using System;
using PolyMatch3.Core;
using PolyMatch3.Logging;
using PolyMatch3.Matcher;

namespace PolyMatch3.Diagnostics
{
    /// <summary>
    /// 统一配置校验：开局前调用一次，把"运行期才爆"的配置错误提前到启动期。
    /// 校验边界原则：只拦"会导致程序崩溃/未定义行为"与"与框架模型根本不兼容"的问题；
    /// 不拦设计异味（不对称拓扑、孤立格、图案重叠均为合法设计）。
    /// 分层：图案自身合法性由 FixedPatternMatcher 构造时校验；
    /// 棋盘自身与"图案 × 棋盘"一致性由本类校验。
    /// </summary>
    public static class BoardValidator
    {
        /// <summary>
        /// 校验棋盘 CSR 拓扑：必须已冻结；偏移表单调不减且首尾闭合；邻居值 ∈ [0, CellCount)。
        /// 正常路径下非法值已被 AddEdge 在写入当场拦截，本方法为内部安全网。
        /// </summary>
        public static void ValidateBoard(GraphBoard board)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (!board.IsTopologyFrozen)
                throw new InvalidOperationException("棋盘拓扑未冻结：加边完成后请先调用 FreezeTopology()");

            ReadOnlySpan<int> offsets = board.NeighborOffsets;
            ReadOnlySpan<int> neighbors = board.Neighbors;
            int slots = board.CellCount * board.EdgeTypeCount;

            if (offsets.Length != slots + 1)
                throw new InvalidOperationException($"偏移表长度 {offsets.Length} 与期望 {slots + 1}（CellCount×EdgeTypeCount+1）不一致");
            if (offsets[0] != 0)
                throw new InvalidOperationException("偏移表首元素必须为 0");
            if (offsets[slots] != neighbors.Length)
                throw new InvalidOperationException($"偏移表末元素 {offsets[slots]} 与邻居表长度 {neighbors.Length} 不闭合");

            for (int i = 0; i < slots; i++)
            {
                if (offsets[i + 1] < offsets[i])
                    throw new InvalidOperationException($"偏移表在槽位 {i} 处非单调：offsets[{i}]={offsets[i]} > offsets[{i + 1}]={offsets[i + 1]}");
            }

            for (int i = 0; i < neighbors.Length; i++)
            {
                int n = neighbors[i];
                if (n < 0 || n >= board.CellCount)
                    throw new InvalidOperationException($"邻居表[{i}] 值 {n} 非法（合法范围 [0, {board.CellCount})）");
            }
        }

        /// <summary>
        /// 校验"图案 × 棋盘"一致性：图案使用的边索引在棋盘边类型范围内。
        /// （图案自身合法性——Id/优先级唯一、变体非空、步数 ≥1、缓冲容量——由 FixedPatternMatcher 构造时校验。）
        /// </summary>
        public static void ValidatePatterns(GraphBoard board, Pattern[] patterns)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (patterns == null) throw new ArgumentNullException(nameof(patterns));

            foreach (var p in patterns)
            {
                if (p == null)
                    throw new ArgumentException("图案数组包含 null 元素", nameof(patterns));

                for (int v = 0; v < p.Variants.Length; v++)
                {
                    foreach (var (edge, _) in p.Variants[v])
                    {
                        if (edge < 0 || edge >= board.EdgeTypeCount)
                        {
                            throw new InvalidOperationException(
                                $"图案 {p.Id} 变体[{v}] 使用了边索引 {edge}，超出棋盘边类型范围 [0, {board.EdgeTypeCount})（注册表：{string.Join(", ", GetEdgeNames(board))}）");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 完整校验（棋盘 + 图案）。建议在游戏初始化完成、开局前调用一次。
        /// </summary>
        public static void Validate(GraphBoard board, Pattern[] patterns)
        {
            ValidateBoard(board);
            ValidatePatterns(board, patterns);
            Log.Info("Validator", $"配置校验通过：{board.Width}x{board.Height}（{board.CellCount} 格，{board.EdgeTypeCount} 种边），图案数={patterns.Length}");
        }

        private static string[] GetEdgeNames(GraphBoard board)
        {
            var names = new string[board.EdgeTypes.Count];
            for (int i = 0; i < names.Length; i++) names[i] = board.EdgeTypes.GetName(i);
            return names;
        }
    }
}
