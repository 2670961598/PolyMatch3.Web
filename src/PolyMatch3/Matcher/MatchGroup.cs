using System.Collections.Generic;

namespace PolyMatch3.Matcher
{
    /// <summary>
    /// 匹配结果组，描述一次命中所涉及的格子及图案信息。
    /// CellIds 为去重后的格子集合：锚点固定在第 0 位，其余按首次命中顺序（确定性）。
    /// 数据载体，可继承挂载玩法数据。
    /// </summary>
    public class MatchGroup
    {
        public int AnchorId;
        public List<int> CellIds;
        public string PatternId = "";
        public int Priority;

        /// <summary>命中的变体下标（对应 Pattern.Variants 的索引）。</summary>
        public int VariantIndex;
    }
}
