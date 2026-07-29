using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;

namespace PolyMatch3.Step
{
    /// <summary>
    /// 复合 Step 基类：把多个子 Step 捆成一个"原子"（如 SwapAndValidate、技能连招）。
    /// 不限制嵌套层数，但禁止循环引用（执行期重入即抛）。
    /// 默认策略：任一子 Step 失败即短路停止；全部成功才算成功。均可重载。
    /// 组合语义（不建议但允许）：玩法自行继承重载。
    /// </summary>
    public abstract class CompositeStep : IStep
    {
        // 循环引用检测：执行链上重入即环（A→B→A）
        private int _active;

        public abstract string Name { get; }

        public virtual StepAttributes Attributes => new StepAttributes();

        public abstract IStep[] GetSubSteps();

        /// <summary>
        /// 成功聚合：已执行子 Step 的结果如何聚合为整体结果。默认：全部成功才算成功。
        /// </summary>
        public virtual bool AggregateSuccess(StepResult[] results)
        {
            for (int i = 0; i < results.Length; i++)
            {
                if (!results[i].Success) return false;
            }
            return true;
        }

        /// <summary>
        /// 短路策略：某个子 Step 执行后是否提前终止。默认：失败即停（原子捆绑，一损俱损）。
        /// </summary>
        public virtual bool ShouldShortCircuit(IStep step, StepResult result)
        {
            return !result.Success;
        }

        public async Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            if (_active > 0)
                throw new InvalidOperationException($"CompositeStep 循环引用：{Name} 在其子 Step 链中被再次执行");
            _active++;
            try
            {
                var subSteps = GetSubSteps();
                if (subSteps == null)
                    throw new InvalidOperationException($"CompositeStep {Name} 的 GetSubSteps() 返回了 null");
                var results = new List<StepResult>(subSteps.Length);
                var events = new List<GameEvent>();

                foreach (var sub in subSteps)
                {
                    if (sub == null)
                        throw new InvalidOperationException($"CompositeStep {Name} 的子 Step 数组包含 null");
                    var result = await sub.ExecuteAsync(board, ctx);
                    if (result == null)
                        throw new InvalidOperationException($"子 Step {sub.Name} 返回了 null 结果");
                    results.Add(result);
                    if (result.Events != null)
                        events.AddRange(result.Events);

                    if (ShouldShortCircuit(sub, result))
                        break;
                }

                return new StepResult
                {
                    Success = AggregateSuccess(results.ToArray()),
                    Events = events,
                    SubResults = results
                };
            }
            finally
            {
                _active--;
            }
        }
    }
}
