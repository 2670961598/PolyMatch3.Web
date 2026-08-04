using System.Collections.Generic;
using PolyMatch3.Core;

namespace PolyMatch3.Matcher
{
    /// <summary>
    /// 匹配仲裁器（去重策略）：纯函数工具，输入棋盘 + 匹配器原始输出（未去重的全量组），
    /// 输出存活组列表。"哪些匹配算数"完全由本接口的实现决定——
    /// 不去重、完全包含压制、任意重叠压制等各为一种实现，玩法可自定义并注册。
    /// 约束（框架确定性军规）：同输入必须同输出；不得修改输入列表与组对象；
    /// 输出顺序完全确定（约定：优先级降序，同优先级保持输入顺序）。
    /// Id 为稳定字符串标识：配置/编辑器序列化存 Id，运行时经 MatchArbiters 注册表解析回实例。
    /// </summary>
    public interface IMatchArbiter
    {
        /// <summary>稳定标识（全局唯一，注册表键）。内置：none / containment / overlap。</summary>
        string Id { get; }

        /// <summary>执行仲裁。返回新的存活组列表（不修改 raw）。</summary>
        List<MatchGroup> Arbitrate(GraphBoard board, IReadOnlyList<MatchGroup> raw);
    }
}
