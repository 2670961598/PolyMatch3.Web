using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PolyMatch3.Defs
{
    /// <summary>
    /// Step 编排图定义（蓝图的数据形态）：节点 = Step 实例（catalog key + JSON 参数包），
    /// 边 = 条件转移。被 GraphStepManager 直接解释执行，无代码生成——
    /// 编辑时和运行时逻辑完全一样，后续可视化编排器编辑的就是这份 JSON。
    /// </summary>
    public sealed class StepGraphDef
    {
        /// <summary>入口节点 id（首步执行的节点）。</summary>
        [JsonProperty("entry")] public string Entry;

        [JsonProperty("nodes")] public List<StepNodeDef> Nodes;

        /// <summary>条件转移边：同一 from 节点的出边按声明序逐条判定，第一条命中的生效。</summary>
        [JsonProperty("edges")] public List<StepEdgeDef> Edges;
    }

    /// <summary>图节点：id + Step catalog key + 参数包（形状由各 Step 工厂自行解释）。</summary>
    public sealed class StepNodeDef
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("step")] public string Step;

        /// <summary>Step 参数包（可为 null，各工厂给默认值）。禁用 $type 多态：行为只经 catalog key 引用。</summary>
        [JsonProperty("params")] public JObject Params;
    }

    /// <summary>条件转移边：when 封闭集合三选一（对上一步 StepResult.Success 判定）。</summary>
    public sealed class StepEdgeDef
    {
        [JsonProperty("from")] public string From;
        [JsonProperty("to")] public string To;

        /// <summary>转移条件：always / onSuccess / onFailure（默认 always）。</summary>
        [JsonProperty("when")] public string When = StepWhen.Always;
    }

    /// <summary>转移条件的封闭集合（非法值在图加载校验期抛出）。</summary>
    public static class StepWhen
    {
        public const string Always = "always";
        public const string OnSuccess = "onSuccess";
        public const string OnFailure = "onFailure";

        public static bool IsValid(string when)
        {
            return when == Always || when == OnSuccess || when == OnFailure;
        }

        /// <summary>该条件是否命中上一步结果。</summary>
        public static bool Matches(string when, bool success)
        {
            return when == Always || (when == OnSuccess) == success;
        }
    }
}
