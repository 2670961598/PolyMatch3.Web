using PolyMatch3.Core;

namespace PolyMatch3.Step
{
    /// <summary>
    /// 棋子回调钩子（可选实现）：棋子在棋局事件点被触发。
    /// 触发与调度（时机、顺序、连锁、防环）由编排层/工具层负责——
    /// 约定一律按格 id 升序触发、随机只走 ctx.Random，确定性不破。
    /// 棋子保持无状态：实例级数据由玩法侧平行数组持有，经 board/ctx 读写。
    /// </summary>
    public interface IPieceHooks
    {
        /// <summary>棋子生成到 cellId 时触发（Refill/生成类工具调用）。默认空实现由 PieceHookBase 提供。</summary>
        void OnSpawn(GraphBoard board, int cellId, StepContext ctx);

        /// <summary>棋子被消除（格子即将置 0）时触发（消除类工具调用）。</summary>
        void OnEliminate(GraphBoard board, int cellId, StepContext ctx);
    }

    /// <summary>全空实现的便捷基类：只覆盖关心的钩子。</summary>
    public abstract class PieceHookBase : IPieceHooks
    {
        public virtual void OnSpawn(GraphBoard board, int cellId, StepContext ctx) { }
        public virtual void OnEliminate(GraphBoard board, int cellId, StepContext ctx) { }
    }
}
