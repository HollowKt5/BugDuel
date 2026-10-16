using System;
using System.Collections.Generic;

namespace BugDuel.Domain
{
    /// <summary>Selectable habitat template, independent of species IDs. Stage changes can require relocation.</summary>
    public sealed class HabitatDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public HabitatConditions Environment { get; }
        public IReadOnlyList<LifeStage> AllowedStages { get; }
        public IReadOnlyList<string> Tags { get; }
        public HabitatDefinition(string id, string displayName, HabitatConditions environment,
            IEnumerable<LifeStage> allowedStages = null, IEnumerable<string> tags = null)
        {
            Id = DomainGuard.Id(id, nameof(id));
            DisplayName = DomainGuard.Text(displayName, nameof(displayName));
            Environment = environment ?? throw new ArgumentNullException(nameof(environment));
            var stages = new List<LifeStage>(allowedStages ?? new[] { LifeStage.Egg, LifeStage.Larva, LifeStage.Pupa, LifeStage.Adult });
            var uniqueStages = new HashSet<LifeStage>();
            foreach (LifeStage stage in stages)
                if (!Enum.IsDefined(typeof(LifeStage), stage) || stage == LifeStage.Dead || !uniqueStages.Add(stage))
                    throw new ArgumentException("Habitat stages must be distinct living stages.", nameof(allowedStages));
            if (stages.Count == 0) throw new ArgumentException("A habitat must allow at least one living stage.", nameof(allowedStages));
            AllowedStages = stages.AsReadOnly();
            var tagValues = new List<string>(tags ?? Array.Empty<string>());
            var uniqueTags = new HashSet<string>(StringComparer.Ordinal);
            foreach (string tag in tagValues)
                if (!uniqueTags.Add(DomainGuard.Id(tag, nameof(tags)))) throw new ArgumentException("Habitat tags must be unique.", nameof(tags));
            Tags = tagValues.AsReadOnly();
        }
        public bool Allows(LifeStage stage)
        {
            foreach (LifeStage allowed in AllowedStages) if (allowed == stage) return true;
            return false;
        }

        /// <summary>Illustrative game environments, not biological care recommendations.</summary>
        public static IReadOnlyList<HabitatDefinition> CreateDefaults() => Array.AsReadOnly(new[]
        {
            new HabitatDefinition("habitat.adult", "成虫饲养箱", new HabitatConditions(24, 70, 95, .1f),
                new[] { LifeStage.Adult }, new[] { "adult", "feeding" }),
            new HabitatDefinition("habitat.substrate", "幼虫发酵基质箱", new HabitatConditions(24, 75, 95, 0),
                new[] { LifeStage.Egg, LifeStage.Larva, LifeStage.Pupa }, new[] { "substrate", "development" }),
            new HabitatDefinition("habitat.recovery", "隔离休养箱", new HabitatConditions(24, 70, 100, 0, true),
                tags: new[] { "isolation", "rest" })
        });
    }

    /// <summary>One complete immutable content revision; publish only after validating all consuming state.</summary>
    public sealed class ContentCatalog
    {
        private readonly Dictionary<string, HabitatDefinition> habitats = new Dictionary<string, HabitatDefinition>(StringComparer.Ordinal);
        public int Version { get; }
        public SpeciesCatalog Species { get; }
        public IReadOnlyList<HabitatDefinition> Habitats { get; }
        public ContentCatalog(int version, IEnumerable<SpeciesDefinition> species, IEnumerable<HabitatDefinition> habitats)
        {
            if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
            Version = version;
            Species = new SpeciesCatalog(species);
            if (Species.Definitions.Count == 0) throw new ArgumentException("Content needs at least one species.", nameof(species));
            if (habitats == null) throw new ArgumentNullException(nameof(habitats));
            foreach (HabitatDefinition habitat in habitats)
            {
                if (habitat == null || this.habitats.ContainsKey(habitat.Id)) throw new ArgumentException("Habitats must have distinct non-null definitions.", nameof(habitats));
                this.habitats.Add(habitat.Id, habitat);
            }
            if (this.habitats.Count == 0) throw new ArgumentException("Content needs at least one habitat.", nameof(habitats));
            var values = new List<HabitatDefinition>(this.habitats.Values);
            values.Sort((first, second) => StringComparer.Ordinal.Compare(first.Id, second.Id));
            Habitats = values.AsReadOnly();
        }
        public bool TryGetHabitat(string id, out HabitatDefinition habitat)
        { habitat = null; return id != null && habitats.TryGetValue(id, out habitat); }
    }
}
