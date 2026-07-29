using System;

namespace PolyMatch3.Matcher
{
    /// <summary>
    /// 图案定义：一个名字 + 全局唯一优先级 + 多个匹配变体（OR 语义）。
    /// 变体 = 一组臂（AND 语义）；臂 = (边索引, 步数)，星形语义：每臂从锚点独立出发。
    /// 一种图案覆盖一类消除规则，例如"三连" = (上1下1) | (左1右1)，"四连" = 四种锚位变体。
    /// 数据载体，可继承挂载玩法数据。
    /// </summary>
    public class Pattern
    {
        public string Id = "";

        /// <summary>全局唯一优先级（仲裁模型要求：重合时高级压制低级）。</summary>
        public int Priority;

        /// <summary>
        /// 匹配变体（OR）：任一变体的全部臂命中即产生一个 MatchGroup（带变体下标）。
        /// 每个变体是一组 (边索引, 步数) 臂；同类型臂在同一变体内必须绑定不同邻居。
        /// </summary>
        public (int edge, int steps)[][] Variants = Array.Empty<(int, int)[]>();

        /// <summary>
        /// 构造图案。每个数组参数是一个变体（一组 (边索引, 步数) 臂）；单变体图案传一个数组即可。
        /// 例：new Pattern("三连", 10, new[]{(Up,1),(Down,1)}, new[]{(Left,1),(Right,1)})
        /// </summary>
        public Pattern(string id, int priority, params (int edge, int steps)[][] variants)
        {
            Id = id;
            Priority = priority;
            Variants = variants ?? Array.Empty<(int, int)[]>();
        }
    }
}
