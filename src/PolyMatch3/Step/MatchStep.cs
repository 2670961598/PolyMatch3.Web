using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Matcher;

namespace PolyMatch3.Step
{
    /// <summary>
    /// 全图匹配（Step 层的最底层能力 Step）：把匹配器**原始输出（未去重的全量组）**写入黑板，不发事件。
    /// 去重/仲裁不在本 Step 内做——需要时在其后接 ArbitrateStep（可串多个、各用各的 IMatchArbiter、
    /// 各写各的黑板键，例如消除用一组策略、生成物用另一组策略）。
    /// Success = 是否存在匹配（供编排层决定进入结算还是结束）。
    /// </summary>
    public sealed class MatchStep : IStep
    {
        public const string DefaultKey = "matches";

        private readonly IMatcher _matcher;
        private readonly string _resultKey;

        public MatchStep(IMatcher matcher, string resultKey = DefaultKey)
        {
            _matcher = matcher ?? throw new System.ArgumentNullException(nameof(matcher));
            if (string.IsNullOrEmpty(resultKey))
                throw new System.ArgumentException("resultKey 不能为空：匹配结果会写入黑板该键，空键会让后续 Step 永远读不到结果（静默空转）", nameof(resultKey));
            _resultKey = resultKey;
        }

        public string Name => "Match";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            var raw = _matcher.Match(board);
            ctx.Info.Set(_resultKey, raw);
            return Task.FromResult(new StepResult { Success = raw.Count > 0 });
        }
    }
}
