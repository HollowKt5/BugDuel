using BugDuel.Domain;

namespace BeetleArena
{
    /// <summary>Runnable baseline content package; all values are sample gameplay parameters. Production species require design calibration.</summary>
    public static class BeetleContentExamples
    {
        public static ContentCatalog Create(int version = 1) => new ContentCatalog(version, new[]
        {
            new SpeciesDefinition("example.horn-beetle", "角斗甲虫（示例）", 55, 8),
            // Husbandry can introduce new species early; mandible combat unsupported by current arena models remains disabled.
            new SpeciesDefinition("example.stag-beetle", "大颚甲虫（示例）", 60, 9, combatEnabled: false)
        }, HabitatDefinition.CreateDefaults());
    }
}
