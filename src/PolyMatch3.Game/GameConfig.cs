using System;
using System.Text.Json;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Tools;

namespace PolyMatch3.Game
{
    /// <summary>
    /// 【导读】开局配置（工具箱面板的 JSON 契约）：全部字段可选，缺省 = 现状行为。
    /// Bridge 的 NewGameWithConfig 解析本类后交给 GameSession.Create 装配——
    /// 面板上的每个开关对应一个工具注入点，这正是"装配即玩法"的演示。
    /// </summary>
    public sealed class GameConfig
    {
        // ---- 基础开局 ----
        public int Mode { get; set; }
        public int Width { get; set; } = 8;
        public int Height { get; set; } = 8;
        public int Colors { get; set; } = 5;
        public string Seed { get; set; } = "42";
        public bool Bombs { get; set; }
        /// <summary>指定棋盘（csv，0=空，行优先）；null/空 = 种子随机。</summary>
        public string Pieces { get; set; }

        // ---- 工具选项 ----
        /// <summary>拓扑：rect（默认）/ torus / mobius（仅矩形模式有效）。</summary>
        public string Topology { get; set; } = "rect";
        /// <summary>重力：column（默认）/ field（势场重力，汇点=底行）。</summary>
        public string Gravity { get; set; } = "column";
        /// <summary>仲裁器 Id（MatchArbiters 注册表）：containment（默认）/ none / overlap。</summary>
        public string Arbiter { get; set; }
        /// <summary>生成物裁决 Id（SpawnResolvers 注册表）：winner-take-all（默认）/ both-apply。仅传统模式。</summary>
        public string SpawnResolver { get; set; }
        /// <summary>炸弹范围：rect-square:1（默认）/ radius:1 / line:row / line:col。</summary>
        public string BombRange { get; set; }
        /// <summary>true = 插入 ScoreStep（演示修饰符套：固定加分 + 连锁乘算）。</summary>
        public bool Score { get; set; }
        /// <summary>&gt;0 = 步数预算（行动配额演示，turn.ap），用尽即终局；0 = 不限。</summary>
        public int Moves { get; set; }

        /// <summary>解析 JSON（大小写不敏感）。空/ null 给全默认。</summary>
        public static GameConfig Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new GameConfig();
            var cfg = JsonSerializer.Deserialize<GameConfig>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (cfg == null) throw new ArgumentException("配置 JSON 解析失败", nameof(json));
            return cfg;
        }

        /// <summary>解析指定棋盘 csv 为棋子数组；未指定返回 null。</summary>
        public int[] ParsePieces()
        {
            if (string.IsNullOrWhiteSpace(Pieces)) return null;
            var parts = Pieces.Split(new[] { ',', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            return Array.ConvertAll(parts, int.Parse);
        }

        /// <summary>仲裁器（未指定 = Containment 现状）。未知 Id 由注册表抛错。</summary>
        public IMatchArbiter BuildArbiter()
        {
            return string.IsNullOrEmpty(Arbiter) ? MatchArbiters.Containment : MatchArbiters.Get(Arbiter);
        }

        /// <summary>生成物裁决（未指定 = WinnerTakeAll 现状）。</summary>
        public ISpawnResolver BuildResolver()
        {
            return string.IsNullOrEmpty(SpawnResolver) ? SpawnResolvers.WinnerTakeAll : SpawnResolvers.Get(SpawnResolver);
        }

        /// <summary>炸弹范围选择器（未指定 = 3×3 现状）。矩形边索引：U=0 D=1 L=2 R=3。</summary>
        public ICellSelector BuildSelector()
        {
            switch (BombRange)
            {
                case null:
                case "":
                case "rect-square:1": return new RectSquareSelector(1);
                case "radius:1": return new RadiusSelector(1);
                case "radius:2": return new RadiusSelector(2);
                case "line:row": return new LineSelector(2, 3);
                case "line:col": return new LineSelector(0, 1);
                default:
                    throw new ArgumentException($"未知炸弹范围：\"{BombRange}\"（可用：rect-square:1 / radius:1 / radius:2 / line:row / line:col）");
            }
        }

        /// <summary>演示计分修饰符套（固定加分 + 连锁乘算）。Score=false 时返回 null。</summary>
        public IScoreModifier[] BuildScoreModifiers()
        {
            if (!Score) return null;
            return new IScoreModifier[]
            {
                new FlatBonusModifier("消除奖励+10", 50, 10),
                new CascadeScaleModifier("连锁×N", 10),
            };
        }
    }
}
