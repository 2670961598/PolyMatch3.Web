using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using PolyMatch3.Game;
using PolyMatch3.Logging;

namespace PolyMatch3.Bridge
{
    /// <summary>
    /// 【导读】JS ↔ PolyMatch3 桥接层：浏览器里 JS 唯一能碰的就是这里的 [JSExport] 函数；
    /// 逻辑层事件经 [JSImport] onGameEvent 推回 JS（在 main.js 的 setModuleImports 里注册回调）。
    ///
    /// 数据流：
    ///   JS 开局（NewGame/NewGameWithBoard）→ 返回棋盘 JSON（含 cells，炸弹模式含 kinds）；
    ///   JS 点格子 → OfferSwap(a, b) → 逻辑层异步跑连锁；
    ///   逻辑层每个游戏事件/日志 → onGameEvent(json) 推送 → 前端排队播动画；
    ///   动画播完前端再调 GetBoard() 拿权威快照校准（镜像只是动画，逻辑层为准）。
    ///
    /// 注意：种子以字符串传递（JS Number 只有 53 位精度，ulong 放不下）；
    /// 日志 Sink 幂等注册（EnsureLogSink），[Perf] 每步耗时也走同一通道。
    /// </summary>
    [SupportedOSPlatform("browser")]
    public partial class GameBridge
    {
        private static GameSession _session;
        private static bool _logSinkReady;

        /// <summary>幂等注册日志转发 Sink（Main 之外再防一道：日志事件与游戏事件走同一 JS 通道）。</summary>
        private static void EnsureLogSink()
        {
            if (_logSinkReady) return;
            Log.MinLevel = LogLevel.Info;
            Log.AddSink(new JsLogSink());
            _logSinkReady = true;
        }

        public static void Main(string[] args)
        {
            EnsureLogSink();
            Console.WriteLine("PolyMatch3 WASM bridge ready");
        }

        /// <summary>随机开局（同种子同棋盘）。mode：0=矩形 1=三角形 2=六边形；bombs≠0 开启炸弹模式。返回棋盘 JSON。</summary>
        [JSExport]
        public static string NewGame(int mode, int width, int height, int colors, string seed, int bombs)
        {
            EnsureLogSink();
            _session = GameSession.Create(mode, width, height, colors, ulong.Parse(seed), null, bombs != 0);
            _session.OnEventJson = PushEvent;
            _session.OnError = PushError;
            return _session.BoardJson();
        }

        /// <summary>指定棋盘开局（正确性测试用）。piecesCsv 为逗号分隔的棋子值（0=空，行优先）。返回棋盘 JSON。</summary>
        [JSExport]
        public static string NewGameWithBoard(int mode, int width, int height, int colors, string seed, string piecesCsv, int bombs)
        {
            EnsureLogSink();
            var parts = piecesCsv.Split(new[] { ',', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var pieces = Array.ConvertAll(parts, int.Parse);
            _session = GameSession.Create(mode, width, height, colors, ulong.Parse(seed), pieces, bombs != 0);
            _session.OnEventJson = PushEvent;
            _session.OnError = PushError;
            return _session.BoardJson();
        }

        /// <summary>玩家交换两格（非法输入逻辑层自动丢弃）。</summary>
        [JSExport]
        public static void OfferSwap(int a, int b)
        {
            _session?.OfferSwap(a, b);
        }

        /// <summary>当前棋盘 JSON。</summary>
        [JSExport]
        public static string GetBoard()
        {
            return _session?.BoardJson() ?? "{}";
        }

        /// <summary>累计消除数。</summary>
        [JSExport]
        public static int GetScore()
        {
            return _session?.Score ?? 0;
        }

        [JSImport("onGameEvent", "gameBridge")]
        internal static partial void OnGameEvent(string json);

        private static void PushEvent(string json)
        {
            OnGameEvent(json);
        }

        internal static void PushLog(string tag, string message)
        {
            var safe = (message ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
            OnGameEvent("{\"seq\":-2,\"step\":\"\",\"type\":\"Log\",\"tag\":\"" + tag + "\",\"cells\":[],\"message\":\"" + safe + "\"}");
        }

        /// <summary>日志 → JS 事件面板的转发 Sink。</summary>
        private sealed class JsLogSink : ILogSink
        {
            public void Emit(in LogEntry entry)
            {
                PushLog(entry.Tag, entry.Message);
            }

            public void Flush() { }
        }

        private static void PushError(string message)
        {
            var safe = (message ?? "未知错误").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
            OnGameEvent("{\"seq\":-1,\"step\":\"\",\"type\":\"Error\",\"cells\":[],\"message\":\"" + safe + "\"}");
        }
    }
}
