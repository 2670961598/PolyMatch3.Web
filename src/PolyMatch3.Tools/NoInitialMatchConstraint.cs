using PolyMatch3.Core;
using PolyMatch3.Matcher;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 约束：初始棋盘无任何匹配（经典三消开局要求）。
    /// 用法：BoardInitializer.FillRandom(board, rng, colors, new NoInitialMatchConstraint(matcher));
    /// </summary>
    public sealed class NoInitialMatchConstraint : IBoardFillConstraint
    {
        private readonly IMatcher _matcher;

        public NoInitialMatchConstraint(IMatcher matcher)
        {
            _matcher = matcher ?? throw new System.ArgumentNullException(nameof(matcher));
        }

        public bool Accept(GraphBoard board)
        {
            return _matcher.Match(board).Count == 0;
        }
    }
}
