using System.Collections.Generic;
using Newtonsoft.Json;
using PolyMatch3.Core;
using PolyMatch3.Matcher;

namespace PolyMatch3.Defs
{
    /// <summary>
    /// 图案集定义（数据层）→ Pattern[]（运行层）。
    /// 臂按<strong>边名</strong>引用，构建时对照棋盘的 EdgeTypeRegistry 解析为索引；
    /// 名字不存在 → 加载期抛可行动错误。图案自身合法性（Id/优先级唯一、变体非空、步数 ≥1）
    /// 由 FixedPatternMatcher 构造时全量校验（L1 检查原样生效）。
    /// </summary>
    public sealed class PatternSetDef
    {
        [JsonProperty("patterns")] public List<PatternDef> Patterns;

        /// <summary>按棋盘边词汇表构建 Pattern[]。</summary>
        public Pattern[] ToPatterns(EdgeTypeRegistry edgeTypes)
        {
            if (edgeTypes == null) throw new DefsException("PatternSetDef.ToPatterns 需要棋盘边词汇表（EdgeTypeRegistry），传入了 null。");
            if (Patterns == null || Patterns.Count == 0)
                throw new DefsException("PatternSetDef.patterns 为空：关卡至少需要一个匹配图案（如\"三连\"）。");

            var result = new Pattern[Patterns.Count];
            for (int i = 0; i < Patterns.Count; i++)
            {
                var p = Patterns[i];
                if (p == null)
                    throw new DefsException($"PatternSetDef.patterns[{i}] 为 null：请删除该空项或补全图案定义。");
                if (string.IsNullOrEmpty(p.Name))
                    throw new DefsException($"PatternSetDef.patterns[{i}] 缺少 name：每个图案必须有名字（用于日志与匹配组溯源）。");
                if (p.Variants == null || p.Variants.Count == 0)
                    throw new DefsException($"图案 '{p.Name}' 未定义任何变体（variants 为空）：请补充至少一个变体，如 [[{{\"edge\":\"Up\",\"steps\":1}},{{\"edge\":\"Down\",\"steps\":1}}]]。");

                var variants = new (int edge, int steps)[p.Variants.Count][];
                for (int v = 0; v < p.Variants.Count; v++)
                {
                    var armDefs = p.Variants[v];
                    if (armDefs == null || armDefs.Count == 0)
                        throw new DefsException($"图案 '{p.Name}' 的变体[{v}] 未定义任何臂：变体至少需要一条臂。");

                    var arms = new (int edge, int steps)[armDefs.Count];
                    for (int a = 0; a < armDefs.Count; a++)
                    {
                        var arm = armDefs[a];
                        if (arm == null)
                            throw new DefsException($"图案 '{p.Name}' 变体[{v}] 的臂[{a}] 为 null：请删除该空项。");
                        if (arm.Steps < 1)
                            throw new DefsException($"图案 '{p.Name}' 变体[{v}] 臂[{a}]（边 '{arm.Edge}'）的 steps = {arm.Steps} 非法（必须 ≥ 1）。请修正 steps。");
                        arms[a] = (BoardDef.ResolveEdgeName(edgeTypes, arm.Edge, $"图案 '{p.Name}' 变体[{v}] 臂[{a}]"), arm.Steps);
                    }
                    variants[v] = arms;
                }

                result[i] = new Pattern(p.Name, p.Priority, variants);
            }
            return result;
        }
    }

    /// <summary>图案定义：名字 + 全局唯一优先级 + 多个匹配变体（OR 语义）。</summary>
    public sealed class PatternDef
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("priority")] public int Priority;

        /// <summary>变体列表（OR）：每个变体是一组臂（AND 语义）。</summary>
        [JsonProperty("variants")] public List<List<ArmDef>> Variants;
    }

    /// <summary>图案臂：沿 edge 边从锚点走 steps 步。</summary>
    public sealed class ArmDef
    {
        [JsonProperty("edge")] public string Edge;
        [JsonProperty("steps")] public int Steps = 1;
    }
}
