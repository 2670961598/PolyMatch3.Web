using System.Collections.Generic;
using PolyMatch3.Matcher;

namespace PolyMatch3.Tools
{
    /// <summary>
    /// 生成物裁决策略：从仲裁后的存活匹配组中，裁定**哪些组的生成物生效**。
    /// 与组级仲裁（IMatchArbiter 决定"哪些匹配算数"）是正交的第二层决策——
    /// 十字交叉的四连和五连：消除上可以都消（containment），生成物上可以只认五连
    /// （WinnerTakeAll）或两个都生成（BothApply），由本接口的实现决定。
    /// 约定（确定性军规）：同输入同输出；只有 table 中有映射的组才是候选
    /// （无生成物的组既不生效也不参与阻挡）；输出保持输入相对顺序。
    /// MergeUpgrade（交叉融合升级为更高一级生成物）预留：实现本接口即可，框架不内置。
    /// </summary>
    public interface ISpawnResolver
    {
        /// <summary>稳定标识（供配置/编辑器引用）。内置：winner-take-all / both-apply。</summary>
        string Id { get; }

        /// <summary>
        /// 裁出生效组。groups 为仲裁输出（约定已按优先级降序）；返回新的生效组列表
        /// （不修改输入），每组在锚点生成 table 里映射的生成物。
        /// </summary>
        List<MatchGroup> Resolve(IReadOnlyList<MatchGroup> groups, SpawnTable table);
    }
}
