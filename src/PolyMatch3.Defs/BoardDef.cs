using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using PolyMatch3.Core;

namespace PolyMatch3.Defs
{
    /// <summary>
    /// 棋盘定义（数据层）→ GraphBoard（运行层）。
    /// 边一律按<strong>名字</strong>引用（数据在边表增删后依然稳定），构建时对 EdgeTypeRegistry 解析成索引。
    /// generator 为 null 时走手工边；当前实现的生成器只有 "rect"，hex/triangle 留接口。
    /// </summary>
    public sealed class BoardDef
    {
        [JsonProperty("width")] public int Width;
        [JsonProperty("height")] public int Height;

        /// <summary>边词汇表，注册顺序即索引（如 ["Up","Down","Left","Right"]）。</summary>
        [JsonProperty("edgeTypes")] public List<string> EdgeTypes;

        /// <summary>拓扑生成器："rect" / "hex" / "triangle" / null（手工边）。</summary>
        [JsonProperty("generator")] public string Generator;

        /// <summary>手工边列表（generator = null 时使用）。</summary>
        [JsonProperty("edges")] public List<EdgeDef> Edges;

        /// <summary>可选：每格布局坐标（表现层用，编辑器期产出）。数据层只透传不解释。</summary>
        [JsonProperty("positions")] public List<float[]> Positions;

        /// <summary>
        /// 构建棋盘：注册边词汇表 → 生成器或手工边加边 → FreezeTopology。
        /// 一切非法配置在此处抛可行动错误（含出问题的对象、实际值、修法）。
        /// </summary>
        public GraphBoard ToGraphBoard()
        {
            if (Width <= 0)
                throw new DefsException($"BoardDef.width = {Width} 非法（必须 ≥ 1）。请修正关卡 JSON 的 board.width。");
            if (Height <= 0)
                throw new DefsException($"BoardDef.height = {Height} 非法（必须 ≥ 1）。请修正关卡 JSON 的 board.height。");
            if (EdgeTypes == null || EdgeTypes.Count == 0)
                throw new DefsException("BoardDef.edgeTypes 为空：棋盘至少需要一种边类型（如 [\"Up\",\"Down\",\"Left\",\"Right\"]）。");

            var registry = new EdgeTypeRegistry();
            for (int i = 0; i < EdgeTypes.Count; i++)
            {
                var name = EdgeTypes[i];
                if (string.IsNullOrEmpty(name))
                    throw new DefsException($"BoardDef.edgeTypes[{i}] 为空：边类型名不能为空。");
                try
                {
                    registry.Register(name);
                }
                catch (ArgumentException)
                {
                    throw new DefsException($"BoardDef.edgeTypes 中存在重复边名 '{name}'（第 {i} 项）。边名必须唯一，请删除重复项。");
                }
            }

            var board = new GraphBoard(Width, Height, registry);

            if (string.IsNullOrEmpty(Generator) || Generator == "null")
            {
                ApplyManualEdges(board, registry);
            }
            else if (Generator == "rect")
            {
                board.BuildRectNeighbors(
                    ResolveGeneratorEdge(registry, "Up"),
                    ResolveGeneratorEdge(registry, "Down"),
                    ResolveGeneratorEdge(registry, "Left"),
                    ResolveGeneratorEdge(registry, "Right"));
            }
            else if (Generator == "hex" || Generator == "triangle")
            {
                throw new DefsException(
                    $"BoardDef.generator = '{Generator}' 暂未实现（留接口，公式可后续搬入）。当前请改用 \"rect\"，或 generator=null 配合手工边 edges。");
            }
            else
            {
                throw new DefsException(
                    $"BoardDef.generator = '{Generator}' 未知（合法值：\"rect\" / \"hex\" / \"triangle\" / null）。请修正关卡 JSON 的 board.generator。");
            }

            board.FreezeTopology();
            return board;
        }

        /// <summary>手工边加边：边按名字解析，bidirectional 展开为两条有向边。</summary>
        private void ApplyManualEdges(GraphBoard board, EdgeTypeRegistry registry)
        {
            if (Edges == null) return;

            for (int i = 0; i < Edges.Count; i++)
            {
                var e = Edges[i];
                if (e == null)
                    throw new DefsException($"BoardDef.edges[{i}] 为 null：请删除该空项或补全边定义。");
                int edgeIndex = ResolveEdgeName(registry, e.Edge, $"BoardDef.edges[{i}]");
                CheckCell(e.From, $"BoardDef.edges[{i}].from");
                CheckCell(e.To, $"BoardDef.edges[{i}].to");

                board.AddEdge(e.From, edgeIndex, e.To);
                if (e.Bidirectional)
                    board.AddEdge(e.To, edgeIndex, e.From);
            }
        }

        private void CheckCell(int cell, string where)
        {
            long count = (long)Width * Height;
            if (cell < 0 || cell >= count)
                throw new DefsException($"{where} = {cell} 越界（棋盘 {Width}×{Height} 共 {count} 格，合法范围 [0, {count})）。请修正该手工边。");
        }

        /// <summary>rect 生成器按约定名解析四方向索引（注册顺序不再影响语义，数据更稳定）。</summary>
        private static int ResolveGeneratorEdge(EdgeTypeRegistry registry, string name)
        {
            if (!registry.TryGetIndex(name, out int index))
                throw new DefsException(
                    $"generator = \"rect\" 需要边注册表中存在 '{name}' 边（现有：{ListNames(registry)}）。请在 BoardDef.edgeTypes 中补充，或改用 generator=null 手工边。");
            return index;
        }

        /// <summary>边名解析（手工边/图案臂共用）：未注册即抛含现有边名的可行动错误。</summary>
        internal static int ResolveEdgeName(EdgeTypeRegistry registry, string name, string where)
        {
            if (string.IsNullOrEmpty(name))
                throw new DefsException($"{where} 的边名为空：请填写 BoardDef.edgeTypes 中已注册的边名（现有：{ListNames(registry)}）。");
            if (!registry.TryGetIndex(name, out int index))
                throw new DefsException(
                    $"{where} 引用了未注册的边名 '{name}'（注册表现有：{ListNames(registry)}）。请修正拼写，或在 BoardDef.edgeTypes 中注册该边。");
            return index;
        }

        internal static string ListNames(EdgeTypeRegistry registry)
        {
            var names = new string[registry.Count];
            for (int i = 0; i < names.Length; i++) names[i] = registry.GetName(i);
            return string.Join(", ", names);
        }
    }

    /// <summary>手工边：from →(edge 名)→ to；bidirectional = true 时展开为两条有向边（默认 true）。</summary>
    public sealed class EdgeDef
    {
        [JsonProperty("from")] public int From;
        [JsonProperty("edge")] public string Edge;
        [JsonProperty("to")] public int To;
        [JsonProperty("bidirectional")] public bool Bidirectional = true;
    }
}
