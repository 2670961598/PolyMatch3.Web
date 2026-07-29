using PolyMatch3.Matcher;

namespace PolyMatch3.Step
{
    /// <summary>匹配命中事件（一组一个）。</summary>
    public sealed class MatchEvent : GameEvent
    {
        public readonly int Priority;
        public readonly int VariantIndex;

        public MatchEvent(MatchGroup group)
        {
            Type = "Match";
            CellIds = group.CellIds.ToArray();
            Priority = group.Priority;
            VariantIndex = group.VariantIndex;
        }
    }
}
