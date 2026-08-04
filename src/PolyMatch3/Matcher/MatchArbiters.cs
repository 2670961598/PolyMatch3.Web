using System;
using System.Collections.Generic;
using PolyMatch3.Core;

namespace PolyMatch3.Matcher
{
    /// <summary>
    /// 仲裁器注册表：Id → IMatchArbiter 实例。内置 none / containment / overlap 已预注册；
    /// 玩法自定义仲裁器 Register 后即可按 Id 解析（配置/编辑器序列化只存 Id 字符串）。
    /// 注册表只增不改：重复 Id 直接抛异常（静默覆盖会让同一 Id 在不同机器上行为不同，破坏确定性）。
    /// </summary>
    public static class MatchArbiters
    {
        /// <summary>不去重：原始全量组原样通过。</summary>
        public static readonly IMatchArbiter None = new NoneArbiter();

        /// <summary>覆盖去重：格子集合被更高/同优先级组完全包含才压制，部分重叠共存（仲裁 v2 规则）。</summary>
        public static readonly IMatchArbiter Containment = new ContainmentArbiter();

        /// <summary>交叉去重：与已接收组有任意格子重合即压制（赢家通吃，存活组两两不相交）。</summary>
        public static readonly IMatchArbiter Overlap = new OverlapSuppressArbiter();

        private static readonly Dictionary<string, IMatchArbiter> ById = new Dictionary<string, IMatchArbiter>();

        static MatchArbiters()
        {
            Register(None);
            Register(Containment);
            Register(Overlap);
        }

        /// <summary>注册仲裁器。Id 为空或重复注册直接抛异常。</summary>
        public static void Register(IMatchArbiter arbiter)
        {
            if (arbiter == null) throw new ArgumentNullException(nameof(arbiter));
            if (string.IsNullOrEmpty(arbiter.Id))
                throw new ArgumentException("仲裁器 Id 不能为空：注册表以 Id 为键，空 Id 无法被配置/编辑器引用", nameof(arbiter));
            if (ById.TryGetValue(arbiter.Id, out var existing) && !ReferenceEquals(existing, arbiter))
                throw new ArgumentException($"仲裁器 Id 重复注册：\"{arbiter.Id}\"（{existing.GetType().Name} vs {arbiter.GetType().Name}）。同一 Id 两种实现会破坏确定性", nameof(arbiter));
            ById[arbiter.Id] = arbiter;
        }

        /// <summary>按 Id 解析。未注册抛 KeyNotFoundException 并列出可用 Id。</summary>
        public static IMatchArbiter Get(string id)
        {
            if (TryGet(id, out var arbiter)) return arbiter;
            throw new KeyNotFoundException($"未注册的仲裁器 Id：\"{id}\"。可用：{string.Join(", ", ById.Keys)}");
        }

        public static bool TryGet(string id, out IMatchArbiter arbiter)
        {
            arbiter = null;
            return id != null && ById.TryGetValue(id, out arbiter);
        }

        /// <summary>已注册的全部 Id（编辑器下拉框数据源）。</summary>
        public static IEnumerable<string> RegisteredIds => ById.Keys;

        /// <summary>确定性全序：优先级降序，同优先级按输入下标升序。返回输入下标序列。</summary>
        internal static int[] SortedOrder(IReadOnlyList<MatchGroup> groups)
        {
            var order = new int[groups.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int c = groups[b].Priority.CompareTo(groups[a].Priority);
                return c != 0 ? c : a.CompareTo(b);
            });
            return order;
        }
    }

    /// <summary>不去重：原始全量组原样通过（返回拷贝，不修改输入）。消哪些、怎么消全由玩法决定。</summary>
    public sealed class NoneArbiter : IMatchArbiter
    {
        public string Id => "none";

        public List<MatchGroup> Arbitrate(GraphBoard board, IReadOnlyList<MatchGroup> raw)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            return new List<MatchGroup>(raw);
        }
    }

    /// <summary>
    /// 覆盖去重（仲裁 v2 规则）：
    ///   1. 图案优先级全局唯一；
    ///   2. **完全包含才压制**：一组的格子集合被已接收（更高或同优先级）组完全包含时，整组丢弃；
    ///      仅仅是部分重叠 → 两组共存，一起消除（如四连末端拐个弯带个三连：四连三连都消）；
    ///   3. 同优先级组格子集合完全相同（如同一直线的两个锚位变体）→ 后到的被先到的包含，丢弃。
    /// 输出顺序：优先级降序，同优先级保持输入顺序（完全确定）。
    /// </summary>
    public sealed class ContainmentArbiter : IMatchArbiter
    {
        public string Id => "containment";

        public List<MatchGroup> Arbitrate(GraphBoard board, IReadOnlyList<MatchGroup> raw)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (raw.Count <= 1) return new List<MatchGroup>(raw);

            var order = MatchArbiters.SortedOrder(raw);

            // 贪心接收：仅当候选组的格子集合被某个已接收组**完全包含**时才丢弃。
            // 已接收组优先级必然 ≥ 候选（降序遍历），故包含必然来自更高或同级。
            var accepted = new List<MatchGroup>(raw.Count);
            var acceptedSets = new List<HashSet<int>>(raw.Count);

            foreach (var gi in order)
            {
                var g = raw[gi];

                bool contained = false;
                for (int i = 0; i < accepted.Count; i++)
                {
                    // 已接收组格子数小于候选时不可能包含
                    if (accepted[i].CellIds.Count < g.CellIds.Count) continue;
                    if (acceptedSets[i].IsSupersetOf(g.CellIds))
                    {
                        contained = true;
                        break;
                    }
                }
                if (contained) continue;

                accepted.Add(g);
                acceptedSets.Add(new HashSet<int>(g.CellIds));
            }

            return accepted;
        }
    }

    /// <summary>
    /// 交叉去重（赢家通吃）：按优先级降序贪心接收，候选组与任一已接收组有**任意格子重合**即整组丢弃。
    /// 存活组两两不相交——十字交叉的四连和五连只留五连，末端相交的三连四连只留四连。
    /// 输出顺序：优先级降序，同优先级保持输入顺序（完全确定）。
    /// </summary>
    public sealed class OverlapSuppressArbiter : IMatchArbiter
    {
        public string Id => "overlap";

        public List<MatchGroup> Arbitrate(GraphBoard board, IReadOnlyList<MatchGroup> raw)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (raw.Count <= 1) return new List<MatchGroup>(raw);

            var order = MatchArbiters.SortedOrder(raw);

            // 存活组两两不相交（构造保证），故一个并集集合即可判重
            var accepted = new List<MatchGroup>(raw.Count);
            var acceptedCells = new HashSet<int>();

            foreach (var gi in order)
            {
                var g = raw[gi];

                bool overlaps = false;
                foreach (var c in g.CellIds)
                {
                    if (acceptedCells.Contains(c)) { overlaps = true; break; }
                }
                if (overlaps) continue;

                accepted.Add(g);
                foreach (var c in g.CellIds) acceptedCells.Add(c);
            }

            return accepted;
        }
    }
}
