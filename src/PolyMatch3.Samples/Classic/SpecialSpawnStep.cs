using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Step;

namespace PolyMatch3.Samples.Classic
{
    /// <summary>
    /// 特殊棋子生成（MatchStep 之后、SpecialEliminateStep 之前执行）：
    /// 按组图案映射生成物（四连→线弹分方向、十字/T字→星弹、五连→宝石），
    /// 生成组锚点"免消转弹"（锚点+种类写黑板，由 SpecialEliminateStep 在清完后落 kind 层）。
    /// 去重：组按仲裁序（优先级降序）接收，与已接收组有格子重合的跳过——
    /// 部分重叠的多个图案一起消除，但生成物以最高优先级为准。
    /// </summary>
    public sealed class SpecialSpawnStep : IStep
    {
        /// <summary>生成列表的黑板键：List&lt;(int cell, int kind)&gt;（SpecialEliminateStep 消费）。</summary>
        public const string SpawnKey = "specialSpawns";

        private readonly string _sourceKey;

        public SpecialSpawnStep(string sourceKey = MatchStep.DefaultKey)
        {
            _sourceKey = sourceKey;
        }

        public string Name => "SpecialSpawn";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            ctx.Info.Remove(SpawnKey); // 不残留上一轮
            if (!ctx.Info.TryGet<List<MatchGroup>>(_sourceKey, out var groups) || groups.Count == 0)
                return Task.FromResult(new StepResult { Success = false });

            var acceptedCells = new HashSet<int>();
            var spawns = new List<(int cell, int kind)>();
            foreach (var g in groups) // 仲裁输出已按优先级降序
            {
                int kind = ClassicSetup.KindFor(g);
                if (kind == 0) continue; // 三连等无生成物

                bool overlaps = false;
                foreach (var c in g.CellIds)
                {
                    if (acceptedCells.Contains(c)) { overlaps = true; break; }
                }
                if (overlaps) continue;

                foreach (var c in g.CellIds) acceptedCells.Add(c);
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
