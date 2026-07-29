using System;
using System.Threading.Tasks;
using PolyMatch3.Core;

namespace PolyMatch3.Step
{
    /// <summary>
    /// 管道式 StepManager，按固定顺序依次返回 Step。
    /// 适合线性流程（如初始化 → 演示 → 结束），也是最简单的参考实现。
    /// </summary>
    public sealed class PipelineStepManager : IStepManager
    {
        private readonly IStep[] _steps;
        private int _currentIndex;
        private readonly bool _loop;

        public PipelineStepManager(IStep[] steps, bool loop = false)
        {
            _steps = steps ?? throw new ArgumentNullException(nameof(steps));
            if (steps.Length == 0) throw new ArgumentException("管道至少需要一个 Step", nameof(steps));
            _loop = loop;
        }

        public Task<StepDecision> DecideNextAsync(GraphBoard board, StepContext ctx, StepResult lastResult)
        {
            if (_currentIndex >= _steps.Length)
            {
                if (_loop)
                {
                    _currentIndex = 0;
                }
                else
                {
                    return Task.FromResult(StepDecision.End("管道执行完毕"));
                }
            }

            var step = _steps[_currentIndex];
            _currentIndex++;
            return Task.FromResult(StepDecision.Next(step));
        }
    }
}
