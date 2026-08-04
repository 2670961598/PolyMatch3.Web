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

    /// <summary>洗牌事件（CellIds = 被重排的格子，前端整批刷新即可）。</summary>
    public sealed class ShuffleEvent : GameEvent
    {
        public ShuffleEvent(int[] cellIds)
        {
            Type = "Shuffle";
            CellIds = cellIds;
        }
    }

    /// <summary>
    /// 计分事件（每步消除至多一个）：Delta = 本次得分，Total = 累计总分；
    /// Sources 与 Deltas 一一对应（修饰符应用顺序的分明细，前端只挑关键源展示）。
    /// </summary>
    public sealed class ScoreEvent : GameEvent
    {
        public readonly int Delta;
        public readonly int Total;
        public readonly string[] Sources;
        public readonly int[] Deltas;

        public ScoreEvent(int delta, int total, System.Collections.Generic.List<(string source, int delta)> contributions)
        {
            Type = "Score";
            Delta = delta;
            Total = total;
            CellIds = new int[0];
            Sources = new string[contributions.Count];
            Deltas = new int[contributions.Count];
            for (int i = 0; i < contributions.Count; i++)
            {
                Sources[i] = contributions[i].source;
                Deltas[i] = contributions[i].delta;
            }
        }
    }
}
