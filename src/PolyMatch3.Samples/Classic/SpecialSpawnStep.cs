using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Step;
using PolyMatch3.Tools;

namespace PolyMatch3.Samples.Classic
{
    /// <summary>
    /// 特殊棋子生成（ArbitrateStep 之后、SpecialEliminateStep 之前执行）：
    /// 生成物裁决（ISpawnResolver）从存活组中裁出生效组，生效组锚点"免消转弹"
    /// （锚点 + kind 写黑板，由 SpecialEliminateStep 在清完后落 kind 层）。
    /// 映射表（SpawnTable）决定哪些图案有生成物（字符串即配置，开局 Validate）；
    /// 裁决策略决定重叠时谁生效：WinnerTakeAll（默认，只认最高优先级）/ BothApply（交叉都生成）。
    /// </summary>
    public sealed class SpecialSpawnStep : IStep
    {
        /// <summary>生成列表的黑板键：List&lt;(int cell, int kind)&gt;（SpecialEliminateStep 消费）。</summary>
        public const string SpawnKey = "specialSpawns";

        private readonly string _sourceKey;
        private readonly SpawnTable _table;
        private readonly ISpawnResolver _resolver;

        public SpecialSpawnStep(SpawnTable table, ISpawnResolver resolver = null, string sourceKey = MatchStep.DefaultKey)
        {
            _table = table ?? throw new System.ArgumentNullException(nameof(table));
            _resolver = resolver ?? SpawnResolvers.WinnerTakeAll;
            _sourceKey = sourceKey;
        }

        public string Name => "SpecialSpawn";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            ctx.Info.Remove(SpawnKey); // 不残留上一轮
            if (!ctx.Info.TryGet<List<MatchGroup>>(_sourceKey, out var groups) || groups.Count == 0)
                return Task.FromResult(new StepResult { Success = false });

            var effective = _resolver.Resolve(groups, _table);
            if (effective.Count == 0)
                return Task.FromResult(new StepResult { Success = false });

            var spawns = new List<(int cell, int kind)>();
            foreach (var g in effective)
            {
                // 生效组必有映射（裁决器已按表过滤），载荷由玩法按生成物 Id 解释
                int kind = ClassicSetup.KindForSpawn(g, _table.TryGet(g.PatternId, out var spawnId) ? spawnId : null);
                if (kind == 0) continue;
                spawns.Add((g.AnchorId, kind));
            }

            if (spawns.Count == 0)
                return Task.FromResult(new StepResult { Success = false });

            // 锚点升序（确定性）
            spawns.Sort((a, b) => a.cell.CompareTo(b.cell));
            ctx.Info.Set(SpawnKey, spawns);

            var result = new StepResult { Success = true };
            foreach (var (cell, kind) in spawns)
                result.Events.Add(new SpecialSpawnEvent(cell, kind));
            return Task.FromResult(result);
        }
    }
}
