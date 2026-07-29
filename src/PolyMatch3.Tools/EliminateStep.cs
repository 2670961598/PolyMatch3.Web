using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PolyMatch3.Core;
using PolyMatch3.Matcher;
using PolyMatch3.Step;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 消除：读取黑板上的 MatchGroup 列表（默认由 MatchStep 写入），按格子并集清空颜色层（置 0）。
    /// 骨架固定：读黑板 → 算消除集 → 逐格清除 → 累计 eliminatedTotal → 发事件；
    /// 接缝可重载：消除集怎么算（炸弹展开）、每格清除时做什么（触发回调/同步平行层）、
    /// 全部清完后做什么、事件长什么样。
    /// </summary>
    public class EliminateStep : IStep
    {
        /// <summary>累计消除数的黑板键。</summary>
        public const string EliminatedTotalKey = "eliminatedTotal";

        private readonly string _sourceKey;
        private readonly PieceRegistry _pieces; // 可选：提供时 OnCellCleared 默认分发 IPieceHooks

        public EliminateStep(string sourceKey = MatchStep.DefaultKey, PieceRegistry pieces = null)
        {
            if (string.IsNullOrEmpty(sourceKey))
                throw new ArgumentException("sourceKey 不能为空：EliminateStep 从黑板该键读取 MatchGroup 列表，空键会静默读不到任何结果", nameof(sourceKey));
            _sourceKey = sourceKey;
            _pieces = pieces;
        }

        public virtual string Name => "Eliminate";
        public virtual StepAttributes Attributes => new StepAttributes();

        public virtual Task<StepResult> ExecuteAsync(GraphBoard board, StepContext ctx)
        {
            if (!TryReadGroups(ctx, out var groups))
                return Task.FromResult(new StepResult { Success = false });

            var cells = CollectCells(board, groups, ctx);
            if (cells.Count == 0)
                return Task.FromResult(new StepResult { Success = false });

            // 清除前快照棋子值（供接缝②分发回调）
            var clearedTypes = new int[cells.Count];
            for (int i = 0; i < cells.Count; i++)
                clearedTypes[i] = board.GetPieceType(cells[i]);

            for (int i = 0; i < cells.Count; i++)
            {
                board.SetPieceType(cells[i], PieceRegistry.EmptyId);
                OnCellCleared(board, cells[i], clearedTypes[i], ctx);
            }
            AfterCleared(board, cells, ctx);

            // 累计消除数（玩法目标系统可直接读）
            ctx.Info.TryGet<int>(EliminatedTotalKey, out var total);
            ctx.Info.Set(EliminatedTotalKey, total + cells.Count);

            return Task.FromResult(new StepResult
            {
                Success = true,
                Events = { CreateEvent(cells) }
            });
        }

        /// <summary>接缝⓪：消除依据从哪来（默认：黑板上的匹配组列表，非空才算有）。交互场景可重载为强制种子。</summary>
        protected virtual bool TryReadGroups(StepContext ctx, out List<MatchGroup> groups)
        {
            return ctx.Info.TryGet<List<MatchGroup>>(_sourceKey, out groups) && groups.Count > 0;
        }

        /// <summary>接缝①：消除集怎么算（默认 = 匹配组格子并集，按首次出现序）。炸弹玩法重载为"并集 + 爆炸展开 + 连锁迭代"。</summary>
        protected virtual List<int> CollectCells(GraphBoard board, List<MatchGroup> groups, StepContext ctx)
        {
            var cells = new List<int>();
            var seen = new HashSet<int>();
            foreach (var g in groups)
            {
                foreach (var c in g.CellIds)
                {
                    if (seen.Add(c)) cells.Add(c);
                }
            }
            return cells;
        }

        /// <summary>
        /// 接缝②：每格被清空（置 0）时回调，携带被清掉的棋子值。
        /// 默认：若构造时给了注册表且该棋子实现 IPieceHooks，则触发 OnEliminate。
        /// </summary>
        protected virtual void OnCellCleared(GraphBoard board, int cellId, int clearedType, StepContext ctx)
        {
            if (_pieces != null && _pieces.Get(clearedType) is IPieceHooks hooks)
                hooks.OnEliminate(board, cellId, ctx);
        }

        /// <summary>接缝③：全部清除完成后回调（默认空；特殊棋子生成、连锁触发挂这里）。</summary>
        protected virtual void AfterCleared(GraphBoard board, List<int> clearedCells, StepContext ctx) { }

        /// <summary>接缝④：消除事件构造（默认 EliminateEvent，玩法可继承加料）。</summary>
        protected virtual GameEvent CreateEvent(List<int> cells)
        {
            return new EliminateEvent(cells.ToArray());
        }
    }
}
