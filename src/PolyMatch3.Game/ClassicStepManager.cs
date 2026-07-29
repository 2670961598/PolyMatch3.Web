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
    ///   PlayerSwap ──交换成功──▶ Match ──┬─有匹配──▶ [BombSpawn（仅炸弹模式）] ─▶ Eliminate ─▶ Gravity ─▶ Refill ─▶ Match（连锁判定）
    ///       ▲                            └─无匹配──┬─交换后首次判定：SwapBack（弹回）─▶ PlayerSwap
    ///       └──────────────────────────────────────┴─连锁后判定：直接回 PlayerSwap
    ///
    /// 关键设计：
    ///   1. 初始状态不做匹配检测（开局自带的匹配原样保留），直接等玩家输入；
    ///   2. 永不主动结束（休闲局无步数限制），结束条件由上层玩法追加；
    ///   3. 全部使用新工具：PathGravityStep（显式路径重力）+ RefillStep v2（注册表驱动的生成钩子）；
    ///   4. 炸弹模式只是"换两个 Step"（BombSpawnOnMatchStep + BombEliminateStep / KindGravityStep），
    ///      状态机骨架不变——这正是工具可插拔的演示。
    /// </summary>
    public sealed class ClassicStepManager : IStepManager
    {
        private readonly PlayerSwapStep _playerSwap;
        private readonly MatchStep _match;
        private readonly IStep _bombSpawn;   // 可空：仅炸弹模式
        private readonly EliminateStep _eliminate;
        private readonly PathGravityStep _gravity;
        private readonly RefillStep _refill;
        private readonly Samples.KindLayer _kinds;

        private string _lastIssued;
        private bool _matchAfterSwap;

        public ClassicStepManager(InputChannel<(int a, int b)> input, IMatcher matcher, int colorCount,
            int width, int height, PieceRegistry pieces, bool bombs, KindLayer kinds)
        {
            _playerSwap = new PlayerSwapStep(input, kinds);
            _match = new MatchStep(matcher);
            _refill = new RefillStep(colorCount, pieces);
            _kinds = kinds;

            var edges = PathGravityStep.BuildColumnEdges(width, height);
            _gravity = bombs
                ? new KindGravityStep(kinds, width * height, edges)
                : new PathGravityStep(width * height, edges);

            if (bombs)
            {
                _bombSpawn = new BombSpawnOnMatchStep(minPriority: 80); // 四连及以上生成炸弹
                _eliminate = new BombEliminateStep(kinds, pieces: pieces);
            }
            else
            {
                _eliminate = new EliminateStep(pieces: pieces);
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
                d = StepDecision.Next(_playerSwap);
                _lastIssued = "PlayerSwap";
            }
            else
            {
                switch (_lastIssued)
                {
                    case "PlayerSwap":
                        _matchAfterSwap = true;
                        d = StepDecision.Next(_match);
                        _lastIssued = "Match";
                        break;
                    case "Match":
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
    }
}
