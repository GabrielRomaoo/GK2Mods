using System.Collections.Generic;
using System.Linq;

// Planning logic with no game types, so tests/FactoryPlanner.Tests can run it on data read from the game files.
namespace GK2Mods.FactoryPlanner.Core
{
    public struct Ingredient
    {
        public string Item;
        public int Count;

        public Ingredient(string item, int count)
        {
            Item = item;
            Count = count;
        }
    }

    /// <summary>A factory recipe (conv_*): one machine runs one recipe.</summary>
    public sealed class Recipe
    {
        public string Id;
        /// <summary>Machines that can run it, cheapest first (e.g. assemblybench_t1, _t2, _t3).</summary>
        public List<string> Stations = new List<string>();
        /// <summary>Extension the machine needs attached (hammer, bellows...), or null.</summary>
        public string Extension;
        public List<Ingredient> Needs = new List<Ingredient>();
        /// <summary>Craft length in progress ticks.</summary>
        public int Duration;
        public string OutItem;
        public int OutCount = 1;
        public bool Unlocked;
        /// <summary>Technology that teaches it (for locked recipes), or null.</summary>
        public string TechId;

        public string Station => Stations.Count > 0 ? Stations[0] : null;
    }

    /// <summary>A town order: N boxes of one item for one vendor.</summary>
    public sealed class Order
    {
        public string Id;
        public string VendorId;
        public string BoxItem;
        public int Count;
        public bool Urgent;
        public int Tier;
        public float Happiness;
        /// <summary>The vendor's level allows it and the vendor takes orders.</summary>
        public bool Available;
        public bool DoneThisWeek;
        public bool DoneOnce;
        public bool Accepted;
    }

    /// <summary>What the factory has today: which recipes some machine already runs.</summary>
    public sealed class FactoryState
    {
        public HashSet<string> RunningRecipes = new HashSet<string>();
        /// <summary>Machine count per station id (conveyor_furnace_t1...).</summary>
        public Dictionary<string, int> Stations = new Dictionary<string, int>();
        /// <summary>Extension count per id.</summary>
        public Dictionary<string, int> Extensions = new Dictionary<string, int>();
    }

    public sealed class RecipeBook
    {
        private readonly Dictionary<string, List<Recipe>> byOutput = new Dictionary<string, List<Recipe>>();

        public IReadOnlyDictionary<string, Recipe> ById { get; }

        public RecipeBook(IEnumerable<Recipe> recipes)
        {
            var byId = new Dictionary<string, Recipe>();
            foreach (Recipe r in recipes)
            {
                if (r == null || string.IsNullOrEmpty(r.OutItem) || byId.ContainsKey(r.Id))
                {
                    continue;
                }
                byId[r.Id] = r;
                if (!byOutput.TryGetValue(r.OutItem, out List<Recipe> list))
                {
                    byOutput[r.OutItem] = list = new List<Recipe>();
                }
                list.Add(r);
            }
            ById = byId;
        }

        public IReadOnlyList<Recipe> For(string item) =>
            item != null && byOutput.TryGetValue(item, out List<Recipe> list) ? list : (IReadOnlyList<Recipe>)new Recipe[0];

        public bool IsMade(string item) => For(item).Count > 0;

        /// <summary>
        /// The recipe the plan uses for an item: one the factory already runs, else one already learned
        /// (with a forced choice from the player first), else the first one (locked).
        /// </summary>
        public Recipe Choose(string item, FactoryState factory, IReadOnlyDictionary<string, string> preferred = null)
        {
            IReadOnlyList<Recipe> options = For(item);
            if (options.Count == 0)
            {
                return null;
            }
            if (preferred != null && preferred.TryGetValue(item, out string pick))
            {
                Recipe chosen = options.FirstOrDefault(r => r.Id == pick && r.Unlocked);
                if (chosen != null)
                {
                    return chosen;
                }
            }
            return options.FirstOrDefault(r => factory != null && factory.RunningRecipes.Contains(r.Id) && r.Unlocked)
                ?? options.FirstOrDefault(r => r.Unlocked)
                ?? options[0];
        }
    }
}
