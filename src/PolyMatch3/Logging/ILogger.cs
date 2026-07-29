namespace PolyMatch3.Logging
{
    /// <summary>
    /// 日志注入接口：Step/Orchestrator 通过 StepContext.Logger 写日志，
    /// 测试时可替换为探针实现，生产环境默认 <see cref="FacadeLogger"/> 转发到 Log 门面。
    /// </summary>
    public interface ILogger
    {
        void Write(LogLevel level, string tag, string message);
    }

    /// <summary>
    /// 转发到静态门面 <see cref="Log"/> 的默认实现（单例）。
    /// </summary>
    public sealed class FacadeLogger : ILogger
    {
        public static readonly FacadeLogger Instance = new FacadeLogger();

        private FacadeLogger() { }

        public void Write(LogLevel level, string tag, string message)
        {
            Log.Write(level, tag, message);
        }
    }
}
