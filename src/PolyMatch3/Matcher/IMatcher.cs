using System.Collections.Generic;
using PolyMatch3.Core;

namespace PolyMatch3.Matcher
{
    /// <summary>
    /// 匹配器接口。
    /// 输入棋盘，输出 MatchGroup 列表。
    /// </summary>
    public interface IMatcher
    {
        /// <summary>
        /// 执行全图匹配。
        /// </summary>
        List<MatchGroup> Match(GraphBoard board);
    }
}
