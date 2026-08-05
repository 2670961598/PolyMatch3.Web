using PolyMatch3.Core;
using PolyMatch3.Defs;
using PolyMatch3.Samples;
using PolyMatch3.Samples.Bomb;
using PolyMatch3.Tools;

namespace PolyMatch3.Game
{
    /// <summary>
    /// 【导读】Web 侧图编排的 Step catalog 引导：内置 Tools Step（CreateDefault：match/arbitrate/
    /// eliminate/gravity/fieldGravity/refill/score/beginTurn/spendAp/deadlockCheck/shuffle/count/swap）+
    /// 本层注册的输入/炸弹 Step。kinds != null（炸弹模式）时全部 bomb 系节点共享同一 KindLayer，
    /// 且 shuffle 走双层同步的 KindShuffleStep（单层洗牌会把 kind 层洗错位）。
    /// </summary>
    public static class GraphCatalog
    {
        public static StepCatalog Create(Samples.KindLayer kinds, ICellSelector bombRange)
        {
            var catalog = StepCatalog.CreateDefault();

            catalog.Register("playerSwap", (ctx, p) => new PlayerSwapStep(ctx.Input, kinds));
            catalog.Register("revertSwap", (ctx, p) => new RevertSwapStep(kinds)); // 黑板读最近交换（图节点静态化）

            if (kinds != null)
            {
                catalog.Register("bombSpawn", (ctx, p) => new BombSpawnOnMatchStep(
                    p?.Value<int?>("minPriority") ?? 80));
                catalog.Register("bombEliminate", (ctx, p) => new BombEliminateStep(
                    kinds, bombRange, pieces: ctx.Pieces));
                // 注：kindGravity 的列边生成假定矩形棋盘（炸弹示例棋盘均为矩形）
                catalog.Register("kindGravity", (ctx, p) => new KindGravityStep(
                    kinds, ctx.Board.CellCount,
                    PathGravityStep.BuildColumnEdges(ctx.Board.Width, ctx.Board.Height)));
                // 炸弹模式的洗牌必须双层同步（单层会把 kind 洗错位）——用专属 key
                catalog.Register("kindShuffle", (ctx, p) => new KindShuffleStep(
                    kinds, ctx.Matcher, p?.Value<int?>("maxAttempts") ?? 32));
            }
            return catalog;
        }
    }
}
