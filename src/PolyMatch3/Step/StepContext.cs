using System.Collections.Generic;
using PolyMatch3.Core;
using PolyMatch3.Logging;

namespace PolyMatch3.Step
{
    /// <summary>
    /// Step 执行上下文：贯穿整局的数据袋。
    /// 框架不强制任何 Step 使用它——需要随机源/日志/黑板/历史时才取用；
    /// 玩法也可完全使用自己的全局状态，框架不警察。
    /// </summary>
    public class StepContext
    {
        /// <summary>确定性随机源（RefillStep 等随机消费的唯一合法来源）。</summary>
        public IRandom Random;

        /// <summary>日志出口（默认转发到 Log 门面，测试可替换探针）。</summary>
        public ILogger Logger = FacadeLogger.Instance;

        /// <summary>全局信息表：跨 Step 传递信息（写了后面读，没有就走默认行为）。</summary>
        public readonly Blackboard Info = new Blackboard();

        /// <summary>当前步序号（由 Orchestrator 维护，Step 只读使用）。</summary>
        public int StepIndex { get; internal set; }

        /// <summary>已执行 Step 的结果历史（由 Orchestrator 维护追加）。</summary>
        public readonly List<StepResult> History = new List<StepResult>();

        public StepContext(IRandom random = null)
        {
            Random = random;
        }
    }
}
