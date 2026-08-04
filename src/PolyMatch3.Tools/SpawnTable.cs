using System;
using System.Collections.Generic;
using PolyMatch3.Matcher;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 生成物映射表：图案 Id（字符串）→ 生成物 Id（字符串）。
    /// 字符串即配置（owner 定稿：不用数字）——某一局的图案集/生成物集装配好后，
    /// 开局时调一次 <see cref="Validate"/> 做合法校验：映射键必须是已配置图案、
    /// 生成物 Id 必须被玩法认可（回调判定，如"是否已注册的棋子/种类"）。
    /// 表只增不改：重复键直接抛（静默覆盖会让配置文件的表现与读到的不同）。
    /// spawnId 的具体含义由玩法解释（如 Classic：四连→"line"，再按变体分出横/竖消弹）。
    /// </summary>
    public sealed class SpawnTable
    {
        private readonly Dictionary<string, string> _byPatternId = new Dictionary<string, string>();

        public int Count => _byPatternId.Count;

        /// <summary>登记一条映射。图案 Id / 生成物 Id 空或重复登记直接抛。</summary>
        public SpawnTable Add(string patternId, string spawnId)
        {
            if (string.IsNullOrEmpty(patternId))
                throw new ArgumentException("图案 Id 不能为空", nameof(patternId));
            if (string.IsNullOrEmpty(spawnId))
                throw new ArgumentException("生成物 Id 不能为空", nameof(spawnId));
            if (_byPatternId.ContainsKey(patternId))
                throw new ArgumentException($"生成物映射重复登记：图案 \"{patternId}\"（已有 \"{_byPatternId[patternId]}\"，新 \"{spawnId}\"）", nameof(patternId));
            _byPatternId.Add(patternId, spawnId);
            return this;
        }

        public bool TryGet(string patternId, out string spawnId)
        {
            return _byPatternId.TryGetValue(patternId, out spawnId);
        }

        /// <summary>已登记的全部图案 Id（校验/调试用）。</summary>
        public IEnumerable<string> PatternIds => _byPatternId.Keys;

        /// <summary>
        /// 开局合法校验：映射键必须出现在本局图案集中；isSpawnIdValid 提供时
        /// 每个生成物 Id 也会被玩法回调认可（如"是否已注册的种类"）。失败即抛，列出全部问题。
        /// </summary>
        public void Validate(IReadOnlyList<Pattern> patterns, Func<string, bool> isSpawnIdValid = null)
        {
            if (patterns == null) throw new ArgumentNullException(nameof(patterns));

            var known = new HashSet<string>();
            foreach (var p in patterns) known.Add(p.Id);

            var problems = new List<string>();
            foreach (var (patternId, spawnId) in _byPatternId)
            {
                if (!known.Contains(patternId))
                    problems.Add($"映射键 \"{patternId}\" 不在本局图案集中（{string.Join(", ", known)}）");
                if (isSpawnIdValid != null && !isSpawnIdValid(spawnId))
                    problems.Add($"生成物 Id \"{spawnId}\"（图案 \"{patternId}\"）未被玩法注册");
            }
            if (problems.Count > 0)
                throw new InvalidOperationException("生成物映射表校验失败：\n - " + string.Join("\n - ", problems));
        }
    }
}
