namespace PolyMatch3.Step
{
    /// <summary>
    /// 游戏事件基类。具体事件（交换/匹配/消除/下落/生成……）继承它并携带结构化字段。
    /// 事件是回放日志的载荷；表现层分发在 Unity 阶段对接。
    /// </summary>
    public abstract class GameEvent
    {
        public string Type = "";
        public int[] CellIds = System.Array.Empty<int>();
    }
}
