using System.Threading.Tasks;
using PolyMatch3.Core;

namespace PolyMatch3.Step
{
    /// <summary>
    /// Step 接口：对世界状态的一次操作意图 + 执行逻辑 + 执行结果。
    /// 原子 Step 与复合 Step 均实现此接口。
    /// </summary>
    public interface IStep
    {
        string Name { get; }
        StepAttributes Attributes { get; }

        /// <summary>
        /// 执行 Step 逻辑。计算型 Step 立即返回已完成任务，等待型 Step 真正异步等待。
        /// 实现可直接读写棋盘，或通过 ctx 获取随机源/日志/黑板/历史。
        /// </summary>
        Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx);
    }
}
