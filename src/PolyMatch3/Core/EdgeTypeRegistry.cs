using System;
using System.Collections.Generic;

namespace PolyMatch3.Core
{
    /// <summary>
    /// 边类型注册表：每个棋盘的专属"边词汇表"。
    /// 名称↔紧凑索引双向映射，数量不限；棋盘创建时冻结，冻结后不可再注册。
    /// 不同棋盘各自定义各自的边类型集合——矩形四方向只是万千配置之一。
    /// </summary>
    public sealed class EdgeTypeRegistry
    {
        private readonly List<string> _names = new List<string>();
        private readonly Dictionary<string, int> _indices = new Dictionary<string, int>();
        private bool _frozen;

        /// <summary>已注册的边类型数量。</summary>
        public int Count => _names.Count;

        /// <summary>是否已冻结（棋盘定型后不可再注册）。</summary>
        public bool IsFrozen => _frozen;

        /// <summary>
        /// 注册一种边类型，返回分配的紧凑索引（从 0 递增）。
        /// </summary>
        public int Register(string name)
        {
            if (_frozen)
                throw new InvalidOperationException("注册表已冻结（棋盘已定型），不能再注册边类型");
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("边类型名不能为空", nameof(name));
            if (_indices.ContainsKey(name))
                throw new ArgumentException($"边类型名重复：{name}", nameof(name));

            _indices.Add(name, _names.Count);
            _names.Add(name);
            return _names.Count - 1;
        }

        /// <summary>按名称取索引。未注册抛 <see cref="KeyNotFoundException"/>。</summary>
        public int GetIndex(string name)
        {
            if (!_indices.TryGetValue(name, out var index))
                throw new KeyNotFoundException($"未注册的边类型：{name}");
            return index;
        }

        public bool TryGetIndex(string name, out int index)
        {
            return _indices.TryGetValue(name, out index);
        }

        /// <summary>按索引取名称。</summary>
        public string GetName(int index)
        {
            return _names[index];
        }

        /// <summary>冻结：此后注册表只读。由 GraphBoard 构造时自动调用。</summary>
        public void Freeze()
        {
            _frozen = true;
        }

        /// <summary>
        /// 矩形四方向便利注册：Up=0, Down=1, Left=2, Right=3。
        /// 矩形网格只是万千拓扑配置之一，与任何自定义注册表地位平等。
        /// </summary>
        public static EdgeTypeRegistry CreateRect()
        {
            var registry = new EdgeTypeRegistry();
            registry.Register("Up");
            registry.Register("Down");
            registry.Register("Left");
            registry.Register("Right");
            return registry;
        }
    }
}
