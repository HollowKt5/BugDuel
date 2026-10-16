using System;
using System.Collections.Generic;

namespace BugDuel.Domain
{
    public sealed class FoodDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public FoodPortion Portion { get; }
        public FoodDefinition(string id, string displayName, FoodPortion portion)
        {
            Id = DomainGuard.Id(id, nameof(id));
            DisplayName = DomainGuard.Text(displayName, nameof(displayName));
            Portion = portion ?? throw new ArgumentNullException(nameof(portion));
        }
    }

    /// <summary>Reusable feeding definitions. Quantities are gameplay tuning, not nutrition advice.</summary>
    public sealed class FoodCatalog
    {
        private readonly Dictionary<string, FoodDefinition> foods = new Dictionary<string, FoodDefinition>(StringComparer.Ordinal);
        public IReadOnlyList<FoodDefinition> Definitions { get; }
        public FoodCatalog(IEnumerable<FoodDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            var values = new List<FoodDefinition>();
            foreach (FoodDefinition food in definitions)
            {
                if (food == null || foods.ContainsKey(food.Id)) throw new ArgumentException("Food definitions must have unique non-null IDs.", nameof(definitions));
                foods.Add(food.Id, food);
                values.Add(food);
            }
            Definitions = values.AsReadOnly();
        }
        public bool TryGet(string id, out FoodDefinition food)
        { food = null; return id != null && foods.TryGetValue(id, out food); }

        public static FoodCatalog CreateDefaults() => new FoodCatalog(new[]
        {
            new FoodDefinition("food.sweet-potato", "红薯", new FoodPortion(FoodKind.SweetPotato, 30, 8)),
            new FoodDefinition("food.banana", "香蕉", new FoodPortion(FoodKind.Fruit, 25, 15)),
            new FoodDefinition("food.apple", "苹果", new FoodPortion(FoodKind.Fruit, 15, 25)),
            new FoodDefinition("food.beetle-jelly", "甲虫果冻", new FoodPortion(FoodKind.BeetleJelly, 20, 20))
        });
    }
}
