using System.Collections.Generic;

namespace PolyMatch3.Step
{
    /// <summary>
    /// Step 执行结果：发生了什么（供 StepManager / 复合 Step 判定下一步）。
    /// </summary>
    public sealed class StepResult
    {
        /// <summary>Step 是否成功执行（语义由 Step 自定义，如 MatchStep 无匹配即 false）。</summary>
        public bool Success;

        /// <summary>本次 Step 产生的游戏事件（回放日志的载荷；表现层分发在 Unity 阶段对接）。</summary>
        public List<GameEvent> Events = new List<GameEvent>();

        /// <summary>子 Step 的执行结果（复合 Step 使用，原子 Step 为 null）。</summary>
        public List<StepResult> SubResults;
    }
}
