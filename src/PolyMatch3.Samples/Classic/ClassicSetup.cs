using PolyMatch3.Matcher;

namespace PolyMatch3.Samples.Classic
{
    /// <summary>
    /// 特殊棋子种类（kind 平行数组的值，0=普通）。
    /// 四连=线弹（分方向）、十字/T字=星弹、五连=宝石。
    /// </summary>
    public static class SpecialKind
    {
        /// <summary>横消弹：触发时消除所在**行**（竖着四连生成）。</summary>
        public const int LineH = 1;
        /// <summary>竖消弹：触发时消除所在**列**（横着四连生成）。</summary>
        public const int LineV = 2;
        /// <summary>星形弹：触发时消除星形范围（正交两格 + 斜角一格；十字/T字生成）。</summary>
        public const int Star = 3;
        /// <summary>宝石：与普通棋子交换清除全棋盤该颜色；与其他特殊子交换有联动（见 GemInteractStep）。</summary>
        public const int Gem = 4;
    }

    /// <summary>
    /// 传统矩形三消的图案集与生成规则（代码即配置）。
    /// 优先级：五连(100) &gt; 十字(95) &gt; T字(90) &gt; 四连(80) &gt; 三连(10)。
    /// 四连变体序约定：0,1=竖线（U/D 臂）2,3=横线（L/R 臂）——生成线弹方向依赖此约定。
    /// </summary>
    public static class ClassicSetup
    {
        public const int U = 0, D = 1, L = 2, R = 3;

        public static Pattern[] CreatePatterns()
        {
            return new[]
            {
                new Pattern("五连", 100,
                    new[] { (U, 2), (D, 2) },
                    new[] { (L, 2), (R, 2) }),
                new Pattern("十字", 95,
                    new[] { (U, 1), (D, 1), (L, 1), (R, 1) }),
                new Pattern("T字", 90,
                    new[] { (U, 1), (L, 1), (R, 1) },
                    new[] { (D, 1), (L, 1), (R, 1) },
                    new[] { (L, 1), (U, 1), (D, 1) },
                    new[] { (R, 1), (U, 1), (D, 1) }),
                new Pattern("四连", 80,
                    new[] { (U, 1), (D, 2) },   // 变体0：竖线
                    new[] { (D, 1), (U, 2) },   // 变体1：竖线
                    new[] { (L, 1), (R, 2) },   // 变体2：横线
                    new[] { (R, 1), (L, 2) }),  // 变体3：横线
                new Pattern("三连", 10,
                    new[] { (U, 1), (D, 1) },
                    new[] { (L, 1), (R, 1) }),
            };
        }

        /// <summary>匹配组 → 特殊棋子种类；三连等无生成物的返回 0。</summary>
        public static int KindFor(MatchGroup group)
        {
            switch (group.PatternId)
            {
                case "四连": return group.VariantIndex < 2 ? SpecialKind.LineH : SpecialKind.LineV;
                case "十字":
                case "T字": return SpecialKind.Star;
                case "五连": return SpecialKind.Gem;
                default: return 0;
            }
        }
    }
}
