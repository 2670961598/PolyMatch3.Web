using System;
using System.Collections.Generic;

namespace PolyMatch3.Logging
{
    /// <summary>
    /// 日志静态门面：框架与业务的统一入口。输出目标（Sink）可插拔、可组合。
    /// 纯 BCL 零依赖，线程安全（Emit 串行化）。
    /// 用法：游戏启动时 Log.AddSink(...) 一次，之后各处 Log.Info(tag, msg) 即可。
    /// </summary>
    public static class Log
    {
        /// <summary>最低输出级别，低于此级别直接丢弃。</summary>
        public static LogLevel MinLevel = LogLevel.Debug;

        private static readonly List<ILogSink> _sinks = new List<ILogSink>();
        private static readonly object _gate = new object();

        public static int SinkCount
        {
            get { lock (_gate) return _sinks.Count; }
        }

        public static void AddSink(ILogSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            lock (_gate) _sinks.Add(sink);
        }

        public static bool RemoveSink(ILogSink sink)
        {
            lock (_gate) return _sinks.Remove(sink);
        }

        public static void ClearSinks()
        {
            lock (_gate) _sinks.Clear();
        }

        public static bool IsEnabled(LogLevel level)
        {
            return level >= MinLevel;
        }

        public static void Write(LogLevel level, string tag, string message, Exception exception = null)
        {
            if (level < MinLevel) return;
            lock (_gate)
            {
                if (_sinks.Count == 0) return;
                var entry = new LogEntry(level, tag, message, exception);
                foreach (var sink in _sinks)
                    sink.Emit(in entry);
            }
        }

        public static void Trace(string tag, string message) { Write(LogLevel.Trace, tag, message); }
        public static void Debug(string tag, string message) { Write(LogLevel.Debug, tag, message); }
        public static void Info(string tag, string message) { Write(LogLevel.Info, tag, message); }
        public static void Warn(string tag, string message) { Write(LogLevel.Warning, tag, message); }
        public static void Error(string tag, string message, Exception exception = null) { Write(LogLevel.Error, tag, message, exception); }
        public static void Fatal(string tag, string message, Exception exception = null) { Write(LogLevel.Fatal, tag, message, exception); }

        public static void Flush()
        {
            lock (_gate)
            {
                foreach (var sink in _sinks)
                    sink.Flush();
            }
        }
    }
}
