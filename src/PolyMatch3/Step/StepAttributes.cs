namespace PolyMatch3.Step
{
    /// <summary>
    /// Step 声明式属性：描述 Step 的运行时特征，由 Orchestrator 消费。
    /// 框架只定义有真实消费者的标志；这是一个 class ——
    /// 玩法可继承扩展自己的属性（class MyAttributes : StepAttributes { public bool NeedsNetwork; }），
    /// Orchestrator 只读基类字段，玩法系统读派生字段。
    /// </summary>
    public class StepAttributes
    {
        /// <summary>
        /// 是否阻塞：该 Step 会等待外部条件（玩家输入、网络确认等），其 Task 不完成 Orchestrator 不推进。
        /// Orchestrator 在日志中标记阻塞点（回放/排查的关键锚点）。
        /// </summary>
        public bool IsBlocking;

        /// <summary>
        /// 是否依赖玩家输入。Orchestrator 据此识别"等待玩家"状态（回放日志的输入记录点）。
        /// </summary>
        public bool IsUserInput;
    }
}
