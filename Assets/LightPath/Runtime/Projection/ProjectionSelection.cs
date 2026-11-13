using System.Collections.Generic;
namespace DelayNoMore.LightPath
{
    /** @brief 投影对象循环选择，跳过不可操作和停用对象。 */
    public static class ProjectionSelection
    {
        /** @brief 按房间登记顺序选择前后对象，无候选时保留当前索引。 */
        public static int Next(IReadOnlyList<ProjectionSource> sources, int current, int step)
        {
            if (sources == null) return current;
            return NextIndex(sources.Count, current, step, index =>
            {
                var source = sources[index];
                return source && source.isActiveAndEnabled && source.Definition && source.Definition.Movable;
            });
        }
        /** @brief 在可选索引之间循环；同一轮滚动至多切换一个对象。 */
        public static int NextIndex(int count, int current, int step, System.Predicate<int> available)
        {
            if (count == 0 || step == 0) return current;
            for (int i = 1; i <= count; i++)
            {
                int index = ((current + (step > 0 ? i : -i)) % count + count) % count;
                if (available(index)) return index;
            }
            return current;
        }
    }
}
