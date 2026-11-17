using System.Collections.Generic;

namespace GK2Mods.FactoryPlanner.Core
{
    /// <summary>What a conveyor chest side lets out.</summary>
    public enum SideMode
    {
        /// <summary>The game's own "?" side: the first item in the chest.</summary>
        Any,
        /// <summary>Only the listed items.</summary>
        Items,
        /// <summary>What the machines down this side's belt still lack for their recipe, and no more than that.</summary>
        Demand,
        /// <summary>Everything no other side of the chest lists.</summary>
        Rest,
        /// <summary>Nothing.</summary>
        Blocked,
    }

    public static class SidePicker
    {
        /// <summary>
        /// The item a side gives next, or null when it has nothing to give.
        /// </summary>
        /// <param name="inventory">Item ids the chest holds, in chest order.</param>
        /// <param name="items">The side's list (Items mode).</param>
        /// <param name="claimedElsewhere">Items the chest's other sides list (Rest mode).</param>
        /// <param name="last">The item this side gave last: a list takes turns, starting after it.</param>
        public static string Pick(SideMode mode, IReadOnlyList<string> inventory, IReadOnlyList<string> items,
            ICollection<string> claimedElsewhere, string last)
        {
            switch (mode)
            {
                case SideMode.Blocked:
                    return null;
                case SideMode.Any:
                    return inventory.Count > 0 ? inventory[0] : null;
                case SideMode.Rest:
                    foreach (string id in inventory)
                    {
                        if (claimedElsewhere == null || !claimedElsewhere.Contains(id))
                        {
                            return id;
                        }
                    }
                    return null;
                default:
                {
                    if (items == null || items.Count == 0)
                    {
                        return null;
                    }
                    var held = new HashSet<string>(inventory);
                    int start = 0;
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (items[i] == last)
                        {
                            start = i + 1;
                            break;
                        }
                    }
                    for (int i = 0; i < items.Count; i++)
                    {
                        string id = items[(start + i) % items.Count];
                        if (held.Contains(id))
                        {
                            return id;
                        }
                    }
                    return null;
                }
            }
        }

        /// <summary>Adds the item to a side's list, or takes it out when it is already there.</summary>
        public static List<string> Toggle(IEnumerable<string> items, string id)
        {
            var result = new List<string>(items);
            if (!result.Remove(id))
            {
                result.Add(id);
            }
            return result;
        }

        /// <summary>One machine down a demand side: what a craft takes and what the machine already holds.</summary>
        public sealed class Taker
        {
            public Dictionary<string, int> Needs = new Dictionary<string, int>();
            public Dictionary<string, int> Has = new Dictionary<string, int>();
        }

        /// <summary>
        /// The item a demand side gives next: the one the machines lack the most, counting what is already on the
        /// belts on the way to them. Null when nothing is lacking or the chest has none of it. Sending only what is
        /// lacking keeps one belt free to carry every ingredient of a recipe without one of them filling it up.
        /// </summary>
        /// <param name="inventory">Item ids the chest holds.</param>
        /// <param name="onTheWay">Items already on the belts between the chest and the machines.</param>
        public static string PickDemand(IReadOnlyList<string> inventory, IEnumerable<Taker> takers, IDictionary<string, int> onTheWay)
        {
            var lacking = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (Taker t in takers)
            {
                foreach (var need in t.Needs)
                {
                    t.Has.TryGetValue(need.Key, out int has);
                    int missing = need.Value - has;
                    if (missing <= 0)
                    {
                        continue;
                    }
                    if (!lacking.ContainsKey(need.Key))
                    {
                        lacking[need.Key] = 0;
                        order.Add(need.Key);
                    }
                    lacking[need.Key] += missing;
                }
            }
            string best = null;
            int most = 0;
            foreach (string id in order)
            {
                int coming = 0;
                onTheWay?.TryGetValue(id, out coming);
                int left = lacking[id] - coming;
                if (left > most && Holds(inventory, id))
                {
                    most = left;
                    best = id;
                }
            }
            return best;
        }

        /// <summary>Leaves each machine only the ingredients every machine takes (for machines sharing a splitter).</summary>
        public static void KeepCommonNeeds(IList<Taker> takers)
        {
            if (takers.Count < 2)
            {
                return;
            }
            var common = new HashSet<string>(takers[0].Needs.Keys);
            for (int i = 1; i < takers.Count; i++)
            {
                common.IntersectWith(takers[i].Needs.Keys);
            }
            foreach (Taker t in takers)
            {
                foreach (string id in new List<string>(t.Needs.Keys))
                {
                    if (!common.Contains(id))
                    {
                        t.Needs.Remove(id);
                    }
                }
            }
        }

        /// <summary>How much use a splitter's exit has for an item, best first.</summary>
        public enum ExitRank
        {
            /// <summary>Only machines down this exit, and none takes the item: it would stop at an input and block the belt.</summary>
            Useless,
            /// <summary>A machine down this exit takes the item, but has (or is about to get) all it needs for now.</summary>
            Takes,
            /// <summary>The exit also leads to a chest or the like, which takes anything.</summary>
            Open,
            /// <summary>A machine down this exit still lacks the item.</summary>
            Lacks,
        }

        /// <param name="coming">How many of the item are already on this exit's belts.</param>
        /// <param name="open">The exit also ends somewhere that isn't a machine.</param>
        public static ExitRank RankExit(string item, IEnumerable<Taker> takers, int coming, bool open)
        {
            int lacking = 0;
            bool takes = false;
            foreach (Taker t in takers)
            {
                if (!t.Needs.TryGetValue(item, out int need))
                {
                    continue;
                }
                takes = true;
                t.Has.TryGetValue(item, out int has);
                if (need > has)
                {
                    lacking += need - has;
                }
            }
            return lacking - coming > 0 ? ExitRank.Lacks : open ? ExitRank.Open : takes ? ExitRank.Takes : ExitRank.Useless;
        }

        /// <summary>The exit (0 or 1) with the better use for the item; -1 when they are even (the splitter takes turns).</summary>
        public static int PickExit(ExitRank first, ExitRank second) => first == second ? -1 : first > second ? 0 : 1;

        private static bool Holds(IReadOnlyList<string> inventory, string id)
        {
            for (int i = 0; i < inventory.Count; i++)
            {
                if (inventory[i] == id)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Right click on a side with no item: Any, then Demand, Rest, Blocked, then Any again.</summary>
        public static SideMode Next(SideMode mode)
        {
            switch (mode)
            {
                case SideMode.Any:
                    return SideMode.Demand;
                case SideMode.Demand:
                    return SideMode.Rest;
                case SideMode.Rest:
                    return SideMode.Blocked;
                default:
                    return SideMode.Any;
            }
        }
    }
}
