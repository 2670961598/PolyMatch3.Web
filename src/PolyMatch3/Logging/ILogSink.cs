namespace PolyMatch3.Logging
{
    /// <summary>
    /// 日志输出目标。不同平台/需求填充不同实现：
    /// Unity 编辑器用 UnityDebugLogSink，独立进程用 ConsoleLogSink/FileLogSink，
    /// 测试与局内诊断用 BufferedLogSink，关闭日志用 NullLogSink。
    /// 并发契约：实现可假设 Emit 已被 Log 门面串行化（锁内调用）——
    /// 绕过门面直接多线程调 Emit 是不受支持的用法，Sink 实现自身无需加锁。
    /// </summary>
    public interface ILogSink
    {
        void Emit(in LogEntry entry);

        /// <summary>冲刷缓冲（无缓冲的实现空实现即可）。</summary>
        void Flush();
    }
}
