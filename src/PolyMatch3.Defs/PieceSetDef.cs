using System.Collections.Generic;
using Newtonsoft.Json;
using PolyMatch3.Core;

namespace PolyMatch3.Defs
{
    /// <summary>
    /// 棋子集定义（数据层）→ PieceRegistry（运行层）。
    /// 数组顺序即棋子 id（1..N，0 = 空为全框架硬约定）；behavior 是 catalog key → IPiece 工厂。
    /// </summary>
    public sealed class PieceSetDef
    {
        [JsonProperty("pieces")] public List<PieceDef> Pieces;

        /// <summary>经 catalog 构建棋子注册表（构建即 Freeze）。</summary>
        public PieceRegistry ToRegistry(PieceCatalog catalog)
        {
            if (catalog == null) throw new DefsException("PieceSetDef.ToRegistry 需要 PieceCatalog，传入了 null。");
            if (Pieces == null || Pieces.Count == 0)
                throw new DefsException("PieceSetDef.pieces 为空：关卡至少需要一种棋子（如 { \"key\": \"red\", \"behavior\": \"color\" }）。");

            var registry = new PieceRegistry();
            for (int i = 0; i < Pieces.Count; i++)
            {
                var p = Pieces[i];
                if (p == null)
                    throw new DefsException($"PieceSetDef.pieces[{i}] 为 null：请删除该空项或补全棋子定义。");
                if (string.IsNullOrEmpty(p.Key))
                    throw new DefsException($"PieceSetDef.pieces[{i}] 缺少 key：棋子 key 即其 Id（注册表内唯一，日志/调试引用用）。");
                if (string.IsNullOrEmpty(p.Behavior))
                    throw new DefsException($"棋子 '{p.Key}' 缺少 behavior：请填 catalog 中已注册的行为 key（如 \"color\"）。");

                var piece = catalog.Build(p.Behavior, p.Key);
                try
                {
                    registry.Register(piece);
                }
                catch (System.ArgumentException)
                {
                    throw new DefsException($"棋子 key '{p.Key}' 重复注册（PieceSetDef.pieces 第 {i} 项）。key 必须唯一，请删除或改名重复项。");
                }
            }
            registry.Freeze();
            return registry;
        }
    }

    /// <summary>棋子定义：key（= 棋子 Id）+ 行为 catalog key。</summary>
    public sealed class PieceDef
    {
        [JsonProperty("key")] public string Key;
        [JsonProperty("behavior")] public string Behavior;
    }
}
