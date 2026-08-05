using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Step;

namespace PolyMatch3.Defs
{
    /// <summary>
    /// 图解释执行器：把 StepGraphDef 直接解释执行为 IStepManager（无代码生成）。
    /// 持有图 + 当前节点游标 + 每节点缓存的 IStep 实例（构造期全量构建——
    /// 参数错误与图结构错误一样，全部启动期抛可行动消息）。
    /// 转移规则：首调用返回 entry 节点；之后按 lastResult.Success 在当前节点出边中
    /// 按声明序选第一条命中的边；无边命中 → StepDecision.End（终止原因含节点 id）。
    /// 循环是图的合法形态（连锁环），不做无环要求；死循环由 Orchestrator maxSteps 兜底。
    /// </summary>
    public sealed class GraphStepManager : IStepManager
    {
        private sealed class Node
        {
            public string Id;
            public IStep Step;
            public readonly List<StepEdgeDef> OutEdges = new List<StepEdgeDef>();
        }

        private readonly Dictionary<string, Node> _nodes;
        private readonly Node _entry;
        private Node _current;

        public GraphStepManager(StepGraphDef def, StepCatalog catalog, StepBuildContext buildCtx)
        {
            if (def == null) throw new DefsException("StepGraphDef 为 null：关卡必须包含 stepGraph 定义。");
            if (catalog == null) throw new DefsException("GraphStepManager 需要 StepCatalog，传入了 null。");
            if (buildCtx == null) throw new DefsException("GraphStepManager 需要 StepBuildContext，传入了 null。");

            _nodes = BuildNodes(def, catalog, buildCtx);
            LinkEdges(def, _nodes);

            if (string.IsNullOrEmpty(def.Entry))
                throw new DefsException($"StepGraph 缺少 entry（入口节点 id）。已声明节点：{ListNodeIds(_nodes)}。");
            if (!_nodes.TryGetValue(def.Entry, out _entry))
                throw new DefsException($"StepGraph 入口节点 '{def.Entry}' 不存在（已声明节点：{ListNodeIds(_nodes)}）。请修正 entry，或补充该节点。");
        }

        /// <summary>按节点 id 取已构建的 Step 实例（LevelSession 查询输入状态等用）。不存在返回 false。</summary>
        public bool TryGetStep(string nodeId, out IStep step)
        {
            step = null;
            if (nodeId == null) return false;
            if (_nodes.TryGetValue(nodeId, out var node))
            {
                step = node.Step;
                return true;
            }
            return false;
        }

        /// <summary>全部节点的 Step 实例（按节点 id）。供会话层扫描输入型 Step 等。</summary>
        public IEnumerable<KeyValuePair<string, IStep>> NodeSteps
        {
            get
            {
                foreach (var kv in _nodes)
                    yield return new KeyValuePair<string, IStep>(kv.Key, kv.Value.Step);
            }
        }

        public Task<StepDecision> DecideNextAsync(GraphBoard board, StepContext ctx, StepResult lastResult)
        {
            if (lastResult == null)
            {
                _current = _entry;
                return Task.FromResult(StepDecision.Next(_entry.Step));
            }

            var outEdges = _current.OutEdges;
            for (int i = 0; i < outEdges.Count; i++)
            {
                var edge = outEdges[i];
                if (!StepWhen.Matches(edge.When, lastResult.Success)) continue;

                _current = _nodes[edge.To];
                return Task.FromResult(StepDecision.Next(_current.Step));
            }

            return Task.FromResult(StepDecision.End(
                $"节点 '{_current.Id}' 无出边命中（上一步 Success={lastResult.Success}）"));
        }

        /// <summary>构建并校验全部节点：id 唯一非空、step key 在 catalog 中（含参数包构建错误，启动期暴露）。</summary>
        private static Dictionary<string, Node> BuildNodes(StepGraphDef def, StepCatalog catalog, StepBuildContext buildCtx)
        {
            if (def.Nodes == null || def.Nodes.Count == 0)
                throw new DefsException("StepGraph.nodes 为空：编排图至少需要一个节点。");

            var nodes = new Dictionary<string, Node>();
            for (int i = 0; i < def.Nodes.Count; i++)
            {
                var n = def.Nodes[i];
                if (n == null)
                    throw new DefsException($"StepGraph.nodes[{i}] 为 null：请删除该空项或补全节点定义。");
                if (string.IsNullOrEmpty(n.Id))
                    throw new DefsException($"StepGraph.nodes[{i}] 缺少 id：每个节点必须有唯一 id（转移边按 id 引用）。");
                if (nodes.ContainsKey(n.Id))
                    throw new DefsException($"StepGraph 节点 id 重复：'{n.Id}'（nodes 第 {i} 项）。节点 id 必须唯一，请改名。");
                if (string.IsNullOrEmpty(n.Step))
                    throw new DefsException($"StepGraph 节点 '{n.Id}' 缺少 step（catalog key）：请填写已注册的 step key。");

                nodes.Add(n.Id, new Node
                {
                    Id = n.Id,
                    Step = catalog.Build(n.Step, buildCtx, n.Params, n.Id),
                });
            }
            return nodes;
        }

        /// <summary>连接转移边：两端节点存在、when 合法（封闭集合三选一）。</summary>
        private static void LinkEdges(StepGraphDef def, Dictionary<string, Node> nodes)
        {
            if (def.Edges == null) return;

            for (int i = 0; i < def.Edges.Count; i++)
            {
                var e = def.Edges[i];
                if (e == null)
                    throw new DefsException($"StepGraph.edges[{i}] 为 null：请删除该空项或补全边定义。");
                if (!nodes.ContainsKey(e.From ?? ""))
                    throw new DefsException($"StepGraph 边[{i}] 的起点 '{e.From}' 不是已声明节点（已声明节点：{ListNodeIds(nodes)}）。请修正 from。");
                if (!nodes.ContainsKey(e.To ?? ""))
                    throw new DefsException($"StepGraph 边[{i}]（{e.From} → ?）的终点 '{e.To}' 不是已声明节点（已声明节点：{ListNodeIds(nodes)}）。请修正 to。");
                if (!StepWhen.IsValid(e.When))
                    throw new DefsException($"StepGraph 边[{i}]（{e.From} → {e.To}）的 when = '{e.When}' 非法（封闭集合三选一：always / onSuccess / onFailure）。请修正 when。");

                nodes[e.From].OutEdges.Add(e);
            }
        }

        private static string ListNodeIds(Dictionary<string, Node> nodes)
        {
            var ids = new List<string>(nodes.Keys);
            ids.Sort(System.StringComparer.Ordinal);
            return string.Join(", ", ids);
        }
    }
}
