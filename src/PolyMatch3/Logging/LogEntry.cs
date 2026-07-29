using System;

namespace PolyMatch3.Logging
{
    /// <summary>
    /// 单条日志记录。值类型，除 Tag/Message 字符串外不引入额外分配。
    /// </summary>
    public readonly struct LogEntry
    {
        /// <summary>UTC 时间戳（Ticks）。仅用于展示与人工排查，不作为任何排序/回放依据。</summary>
        public readonly long UtcTicks;
        public readonly LogLevel Level;
        /// <summary>模块标签，如 "Matcher" / "Orchestrator"。</summary>
        public readonly string Tag;
        public readonly string Message;
        public readonly Exception Exception;

        public LogEntry(LogLevel level, string tag, string message, Exception exception = null)
        {
            UtcTicks = DateTime.UtcNow.Ticks;
            Level = level;
            Tag = tag ?? "";
            Message = message ?? "";
            Exception = exception;
        }

        public override string ToString()
        {
            var time = new DateTime(UtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss.fff");
            var text = $"[{time}][{Level}][{Tag}] {Message}";
            return Exception == null ? text : $"{text}\n{Exception}";
        }
    }
}
