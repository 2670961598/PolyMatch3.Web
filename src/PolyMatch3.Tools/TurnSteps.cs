using System;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Step;

namespace PolyMatch3.Tools
{
    /// <summary>回合状态的黑板键约定（谁写谁负责：编排层补点，输入型 Step 扣点）。</summary>
    public static class TurnKeys
    {
        /// <summary>回合序号（BeginTurnStep 递增）。</summary>
        public const string Index = "turn.index";
        /// <summary>剩余行动点。</summary>
        public const string Ap = "turn.ap";
    }

    /// <summary>
    /// 回合开始：turn.index +1（缺省 0 起），AP 补满为指定值。
    /// 编排层在"回输入之前"派发——小丑牌式牌堆差异（不同的 AP 上限）就挂在构造参数上。
    /// </summary>
    public sealed class BeginTurnStep : IStep
    {
        private readonly int _ap;

        public BeginTurnStep(int ap)
        {
            if (ap <= 0) throw new ArgumentOutOfRangeException(nameof(ap), ap, "AP 上限必须 ≥ 1");
            _ap = ap;
        }

        public string Name => "BeginTurn";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            ctx.Info.TryGet<int>(TurnKeys.Index, out var index);
            ctx.Info.Set(TurnKeys.Index, index + 1);
            ctx.Info.Set(TurnKeys.Ap, _ap);
            return Task.FromResult(new StepResult { Success = true });
        }
    }

    /// <summary>
    /// 行动点闸：消耗 cost 点 AP。不足则 Success=false 且**不扣点**
    /// （非法/超支操作不改变任何状态，与"非法输入自动丢弃"同一语义）。
    /// 输入型 Step 在执行操作前派发本闸；编排层据 Success 决定放行还是驳回。
    /// </summary>
    public sealed class SpendApStep : IStep
    {
        private readonly int _cost;

        public SpendApStep(int cost)
        {
            if (cost <= 0) throw new ArgumentOutOfRangeException(nameof(cost), cost, "成本必须 ≥ 1");
            _cost = cost;
        }

        public string Name => "SpendAp";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            ctx.Info.TryGet<int>(TurnKeys.Ap, out var ap);
            if (ap < _cost)
                return Task.FromResult(new StepResult { Success = false });
            ctx.Info.Set(TurnKeys.Ap, ap - _cost);
            return Task.FromResult(new StepResult { Success = true });
        }
    }
}
