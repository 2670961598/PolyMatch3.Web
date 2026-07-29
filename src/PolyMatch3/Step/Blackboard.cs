using System.Collections.Generic;

namespace PolyMatch3.Step
{
    /// <summary>
    /// 一局一张的全局信息表：跨 Step 传递信息的唯一框架机制。
    /// 无生命周期规则——谁写谁负责；后面的 Step 读取，有就按它执行，没有就走默认行为。
    /// </summary>
    public sealed class Blackboard
    {
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();

        public void Set(string key, object value)
        {
            _data[key] = value;
        }

        public bool TryGet<T>(string key, out T value)
        {
            if (_data.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public bool Contains(string key)
        {
            return _data.ContainsKey(key);
        }

        public bool Remove(string key)
        {
            return _data.Remove(key);
        }

        public void Clear()
        {
            _data.Clear();
        }
    }
}
