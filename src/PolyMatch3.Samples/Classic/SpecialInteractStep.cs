using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Step;

namespace PolyMatch3.Samples.Classic
{
    /// <summary>
    /// 特殊子 + 特殊子交换交互（均非宝石；不交换位置，直接结算）：
    ///   线弹 + 线弹 / 星弹 + 星弹：两者各自触发（种子 = 两格，展开闭包完成其余）；
    ///   线弹 + 星弹：线弹的行/列**拓展为三行/列**（线弹位置 ±1），星弹自身也触发；
    /// 结算方式：种子写黑板 forceSeeds，由 SpecialEliminateStep 统一清除/展开/计数。
    /// </summary>
    public sealed class SpecialInteractStep : IStep
    {
        private readonly KindLayer _kinds;
        private readonly int _a;
        private readonly int _b;

        public SpecialInteractStep(KindLayer kinds, int a, int b)
        {
            _kinds = kinds;
            _a = a;
            _b = b;
        }

        public string Name => "SpecialInteract";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            int ka = _kinds.Get(_a), kb = _kinds.Get(_b);
            var seeds = new SortedSet<int>();

            bool aLine = ka == SpecialKind.LineH || ka == SpecialKind.LineV;
            bool bLine = kb == SpecialKind.LineH || kb == SpecialKind.LineV;
            bool aStar = ka == SpecialKind.Star, bStar = kb == SpecialKind.Star;

            if ((aLine && bStar) || (bLine && aStar))
            {
                // 线 + 星：线拓展为三行/列，星自身触发
                int lineCell = aLine ? _a : _b;
                int lineKind = aLine ? ka : kb;
                int starCell = aLine ? _b : _a;

                if (lineKind == SpecialKind.LineH)
                    foreach (var c in SpecialEliminateStep.RowCells(board, lineCell / board.Width, 2)) seeds.Add(c);
                else
                    foreach (var c in SpecialEliminateStep.ColCells(board, lineCell % board.Width, 2)) seeds.Add(c);
                seeds.Add(starCell);
            }
            else
            {
                // 同类（线+线 / 星+星）：各自触发
                seeds.Add(_a);
                seeds.Add(_b);
            }

            ctx.Info.Set(SpecialEliminateStep.ForceSeedsKey, new List<int>(seeds));
            return Task.FromResult(new StepResult { Success = true });
        }
    }
}
