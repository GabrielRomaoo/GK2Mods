using System;
using System.Collections.Generic;
using System.Linq;

namespace GK2Mods.FactoryPlanner.Core
{
    /// <summary>One machine type of a stage: a recipe, how often it runs and how many machines that takes.</summary>
    public sealed class StageStep
    {
        public Recipe Recipe;
        public int Crafts;
        /// <summary>Share of one machine's week the crafts take (1 = one machine always busy).</summary>
        public double Load;
        public int Machines;
        /// <summary>Main raw material behind the output (the family it is grouped under); null if none.</summary>
        public string Family;
        /// <summary>A player's own order: a machine of its own whose output goes to a chest, not to the next stage.</summary>
        public bool Own;
    }

    /// <summary>
    /// A step of the factory: its machines take what they need from the stocks of the steps before (or the entry
    /// for raw materials) and drop what they make in the stage's own stock.
    /// </summary>
    public sealed class Stage
    {
        /// <summary>1 for the first refining step, up to the stage of the boxes.</summary>
        public int Index;
        public bool Final;
        public List<StageStep> Steps = new List<StageStep>();
        /// <summary>Item -> amount a week the stage puts in its stock.</summary>
        public Dictionary<string, int> Output = new Dictionary<string, int>();
        /// <summary>Item -> amount a week the stage takes, from the entry or the stocks before.</summary>
        public Dictionary<string, int> Input = new Dictionary<string, int>();

        public int MachineCount => Steps.Sum(s => s.Machines);
    }

    public sealed class StagePlan
    {
        /// <summary>Raw materials (brought into the factory) -> amount a week.</summary>
        public Dictionary<string, int> Entry = new Dictionary<string, int>();
        public List<Stage> Stages = new List<Stage>();
        /// <summary>Item -> stage that makes it (0 for the entry).</summary>
        public Dictionary<string, int> MadeAt = new Dictionary<string, int>();
        /// <summary>Item -> stages that take it.</summary>
        public Dictionary<string, SortedSet<int>> UsedAt = new Dictionary<string, SortedSet<int>>();

        /// <summary>
        /// Conveyor chests the stock after a stage needs: each side takes one incoming line (machines of one
        /// recipe can share it) or gives one item (a filtered side), four sides per chest.
        /// </summary>
        public int ChestsFor(Stage stage)
        {
            int sides = stage.Steps.Count + stage.Output.Keys.Count(item => UsedAt.ContainsKey(item));
            return stage.Final ? 0 : Math.Max(1, (sides + 3) / 4);
        }

        public int EntryChests => Math.Max(1, (Entry.Count + 3) / 4);
    }

    /// <summary>
    /// Splits a week's line into stages by depth: a recipe sits one stage after the deepest of its ingredients
    /// made in the factory, raw materials count as stage 0, and the boxes of the schedule all go to the last stage.
    /// Within a stage the machines are grouped by the kind of output (the raw material family behind it).
    /// </summary>
    public static class Stager
    {
        public static StagePlan Build(LineResult line, RecipeBook book, IEnumerable<string> finalItems,
            Func<Recipe, int, double> load, Func<string, string> family = null)
        {
            var plan = new StagePlan();
            var recipeFor = new Dictionary<string, Recipe>();
            foreach (string id in line.RecipeOrder)
            {
                if (book.ById.TryGetValue(id, out Recipe r))
                {
                    recipeFor[r.OutItem] = r;
                }
            }
            foreach (string id in line.Own.Keys)
            {
                if (book.ById.TryGetValue(id, out Recipe r) && !recipeFor.ContainsKey(r.OutItem))
                {
                    recipeFor[r.OutItem] = r;
                }
            }
            var finals = new HashSet<string>(finalItems.Where(recipeFor.ContainsKey));

            var depth = new Dictionary<string, int>();
            int Depth(string item, int guard)
            {
                if (depth.TryGetValue(item, out int d))
                {
                    return d;
                }
                if (!recipeFor.TryGetValue(item, out Recipe recipe) || guard > 32)
                {
                    return depth[item] = 0;
                }
                depth[item] = 0;  // a cycle reads as raw
                int deepest = recipe.Needs.Select(n => Depth(n.Item, guard + 1)).DefaultIfEmpty(0).Max();
                return depth[item] = deepest + 1;
            }
            foreach (string item in recipeFor.Keys)
            {
                Depth(item, 0);
            }
            int last = recipeFor.Keys.Where(i => !finals.Contains(i)).Select(i => depth[i]).DefaultIfEmpty(0).Max() + 1;
            if (finals.Count == 0)
            {
                last = Math.Max(1, last - 1);
            }
            foreach (string item in finals)
            {
                depth[item] = Math.Max(depth[item], last);
            }

            var stages = new Dictionary<int, Stage>();
            var jobs = line.RecipeOrder.Where(line.Crafts.ContainsKey).Select(id => (id, crafts: line.Crafts[id], own: false))
                .Concat(line.Own.Select(kv => (id: kv.Key, crafts: kv.Value, own: true)));
            foreach (var (id, crafts, own) in jobs)
            {
                if (!book.ById.TryGetValue(id, out Recipe recipe))
                {
                    continue;
                }
                int index = depth.TryGetValue(recipe.OutItem, out int at0) ? at0 : 1;
                if (!stages.TryGetValue(index, out Stage stage))
                {
                    stages[index] = stage = new Stage { Index = index };
                }
                double use = load(recipe, crafts);
                stage.Steps.Add(new StageStep
                {
                    Recipe = recipe,
                    Crafts = crafts,
                    Load = use,
                    Machines = Math.Max(1, (int)Math.Ceiling(use - 1e-6)),
                    Family = family?.Invoke(recipe.OutItem),
                    Own = own,
                });
                Add(stage.Output, recipe.OutItem, crafts * recipe.OutCount);
                if (!own || !plan.MadeAt.ContainsKey(recipe.OutItem))
                {
                    plan.MadeAt[recipe.OutItem] = index;
                }
                foreach (Ingredient need in recipe.Needs)
                {
                    Add(stage.Input, need.Item, crafts * need.Count);
                    if (!plan.UsedAt.TryGetValue(need.Item, out SortedSet<int> at))
                    {
                        plan.UsedAt[need.Item] = at = new SortedSet<int>();
                    }
                    at.Add(index);
                }
            }
            foreach (var kv in line.Inputs)
            {
                plan.Entry[kv.Key] = kv.Value;
                plan.MadeAt[kv.Key] = 0;
            }

            // Number the stages 1..n without gaps, families together, the busiest first.
            int n = 0;
            foreach (Stage stage in stages.OrderBy(kv => kv.Key).Select(kv => kv.Value))
            {
                int old = stage.Index;
                stage.Index = ++n;
                foreach (string item in stage.Output.Keys)
                {
                    plan.MadeAt[item] = n;
                }
                foreach (SortedSet<int> at in plan.UsedAt.Values)
                {
                    if (at.Remove(old))
                    {
                        at.Add(-n);  // negative while renumbering, so a later old index can't collide
                    }
                }
                stage.Steps = stage.Steps.OrderBy(s => s.Family ?? "￿", StringComparer.Ordinal)
                    .ThenByDescending(s => s.Load).ToList();
                plan.Stages.Add(stage);
            }
            foreach (string key in plan.UsedAt.Keys.ToList())
            {
                plan.UsedAt[key] = new SortedSet<int>(plan.UsedAt[key].Select(i => -i));
            }
            if (plan.Stages.Count > 0)
            {
                plan.Stages[plan.Stages.Count - 1].Final = finals.Count > 0;
            }
            return plan;
        }

        /// <summary>
        /// Every factory product by stage, regardless of any order: the recipe the factory would use for each
        /// item, a stage one past its most refined ingredient, and the products nothing else consumes (the
        /// boxes) all on the last stage. One module (machine) per recipe.
        /// </summary>
        public static StagePlan Catalog(RecipeBook book, FactoryState factory, Func<string, string> family = null)
        {
            var line = new LineResult();
            var outputs = new HashSet<string>(book.ById.Values.Select(r => r.OutItem));
            var used = new HashSet<string>();
            var chosen = new List<Recipe>();
            foreach (string item in outputs.OrderBy(i => i, StringComparer.Ordinal))
            {
                Recipe r = book.Choose(item, factory);
                if (r == null)
                {
                    continue;
                }
                chosen.Add(r);
                used.UnionWith(r.Needs.Select(n => n.Item));
            }
            foreach (Recipe r in chosen)
            {
                line.RecipeOrder.Add(r.Id);
                line.Crafts[r.Id] = 1;
                foreach (Ingredient need in r.Needs.Where(n => !outputs.Contains(n.Item)))
                {
                    line.Inputs[need.Item] = 0;
                }
            }
            var finals = chosen.Select(r => r.OutItem).Where(i => !used.Contains(i)).ToList();
            StagePlan plan = Build(line, book, finals, (r, crafts) => 0.0, family);
            foreach (Stage stage in plan.Stages)
            {
                foreach (StageStep step in stage.Steps)
                {
                    step.Crafts = 0;
                    step.Machines = 1;
                }
            }
            return plan;
        }

        private static void Add(Dictionary<string, int> map, string key, int value) =>
            map[key] = map.TryGetValue(key, out int v) ? v + value : value;
    }
}
