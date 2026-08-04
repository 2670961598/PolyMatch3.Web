using System;
using System.Collections.Generic;

namespace PolyMatch3.Matcher
{
    /// <summary>
    /// 兼容外观：等价于 MatchArbiters.Containment（仲裁 v2：完全包含才压制，部分重叠共存）。
    /// 保留给既有调用方与差分测试；新代码请用 ArbitrateStep + 任意 IMatchArbiter（见 MatchArbiters）。
    /// </summary>
    public static class MatchArbitrator
    {
        /// <summary>
        /// 执行仲裁（覆盖去重）。groups 为匹配器原始输出（不修改），cellCount 保留用于签名兼容（当前实现不需要工作数组）。
        /// 返回新的存活组列表。
        /// </summary>
        public static List<MatchGroup> Arbitrate(List<MatchGroup> groups, int cellCount)
        {
            if (groups == null) throw new ArgumentNullException(nameof(groups));
            if (cellCount <= 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
            return MatchArbiters.Containment.Arbitrate(null, groups);
        }
    }
}
