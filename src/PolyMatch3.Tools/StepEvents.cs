using PolyMatch3.Step;

namespace PolyMatch3.Tools
{
    /// <summary>棋子交换事件（CellIds = [a, b]）。</summary>
    public sealed class SwapEvent : GameEvent
    {
        public SwapEvent(int a, int b)
        {
            Type = "Swap";
            CellIds = new[] { a, b };
        }
    }

    /// <summary>棋子消除事件（CellIds = 被清空的格子，已并集去重）。</summary>
    public sealed class EliminateEvent : GameEvent
    {
        public EliminateEvent(int[] cellIds)
        {
            Type = "Eliminate";
            CellIds = cellIds;
        }
    }

    /// <summary>棋子下落事件。FromTo = [from0, to0, from1, to1, ...]（最终位置映射）。</summary>
    public sealed class FallEvent : GameEvent
    {
        public readonly int[] FromTo;

        public FallEvent(int[] fromTo, int[] cellIds)
        {
            Type = "Fall";
            FromTo = fromTo;
            CellIds = cellIds;
        }
    }

    /// <summary>棋子生成事件。CellIds 与 PieceTypes 一一对应。</summary>
    public sealed class SpawnEvent : GameEvent
    {
        public readonly int[] PieceTypes;

        public SpawnEvent(int[] cellIds, int[] pieceTypes)
        {
            Type = "Spawn";
            CellIds = cellIds;
            PieceTypes = pieceTypes;
        }
    }
}
