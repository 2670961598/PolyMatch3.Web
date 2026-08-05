using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Samples;
using PolyMatch3.Samples.Bomb;
using PolyMatch3.Step;
using PolyMatch3.Tools;

namespace PolyMatch3.Game
{
    /// <summary>
    /// 【导读】经典三消主循环（最简状态机），本类是玩法侧的"大脑"，回答每一步之后该干什么。
    ///
    /// 状态机流转（_lastIssued 记录上一次派发的 Step）：
    ///   PlayerSwap ──交换成功──▶ [SpendAp（步数预算开启时）] ─▶ Match ─▶ Arbitrate ─┬─有存活──▶ [BombSpawn（仅炸弹模式）] ─▶ Eliminate
    ///       ▲                            └─无存活──┬─交换后首次判定：SwapBack（弹回）─▶ PlayerSwap
    ///       │                                      └─连锁后判定：直接回 PlayerSwap
    ///   Eliminate ─▶ [Score（计分开启时）] ─▶ Gravity ─▶ Refill ─▶ [Deadlock（开启时）→ 无合法手 Shuffle] ─▶ Match（连锁判定）
    ///
    /// 关键设计：
    ///   1. 初始状态不做匹配检测（开局自带的匹配原样保留），直接等玩家输入；
    ///   2. 永不主动结束（休闲局无步数限制时）；movesBudget &gt; 0 时步数用尽即终局（行动配额演示）；
    ///   3. 工具全部构造注入（仲裁器/炸弹范围/重力模式/计分修饰符），默认 = 现状行为——工具箱面板就是在替玩家做装配；
    ///   4. 炸弹模式只是"换两个 Step"（BombSpawnOnMatchStep + BombEliminateStep / KindGravityStep），状态机骨架不变。
    /// </summary>
    public sealed class ClassicStepManager : IStepManager
    {
        private readonly PlayerSwapStep _playerSwap;
        private readonly MatchStep _match;
        private readonly ArbitrateStep _arbitrate;
        private readonly IStep _bombSpawn;   // 可空：仅炸弹模式
        private readonly EliminateStep _eliminate;
        private readonly IStep _gravity;
        private readonly RefillStep _refill;
        private readonly Samples.KindLayer _kinds;
        private readonly IStep _score;       // 可空：计分开启时
        private readonly IStep _deadlock;    // 可空：死局闭环开启时
        private readonly IStep _shuffle;     // 可空：死局闭环开启时
        private readonly int _movesBudget;

        private string _lastIssued;
        private bool _matchAfterSwap;

        public ClassicStepManager(InputChannel<(int a, int b)> input, IMatcher matcher, int colorCount,
            int width, int height, PieceRegistry pieces, bool bombs, KindLayer kinds,
            IMatchArbiter arbiter = null, ICellSelector bombRange = null, bool fieldGravity = false,
            IScoreModifier[] scoreModifiers = null, int movesBudget = 0, bool deadlockShuffle = false)
        {
            _playerSwap = new PlayerSwapStep(input, kinds);
            _match = new MatchStep(matcher);
            _arbitrate = new ArbitrateStep(arbiter ?? MatchArbiters.Containment);
            _refill = new RefillStep(colorCount, pieces);
            _kinds = kinds;
            _movesBudget = movesBudget;

            // 势场重力目前不同步 kind 层：炸弹模式下仍用列重力（双层同步铁律优先）
            if (fieldGravity && !bombs)
            {
                var sinks = new int[width];
                for (int x = 0; x < width; x++) sinks[x] = (height - 1) * width + x;
                _gravity = new FieldGravityStep(sinks);
            }
            else
            {
                var edges = PathGravityStep.BuildColumnEdges(width, height);
                _gravity = bombs
                    ? new KindGravityStep(kinds, width * height, edges)
                    : new PathGravityStep(width * height, edges);
            }

            if (bombs)
            {
                _bombSpawn = new BombSpawnOnMatchStep(minPriority: 80); // 四连及以上生成炸弹
                _eliminate = new BombEliminateStep(kinds, bombRange, pieces: pieces);
            }
            else
            {
                _eliminate = new EliminateStep(pieces: pieces);
            }

            if (scoreModifiers != null) _score = new ScoreStep(scoreModifiers);
            if (deadlockShuffle)
            {
                _deadlock = new DeadlockCheckStep(matcher);
                _shuffle = new ShuffleStep(matcher);
            }
        }

        /// <summary>供表现层查询"是否可点击"。</summary>
        public PlayerSwapStep PlayerSwap => _playerSwap;

        public Task<StepDecision> DecideNextAsync(GraphBoard board, StepContext ctx, StepResult last)
        {
            StepDecision d;
            if (last == null)
            {
                // 初始状态不做任何匹配检测（开局自带匹配原样保留），直接等玩家输入
                if (_movesBudget > 0) ctx.Info.Set(TurnKeys.Ap, _movesBudget);
                d = StepDecision.Next(_playerSwap);
                _lastIssued = "PlayerSwap";
            }
            else
            {
                switch (_lastIssued)
                {
                    case "PlayerSwap":
                        if (_movesBudget > 0)
                        {
                            d = StepDecision.Next(new SpendApStep(1));
                            _lastIssued = "SpendAp";
                            break;
                        }
                        goto case "SpendAp";
                    case "SpendAp":
                        if (_movesBudget > 0 && !last.Success)
                        {
                            d = StepDecision.End("步数用完");
                            break;
                        }
                        _matchAfterSwap = true;
                        d = StepDecision.Next(_match);
                        _lastIssued = "Match";
                        break;
                    case "Match":
                        // 匹配后固定接仲裁，存活组才是后续生成/消除的依据
                        d = StepDecision.Next(_arbitrate);
                        _lastIssued = "Arbitrate";
                        break;
                    case "Arbitrate":
                        if (last.Success)
                        {
                            if (_bombSpawn != null)
                            {
                                d = StepDecision.Next(_bombSpawn);
                                _lastIssued = "BombSpawn";
                            }
                            else
                            {
                                d = StepDecision.Next(_eliminate);
                                _lastIssued = "Eliminate";
                            }
                        }
                        else if (_matchAfterSwap)
                        {
                            // 交换未产生匹配：撤销，重新等输入
                            ctx.Info.TryGet<(int a, int b)>(PlayerSwapStep.LastSwapKey, out var swap);
                            d = StepDecision.Next(new RevertSwapStep(swap.a, swap.b, _kinds));
                            _lastIssued = "SwapBack";
                        }
                        else
                        {
                            // 连锁结束：回到等输入
                            d = StepDecision.Next(_playerSwap);
                            _lastIssued = "PlayerSwap";
                        }
                        break;
                    case "BombSpawn":
                        d = StepDecision.Next(_eliminate);
                        _lastIssued = "Eliminate";
                        break;
                    case "SwapBack":
                        d = StepDecision.Next(_playerSwap);
                        _lastIssued = "PlayerSwap";
                        break;
                    case "Eliminate":
                        if (_score != null)
                        {
                            d = StepDecision.Next(_score);
                            _lastIssued = "Score";
                            break;
                        }
                        goto case "Score";
                    case "Score":
                        d = StepDecision.Next(_gravity);
                        _lastIssued = "Gravity";
                        break;
                    case "Gravity":
                        d = StepDecision.Next(_refill);
                        _lastIssued = "Refill";
                        break;
                    case "Refill":
                        if (_deadlock != null)
                        {
                            d = StepDecision.Next(_deadlock);
                            _lastIssued = "Deadlock";
                            break;
                        }
                        goto case "Deadlock";
                    case "Deadlock":
                        if (_deadlock != null && !last.Success)
                        {
                            // 死局：洗牌（自带"洗出合法手"重试保险丝）后再进入连锁判定
                            d = StepDecision.Next(_shuffle);
                            _lastIssued = "Shuffle";
                            break;
                        }
                        goto case "Shuffle";
                    case "Shuffle":
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
    }
}
