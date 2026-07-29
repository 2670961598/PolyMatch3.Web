namespace PolyMatch3.Step
{
    /// <summary>
    /// 事件出口：逻辑层 → 表现层/回放/测试的事件订阅点。
    /// 由 Orchestrator 在每步边界驱动：OnStepBegin → OnEvent×N → OnStepEnd。
    /// 视图把事件入队后按自己的节奏播放动画；回放器按 Seq 落盘。
    /// </summary>
    public interface IEventSink
    {
        /// <summary>一个 Step 即将执行（动画可开始新批次）。</summary>
        void OnStepBegin(int stepIndex, string stepName);

        /// <summary>一个事件产生（按 Seq 单调顺序到达）。</summary>
        void OnEvent(in GameEventEnvelope envelope);

        /// <summary>一个 Step 执行完毕（本批事件到齐）。</summary>
        void OnStepEnd(int stepIndex, string stepName);
    }
}
