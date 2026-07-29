using System;
using System.Collections.Generic;

namespace PolyMatch3.Core
{
    /// <summary>
    /// 棋子注册表：棋子 int 值 ↔ 棋子逻辑（IPiece）的映射。
    /// 硬约定：<see cref="EmptyId"/>（0）永远表示"空"（什么也没有），不可注册、不可占用；
    /// 注册顺序即棋子 id（1..N）——顺序只是分配方式，不应影响任何运行逻辑。
    /// 两阶段：构建期 Register → Freeze 定型，此后只读（与棋盘拓扑同生命周期）。
    /// </summary>
    public sealed class PieceRegistry
    {
        /// <summary>空棋子 id（硬约定）。消除后置 0、填充跳过 0，全框架统一。</summary>
        public const int EmptyId = 0;

        private readonly List<IPiece> _pieces = new List<IPiece>();
        private readonly HashSet<string> _names = new HashSet<string>();

        /// <summary>注册表是否已冻结（冻结后方可查询，与棋盘 FreezeTopology 同节奏）。</summary>
        public bool IsFrozen { get; private set; }

        /// <summary>已注册棋子数（不含空）。</summary>
        public int Count => _pieces.Count;

        /// <summary>注册棋子，返回分配的 id（= 注册顺序，从 1 开始）。重名/冻结后注册即抛。</summary>
        public int Register(IPiece piece)
        {
            if (IsFrozen) throw new InvalidOperationException("棋子注册表已冻结，不能再注册");
            if (piece == null) throw new ArgumentNullException(nameof(piece));
            if (string.IsNullOrEmpty(piece.Id)) throw new ArgumentException("棋子 Id 不能为空", nameof(piece));
            if (!_names.Add(piece.Id))
                throw new ArgumentException($"棋子名重复注册：{piece.Id}", nameof(piece));

            _pieces.Add(piece);
            return _pieces.Count; // id 从 1 开始，0 保留为空
        }

        /// <summary>按棋子数组顺序批量注册（顺序即 id）。返回最后一个分配的 id。</summary>
        public int RegisterAll(params IPiece[] pieces)
        {
            if (pieces == null) throw new ArgumentNullException(nameof(pieces));
            int last = EmptyId;
            foreach (var p in pieces) last = Register(p);
            return last;
        }

        /// <summary>定型（幂等）。此后注册即抛。</summary>
        public void Freeze()
        {
            IsFrozen = true;
        }

        /// <summary>按 id 取棋子逻辑。id=0（空）返回 null；越界即抛。</summary>
        public IPiece Get(int id)
        {
            if (id == EmptyId) return null;
            if ((uint)(id - 1) >= (uint)_pieces.Count)
                throw new ArgumentOutOfRangeException(nameof(id), id, $"合法范围 [1, {_pieces.Count}]，0 为空");
            return _pieces[id - 1];
        }

        /// <summary>id 是否是已注册棋子（0 或越界均为 false）。</summary>
        public bool Contains(int id)
        {
            return id >= 1 && id <= _pieces.Count;
        }
    }
}
