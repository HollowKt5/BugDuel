using BugDuel.Domain;

namespace BeetleArena
{
    /// <summary>可运行的内容包起点，全部数值为示例游戏参数；真实物种需策划和模型标定。</summary>
    public static class BeetleContentExamples
    {
        public static ContentCatalog Create(int version = 1) => new ContentCatalog(version, new[]
        {
            new SpeciesDefinition("example.horn-beetle", "角斗甲虫（示例）", 55, 8),
            // 养殖可以先加入新物种；当前角斗模型不支持的大颚战斗保持关闭。
            new SpeciesDefinition("example.stag-beetle", "大颚甲虫（示例）", 60, 9, combatEnabled: false)
        }, HabitatDefinition.CreateDefaults());
    }
}
