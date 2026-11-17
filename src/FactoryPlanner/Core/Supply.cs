using System;
using System.Collections.Generic;
using System.Linq;

namespace GK2Mods.FactoryPlanner.Core
{
    /// <summary>One delivery: so much of an item a day into one conveyor chest.</summary>
    public sealed class SupplyLine
    {
        public string Item;
        /// <summary>Key of the chest (FeedGraph) the item goes into.</summary>
        public string Chest;
        public int PerDay;
    }

    public sealed class SupplyPlan
    {
        public List<SupplyLine> Lines = new List<SupplyLine>();
        /// <summary>Items a machine needs from outside with no conveyor chest on its input belts to put them in.</summary>
        public List<(string Item, string Machine)> NoChest = new List<(string, string)>();
    }

    /// <summary>
    /// What the week needs from outside the factory, and where to put it. An ingredient comes from outside when the
    /// factory has no reserve of it and no machine of the plan makes it (special wood, flax, steel ingots...). It goes
    /// into a conveyor chest up the input belts of the machine that uses it, preferably one that already holds it or
    /// one nothing feeds (a chest filled by hand).
    /// </summary>
    public static class SupplyPlanner
    {
        public const int DaysAWeek = 6;

        /// <param name="inFactory">Items the factory's own reserves give (ore, coal, wood, the garden depot...).</param>
        public static SupplyPlan Plan(DispatchPlan plan, FeedGraph feed, ICollection<string> inFactory)
        {
            var result = new SupplyPlan();
            if (plan == null || feed == null)
            {
                return result;
            }
            var made = new HashSet<string>(plan.Assignments.Select(a => a.Recipe.OutItem));
            foreach (Assignment a in plan.Assignments)
            {
                foreach (Ingredient need in a.Recipe.Needs)
                {
                    if (made.Contains(need.Item) || (inFactory != null && inFactory.Contains(need.Item)))
                    {
                        continue;
                    }
                    int perDay = (int)Math.Ceiling(need.Count * Math.Max(1, a.Crafts) / (double)DaysAWeek);
                    string chest = feed.EntryChests(a.Machine.Key, need.Item).FirstOrDefault();
                    if (chest == null)
                    {
                        result.NoChest.Add((need.Item, a.Machine.Key));
                        continue;
                    }
                    SupplyLine line = result.Lines.FirstOrDefault(l => l.Item == need.Item && l.Chest == chest);
                    if (line == null)
                    {
                        result.Lines.Add(line = new SupplyLine { Item = need.Item, Chest = chest });
                    }
                    line.PerDay += perDay;
                }
            }
            return result;
        }

        /// <summary>How much to carry now: tops the chest up to a day's worth, within the stock outside and what the zombies can carry.</summary>
        public static int Amount(int perDay, int inChest, int stock, int carry) =>
            Math.Max(0, Math.Min(Math.Min(perDay - inChest, stock), carry));
    }
}
