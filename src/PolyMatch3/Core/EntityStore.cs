using System;
using System.Collections.Generic;

namespace PolyMatch3.Core
{
    /// <summary>
    /// 实体仓库（战棋方向的地基）：实体 id 顺序分配（**0=无实体**，对齐 0=空军规），
    /// cellId ↔ entityId 的映射由 ParallelLayer&lt;int&gt; 承担（棋子怎么动实体就怎么动，
    /// 同步走 Cells.Swap / Cells.Move / Cells.Clear，与 KindLayer 同一纪律）。
    /// 军规：实体生成只走 ctx.Random；实体状态必须可序列化（存档/回放第一天就要成立）。
    /// </summary>
    public sealed class EntityStore
    {
        private readonly List<object> _entities = new List<object>(); // id = 下标 + 1

        public EntityStore(int cellCount)
        {
            Cells = new ParallelLayer<int>(cellCount);
        }

        /// <summary>格子 → 实体 id 的平行层（同步操作直接调它）。</summary>
        public ParallelLayer<int> Cells { get; }

        public int Count => _entities.Count;

        /// <summary>登记实体，返回顺序分配的 id（1..N）。null 实体即抛（0=无实体的语义不能被占用）。</summary>
        public int Register(object entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _entities.Add(entity);
            return _entities.Count; // 下标 + 1
        }

        /// <summary>按 id 取实体（id 越界即抛）。</summary>
        public T Get<T>(int entityId) where T : class
        {
            if (entityId < 1 || entityId > _entities.Count)
                throw new ArgumentOutOfRangeException(nameof(entityId), entityId, $"合法范围 [1, {_entities.Count}]");
            return (T)_entities[entityId - 1];
        }

        /// <summary>格子上无实体 ⇒ 0；有 ⇒ 实体 id。</summary>
        public int IdAt(int cellId) => Cells.Get(cellId);

        /// <summary>格子上的实体（无 ⇒ null）。</summary>
        public T At<T>(int cellId) where T : class
        {
            int id = Cells.Get(cellId);
            return id == 0 ? null : Get<T>(id);
        }

        /// <summary>把已登记的实体放到格子上（entityId 必须先 Register）。</summary>
        public void Place(int cellId, int entityId)
        {
            if (entityId < 1 || entityId > _entities.Count)
                throw new ArgumentOutOfRangeException(nameof(entityId), entityId, "实体必须先 Register 再 Place");
            Cells.Set(cellId, entityId);
        }
    }
}
