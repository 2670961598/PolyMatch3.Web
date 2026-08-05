using Newtonsoft.Json;

namespace PolyMatch3.Defs
{
    /// <summary>
    /// 关卡定义（数据层）= 棋盘 + 图案集 + 棋子集 + Step 编排图 + 初始化 + 种子。
    /// 四块定义内联在关卡里（自包含，利于 UGC 单文件分享）；
    /// 跨文件引用复用（boardRef 等）留接口不实现，等编辑器期出现真实复用需求再做。
    /// </summary>
    public sealed class LevelDef
    {
        [JsonProperty("name")] public string Name;

        /// <summary>确定性种子：初始化填充与 Refill 共用同一随机源，同种子整局可复现。</summary>
        [JsonProperty("seed")] public ulong Seed;

        /// <summary>随机填充/补充用的颜色数（棋子 id 1..colors）。必须 ≤ 棋子集已注册棋子数。</summary>
        [JsonProperty("colors")] public int Colors;

        [JsonProperty("board")] public BoardDef Board;
        [JsonProperty("patterns")] public PatternSetDef Patterns;
        [JsonProperty("pieces")] public PieceSetDef Pieces;
        [JsonProperty("stepGraph")] public StepGraphDef StepGraph;

        [JsonProperty("init")] public InitDef Init;
    }

    /// <summary>开局初始化：random / randomNoMatch / fixed 三选一。</summary>
    public sealed class InitDef
    {
        /// <summary>初始化方式："random"（种子随机）/ "randomNoMatch"（种子随机且无初始匹配）/ "fixed"（全指定）。</summary>
        [JsonProperty("kind")] public string Kind;

        /// <summary>kind = "fixed" 时的全棋盘棋子值（长度必须 = 格子数；0 = 空）。</summary>
        [JsonProperty("fixedPieces")] public int[] FixedPieces;
    }
}
