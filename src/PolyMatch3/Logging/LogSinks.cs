using System;
using System.Collections.Generic;
using System.IO;

namespace PolyMatch3.Logging
{
    /// <summary>
    /// 输出到 System.Console（纯 C# 环境 / 独立进程使用）。
    /// </summary>
    public sealed class ConsoleLogSink : ILogSink
    {
        public void Emit(in LogEntry entry)
        {
            Console.WriteLine(entry.ToString());
        }

        public void Flush() { }
    }

    /// <summary>
    /// 输出到文本文件（追加模式）。目录不存在时自动创建。
    /// 实现 IDisposable：进程退出前请 Dispose 或调用 Log.Flush()。
    /// </summary>
    public sealed class FileLogSink : ILogSink, IDisposable
    {
        private readonly StreamWriter _writer;

        public FileLogSink(string path, bool append = true, bool autoFlush = true)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _writer = new StreamWriter(path, append) { AutoFlush = autoFlush };
        }

        public void Emit(in LogEntry entry)
        {
            _writer.WriteLine(entry.ToString());
        }

        public void Flush()
        {
            _writer.Flush();
        }

        public void Dispose()
        {
            _writer.Dispose();
        }
    }

    /// <summary>
    /// 内存环形缓冲 Sink：保留最近 N 条。
    /// 用于单元测试断言、局内诊断面板、回放/溯源数据的抓取挂点。
    /// </summary>
    public sealed class BufferedLogSink : ILogSink
    {
        private readonly int _capacity;
        private readonly Queue<LogEntry> _entries;

        public BufferedLogSink(int capacity = 1024)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _entries = new Queue<LogEntry>(capacity);
        }

        public int Count => _entries.Count;

        public void Emit(in LogEntry entry)
        {
            if (_entries.Count >= _capacity) _entries.Dequeue();
            _entries.Enqueue(entry);
        }

        /// <summary>当前缓冲内容的快照（按时间先后排序）。</summary>
        public LogEntry[] Snapshot()
        {
            return _entries.ToArray();
        }

        public void Clear()
        {
            _entries.Clear();
        }

        public void Flush() { }
    }

    /// <summary>
    /// 丢弃所有日志的占位 Sink（单例）。
    /// </summary>
    public sealed class NullLogSink : ILogSink
    {
        public static readonly NullLogSink Instance = new NullLogSink();

        private NullLogSink() { }

        public void Emit(in LogEntry entry) { }

        public void Flush() { }
    }
}
