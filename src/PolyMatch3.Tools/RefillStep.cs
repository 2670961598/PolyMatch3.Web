using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Step;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 填充：为空格生成新棋子。骨架固定（按 CellId 升序找空格 → 生成 → 发 SpawnEvent），
    /// 两个接缝可重载：生成什么棋子（PickPiece）、生成后做什么（OnSpawned）。
    /// 随机消耗只走 ctx.Random（确定性军规），按 id 升序逐格生成——顺序固定，回放可复现。
    /// </summary>
    public class RefillStep : IStep
    {
        private readonly int _colorCount;
        private readonly PieceRegistry _pieces; // 可选：提供时 OnSpawned 默认分发 IPieceHooks

        /// <param name="colorCount">默认 PickPiece 用的颜色数（均匀随机 1..colorCount）。</param>
        /// <param name="pieces">可选棋子注册表：提供后 OnSpawned 默认触发棋子的 IPieceHooks.OnSpawn。</param>
        public RefillStep(int colorCount, PieceRegistry pieces = null)
        {
            if (colorCount <= 0) throw new ArgumentOutOfRangeException(nameof(colorCount));
            _colorCount = colorCount;
            _pieces = pieces;
        }

        public virtual string Name => "Refill";
        public virtual StepAttributes Attributes => new StepAttributes();

        public virtual Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            if (ctx.Random == null)
                throw new InvalidOperationException("RefillStep 需要确定性随机源：请为 StepContext.Random 赋值（军规：随机只走 ctx.Random）");

            var cells = new List<int>();
            var types = new List<int>();

            for (int c = 0; c < board.CellCount; c++)
            {
                if (board.GetPieceType(c) != PieceRegistry.EmptyId) continue;
                int type = PickPiece(board, c, ctx);
                if (type <= PieceRegistry.EmptyId)
                    throw new InvalidOperationException($"PickPiece 在格子 {c} 返回了非法棋子值 {type}（合法值 ≥ 1，0 保留为空）");
                board.SetPieceType(c, type);
                OnSpawned(board, c, type, ctx);
                cells.Add(c);
                types.Add(type);
            }

            if (cells.Count == 0)
                return Task.FromResult(new StepResult { Success = false });

            return Task.FromResult(new StepResult
            {
                Success = true,
                Events = { CreateSpawnEvent(cells.ToArray(), types.ToArray()) }
            });
        }

        /// <summary>接缝①：生成什么棋子（默认 1..colorCount 均匀随机；玩法重载：加权、防爆、混入特殊棋子）。</summary>
        protected virtual int PickPiece(GraphBoard board, int cellId, StepContext ctx)
        {
            return 1 + ctx.Random.Next(_colorCount);
        }

        /// <summary>
        /// 接缝②：每格生成后回调。默认：若构造时给了注册表且该棋子实现 IPieceHooks，则触发 OnSpawn。
        /// 注意：棋子值超出注册表范围会在此时抛 ArgumentOutOfRangeException（配置错误启动期暴露）。
        /// </summary>
        protected virtual void OnSpawned(GraphBoard board, int cellId, int pieceType, StepContext ctx)
        {
            if (_pieces != null && _pieces.Get(pieceType) is IPieceHooks hooks)
                hooks.OnSpawn(board, cellId, ctx);
        }

        /// <summary>接缝③：生成事件构造（默认 SpawnEvent，玩法可继承加料）。</summary>
        protected virtual GameEvent CreateSpawnEvent(int[] cells, int[] types)
        {
            return new SpawnEvent(cells, types);
        }
    }
}
