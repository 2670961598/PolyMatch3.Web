using System.Collections.Generic;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Step;
using PolyMatch3.Tools;

namespace PolyMatch3.Samples.Classic
{
    /// <summary>
    /// 特殊棋子消除（传统三消核心结算）：在 EliminateStep 骨架上重载消除集计算。
    /// 种子来源二选一：
    ///   ① 正常路径：匹配组并集 − 待生成锚点；
    ///   ② 交互路径：黑板 forceSeeds（GemInteractStep / SpecialInteractStep 预先算好）。
    /// 然后对每个特殊格按其种类展开（线弹=整行/整列、星弹=星形、宝石=全棋盘同色），
    /// 展开命中其他特殊子则连锁（闭包迭代，与处理顺序无关，完全确定）。
    /// 清除后把待生成锚点落到 kind 层（颜色层保留——匹配留下的那个子就是特殊子）。
    /// </summary>
    public class SpecialEliminateStep : EliminateStep
    {
        /// <summary>交互路径的强制种子黑板键（List&lt;int&gt;，一次性消费）。</summary>
        public const string ForceSeedsKey = "forceSeeds";

        private readonly KindLayer _kinds;

        public SpecialEliminateStep(KindLayer kinds, string sourceKey = MatchStep.DefaultKey, PieceRegistry pieces = null)
            : base(sourceKey, pieces)
        {
            _kinds = kinds;
        }

        protected override bool TryReadGroups(StepContext ctx, out List<MatchGroup> groups)
        {
            // 交互路径：forceSeeds 在则无需匹配组
            if (ctx.Info.Contains(ForceSeedsKey))
            {
                groups = null;
                return true;
            }
            return base.TryReadGroups(ctx, out groups);
        }

        protected override List<int> CollectCells(GraphBoard board, List<MatchGroup> groups, StepContext ctx)
        {
            var seeds = new HashSet<int>();

            if (ctx.Info.TryGet<List<int>>(ForceSeedsKey, out var forced))
            {
                // 交互路径：种子已由交互 Step 算好（含被触发的特殊格）
                ctx.Info.Remove(ForceSeedsKey);
                foreach (var c in forced) seeds.Add(c);
            }
            else
            {
                // 正常路径：匹配并集 − 待生成锚点
                var anchors = new HashSet<int>();
                if (ctx.Info.TryGet<List<(int cell, int kind)>>(SpecialSpawnStep.SpawnKey, out var spawns))
                {
                    foreach (var (cell, _) in spawns) anchors.Add(cell);
                }
                foreach (var c in base.CollectCells(board, groups, ctx))
                {
                    if (!anchors.Contains(c)) seeds.Add(c);
                }
            }

            // 特殊子展开：闭包迭代（命中其他特殊子连锁触发）
            var result = new HashSet<int>(seeds);
            var queue = new List<int>(seeds);
            for (int qi = 0; qi < queue.Count; qi++)
            {
                int c = queue[qi];
                foreach (var n in ExpandCell(board, c, _kinds.Get(c)))
                {
                    if (board.GetPieceType(n) == PieceRegistry.EmptyId) continue;
                    if (result.Add(n)) queue.Add(n);
                }
            }

            var list = new List<int>(result);
            list.Sort();
            return list;
        }

        /// <summary>单个格子的引爆范围（按种类；普通格 = 无展开）。可重载自定义特殊子。</summary>
        protected virtual IEnumerable<int> ExpandCell(GraphBoard board, int cell, int kind)
        {
            switch (kind)
            {
                case SpecialKind.LineH: return RowCells(board, cell / board.Width, 1);
                case SpecialKind.LineV: return ColCells(board, cell % board.Width, 1);
                case SpecialKind.Star: return StarCells(board, cell);
                case SpecialKind.Gem: return ColorCells(board, board.GetPieceType(cell));
                default: return System.Linq.Enumerable.Empty<int>();
            }
        }

        /// <summary>整行（offset=±1 时为相邻三行，用于线+星联动）。</summary>
        public static IEnumerable<int> RowCells(GraphBoard board, int row, int span)
        {
            for (int y = row - (span - 1); y <= row + (span - 1); y++)
            {
                if (y < 0 || y >= board.Height) continue;
                for (int x = 0; x < board.Width; x++) yield return y * board.Width + x;
            }
        }

        /// <summary>整列（offset=±1 时为相邻三列，用于线+星联动）。</summary>
        public static IEnumerable<int> ColCells(GraphBoard board, int col, int span)
        {
            for (int x = col - (span - 1); x <= col + (span - 1); x++)
            {
                if (x < 0 || x >= board.Width) continue;
                for (int y = 0; y < board.Height; y++) yield return y * board.Width + x;
            }
        }

        /// <summary>星形范围：正交两格 + 斜角一格（欧式距离之和为 2 的星）。</summary>
        public static IEnumerable<int> StarCells(GraphBoard board, int center)
        {
            int w = board.Width;
            int cx = center % w, cy = center / w;
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int m = (dx < 0 ? -dx : dx) + (dy < 0 ? -dy : dy);
                    if (m > 2) continue; // 曼哈顿距离 ≤2：正交 2 格 + 斜角 1 格的星形
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= w || y < 0 || y >= board.Height) continue;
                    yield return y * w + x;
                }
            }
        }

        /// <summary>全棋盘某颜色的格子。</summary>
        public static IEnumerable<int> ColorCells(GraphBoard board, int color)
        {
            if (color == PieceRegistry.EmptyId) yield break;
            for (int c = 0; c < board.CellCount; c++)
            {
                if (board.GetPieceType(c) == color) yield return c;
            }
        }

        protected override void OnCellCleared(GraphBoard board, int cellId, int clearedType, StepContext ctx)
        {
            base.OnCellCleared(board, cellId, clearedType, ctx);
            _kinds.Clear(cellId); // 特殊子被消（含被引爆）：kind 同步归普通
        }

        protected override void AfterCleared(GraphBoard board, List<int> clearedCells, StepContext ctx)
        {
            // 待生成锚点落 kind 层（颜色层保留），生成列表一次性消费
            if (ctx.Info.TryGet<List<(int cell, int kind)>>(SpecialSpawnStep.SpawnKey, out var spawns))
            {
                foreach (var (cell, kind) in spawns) _kinds.Set(cell, kind);
                ctx.Info.Remove(SpecialSpawnStep.SpawnKey);
            }
        }
    }
}
