using System.Collections.Generic;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Step;
using PolyMatch3.Tools;

namespace PolyMatch3.Samples.Bomb
{
    /// <summary>
    /// 炸弹消除：在 EliminateStep 骨架上重载两个接缝——
    /// ① 消除集：匹配并集 − 待生成锚点，然后对每个炸弹格做范围展开（注入的 ICellSelector），
    ///    展开命中其他炸弹则连锁（迭代到不动点）；
    /// ② 清完后：把锚点格落到 kind 层（颜色层保留——四连留下的那个子就是炸弹）。
    /// 展开集合是闭包（与处理顺序无关），队列按首次命中序，完全确定。
    /// </summary>
    public sealed class BombEliminateStep : EliminateStep
    {
        private readonly KindLayer _kinds;
        private readonly ICellSelector _range;

        /// <param name="kinds">kind 平行数组（判定谁是炸弹、清除时同步归普通）。</param>
        /// <param name="range">爆炸范围选择器（默认 RectSquareSelector(1) = 矩形 3×3）。
        /// 换爆炸形状（十字/整行/任意拓扑范围）只换这个参数，不改本类。</param>
        public BombEliminateStep(KindLayer kinds, ICellSelector range = null, string sourceKey = MatchStep.DefaultKey, PieceRegistry pieces = null)
            : base(sourceKey, pieces)
        {
            _kinds = kinds;
            _range = range ?? new RectSquareSelector(1);
        }

        protected override List<int> CollectCells(GraphBoard board, List<MatchGroup> groups, StepContext ctx)
        {
            // 待生成锚点：免消（转化为炸弹，见 AfterCleared；锚点列表在 AfterCleared 一次性消费）
            var anchors = new HashSet<int>();
            if (ctx.Info.TryGet<List<int>>(BombSpawnOnMatchStep.SpawnKey, out var spawnList))
            {
                foreach (var a in spawnList) anchors.Add(a);
            }

            var seeds = new HashSet<int>();
            foreach (var c in base.CollectCells(board, groups, ctx))
            {
                if (!anchors.Contains(c)) seeds.Add(c);
            }

            // 爆炸展开：闭包迭代（炸弹格 → 选择器范围内非空格 → 命中的炸弹再展开）
            var result = new HashSet<int>(seeds);
            var queue = new List<int>(seeds);
            for (int qi = 0; qi < queue.Count; qi++)
            {
                int c = queue[qi];
                if (_kinds.Get(c) == KindLayer.Normal) continue;

                foreach (var n in _range.Select(board, c))
                {
                    if (board.GetPieceType(n) == PieceRegistry.EmptyId) continue;
                    if (result.Add(n)) queue.Add(n);
                }
            }

            var list = new List<int>(result);
            list.Sort();
            return list;
        }

        protected override void OnCellCleared(GraphBoard board, int cellId, int clearedType, StepContext ctx)
        {
            base.OnCellCleared(board, cellId, clearedType, ctx);
            _kinds.Clear(cellId); // 炸弹被消（含被引爆）：kind 同步归普通
        }

        protected override void AfterCleared(GraphBoard board, List<int> clearedCells, StepContext ctx)
        {
            // 锚点格转化为炸弹（颜色层保留原色），锚点列表一次性消费
            if (ctx.Info.TryGet<List<int>>(BombSpawnOnMatchStep.SpawnKey, out var anchors))
            {
                foreach (var a in anchors) _kinds.Set(a, KindLayer.Bomb3x3);
                ctx.Info.Remove(BombSpawnOnMatchStep.SpawnKey);
            }
        }
    }
}
