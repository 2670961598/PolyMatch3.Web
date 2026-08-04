using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Matcher;

namespace PolyMatch3.Step
{
    /// <summary>
    /// 仲裁（去重）：读黑板上的原始 MatchGroup 列表（默认由 MatchStep 写入），用指定的 IMatchArbiter
    /// 过滤，存活组写回黑板——默认 sourceKey == resultKey 原地精炼；配不同的 resultKey 可让
    /// 原始组与仲裁结果共存（例如消除读"共存"策略的结果，生成物读"赢家通吃"策略的结果）。
    /// 对存活组逐组发 MatchEvent（表现层消费的是"生效的匹配"，语义与旧 MatchStep 一致）。
    /// Success = 是否存在存活组（供编排层决定消除还是结束）；读不到源列表或源为空时 Success=false 且不写黑板。
    /// </summary>
    public sealed class ArbitrateStep : IStep
    {
        private readonly IMatchArbiter _arbiter;
        private readonly string _sourceKey;
        private readonly string _resultKey;

        /// <summary>resultKey 不传（null）时默认与 sourceKey 相同（原地精炼）。</summary>
        public ArbitrateStep(IMatchArbiter arbiter, string sourceKey = MatchStep.DefaultKey, string resultKey = null)
        {
            _arbiter = arbiter ?? throw new ArgumentNullException(nameof(arbiter));
            if (string.IsNullOrEmpty(sourceKey))
                throw new ArgumentException("sourceKey 不能为空：ArbitrateStep 从黑板该键读取原始 MatchGroup 列表，空键会静默读不到任何结果", nameof(sourceKey));
            if (resultKey != null && resultKey.Length == 0)
                throw new ArgumentException("resultKey 不能为空字符串：不传（null）表示与 sourceKey 同键原地精炼", nameof(resultKey));
            _sourceKey = sourceKey;
            _resultKey = resultKey ?? sourceKey;
        }

        public string Name => "Arbitrate";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            if (!ctx.Info.TryGet<List<MatchGroup>>(_sourceKey, out var raw) || raw.Count == 0)
                return Task.FromResult(new StepResult { Success = false });

            var final = _arbiter.Arbitrate(board, raw);
            ctx.Info.Set(_resultKey, final);

            var result = new StepResult { Success = final.Count > 0 };
            foreach (var g in final)
                result.Events.Add(new MatchEvent(g));
            return Task.FromResult(result);
        }
    }
}
