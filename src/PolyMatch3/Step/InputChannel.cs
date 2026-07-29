using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PolyMatch3.Step
{
    /// <summary>
    /// 输入通道：表现层 → 逻辑层的唯一合法输入路径。
    /// 表现层 Offer（点击/滑动/技能），输入型 Step（IsUserInput）await WaitAsync 消费；
    /// 消费点即回放记录点。线程安全；同一时刻只允许一个等待者（输入 Step 应串行消费）。
    /// </summary>
    public sealed class InputChannel<T>
    {
        private readonly object _gate = new object();
        private readonly Queue<T> _pending = new Queue<T>();
        private TaskCompletionSource<T> _waiter;

        /// <summary>当前积压的输入数量（诊断用）。</summary>
        public int PendingCount
        {
            get { lock (_gate) return _pending.Count; }
        }

        /// <summary>表现层投递一次输入。无等待者时入积压队列，有等待者时直接交付。</summary>
        public void Offer(T input)
        {
            lock (_gate)
            {
                if (_waiter != null)
                {
                    var waiter = _waiter;
                    _waiter = null;
                    waiter.TrySetResult(input);
                }
                else
                {
                    _pending.Enqueue(input);
                }
            }
        }

        /// <summary>输入型 Step 等待下一次输入（先消费积压队列，再挂起等待）。</summary>
        public Task<T> WaitAsync()
        {
            lock (_gate)
            {
                if (_pending.Count > 0)
                    return Task.FromResult(_pending.Dequeue());

                if (_waiter != null)
                    throw new InvalidOperationException("InputChannel 已有等待者：输入 Step 应串行消费");

                _waiter = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _waiter.Task;
            }
        }
    }
}
