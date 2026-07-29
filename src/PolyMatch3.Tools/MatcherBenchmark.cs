using System;
using System.Diagnostics;
using PolyMatch3.Core;
using PolyMatch3.Matcher;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 匹配器基准测试设施（纯 C#，可在 Unity 外运行）。
    /// 用法：
    ///   var board  = MatcherBenchmark.CreateRandomBoard(100, 100, colorCount: 6, seed: 42);
    ///   var result = MatcherBenchmark.Run(board, MatcherBenchmark.CreateClassicPatterns());
    ///   Console.WriteLine(result);
    /// </summary>
    public static class MatcherBenchmark
    {
        // 矩形四方向索引（与 EdgeTypeRegistry.CreateRect 的注册顺序一致）
        public const int Up = 0;
        public const int Down = 1;
        public const int Left = 2;
        public const int Right = 3;

        /// <summary>
        /// 经典直线图案集（变体模型）：三连/四连/五连，横竖合一，优先级全局唯一。
        /// 三连 = (上1下1) | (左1右1)；四连 = 四种锚位；五连 = (上2下2) | (左2右2)。
        /// </summary>
        public static Pattern[] CreateClassicPatterns()
        {
            return new[]
            {
                new Pattern("五连", 100, new[] { (Up, 2), (Down, 2) }, new[] { (Left, 2), (Right, 2) }),
                new Pattern("四连", 80,
                    new[] { (Up, 1), (Down, 2) }, new[] { (Down, 1), (Up, 2) },
                    new[] { (Left, 1), (Right, 2) }, new[] { (Right, 1), (Left, 2) }),
                new Pattern("三连", 10, new[] { (Up, 1), (Down, 1) }, new[] { (Left, 1), (Right, 1) }),
            };
        }

        /// <summary>
        /// 创建固定种子的随机矩形棋盘（基准 / 演示 / 回放测试通用）。
        /// 使用框架确定性随机源，跨运行时/跨平台可复现。
        /// </summary>
        public static GraphBoard CreateRandomBoard(int width, int height, int colorCount, int seed)
        {
            var board = new GraphBoard(width, height, EdgeTypeRegistry.CreateRect());
            board.BuildRectNeighbors();
            board.FreezeTopology();
            BoardInitializer.FillRandom(board, new XorShift128PlusRandom((ulong)seed), colorCount);
            return board;
        }

        /// <summary>
        /// 串行/并行对比基准。强制两条路径各自运行（绕过自动调度阈值），测量纯路径耗时。
        /// </summary>
        public static BenchmarkResult Run(GraphBoard board, Pattern[] patterns, int iterations = 100, int warmup = 3)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (patterns == null) throw new ArgumentNullException(nameof(patterns));
            if (iterations <= 0) throw new ArgumentOutOfRangeException(nameof(iterations));

            var serial = new FixedPatternMatcher(patterns, parallel: false);
            // parallelWorkThreshold: 1 强制走并行路径（绕过工作量自动回退）
            var parallel = new FixedPatternMatcher(patterns, parallel: true, parallelWorkThreshold: 1);

            var sw = new Stopwatch();

            for (int i = 0; i < warmup; i++)
            {
                serial.Match(board);
                parallel.Match(board);
            }

            int matchGroupCount = 0;

            sw.Restart();
            for (int i = 0; i < iterations; i++)
                matchGroupCount = serial.Match(board).Count;
            sw.Stop();
            double serialMs = sw.Elapsed.TotalMilliseconds / iterations;

            sw.Restart();
            for (int i = 0; i < iterations; i++)
                matchGroupCount = parallel.Match(board).Count;
            sw.Stop();
            double parallelMs = sw.Elapsed.TotalMilliseconds / iterations;

            return new BenchmarkResult(serialMs, parallelMs, matchGroupCount, iterations, Environment.ProcessorCount);
        }
    }

    /// <summary>
    /// 基准结果。
    /// </summary>
    public readonly struct BenchmarkResult
    {
        public readonly double SerialMs;
        public readonly double ParallelMs;
        public readonly int MatchGroupCount;
        public readonly int Iterations;
        public readonly int ProcessorCount;

        public double Speedup => SerialMs / ParallelMs;

        public BenchmarkResult(double serialMs, double parallelMs, int matchGroupCount, int iterations, int processorCount)
        {
            SerialMs = serialMs;
            ParallelMs = parallelMs;
            MatchGroupCount = matchGroupCount;
            Iterations = iterations;
            ProcessorCount = processorCount;
        }

        public override string ToString()
        {
            return $"串行 {SerialMs:F3} ms / 并行 {ParallelMs:F3} ms（加速比 {Speedup:F2}x，{ProcessorCount} 逻辑核，单次命中 {MatchGroupCount} 组，{Iterations} 次迭代）";
        }
    }
}
