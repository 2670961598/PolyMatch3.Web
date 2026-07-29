using System;
using System.Collections.Generic;

namespace PolyMatch3.Matcher
{
    /// <summary>
    /// 优先级仲裁器：把匹配器输出的原始 MatchGroup 列表仲裁为最终消除依据。
    /// 规则（owner 定稿 v2）：
    ///   1. 图案优先级全局唯一；
    ///   2. **完全包含才压制**：一组的格子集合被已接收（更高或同优先级）组完全包含时，整组丢弃；
    ///      仅仅是部分重叠 → 两组共存，一起消除（如四连末端拐个弯带个三连：四连三连都消）；
    ///   3. 同优先级组格子集合完全相同（如同一直线的两个锚位变体）→ 后到的被先到的包含，丢弃。
    /// 输出顺序：优先级降序，同优先级保持输入顺序（完全确定）。
    /// </summary>
    public static class MatchArbitrator
    {
        /// <summary>
        /// 执行仲裁。groups 为匹配器原始输出（不修改），cellCount 保留用于签名兼容（当前实现不需要工作数组）。
        /// 返回新的存活组列表。
        /// </summary>
        public static List<MatchGroup> Arbitrate(List<MatchGroup> groups, int cellCount)
        {
            if (groups == null) throw new ArgumentNullException(nameof(groups));
            if (cellCount <= 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
            if (groups.Count <= 1) return new List<MatchGroup>(groups);

            // 全序排序：优先级降序，同优先级按输入下标升序（保证确定性）
            var order = new int[groups.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int c = groups[b].Priority.CompareTo(groups[a].Priority);
                return c != 0 ? c : a.CompareTo(b);
            });

            // 贪心接收：仅当候选组的格子集合被某个已接收组**完全包含**时才丢弃。
            // 已接收组优先级必然 ≥ 候选（降序遍历），故包含必然来自更高或同级。
            var accepted = new List<MatchGroup>(groups.Count);
            var acceptedSets = new List<HashSet<int>>(groups.Count);

            foreach (var gi in order)
            {
                var g = groups[gi];

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
}
