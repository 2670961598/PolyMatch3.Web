namespace PolyMatch3.Core
{
    /// <summary>
    /// 拓扑构造器库：非常规棋盘的建边工具（与 BuildRectNeighbors 同层，调用后仍需 FreezeTopology）。
    /// 匹配器/选择器只认"锚点 + 沿有向边行走"，与全局拓扑无关——换这些构造器即可让同一套
    /// 图案/规则跑在弯曲空间上。重力在闭合拓扑上不能用"列"模型，配 FieldGravityStep（势场重力）。
    /// </summary>
    public static class BoardBuilders
    {
        /// <summary>
        /// 环面：矩形 + 左右/上下首尾相接（每对接缝各加两条反向边，w=1 或 h=1 的退化维度跳过自环）。
        /// 图案可以跨接缝匹配（如行尾三连接到行首）。
        /// </summary>
        public static void BuildTorusNeighbors(GraphBoard board, int up = 0, int down = 1, int left = 2, int right = 3)
        {
            board.BuildRectNeighbors(up, down, left, right);
            int w = board.Width, h = board.Height;

            if (w > 1)
            {
                for (int y = 0; y < h; y++)
                {
                    int l = y * w, r = y * w + (w - 1);
                    board.AddEdge(l, left, r);
                    board.AddEdge(r, right, l);
                }
            }
            if (h > 1)
            {
                for (int x = 0; x < w; x++)
                {
                    int t = x, b = (h - 1) * w + x;
                    board.AddEdge(t, up, b);
                    board.AddEdge(b, down, t);
                }
            }
        }

        /// <summary>
        /// 莫比乌斯带：矩形 + 仅左右接缝**翻转粘接**（左缘第 y 行接到右缘第 h-1-y 行），上下不闭合。
        /// 跨接缝的直线路径会以镜像方式延续——这是"超现实拓扑"的最小例子。
        /// </summary>
        public static void BuildMobiusNeighbors(GraphBoard board, int up = 0, int down = 1, int left = 2, int right = 3)
        {
            board.BuildRectNeighbors(up, down, left, right);
            int w = board.Width, h = board.Height;
            if (w <= 1) return;

            for (int y = 0; y < h; y++)
            {
                int l = y * w;                       // 左缘第 y 行
                int r = (h - 1 - y) * w + (w - 1);   // 右缘镜像行
                board.AddEdge(l, left, r);
                board.AddEdge(r, right, l);
            }
        }
    }
}
