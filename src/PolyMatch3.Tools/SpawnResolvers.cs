using System;
using System.Collections.Generic;
using PolyMatch3.Matcher;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 生成物裁决注册表 + 内置实现。用法与 MatchArbiters 同构：
    /// 配置/编辑器序列化只存 Id 字符串；自定义裁决 Register 后即可按 Id 解析；
    /// 重复 Id 直接抛（只增不改，防静默覆盖破坏确定性）。
    /// </summary>
    public static class SpawnResolvers
    {
        /// <summary>赢家通吃（传统三消现状）：按输入序（优先级降序）接收，与已接收组有任意格子重合的候选整组跳过。</summary>
        public static readonly ISpawnResolver WinnerTakeAll = new WinnerTakeAllResolver();

        /// <summary>交叉都生效：凡有映射的组全部生成（多锚点都免消都生成），不做重叠压制。</summary>
        public static readonly ISpawnResolver BothApply = new BothApplyResolver();

        private static readonly Dictionary<string, ISpawnResolver> ById = new Dictionary<string, ISpawnResolver>();

        static SpawnResolvers()
        {
            Register(WinnerTakeAll);
            Register(BothApply);
        }

        /// <summary>注册裁决器。Id 为空或重复注册直接抛异常。</summary>
        public static void Register(ISpawnResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            if (string.IsNullOrEmpty(resolver.Id))
                throw new ArgumentException("裁决器 Id 不能为空：注册表以 Id 为键，空 Id 无法被配置/编辑器引用", nameof(resolver));
            if (ById.TryGetValue(resolver.Id, out var existing) && !ReferenceEquals(existing, resolver))
                throw new ArgumentException($"生成物裁决 Id 重复注册：\"{resolver.Id}\"（{existing.GetType().Name} vs {resolver.GetType().Name}）。同一 Id 两种实现会破坏确定性", nameof(resolver));
            ById[resolver.Id] = resolver;
        }

        /// <summary>按 Id 解析。未注册抛 KeyNotFoundException 并列出可用 Id。</summary>
        public static ISpawnResolver Get(string id)
        {
            if (TryGet(id, out var resolver)) return resolver;
            throw new KeyNotFoundException($"未注册的生成物裁决 Id：\"{id}\"。可用：{string.Join(", ", ById.Keys)}");
        }

        public static bool TryGet(string id, out ISpawnResolver resolver)
        {
            resolver = null;
            return id != null && ById.TryGetValue(id, out resolver);
        }

        /// <summary>已注册的全部 Id（编辑器下拉框数据源）。</summary>
        public static IEnumerable<string> RegisteredIds => ById.Keys;
    }

    /// <summary>
    /// 赢家通吃：按输入序（约定 = 优先级降序）贪心接收候选组，
    /// 与任一已接收组有格子重合的候选整组跳过——部分重叠的多个图案一起消除，
    /// 但生成物只认最高优先级（传统三消现状）。
    /// </summary>
    public sealed class WinnerTakeAllResolver : ISpawnResolver
    {
        public string Id => "winner-take-all";

        public List<MatchGroup> Resolve(IReadOnlyList<MatchGroup> groups, SpawnTable table)
        {
            if (groups == null) throw new ArgumentNullException(nameof(groups));
            if (table == null) throw new ArgumentNullException(nameof(table));

            var accepted = new List<MatchGroup>(groups.Count);
            var acceptedCells = new HashSet<int>(); // 接收组两两不相交（构造保证），并集即可判重
            foreach (var g in groups)
            {
                if (!table.TryGet(g.PatternId, out _)) continue; // 无生成物：不生效也不阻挡

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

    /// <summary>
    /// 交叉都生效：凡有映射的组全部生成，不做任何重叠压制——
    /// 十字交叉的四连和五连各生成各的（多个锚点都免消、都落生成物）。
    /// </summary>
    public sealed class BothApplyResolver : ISpawnResolver
    {
        public string Id => "both-apply";

        public List<MatchGroup> Resolve(IReadOnlyList<MatchGroup> groups, SpawnTable table)
        {
            if (groups == null) throw new ArgumentNullException(nameof(groups));
            if (table == null) throw new ArgumentNullException(nameof(table));

            var accepted = new List<MatchGroup>(groups.Count);
            foreach (var g in groups)
            {
                if (table.TryGet(g.PatternId, out _)) accepted.Add(g);
            }
            return accepted;
        }
    }
}
