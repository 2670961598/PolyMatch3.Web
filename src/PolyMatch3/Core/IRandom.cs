namespace PolyMatch3.Core
{
    /// <summary>
    /// 确定性随机源接口。
    /// 军规：一切随机消费只经此接口（通常挂在 StepContext.Random 上），
    /// 禁止在 Step 内 new Random / 使用 UnityEngine.Random —— 这是"同种子+同输入=同结果"的地基。
    /// </summary>
    public interface IRandom
    {
        /// <summary>返回 [0, maxExclusive) 的整数。</summary>
        int Next(int maxExclusive);

        /// <summary>返回 [minInclusive, maxExclusive) 的整数。</summary>
        int Next(int minInclusive, int maxExclusive);

        /// <summary>已消耗的随机数次数（回放一致性校验用）。</summary>
        int Cursor { get; }

        /// <summary>内部状态（可序列化，用于存档/检查点）。</summary>
        ulong State0 { get; set; }
        ulong State1 { get; set; }
    }
}
