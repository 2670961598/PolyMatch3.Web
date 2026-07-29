using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Step;

namespace PolyMatch3.Samples.Bomb
{
    /// <summary>炸弹生成事件（CellIds = 生成炸弹的锚点格）。</summary>
    public sealed class BombSpawnEvent : GameEvent
    {
        public readonly int Kind;

        public BombSpawnEvent(int anchorCell, int kind)
        {
            Type = "BombSpawn";
            CellIds = new[] { anchorCell };
            Kind = kind;
        }
    }

    /// <summary>
    /// 匹配生成炸弹：读取黑板上的匹配组（MatchStep 之后、EliminateStep 之前执行），
    /// 优先级 ≥ minPriority 的组，其锚点格"免消转弹"——锚点列表写黑板（BombEliminateStep 读取），
    /// 炸弹本体在消除完成后由 BombEliminateStep 落到 kind 层。
    /// 去重规则：按输入序接收，与已接收组有格子重合的组跳过——
    /// 同一物理匹配（如一行四连的两个锚位变体，格子集合相同）只生成一颗；
    /// 真正不重合的两个高优组（如十字交叉的两个四连）照常各生成一颗。
    /// </summary>
    public sealed class BombSpawnOnMatchStep : IStep
    {
        /// <summary>锚点列表的黑板键（BombEliminateStep 消费）。</summary>
        public const string SpawnKey = "bombSpawns";

        private readonly int _minPriority;
        private readonly int _bombKind;
        private readonly string _sourceKey;

        public BombSpawnOnMatchStep(int minPriority, int bombKind = KindLayer.Bomb3x3, string sourceKey = MatchStep.DefaultKey)
        {
            _minPriority = minPriority;
            _bombKind = bombKind;
            _sourceKey = sourceKey;
        }

        public string Name => "BombSpawn";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            if (!ctx.Info.TryGet<List<MatchGroup>>(_sourceKey, out var groups) || groups.Count == 0)
                return Task.FromResult(new StepResult { Success = false });

            // 按输入序接收高优组，与已接收组有格子重合的跳过（同一物理匹配只出一颗）
            var acceptedCells = new HashSet<int>();
            var anchors = new SortedSet<int>();
            foreach (var g in groups)
            {
                if (g.Priority < _minPriority) continue;
                bool overlaps = false;
                foreach (var c in g.CellIds)
                {
                    if (acceptedCells.Contains(c)) { overlaps = true; break; }
                }
                if (overlaps) continue;

                foreach (var c in g.CellIds) acceptedCells.Add(c);
                anchors.Add(g.AnchorId);
            }
            if (anchors.Count == 0)
                return Task.FromResult(new StepResult { Success = false });

            var list = new List<int>(anchors);
            ctx.Info.Set(SpawnKey, list);

            var result = new StepResult { Success = true };
            foreach (var a in list)
                result.Events.Add(new BombSpawnEvent(a, _bombKind));
            return Task.FromResult(result);
        }
    }
}
