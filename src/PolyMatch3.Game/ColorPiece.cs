using PolyMatch3.Core;

namespace PolyMatch3.Game
{
    /// <summary>普通颜色棋子（无钩子）：id 即注册顺序（1..colorCount）。</summary>
    public sealed class ColorPiece : IPiece
    {
        public ColorPiece(string id) { Id = id; }
        public string Id { get; }
    }
}
