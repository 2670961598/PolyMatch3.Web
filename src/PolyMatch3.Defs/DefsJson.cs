using System;
using Newtonsoft.Json;

namespace PolyMatch3.Defs
{
    /// <summary>
    /// 数据定义层异常：一切加载期非法配置以此抛出。
    /// 消息军规：中文、可行动——含出问题的对象、实际值、修法。
    /// </summary>
    public sealed class DefsException : Exception
    {
        public DefsException(string message) : base(message) { }
    }

    /// <summary>
    /// Defs JSON 门面（Newtonsoft）：权威格式 = 纯 C# DTO + JSON。
    /// 稳定格式（可读缩进、null 字段省略）；禁用 $type 多态——多态一律走显式字符串 key + catalog 解析。
    /// 编辑器、运行时、服务器验证、回放共用这一份 schema。
    /// </summary>
    public static class DefsJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            // $type 多态永不启用：TypeNameHandling 保持 None（默认值，此处显式声明以固化约定）
            TypeNameHandling = TypeNameHandling.None,
        };

        /// <summary>解析 JSON 为 Def 对象。JSON 语法错误 → DefsException（含行列位置）。</summary>
        public static T Parse<T>(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new DefsException("JSON 内容为空：请提供合法的关卡/定义 JSON 文本。");
            try
            {
                var result = JsonConvert.DeserializeObject<T>(json, Settings);
                if (result == null)
                    throw new DefsException($"JSON 解析结果为空（目标类型 {typeof(T).Name}）：请检查 JSON 顶层结构。");
                return result;
            }
            catch (JsonException e)
            {
                throw new DefsException($"JSON 解析失败：{e.Message}。请对照 schema 检查语法（括号/逗号/引号/类型）。");
            }
        }

        /// <summary>序列化 Def 对象为 JSON（可读缩进、null 字段省略、字段序稳定）。</summary>
        public static string ToJson<T>(T def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return JsonConvert.SerializeObject(def, Settings);
        }
    }
}
