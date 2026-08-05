using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Step;
using PolyMatch3.Tools;

namespace PolyMatch3.Samples.Classic
{
    /// <summary>
    /// 传统三消主循环（完整交互矩阵版）：
    ///   输入 → 按两格 kind 分支：
    ///     含宝石 → GemInteract → 结算链
    ///     双特殊 → SpecialInteract → 结算链
    ///   其余 → 普通交换 → 匹配 → 仲裁（无存活匹配且是交换则弹回）→ SpecialSpawn → 结算链
    ///   结算链 = SpecialEliminate → 重力（kind 同步）→ 补充 → 匹配（连锁）→ 无连锁回输入。
    /// 初始状态不做匹配检测，直接等输入；永不主动结束。
    /// </summary>
    public sealed class ClassicGameManager : IStepManager
    {
        private readonly SwapInputStep _input;
        private readonly MatchStep _match;
        private readonly ArbitrateStep _arbitrate;
        private readonly SpecialSpawnStep _spawn;
        private readonly SpecialEliminateStep _eliminate;
        private readonly PathGravityStep _gravity;
        private readonly RefillStep _refill;
        private readonly KindLayer _kinds;

        private string _lastIssued;
        private bool _matchAfterSwap;

        public ClassicGameManager(InputChannel<(int a, int b)> input, IMatcher matcher, int colorCount,
            int width, int height, KindLayer kinds, PieceRegistry pieces = null,
            IMatchArbiter arbiter = null, ISpawnResolver resolver = null)
        {
            _kinds = kinds;
            _input = new SwapInputStep(input);
            _match = new MatchStep(matcher);
            _arbitrate = new ArbitrateStep(arbiter ?? MatchArbiters.Containment);
            // 开局校验：映射表与本局图案集/生成物注册对不上时，当场抛（不带进局内）
            var spawnTable = ClassicSetup.CreateSpawnTable();
            spawnTable.Validate(ClassicSetup.CreatePatterns(), ClassicSetup.IsSpawnId);
            _spawn = new SpecialSpawnStep(spawnTable, resolver);
            _eliminate = new SpecialEliminateStep(kinds, pieces: pieces);
            _gravity = new KindGravityStep(kinds, width * height, PathGravityStep.BuildColumnEdges(width, height));
            _refill = new RefillStep(colorCount, pieces);
        }

        /// <summary>供表现层查询"是否可点击"。</summary>
        public SwapInputStep Input => _input;

        public Task<StepDecision> DecideNextAsync(GraphBoard board, StepContext ctx, StepResult last)
        {
            StepDecision d;
            if (last == null)
            {
                d = StepDecision.Next(_input);
                _lastIssued = "Input";
            }
            else
            {
                switch (_lastIssued)
                {
                    case "Input":
                        d = BranchAfterInput(board, ctx);
                        break;
                    case "KindSwap":
                        _matchAfterSwap = true;
                        d = StepDecision.Next(_match);
                        _lastIssued = "Match";
                        break;
                    case "Match":
                        // 匹配后固定接仲裁（覆盖去重），存活组才是后续生成/消除的依据
                        d = StepDecision.Next(_arbitrate);
                        _lastIssued = "Arbitrate";
                        break;
                    case "Arbitrate":
                        if (last.Success)
                        {
                            d = StepDecision.Next(_spawn);
                            _lastIssued = "SpecialSpawn";
                        }
                        else if (_matchAfterSwap)
                        {
                            // 交换未产生匹配：弹回（kind 同步），重新等输入
                            ctx.Info.TryGet<(int a, int b)>(SwapInputStep.PairKey, out var pair);
                            var revert = new KindSwapStep(_kinds, pair.a, pair.b);
                            d = StepDecision.Next(revert);
                            _lastIssued = "SwapBack";
                        }
                        else
                        {
                            d = StepDecision.Next(_input);
                            _lastIssued = "Input";
                        }
                        break;
                    case "SpecialSpawn":
                        d = StepDecision.Next(_eliminate);
                        _lastIssued = "SpecialEliminate";
                        break;
                    case "GemInteract":
                    case "SpecialInteract":
                        d = StepDecision.Next(_eliminate);
                        _lastIssued = "SpecialEliminate";
                        break;
                    case "SwapBack":
                        d = StepDecision.Next(_input);
                        _lastIssued = "Input";
                        break;
                    case "SpecialEliminate":
                        d = StepDecision.Next(_gravity);
                        _lastIssued = "Gravity";
                        break;
                    case "Gravity":
                        d = StepDecision.Next(_refill);
                        _lastIssued = "Refill";
                        break;
                    case "Refill":
                        _matchAfterSwap = false;
                        d = StepDecision.Next(_match);
                        _lastIssued = "Match";
                        break;
                    default:
                        d = StepDecision.End($"未知状态：{_lastIssued}");
                        break;
                }
            }
            return Task.FromResult(d);
        }

        /// <summary>输入后按两格 kind 分支（交互矩阵的入口）。</summary>
        private StepDecision BranchAfterInput(GraphBoard board, StepContext ctx)
        {
            ctx.Info.TryGet<(int a, int b)>(SwapInputStep.PairKey, out var pair);
            int ka = _kinds.Get(pair.a), kb = _kinds.Get(pair.b);

            if (ka == SpecialKind.Gem || kb == SpecialKind.Gem)
            {
                _lastIssued = "GemInteract";
                return StepDecision.Next(new GemInteractStep(_kinds, pair.a, pair.b));
            }
            if (ka != 0 && kb != 0)
            {
                _lastIssued = "SpecialInteract";
                return StepDecision.Next(new SpecialInteractStep(_kinds, pair.a, pair.b));
            }

            _lastIssued = "KindSwap";
            return StepDecision.Next(new KindSwapStep(_kinds, pair.a, pair.b));
        }
    }
}
