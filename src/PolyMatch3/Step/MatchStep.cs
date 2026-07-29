using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Matcher;

namespace PolyMatch3.Step
{
    /// <summary>
    /// 全图匹配（Step 层的最底层能力 Step）：结果（MatchGroup 列表）写入黑板。
    /// 仲裁是可选参数：arbitrate=true 时经 MatchArbitrator 去重压制（高级吃低级）；
    /// false 时直接给原始全量组——消哪些、怎么消由玩法自己决定。
    /// Success = 是否存在存活匹配（供编排层决定消除还是结束）。
    /// </summary>
    public sealed class MatchStep : IStep
    {
        public const string DefaultKey = "matches";

        private readonly IMatcher _matcher;
        private readonly bool _arbitrate;
        private readonly string _resultKey;

        public MatchStep(IMatcher matcher, bool arbitrate = true, string resultKey = DefaultKey)
        {
            _matcher = matcher ?? throw new System.ArgumentNullException(nameof(matcher));
            if (string.IsNullOrEmpty(resultKey))
                throw new System.ArgumentException("resultKey 不能为空：匹配结果会写入黑板该键，空键会让后续 EliminateStep 永远读不到结果（静默空转）", nameof(resultKey));
            _arbitrate = arbitrate;
            _resultKey = resultKey;
        }

        public string Name => "Match";
        public StepAttributes Attributes => new StepAttributes();

        public Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            var raw = _matcher.Match(board);
            var groups = _arbitrate ? MatchArbitrator.Arbitrate(raw, board.CellCount) : raw;

            ctx.Info.Set(_resultKey, groups);

            var result = new StepResult { Success = groups.Count > 0 };
            foreach (var g in groups)
                result.Events.Add(new MatchEvent(g));
            return Task.FromResult(result);
        }
    }
}
