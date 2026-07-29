namespace PolyMatch3.Logging
{
    /// <summary>
    /// 日志级别。Off 仅用于 MinLevel（关闭所有日志），不用于记录。
    /// </summary>
    public enum LogLevel : byte
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Fatal = 5,
        Off = 6,
    }
}
