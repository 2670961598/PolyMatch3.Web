using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using PolyMatch3.Defs;

namespace PolyMatch3.Game
{
    /// <summary>
    /// 【导读】GameConfig → StepGraphDef 翻译器：工具箱面板的每个开关 = 图里的节点/参数。
    /// 手写 ClassicStepManager 的等价图（行为不变）：
    ///   [beginTurn] → playerSwap → [spendAp] → match ─成功→ arbitrate → [bombSpawn] → eliminate
    ///                  ▲              └失败→ revertSwap → playerSwap   ↓
    ///   playerSwap ←────────── 连锁无匹配 ← matchChain ← [deadlock/shuffle] ← refill ← gravity ← [score]
    /// 炸弹模式换 bombSpawn/bombEliminate/kindGravity 三个节点；经典交互矩阵（矩形+bombs 传统模式）
    /// 仍走手写的 Samples.Classic.ClassicGameManager（复杂分支的逃生舱，不进图）。
    /// </summary>
    public static class GraphBuilder
    {
        public static StepGraphDef Build(GameConfig cfg, bool bombs)
        {
            var nodes = new List<StepNodeDef>();
            var edges = new List<StepEdgeDef>();

            void Node(string id, string step, JObject prms = null)
                => nodes.Add(new StepNodeDef { Id = id, Step = step, Params = prms });
            void Edge(string from, string to, string when = StepWhen.Always)
                => edges.Add(new StepEdgeDef { From = from, To = to, When = when });

            bool ap = cfg.Moves > 0;
            bool score = cfg.Score;

            // ---- 输入段 ----
            if (ap) Node("begin", "beginTurn", new JObject { ["ap"] = cfg.Moves });
            Node("swap", "playerSwap");
            if (ap) Node("ap", "spendAp", new JObject { ["cost"] = 1 });
            Node("match", "match", new JObject { ["arbitrate"] = false });
            Node("arb", "arbitrate", new JObject { ["arbiter"] = string.IsNullOrEmpty(cfg.Arbiter) ? "containment" : cfg.Arbiter });
            Node("revert", "revertSwap");

            // ---- 结算段 ----
            if (bombs) Node("bombSpawn", "bombSpawn", new JObject { ["minPriority"] = 80 });
            Node("elim", bombs ? "bombEliminate" : "eliminate");
            if (score) Node("score", "score", new JObject { ["flatBonus"] = 10, ["cascadeScale"] = true });
            Node("grav", bombs ? "kindGravity" : (cfg.Gravity == "field" ? "fieldGravity" : "gravity"),
                bombs || cfg.Gravity == "field" ? null : new JObject { ["fallEdge"] = "Down" });
            Node("refill", "refill");

            // ---- 连锁段 ----
            bool deadlock = cfg.Width * cfg.Height <= 256; // 大棋盘死局探测太贵（与原装配一致，炸弹模式同样开启）
            if (deadlock) { Node("dead", "deadlockCheck"); Node("shuf", "shuffle"); }
            Node("matchChain", "match", new JObject { ["arbitrate"] = false });
            Node("arbChain", "arbitrate", new JObject { ["arbiter"] = string.IsNullOrEmpty(cfg.Arbiter) ? "containment" : cfg.Arbiter });

            // ---- 边 ----
            string entry = ap ? "begin" : "swap";
            if (ap) { Edge("begin", "swap"); Edge("swap", "ap"); Edge("ap", "match", StepWhen.OnSuccess); /* ap 失败：无出边 ⇒ 终局（步数用完） */ }
            else Edge("swap", "match");

            Edge("match", "arb", StepWhen.OnSuccess);
            Edge("match", "revert", StepWhen.OnFailure);
            Edge("revert", "swap");
            Edge("arb", bombs ? "bombSpawn" : "elim");
            if (bombs) Edge("bombSpawn", "elim");
            Edge("elim", score ? "score" : "grav");
            if (score) Edge("score", "grav");
            Edge("grav", "refill");

            if (deadlock)
            {
                Edge("refill", "dead");
                Edge("dead", "matchChain", StepWhen.OnSuccess);
                Edge("dead", "shuf", StepWhen.OnFailure);
                Edge("shuf", "matchChain");
            }
            else Edge("refill", "matchChain");

            Edge("matchChain", "arbChain", StepWhen.OnSuccess);
            Edge("matchChain", "swap", StepWhen.OnFailure);
            Edge("arbChain", bombs ? "bombSpawn" : "elim");

            return new StepGraphDef { Entry = entry, Nodes = nodes, Edges = edges };
        }
    }
}
