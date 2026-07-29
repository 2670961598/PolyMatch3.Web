using System.Threading.Tasks;
using PolyMatch3.Core;

namespace PolyMatch3.Step
{
    /// <summary>
    /// StepManager 接口，决策引擎。
    /// 根据当前世界状态和上一步结果，决定下一步执行什么。
    /// 内部实现（状态机/行为树/规则循环/if-else）完全由玩法自定，框架只要这一个答案。
    /// </summary>
    public interface IStepManager
    {
        Task<StepDecision> DecideNextAsync(GraphBoard board, StepContext ctx, StepResult lastResult);
    }
}
