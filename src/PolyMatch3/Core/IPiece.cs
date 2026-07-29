namespace PolyMatch3.Core
{
    /// <summary>
    /// 棋子逻辑契约：纯逻辑、无状态。
    /// 棋子类型 = 棋盘上的一个 int（0=空，硬约定），行为全部以函数形式提供；
    /// 实例级运行时数据（引信、计数器等）由玩法侧平行数组持有，不挂在棋子上。
    /// 回调钩子（生成/消除等）是编排层的可选扩展，见 Step 层的 IPieceHooks——
    /// Core 只关心"这个棋子是谁"。
    /// </summary>
    public interface IPiece
    {
        /// <summary>棋子名（注册表内唯一，用于日志/调试/配置引用）。</summary>
        string Id { get; }
    }
}
