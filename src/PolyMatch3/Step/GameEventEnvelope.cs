namespace PolyMatch3.Step
{
    /// <summary>
    /// 事件信封：给游戏事件加上全局顺序（Seq）与出处（哪一步）。
    /// Seq 由 Orchestrator 单点发放，全局单调递增——动画排序与回放共用一个序。
    /// </summary>
    public readonly struct GameEventEnvelope
    {
        /// <summary>全局单调事件序号（Orchestrator 发放）。</summary>
        public readonly long Seq;

        /// <summary>产生事件的 Step 序号（对应 StepContext.StepIndex）。</summary>
        public readonly int StepIndex;

        /// <summary>产生事件的 Step 名。</summary>
        public readonly string StepName;

        /// <summary>事件本体。</summary>
        public readonly GameEvent Event;

        public GameEventEnvelope(long seq, int stepIndex, string stepName, GameEvent gameEvent)
        {
            Seq = seq;
            StepIndex = stepIndex;
            StepName = stepName;
            Event = gameEvent;
        }
    }
}
