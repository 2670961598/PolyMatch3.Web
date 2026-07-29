using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Logging;

namespace PolyMatch3.Step
{
    /// <summary>
    /// 编排器：游戏流程主循环。
    /// 单线程驱动（DecideNext → await Execute → 收事件 → 维护 History/StepIndex → 终止判定），
    /// 保证日志与历史的顺序确定；Step 内部可以多线程（如匹配器），正确性由 Step 自己保证。
    /// 同时是日志与事件的双重单一写入者：日志/事件 Seq 全局单调——回放/溯源的挂点。
    /// </summary>
    public sealed class Orchestrator
    {
        private readonly GraphBoard _board;
        private readonly IStepManager _manager;
        private readonly StepContext _ctx;
        private readonly List<IEventSink> _eventSinks = new List<IEventSink>();
        private long _seq;
        private long _eventSeq;

        public Orchestrator(GraphBoard board, IStepManager manager, StepContext ctx)
        {
            _board = board ?? throw new ArgumentNullException(nameof(board));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        /// <summary>注册事件出口（表现层/回放/测试探针）。</summary>
        public void AddEventSink(IEventSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            _eventSinks.Add(sink);
        }

        public bool RemoveEventSink(IEventSink sink)
        {
            return _eventSinks.Remove(sink);
        }

        /// <summary>
        /// 驱动主循环直到 StepManager 宣告结束，返回维护完毕的上下文（含完整 History）。
        /// maxSteps 是死循环保险丝。
        /// </summary>
        public async Task<StepContext> RunAsync(int maxSteps = 10000)
        {
            StepResult last = null;

            while (true)
            {
                var decision = await _manager.DecideNextAsync(_board, _ctx, last).ConfigureAwait(false);

                if (!decision.HasNext)
                {
                    _ctx.Logger.Write(LogLevel.Info, "Orch", $"[Seq {_seq++}] 流程结束：{decision.EndReason}");
                    break;
                }

                var step = decision.Step;
                var attrs = step.Attributes;

                _ctx.Logger.Write(LogLevel.Debug, "Orch", $"[Seq {_seq++}] StepBegin #{_ctx.StepIndex} {step.Name}");
                if (attrs.IsUserInput)
                    _ctx.Logger.Write(LogLevel.Info, "Orch", $"[Seq {_seq++}] 等待玩家输入：{step.Name}");
                else if (attrs.IsBlocking)
                    _ctx.Logger.Write(LogLevel.Info, "Orch", $"[Seq {_seq++}] 阻塞等待外部条件：{step.Name}");

                for (int i = 0; i < _eventSinks.Count; i++)
                    _eventSinks[i].OnStepBegin(_ctx.StepIndex, step.Name);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await step.ExecuteAsync(_board, _ctx).ConfigureAwait(false);
                sw.Stop();
                _ctx.Logger.Write(LogLevel.Info, "Perf", $"[Seq {_seq++}] {step.Name} 耗时 {sw.Elapsed.TotalMilliseconds:F2}ms（事件 {result.Events.Count}）");

                _ctx.History.Add(result);
                _ctx.StepIndex++;
                _ctx.Logger.Write(LogLevel.Debug, "Orch", $"[Seq {_seq++}] StepEnd {step.Name} Success={result.Success} Events={result.Events.Count}");

                // 事件盖章分发（Seq 全局单调，与日志同一线程，顺序确定）
                if (result.Events != null)
                {
                    foreach (var ev in result.Events)
                    {
                        var envelope = new GameEventEnvelope(_eventSeq++, _ctx.StepIndex - 1, step.Name, ev);
                        for (int i = 0; i < _eventSinks.Count; i++)
                            _eventSinks[i].OnEvent(in envelope);
                    }
                }
                for (int i = 0; i < _eventSinks.Count; i++)
                    _eventSinks[i].OnStepEnd(_ctx.StepIndex - 1, step.Name);

                if (_ctx.StepIndex >= maxSteps)
                    throw new InvalidOperationException($"Orchestrator 超过 maxSteps({maxSteps})，疑似死循环（最后的 Step：{step.Name}）");

                last = result;
            }

            return _ctx;
        }
    }
}
