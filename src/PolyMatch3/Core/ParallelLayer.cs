using System;

namespace PolyMatch3.Core
{
    /// <summary>
    /// 平行层（KindLayer 的泛化）：与棋盘颜色层等长的泛型平行数组，连续内存保证性能。
    /// default(T) = "无"语义（对齐 0=空军规：int 时 0=无，引用时 null=无）。
    /// 同步铁律：颜色层怎么动，平行层就怎么动——交换/下落成对调用 Swap/Move，消除成对调用 Clear。
    /// int 是 T 的特例（kind 层）；T = 实体 id 时即战棋的实体层（实体本体多一次访存，见 EntityStore）。
    /// </summary>
    public class ParallelLayer<T>
    {
        private readonly T[] _values;

        public ParallelLayer(int cellCount)
        {
            if (cellCount <= 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
            _values = new T[cellCount];
        }

        public int Length => _values.Length;

        public T Get(int cellId) => _values[cellId];

        public void Set(int cellId, T value) => _values[cellId] = value;

        /// <summary>清除（归 default(T)）——跟随颜色层消除。</summary>
        public void Clear(int cellId) => _values[cellId] = default;

        /// <summary>移动同步：from 的值交给 to，from 归 default(T)（跟随颜色层移动/下落）。</summary>
        public void Move(int from, int to)
        {
            _values[to] = _values[from];
            _values[from] = default;
        }

        /// <summary>交换同步（跟随颜色层交换）。</summary>
        public void Swap(int a, int b)
        {
            (_values[a], _values[b]) = (_values[b], _values[a]);
        }

        /// <summary>快照（试算/存档用；扁平数组拷贝，便宜）。</summary>
        public T[] Snapshot() => (T[])_values.Clone();

        /// <summary>整体恢复（与 Snapshot 配对）。</summary>
        public void Restore(T[] snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Length != _values.Length)
                throw new ArgumentException($"快照长度 {snapshot.Length} 与平行层长度 {_values.Length} 不一致", nameof(snapshot));
            Array.Copy(snapshot, _values, _values.Length);
        }
    }
}
