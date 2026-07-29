using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Logging;

namespace PolyMatch3.Matcher
{
    /// <summary>
    /// 固定图案匹配器（参考实现）。
    /// 以每个格子为锚点，按"变体（OR）× 臂（AND）"探测：任一变体的全部臂命中即产生一个 MatchGroup。
    /// 臂沿有向边行走（单边/多边均可）；同类型臂在同一变体内必须绑定不同邻居。
    /// 探测为有界回溯（确定性：按 CSR 顺序取第一个见证），失败路径零堆分配。
    /// 并行按"棋盘规模 × 图案数"与硬件自动决策；结果与串行逐字节一致。
    /// </summary>
    public sealed class FixedPatternMatcher : IMatcher
    {
        /// <summary>
        /// 栈上探测缓冲容量：锚点（1 格）+ 单变体总步数不得超过此值。
        /// 32 足以覆盖常规三消图案。构造时按变体逐一校验，超限在启动期即报错。
        /// </summary>
        public const int ProbeBufferSize = 32;

        /// <summary>
        /// 默认并行工作量阈值（格子数 × 图案数）。低于此值并行调度开销大于收益，自动回退串行。
        /// </summary>
        public const int DefaultParallelWorkThreshold = 2048;

        private readonly Pattern[] _patterns;
        private readonly bool[][] _variantHasDupEdges;
        private readonly bool _parallel;
        private readonly int _maxDegreeOfParallelism;
        private readonly int _parallelWorkThreshold;

        /// <param name="patterns">注册图案集（设计期固定）。构造时做全量合法性校验并防御性拷贝数组；构造后不得再修改 Pattern 实例（变体/优先级等内容），否则预计算与校验失效。</param>
        /// <param name="parallel">是否允许并行（小规模棋盘仍会自动回退串行）。</param>
        /// <param name="maxDegreeOfParallelism">最大并行度。≤0 表示自动（CPU 逻辑核数）。移动端建议手动调低留核。</param>
        /// <param name="parallelWorkThreshold">并行工作量阈值。≤0 表示使用 <see cref="DefaultParallelWorkThreshold"/>；传 1 可强制并行（基准测试用）。</param>
        public FixedPatternMatcher(Pattern[] patterns, bool parallel = true, int maxDegreeOfParallelism = 0, int parallelWorkThreshold = 0)
        {
            if (patterns == null) throw new ArgumentNullException(nameof(patterns));
            ValidatePatterns(patterns);

            _patterns = (Pattern[])patterns.Clone();
            _variantHasDupEdges = ComputeDupEdgeFlags(_patterns);
#if UNITY_WEBGL || BROWSER_WASM
            // WebGL / 浏览器 WASM 平台无线程支持，并行只会白付调度与分配开销，强制串行
            _parallel = false;
#else
            _parallel = parallel;
#endif
            _maxDegreeOfParallelism = maxDegreeOfParallelism;
            _parallelWorkThreshold = parallelWorkThreshold > 0 ? parallelWorkThreshold : DefaultParallelWorkThreshold;

            Log.Debug("Matcher", $"FixedPatternMatcher 初始化：图案数={patterns.Length}，并行={_parallel}，最大并行度={(_maxDegreeOfParallelism > 0 ? _maxDegreeOfParallelism.ToString() : $"自动({Environment.ProcessorCount})")}，工作量阈值={_parallelWorkThreshold}");
        }

        public List<MatchGroup> Match(GraphBoard board)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (!board.IsTopologyFrozen)
                throw new InvalidOperationException("棋盘拓扑未冻结：加边完成后请先调用 FreezeTopology()");

            if (!_parallel || board.CellCount < 2)
                return MatchSequential(board);

            // 智能调度：工作量（格子数 × 图案数）低于阈值时，并行调度开销大于收益，回退串行
            long work = (long)board.CellCount * _patterns.Length;
            return work >= _parallelWorkThreshold ? MatchParallel(board) : MatchSequential(board);
        }

        private List<MatchGroup> MatchSequential(GraphBoard board)
        {
            var results = new List<MatchGroup>();
            var cellCount = board.CellCount;
            ReadOnlySpan<int> pieceTypes = board.PieceTypes;
            ReadOnlySpan<int> offsets = board.NeighborOffsets;
            ReadOnlySpan<int> neighbors = board.Neighbors;
            int stride = board.EdgeTypeCount;

            for (int anchorId = 0; anchorId < cellCount; anchorId++)
            {
                TryMatchAnchor(anchorId, pieceTypes, offsets, neighbors, stride, results);
            }

            return results;
        }

        /// <summary>
        /// 并行匹配（分块策略）。
        /// 块数 = min(最大并行度, 格子数)，每块至少 1 格；余数均摊，任意两块最多相差 1 格。
        /// 每个线程处理一个连续块，维护独立的结果列表，按块号升序合并——
        /// 结果顺序与串行完全一致（跨机器确定，可复现）。
        /// </summary>
        private List<MatchGroup> MatchParallel(GraphBoard board)
        {
            var cellCount = board.CellCount;
            var maxDegree = _maxDegreeOfParallelism > 0 ? _maxDegreeOfParallelism : Environment.ProcessorCount;
            var chunkCount = Math.Min(maxDegree, cellCount);

            var baseSize = cellCount / chunkCount;
            var remainder = cellCount % chunkCount;

            var localResults = new List<MatchGroup>[chunkCount];
            var options = new ParallelOptions { MaxDegreeOfParallelism = chunkCount };

            Parallel.For(0, chunkCount, options, t =>
            {
                // ReadOnlySpan 是 ref struct，不能被 lambda 捕获；在工作体内重新获取（零成本：引用+长度）
                ReadOnlySpan<int> pieceTypes = board.PieceTypes;
                ReadOnlySpan<int> offsets = board.NeighborOffsets;
                ReadOnlySpan<int> neighbors = board.Neighbors;
                int stride = board.EdgeTypeCount;

                var start = t * baseSize + Math.Min(t, remainder);
                var end = start + baseSize + (t < remainder ? 1 : 0);

                var local = new List<MatchGroup>();
                for (int anchorId = start; anchorId < end; anchorId++)
                {
                    TryMatchAnchor(anchorId, pieceTypes, offsets, neighbors, stride, local);
                }

                localResults[t] = local;
            });

            // 按块号升序合并（每个块号必然已被赋值）
            var results = new List<MatchGroup>();
            foreach (var local in localResults)
            {
                results.AddRange(local);
            }

            return results;
        }

        /// <summary>
        /// 以指定锚点尝试所有图案的所有变体。
        /// 快路径（无同型臂的变体）内联直走，仅多候选分支或含同型臂时才进入回溯（均为罕见情形）。
        /// 命中一个变体产出一个 MatchGroup（CellIds 去重、锚点首位、含变体下标）。
        /// </summary>
        private void TryMatchAnchor(int anchorId, ReadOnlySpan<int> pieceTypes, ReadOnlySpan<int> offsets, ReadOnlySpan<int> neighbors, int stride, List<MatchGroup> results)
        {
            int targetType = pieceTypes[anchorId];

            // 空格子不参与匹配
            if (targetType == 0) return;

            Span<int> tempBuffer = stackalloc int[ProbeBufferSize];

            for (int p = 0; p < _patterns.Length; p++)
            {
                var pattern = _patterns[p];
                var variants = pattern.Variants;
                var dupFlags = _variantHasDupEdges[p];

                for (int v = 0; v < variants.Length; v++)
                {
                    var arms = variants[v];
                    tempBuffer[0] = anchorId;
                    int count = 1;
                    bool matched;

                    if (!dupFlags[v])
                    {
                        // 快路径（内联）：假定全程单候选，零函数调用
                        // state: 1=继续/成功 0=确定失败 -1=遇多候选，转回溯
                        int state = 1;
                        for (int i = 0; i < arms.Length && state == 1; i++)
                        {
                            int edge = arms[i].edge;
                            int current = anchorId;
                            int armStart = count; // 本臂已走格子在 tempBuffer 中的起点
                            for (int s = 0; s < arms[i].steps; s++)
                            {
                                int slot = current * stride + edge;
                                int start = offsets[slot];
                                int slotCount = offsets[slot + 1] - start;
                                if (slotCount == 0) { state = 0; break; }
                                if (slotCount > 1) { state = -1; break; }
                                int next = neighbors[start];
                                if (pieceTypes[next] != targetType) { state = 0; break; }
                                // 臂链简单路径约束（与慢路径 WalkChain 一致）：不回到锚点、不与本臂已走格子重复。
                                // 规则拓扑下单候选链天然不会折返，但手工单向拓扑（环/自环）必须拦。
                                if (next == anchorId) { state = 0; break; }
                                bool revisited = false;
                                for (int j = armStart; j < count; j++)
                                {
                                    if (tempBuffer[j] == next) { revisited = true; break; }
                                }
                                if (revisited) { state = 0; break; }
                                tempBuffer[count++] = next;
                                current = next;
                            }
                        }

                        matched = state == 1 || (state == -1 && SlowBindVariant(anchorId, arms, pieceTypes, offsets, neighbors, stride, targetType, tempBuffer, ref count));
                    }
                    else
                    {
                        // 含同型臂的变体：直接完整回溯（需绑定去重）
                        matched = SlowBindVariant(anchorId, arms, pieceTypes, offsets, neighbors, stride, targetType, tempBuffer, ref count);
                    }

                    if (!matched) continue;

                    // 命中：格子去重（锚点固定第 0 位，其余按首次命中顺序）
                    var cellIds = new List<int>(count) { anchorId };
                    for (int i = 1; i < count; i++)
                    {
                        int c = tempBuffer[i];
                        if (c == anchorId) continue;
                        bool seen = false;
                        for (int j = 1; j < cellIds.Count; j++)
                        {
                            if (cellIds[j] == c) { seen = true; break; }
                        }
                        if (!seen) cellIds.Add(c);
                    }

                    results.Add(new MatchGroup
                    {
                        AnchorId = anchorId,
                        CellIds = cellIds,
                        PatternId = pattern.Id,
                        Priority = pattern.Priority,
                        VariantIndex = v
                    });
                }
            }
        }

        /// <summary>
        /// 慢路径：多候选分支或含同型臂时的完整回溯。成功时 tempBuffer 中格子数写入 count（含锚点）。
        /// 确定性：按 CSR 邻居顺序枚举，返回第一个见证。
        /// </summary>
        private static bool SlowBindVariant(int anchorId, (int edge, int steps)[] arms,
            ReadOnlySpan<int> pieceTypes, ReadOnlySpan<int> offsets, ReadOnlySpan<int> neighbors, int stride,
            int targetType, Span<int> tempBuffer, ref int count)
        {
            count = 1;
            Span<int> armFirstStep = stackalloc int[ProbeBufferSize];
            return BindArm(0, arms, anchorId, pieceTypes, offsets, neighbors, stride, targetType, tempBuffer, ref count, armFirstStep);
        }

        /// <summary>
        /// 构造期预计算：每个变体是否含同类型臂（决定快路径是否需要绑定去重）。
        /// </summary>
        private static bool[][] ComputeDupEdgeFlags(Pattern[] patterns)
        {
            var flags = new bool[patterns.Length][];
            for (int p = 0; p < patterns.Length; p++)
            {
                var variants = patterns[p].Variants;
                var patternFlags = new bool[variants.Length];
                for (int v = 0; v < variants.Length; v++)
                {
                    var arms = variants[v];
                    for (int i = 0; i < arms.Length && !patternFlags[v]; i++)
                    {
                        for (int j = i + 1; j < arms.Length; j++)
                        {
                            if (arms[i].edge == arms[j].edge) { patternFlags[v] = true; break; }
                        }
                    }
                }
                flags[p] = patternFlags;
            }
            return flags;
        }

        /// <summary>
        /// 绑定第 armIndex 臂及其后续臂（回溯）。同类型臂必须绑定不同的第一步邻居。
        /// </summary>
        private static bool BindArm(int armIndex, (int edge, int steps)[] arms, int anchorId,
            ReadOnlySpan<int> pieceTypes, ReadOnlySpan<int> offsets, ReadOnlySpan<int> neighbors, int stride,
            int targetType, Span<int> tempBuffer, ref int count, Span<int> armFirstStep)
        {
            if (armIndex == arms.Length) return true;

            var (edge, steps) = arms[armIndex];
            int slot = anchorId * stride + edge;
            int end = offsets[slot + 1];

            for (int idx = offsets[slot]; idx < end; idx++)
            {
                int first = neighbors[idx];
                if (pieceTypes[first] != targetType) continue;
                if (first == anchorId) continue; // 简单路径约束：链不回到锚点（拦自环首步，与 WalkChain 一致）

                // 同类型臂去重：本变体中前面同 edge 的臂已绑定的第一步，不能再绑定
                bool used = false;
                for (int j = 0; j < armIndex; j++)
                {
                    if (arms[j].edge == edge && armFirstStep[j] == first) { used = true; break; }
                }
                if (used) continue;

                // 写入本臂：第一步 + 链式剩余 steps-1 步
                int entry = count;
                armFirstStep[armIndex] = first;
                tempBuffer[count] = first;
                int pos = count + 1;

                bool chainOk = steps == 1
                    || WalkChain(first, edge, steps - 1, pieceTypes, offsets, neighbors, stride, targetType, tempBuffer, ref pos, anchorId, entry);

                if (chainOk)
                {
                    count = pos;
                    if (BindArm(armIndex + 1, arms, anchorId, pieceTypes, offsets, neighbors, stride, targetType, tempBuffer, ref count, armFirstStep))
                        return true;
                }

                // 失败回退
                count = entry;
                armFirstStep[armIndex] = -1;
            }

            return false;
        }

        /// <summary>
        /// 沿 edge 从 fromCell 继续走 remaining 步（每步可分支），路径追加到 tempBuffer（count 处起）。
        /// 简单路径约束：不回到锚点、不与本臂已走格子重复——杜绝 A→B→A 折返凑数（单边型棋盘的路径匹配依赖此约束）；
        /// 跨臂共享格子不受影响（“双线对”仍合法）。
        /// </summary>
        private static bool WalkChain(int fromCell, int edge, int remaining,
            ReadOnlySpan<int> pieceTypes, ReadOnlySpan<int> offsets, ReadOnlySpan<int> neighbors, int stride,
            int targetType, Span<int> tempBuffer, ref int count, int anchorId, int armStart)
        {
            if (remaining == 0) return true;

            int slot = fromCell * stride + edge;
            int end = offsets[slot + 1];

            for (int idx = offsets[slot]; idx < end; idx++)
            {
                int next = neighbors[idx];
                if (pieceTypes[next] != targetType) continue;
                if (next == anchorId) continue; // 不回到锚点
                bool revisited = false;         // 不与本臂已走格子重复
                for (int j = armStart; j < count; j++)
                {
                    if (tempBuffer[j] == next) { revisited = true; break; }
                }
                if (revisited) continue;

                int saved = count;
                tempBuffer[count++] = next;
                if (WalkChain(next, edge, remaining - 1, pieceTypes, offsets, neighbors, stride, targetType, tempBuffer, ref count, anchorId, armStart))
                    return true;
                count = saved;
            }

            return false;
        }

        /// <summary>
        /// 注册期全量校验：图案在设计结束后即固定，任何非法配置都应在启动时暴露，而非运行期崩溃。
        /// </summary>
        private static void ValidatePatterns(Pattern[] patterns)
        {
            var seenIds = new HashSet<string>();
            var seenPriorities = new HashSet<int>();

            foreach (var pattern in patterns)
            {
                if (pattern == null)
                    throw new ArgumentException("图案数组包含 null 元素", nameof(patterns));
                if (string.IsNullOrEmpty(pattern.Id))
                    throw new ArgumentException("存在未命名（Id 为空）的图案", nameof(patterns));
                if (!seenIds.Add(pattern.Id))
                    throw new ArgumentException($"图案 Id 重复：{pattern.Id}", nameof(patterns));
                if (!seenPriorities.Add(pattern.Priority))
                    throw new ArgumentException($"图案优先级重复：{pattern.Priority}（图案 {pattern.Id}）。仲裁模型要求全局唯一优先级", nameof(patterns));
                if (pattern.Variants == null || pattern.Variants.Length == 0)
                    throw new ArgumentException($"图案 {pattern.Id} 未定义任何变体", nameof(patterns));

                for (int v = 0; v < pattern.Variants.Length; v++)
                {
                    var arms = pattern.Variants[v];
                    if (arms == null || arms.Length == 0)
                        throw new ArgumentException($"图案 {pattern.Id} 的变体[{v}] 未定义任何臂", nameof(patterns));

                    int totalSteps = 0;
                    foreach (var (edge, steps) in arms)
                    {
                        if (steps < 1)
                            throw new ArgumentException($"图案 {pattern.Id} 变体[{v}] 存在非法步数 {steps}（边索引 {edge}），步数必须 ≥ 1", nameof(patterns));
                        if (edge < 0)
                            throw new ArgumentException($"图案 {pattern.Id} 变体[{v}] 存在非法边索引 {edge}，必须 ≥ 0", nameof(patterns));
                        totalSteps += steps;
                    }

                    if (totalSteps + 1 > ProbeBufferSize)
                        throw new ArgumentException(
                            $"图案 {pattern.Id} 变体[{v}] 总步数 {totalSteps}（+锚点 1 格）超出探测缓冲容量 {ProbeBufferSize}。请简化图案，或调高 ProbeBufferSize。",
                            nameof(patterns));
                }
            }
        }
    }
}
