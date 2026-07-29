using System.Collections.Generic;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Step;
using PolyMatch3.Tools;

namespace PolyMatch3.Samples.Bomb
{
    /// <summary>
    /// 炸弹消除：在 EliminateStep 骨架上重载两个接缝——
    /// ① 消除集：匹配并集 − 待生成锚点，然后对每个炸弹格做半径展开，展开命中其他炸弹则连锁（迭代到不动点）；
    /// ② 清完后：把锚点格落到 kind 层（颜色层保留——四连留下的那个子就是炸弹）。
    /// 展开集合是闭包（与处理顺序无关），队列按首次命中序，完全确定。
    /// </summary>
    public sealed class BombEliminateStep : EliminateStep
    {
        private readonly KindLayer _kinds;
        private readonly int _radius;

        /// <param name="kinds">kind 平行数组（判定谁是炸弹、清除时同步归普通）。</param>
        /// <param name="radius">爆炸半径（切比雪夫距离，1 = 3×3，假设矩形棋盘）。</param>
        public BombEliminateStep(KindLayer kinds, int radius = 1, string sourceKey = MatchStep.DefaultKey, PieceRegistry pieces = null)
            : base(sourceKey, pieces)
        {
            _kinds = kinds;
            _radius = radius;
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

            // 爆炸展开：闭包迭代（炸弹格 → 半径内非空格 → 命中的炸弹再展开）
            var result = new HashSet<int>(seeds);
            var queue = new List<int>(seeds);
            for (int qi = 0; qi < queue.Count; qi++)
            {
                int c = queue[qi];
                if (_kinds.Get(c) == KindLayer.Normal) continue;

                foreach (var n in RadiusCells(board, c))
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

        /// <summary>矩形棋盘切比雪夫半径内的格子（越界裁剪）。</summary>
        private IEnumerable<int> RadiusCells(GraphBoard board, int center)
        {
            int w = board.Width;
            int cx = center % w, cy = center / w;
            for (int dy = -_radius; dy <= _radius; dy++)
            {
                for (int dx = -_radius; dx <= _radius; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= w || y < 0 || y >= board.Height) continue;
                    yield return y * w + x;
                }
            }
        }
    }
}
