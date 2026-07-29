using PolyMatch3.Step;

namespace PolyMatch3.Samples.Classic
{
    /// <summary>特殊棋子生成事件（CellIds=[锚点格]，Kind=特殊种类）。</summary>
    public sealed class SpecialSpawnEvent : GameEvent
    {
        public readonly int Kind;

        public SpecialSpawnEvent(int cell, int kind)
        {
            Type = "SpecialSpawn";
            CellIds = new[] { cell };
            Kind = kind;
        }
    }

    /// <summary>棋子转换事件（宝石联动：一批格子变为某种特殊棋子）。</summary>
    public sealed class TransformEvent : GameEvent
    {
        public readonly int Kind;

        public TransformEvent(int[] cells, int kind)
        {
            Type = "Transform";
            CellIds = cells;
            Kind = kind;
        }
    }
}
