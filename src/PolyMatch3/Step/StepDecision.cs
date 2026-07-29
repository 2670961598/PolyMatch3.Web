namespace PolyMatch3.Step
{
    /// <summary>
    /// StepManager 的决策输出：下一步执行什么，或流程为何结束。
    /// 终止原因是一等语义（UI 展示 / 回放记录 / 调试都需要）。
    /// </summary>
    public readonly struct StepDecision
    {
        /// <summary>是否有下一步。</summary>
        public readonly bool HasNext;

        /// <summary>下一步要执行的 Step（HasNext=false 时为 null）。</summary>
        public readonly IStep Step;

        /// <summary>流程结束原因（HasNext=true 时为空）。</summary>
        public readonly string EndReason;

        private StepDecision(bool hasNext, IStep step, string endReason)
        {
            HasNext = hasNext;
            Step = step;
            EndReason = endReason;
        }

        public static StepDecision Next(IStep step)
        {
            return new StepDecision(true, step, null);
        }

        public static StepDecision End(string reason = "")
        {
            return new StepDecision(false, null, reason ?? "");
        }
    }
}
