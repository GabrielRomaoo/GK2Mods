using System;
using System.Collections.Generic;
using System.Linq;

namespace GK2Mods.FactoryPlanner.Core
{
    /// <summary>A production line for a week's demand: which recipes run how often, and what has to come in.</summary>
    public sealed class LineResult
    {
        /// <summary>Recipe id -> whole crafts per week.</summary>
        public Dictionary<string, int> Crafts = new Dictionary<string, int>();
        /// <summary>Item -> amount the line makes per week (final boxes and intermediates).</summary>
        public Dictionary<string, int> Made = new Dictionary<string, int>();
        /// <summary>Item -> amount that has to be brought into the factory per week.</summary>
        public Dictionary<string, int> Inputs = new Dictionary<string, int>();
        /// <summary>Recipes the line needs but the player hasn't learned.</summary>
        public HashSet<string> Locked = new HashSet<string>();
        /// <summary>Recipe ids, from the boxes down to the first steps.</summary>
        public List<string> RecipeOrder = new List<string>();
        /// <summary>Recipe id -> crafts the demand alone asks for (Crafts differs where the player set a quantity).</summary>
        public Dictionary<string, int> Needed = new Dictionary<string, int>();
        /// <summary>
        /// Recipe id -> crafts a week of the player's own orders. They run on a machine of their own, apart from the
        /// line's machine for the same recipe, so what they make goes to a chest and not on to the next stage.
        /// Their ingredients are counted in Crafts and Inputs like everything else.
        /// </summary>
        public Dictionary<string, int> Own = new Dictionary<string, int>();
    }

    public sealed class Expansion
    {
        public Order Order;
        public LineResult Line;
        /// <summary>Recipes neither the plan nor the factory runs yet: one new machine each.</summary>
        public List<Recipe> NewRecipes = new List<Recipe>();
        public bool Locked => Line.Locked.Count > 0;
    }

    public sealed class PlanOptions
    {
        public FactoryState Factory;
        /// <summary>Item -> recipe id the player picked for it (e.g. which vegetable fills the box).</summary>
        public Dictionary<string, string> Preferred = new Dictionary<string, string>();
        /// <summary>Items the player brings in ready-made instead of making them in the factory.</summary>
        public HashSet<string> External = new HashSet<string>();
        /// <summary>
        /// Recipe id -> crafts a week the player set by hand. It replaces what the demand asks for, and the
        /// ingredients follow it: more boxes ask for more kits, a recipe set to 0 asks for nothing.
        /// </summary>
        public Dictionary<string, int> Quantities = new Dictionary<string, int>();
        /// <summary>The player's own orders: item -> amount a week, made on a machine of its own (LineResult.Own).</summary>
        public Dictionary<string, int> Own = new Dictionary<string, int>();
    }

    public static class Planner
    {
        private const int MaxDepth = 16;

        /// <summary>
        /// Expands a demand (item -> amount per week) through the factory recipes. Amounts are summed per item
        /// before rounding to whole crafts, so two boxes sharing a kit don't each round up on their own.
        /// </summary>
        public static LineResult Compute(IEnumerable<KeyValuePair<string, int>> demand, RecipeBook book, PlanOptions options = null)
        {
            options = options ?? new PlanOptions();
            var result = new LineResult();
            var wanted = new Dictionary<string, int>();
            foreach (var d in demand)
            {
                if (d.Value > 0)
                {
                    Add(wanted, d.Key, d.Value);
                }
            }

            // The player's own orders: their recipe apart, their ingredients into the demand.
            foreach (var own in options.Own ?? new Dictionary<string, int>())
            {
                Recipe recipe = own.Value > 0 ? book.Choose(own.Key, options.Factory, options.Preferred) : null;
                if (recipe == null)
                {
                    continue;
                }
                int crafts = (own.Value + recipe.OutCount - 1) / Math.Max(1, recipe.OutCount);
                Add(result.Own, recipe.Id, crafts);
                Add(result.Made, recipe.OutItem, crafts * recipe.OutCount);
                if (!recipe.Unlocked)
                {
                    result.Locked.Add(recipe.Id);
                }
                foreach (Ingredient need in recipe.Needs)
                {
                    Add(wanted, need.Item, crafts * need.Count);
                }
            }

            // Order items so every item comes after all the items that consume it.
            var chosen = new Dictionary<string, Recipe>();
            var order = new List<string>();
            var state = new Dictionary<string, int>();  // 1 visiting, 2 done
            foreach (string item in wanted.Keys.ToList())
            {
                Visit(item, 0, book, options, chosen, order, state);
            }
            order.Reverse();

            foreach (string item in order)
            {
                if (!wanted.TryGetValue(item, out int amount) || amount <= 0)
                {
                    continue;
                }
                if (!chosen.TryGetValue(item, out Recipe recipe) || recipe == null)
                {
                    Add(result.Inputs, item, amount);
                    continue;
                }
                int needed = (amount + recipe.OutCount - 1) / Math.Max(1, recipe.OutCount);
                int crafts = options.Quantities != null && options.Quantities.TryGetValue(recipe.Id, out int set) ? Math.Max(0, set) : needed;
                Add(result.Needed, recipe.Id, needed);
                Add(result.Crafts, recipe.Id, crafts);
                Add(result.Made, item, crafts * recipe.OutCount);
                if (!recipe.Unlocked)
                {
                    result.Locked.Add(recipe.Id);
                }
                if (!result.RecipeOrder.Contains(recipe.Id))
                {
                    result.RecipeOrder.Add(recipe.Id);
                }
                foreach (Ingredient need in recipe.Needs)
                {
                    Add(wanted, need.Item, crafts * need.Count);
                }
            }
            return result;
        }

        private static void Visit(string item, int depth, RecipeBook book, PlanOptions options,
            Dictionary<string, Recipe> chosen, List<string> order, Dictionary<string, int> state)
        {
            if (state.TryGetValue(item, out int s))
            {
                return;  // done, or a cycle: the item is then treated as an input
            }
            state[item] = 1;
            Recipe recipe = depth < MaxDepth && !options.External.Contains(item) ? book.Choose(item, options.Factory, options.Preferred) : null;
            chosen[item] = recipe;
            if (recipe != null)
            {
                foreach (Ingredient need in recipe.Needs)
                {
                    if (state.TryGetValue(need.Item, out int ns) && ns == 1)
                    {
                        continue;
                    }
                    Visit(need.Item, depth + 1, book, options, chosen, order, state);
                }
            }
            state[item] = 2;
            order.Add(item);
        }

        public static LineResult ForOrders(IEnumerable<Order> orders, RecipeBook book, PlanOptions options = null) =>
            Compute(orders.GroupBy(o => o.BoxItem).Select(g => new KeyValuePair<string, int>(g.Key, g.Sum(o => o.Count))),
                book, options);

        /// <summary>
        /// Ranks the orders that could be added next: the fewest new machines first (reusing what the plan
        /// and the factory already make), then the biggest reward. Orders needing unlearned recipes go last.
        /// </summary>
        public static List<Expansion> RankExpansions(IEnumerable<Order> candidates, LineResult plan, RecipeBook book,
            PlanOptions options = null)
        {
            options = options ?? new PlanOptions();
            var have = new HashSet<string>(plan?.Crafts.Keys ?? Enumerable.Empty<string>());
            if (options.Factory != null)
            {
                have.UnionWith(options.Factory.RunningRecipes);
            }
            var list = new List<Expansion>();
            foreach (Order order in candidates)
            {
                LineResult line = ForOrders(new[] { order }, book, options);
                var expansion = new Expansion { Order = order, Line = line };
                foreach (string id in line.RecipeOrder)
                {
                    if (!have.Contains(id) && book.ById.TryGetValue(id, out Recipe r))
                    {
                        expansion.NewRecipes.Add(r);
                    }
                }
                list.Add(expansion);
            }
            return list
                .OrderBy(e => e.Locked)
                .ThenBy(e => e.NewRecipes.Count)
                .ThenByDescending(e => e.Order.Happiness)
                .ThenBy(e => e.Order.Id, StringComparer.Ordinal)
                .ToList();
        }

        private static void Add(Dictionary<string, int> map, string key, int value) =>
            map[key] = map.TryGetValue(key, out int v) ? v + value : value;
    }
}
