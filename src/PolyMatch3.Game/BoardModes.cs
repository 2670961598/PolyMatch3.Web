using System;
using PolyMatch3.Core;
using PolyMatch3.Matcher;

namespace PolyMatch3.Game
{
    /// <summary>
    /// 棋盘模式工厂：矩形 / 三角形 / 六边形。拓扑、图案集、重力方向全部按模式配置（代码即配置）。
    /// 三角形：三条边同型（Side），匹配沿边走成"简单路径"，可拐弯、歪歪扭扭；
    /// 六边形：六向有对边（Up/Down、NE/SW、NW/SE 三轴），匹配必须是对边直线。
    /// </summary>
    public static class BoardModes
    {
        public const int Rect = 0;
        public const int Triangle = 1;
        public const int Hex = 2;

        // 矩形边索引（与 EdgeTypeRegistry.CreateRect 一致）
        private const int RU = 0, RD = 1, RL = 2, RR = 3;
        // 三角形边索引
        private const int Side = 0, TDown = 1;
        // 六边形边索引
        private const int HU = 0, HD = 1, NE = 2, SW = 3, NW = 4, SE = 5;

        public static string ModeName(int mode)
        {
            switch (mode)
            {
                case Rect: return "矩形";
                case Triangle: return "三角形";
                case Hex: return "六边形";
                default: return "未知";
            }
        }

        /// <summary>重力方向边索引（三种模式都是 Down=1，同列直落）。</summary>
        public static int GravityEdge(int mode)
        {
            switch (mode)
            {
                case Rect: return RD;
                case Triangle: return TDown;
                case Hex: return HD;
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        public static GraphBoard CreateBoard(int mode, int width, int height)
        {
            switch (mode)
            {
                case Rect: return CreateRectBoard(width, height);
                case Triangle: return CreateTriangleBoard(width, height);
                case Hex: return CreateHexBoard(width, height);
                default: throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知棋盘模式（0=矩形 1=三角形 2=六边形）");
            }
        }

        public static Pattern[] CreatePatterns(int mode)
        {
            switch (mode)
            {
                case Triangle:
                    // 单边型路径匹配：三连=沿 Side 走 2 步，可拐弯（匹配器已带简单路径约束，杜绝折返凑数）
                    return new[]
                    {
                        new Pattern("五连", 100, new[] { (Side, 4) }),
                        new Pattern("四连", 80, new[] { (Side, 3) }),
                        new Pattern("三连", 10, new[] { (Side, 2) }),
                    };
                case Hex:
                    // 三轴对边直线：每轴一组变体
                    return new[]
                    {
                        new Pattern("五连", 100,
                            new[] { (HU, 2), (HD, 2) },
                            new[] { (NE, 2), (SW, 2) },
                            new[] { (NW, 2), (SE, 2) }),
                        new Pattern("四连", 80,
                            new[] { (HU, 1), (HD, 2) }, new[] { (HD, 1), (HU, 2) },
                            new[] { (NE, 1), (SW, 2) }, new[] { (SW, 1), (NE, 2) },
                            new[] { (NW, 1), (SE, 2) }, new[] { (SE, 1), (NW, 2) }),
                        new Pattern("三连", 10,
                            new[] { (HU, 1), (HD, 1) },
                            new[] { (NE, 1), (SW, 1) },
                            new[] { (NW, 1), (SE, 1) }),
                    };
                case Rect:
                    return new[]
                    {
                        new Pattern("五连", 100,
                            new[] { (RU, 2), (RD, 2) },
                            new[] { (RL, 2), (RR, 2) }),
                        new Pattern("四连", 80,
                            new[] { (RU, 1), (RD, 2) },
                            new[] { (RD, 1), (RU, 2) },
                            new[] { (RL, 1), (RR, 2) },
                            new[] { (RR, 1), (RL, 2) }),
                        new Pattern("三连", 10,
                            new[] { (RU, 1), (RD, 1) },
                            new[] { (RL, 1), (RR, 1) }),
                    };
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知棋盘模式");
            }
        }

        // ---- 矩形：Up/Down/Left/Right 双向边 ----

        private static GraphBoard CreateRectBoard(int w, int h)
        {
            var reg = EdgeTypeRegistry.CreateRect();
            var board = new GraphBoard(w, h, reg);
            board.BuildRectNeighbors();
            board.FreezeTopology();
            return board;
        }

        // ---- 三角形：Side=0（三条边同型）+ Down=1（重力，单向）----
        // △ = (x+y) 偶数。邻居：同行左右 +（△ 下方 / ▽ 上方）。

        private static GraphBoard CreateTriangleBoard(int w, int h)
        {
            var reg = new EdgeTypeRegistry();
            reg.Register("Side");   // 0
            reg.Register("Down");   // 1
            var board = new GraphBoard(w, h, reg);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int id = y * w + x;
                    bool up = ((x + y) & 1) == 0;
                    AddTriSide(board, w, h, id, x - 1, y);
                    AddTriSide(board, w, h, id, x + 1, y);
                    AddTriSide(board, w, h, id, x, up ? y + 1 : y - 1);
                    if (y < h - 1) board.AddEdge(id, TDown, id + w); // 重力单向
                }
            }
            board.FreezeTopology();
            return board;
        }

        private static void AddTriSide(GraphBoard board, int w, int h, int id, int nx, int ny)
        {
            // 每个格子都声明自己的邻居 ⇒ 相邻双方各加一条，天然双向
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) return;
            board.AddEdge(id, Side, ny * w + nx);
        }

        // ---- 六边形（平顶，odd-q 列偏移）：Up=0 Down=1 NE=2 SW=3 NW=4 SE=5，重力 Down ----
        // 奇数列向下偏移半格。对边关系：NE↔SW、NW↔SE、Up↔Down。

        private static GraphBoard CreateHexBoard(int w, int h)
        {
            var reg = new EdgeTypeRegistry();
            reg.Register("Up");     // 0
            reg.Register("Down");   // 1
            reg.Register("NE");     // 2
            reg.Register("SW");     // 3
            reg.Register("NW");     // 4
            reg.Register("SE");     // 5
            var board = new GraphBoard(w, h, reg);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int id = y * w + x;
                    bool odd = (x & 1) == 1;
                    AddHexEdge(board, w, h, id, x, y - 1, HU);                  // Up
                    AddHexEdge(board, w, h, id, x, y + 1, HD);                  // Down
                    AddHexEdge(board, w, h, id, x + 1, odd ? y : y - 1, NE);    // NE
                    AddHexEdge(board, w, h, id, x - 1, odd ? y + 1 : y, SW);    // SW
                    AddHexEdge(board, w, h, id, x - 1, odd ? y : y - 1, NW);    // NW
                    AddHexEdge(board, w, h, id, x + 1, odd ? y + 1 : y, SE);    // SE
                }
            }
            board.FreezeTopology();
            return board;
        }

        private static void AddHexEdge(GraphBoard board, int w, int h, int id, int nx, int ny, int edge)
        {
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) return;
            board.AddEdge(id, edge, ny * w + nx);
        }
    }
}
