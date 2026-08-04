using System.Collections.Generic;

namespace PolyMatch3.Core
{
    /// <summary>
    /// 区域选择器（"找格子"原语）：以锚点为中心选出一个格子集合的纯函数策略。
    /// 用途：爆炸范围、道具/技能作用域、编辑器范围预览等一切"给我一片格子"的场景。
    /// 定义在图（沿边行走）而非坐标上 ⇒ 矩形/三角/六边/弯曲拓扑通用；
    /// 坐标系实现（RectSquareSelector）是矩形专用的显式例外，命名上标注。
    /// 约定（确定性军规）：同输入同输出；返回新列表，锚点固定第 0 位，其余按首次命中序；
    /// 只选格子、不过滤内容（空格照选，筛子由消费方叠加）；参数经构造注入。
    /// 消费方发事件前统一按格 id 升序（对齐 IPieceHooks 的调度惯例）。
    /// </summary>
    public interface ICellSelector
    {
        /// <summary>稳定标识（供配置/编辑器引用；参数化实现把参数编入 Id，如 "radius:2"）。</summary>
        string Id { get; }

        /// <summary>选出格子集合。anchor 合法性由调用方保证（棋盘内部工具，不重复校验）。</summary>
        List<int> Select(GraphBoard board, int anchorCellId);
    }
}
