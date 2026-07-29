using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Step;
using PolyMatch3.Tools;

namespace PolyMatch3.Samples.Classic
{
    /// <summary>
    /// 宝石交换交互（交换对至少一方是宝石时替代普通交换；不交换位置，直接结算）：
    ///   宝石 + 普通子：清除全棋盘与该普通子同色的全部棋子（宝石本身也消耗）；
    ///   宝石 + 线弹/星弹：全棋盘与该特殊子同色的棋子**全部变为该特殊子**（TransformEvent），并立刻触发；
    ///   宝石 + 宝石：清空整个棋盘。
    /// 结算方式：把最终消除种子写黑板 forceSeeds，由 SpecialEliminateStep 统一清除/展开/计数。
    /// </summary>
    public sealed class GemInteractStep : IStep
    {
        private readonly KindLayer _kinds;
        private readonly int _a;
        private readonly int _b;

        public GemInteractStep(KindLayer kinds, int a, int b)
        {
            _kinds = kinds;
            _a = a;
            _b = b;
        }

        public string Name => "GemInteract";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            int ka = _kinds.Get(_a), kb = _kinds.Get(_b);
            var seeds = new SortedSet<int>();
            var result = new StepResult { Success = true };

            if (ka == SpecialKind.Gem && kb == SpecialKind.Gem)
            {
                // 宝石 + 宝石：清空棋盘
                for (int c = 0; c < board.CellCount; c++) seeds.Add(c);
                _kinds.Clear(_a);
                _kinds.Clear(_b);
            }
            else
            {
                int gem = ka == SpecialKind.Gem ? _a : _b;
                int other = ka == SpecialKind.Gem ? _b : _a;
                int otherKind = _kinds.Get(other);
                int color = board.GetPieceType(other);

                if (otherKind == 0)
                {
                    // 宝石 + 普通子：清全棋盘该颜色 + 宝石本身
                    foreach (var c in SpecialEliminateStep.ColorCells(board, color)) seeds.Add(c);
                    seeds.Add(gem);
                }
                else
                {
                    // 宝石 + 特殊子：全棋盘该颜色棋子变为该特殊子并立刻触发
                    var transformed = new List<int>();
                    foreach (var c in SpecialEliminateStep.ColorCells(board, color))
                    {
                        if (c == gem) continue;
                        _kinds.Set(c, otherKind);
                        transformed.Add(c);
                        seeds.Add(c);
                    }
                    seeds.Add(gem);
                    if (transformed.Count > 0)
                        result.Events.Add(new TransformEvent(transformed.ToArray(), otherKind));
                }

                // 宝石被本次交换消耗：kind 归普通，避免消除阶段按宝石再展开一次
                _kinds.Clear(gem);
            }

            ctx.Info.Set(SpecialEliminateStep.ForceSeedsKey, new List<int>(seeds));
            return Task.FromResult(result);
        }
    }
}
