#if UNITY_5_3_OR_NEWER
using UnityEngine;

namespace PolyMatch3.Logging
{
    /// <summary>
    /// 输出到 Unity 控制台（Debug.Log / LogWarning / LogError）。
    /// 本文件仅在 Unity 编译环境生效（UNITY_5_3_OR_NEWER），纯 .NET 编译时为空编译单元，
    /// 因此框架保持"零 Unity 依赖"的同时，在 Unity 内开箱可用。
    /// </summary>
    public sealed class UnityDebugLogSink : ILogSink
    {
        public void Emit(in LogEntry entry)
        {
            var text = entry.ToString();
            switch (entry.Level)
            {
                case LogLevel.Warning:
                    Debug.LogWarning(text);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    Debug.LogError(text);
                    break;
                default:
                    Debug.Log(text);
                    break;
            }
        }

        public void Flush() { }
    }
}
#endif
