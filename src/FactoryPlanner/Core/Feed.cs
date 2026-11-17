using System;
using System.Collections.Generic;
using System.Linq;

namespace GK2Mods.FactoryPlanner.Core
{
    public enum FeedKind
    {
        /// <summary>Belt, splitter, underground or station: passes on what it gets.</summary>
        Belt,
        /// <summary>Conveyor chest: pulls everything its belts bring and gives it out on its sides.</summary>
        Chest,
        /// <summary>A reserve or storage that only gives (the zombies fill it).</summary>
        Source,
        Machine,
        Other,
    }

    /// <summary>One piece of the factory's belt network, as the dispatcher sees it.</summary>
    public sealed class FeedNode
    {
        public string Key;
        public FeedKind Kind;
        /// <summary>Pieces that hand items to this one (for a machine: the belts on its inputs).</summary>
        public List<string> Parents = new List<string>();
        /// <summary>What a chest or source holds now.</summary>
        public HashSet<string> Holds = new HashSet<string>();
        /// <summary>What a source is meant for even when empty (a reserve of iron ore).</summary>
        public HashSet<string> Accepts = new HashSet<string>();
        /// <summary>
        /// What can be brought into this chest from outside the factory when a machine needs it. It reaches the
        /// machines like anything the chest holds, but it is never counted as pushed: it only comes when asked for.
        /// </summary>
        public HashSet<string> Offered = new HashSet<string>();
        /// <summary>Chest/source side filters: child key -> the only items that side gives (none = a blocked side).</summary>
        public Dictionary<string, HashSet<string>> FilterFor = new Dictionary<string, HashSet<string>>();
        /// <summary>Children whose side gives whatever the chest's other sides don't list.</summary>
        public HashSet<string> RestFor = new HashSet<string>();
        /// <summary>Children whose side gives only what the machines down the belt lack (it never pushes an item nobody takes).</summary>
        public HashSet<string> DemandFor = new HashSet<string>();
    }

    /// <summary>
    /// The factory's belts as a graph, to answer "what can arrive at this machine". Items are followed back from a
    /// machine's input belts through belts, splitters and chests to where they come from: a reserve, what a chest
    /// holds, or another machine's product (which depends on the recipe the plan gives it).
    /// </summary>
    public sealed class FeedGraph
    {
        public readonly Dictionary<string, FeedNode> Nodes = new Dictionary<string, FeedNode>();
        private Dictionary<string, List<string>> children;

        public FeedNode Add(string key, FeedKind kind)
        {
            if (!Nodes.TryGetValue(key, out FeedNode node))
            {
                Nodes[key] = node = new FeedNode { Key = key, Kind = kind };
                children = null;
            }
            return node;
        }

        public void Link(string from, string to)
        {
            if (Nodes.TryGetValue(to, out FeedNode node) && !node.Parents.Contains(from))
            {
                node.Parents.Add(from);
                children = null;
            }
        }

        private Dictionary<string, List<string>> Children()
        {
            if (children != null)
            {
                return children;
            }
            children = new Dictionary<string, List<string>>();
            foreach (FeedNode node in Nodes.Values)
            {
                foreach (string p in node.Parents)
                {
                    if (!children.TryGetValue(p, out List<string> list))
                    {
                        children[p] = list = new List<string>();
                    }
                    list.Add(node.Key);
                }
            }
            return children;
        }

        /// <summary>What reaches a machine's inputs, and the machines upstream that supply each item.</summary>
        public sealed class Arrival
        {
            public HashSet<string> Items = new HashSet<string>();
            /// <summary>
            /// The items that come whether the machine takes them or not. What comes through a demand side is left
            /// out: it only comes when asked for, so it can't sit on the belt in the way.
            /// </summary>
            public HashSet<string> Pushed = new HashSet<string>();
            /// <summary>Item -> machines whose product reaches here.</summary>
            public Dictionary<string, HashSet<string>> FromMachines = new Dictionary<string, HashSet<string>>();
            /// <summary>Every machine met upstream, supplying or not.</summary>
            public HashSet<string> Upstream = new HashSet<string>();
        }

        /// <param name="productOf">The item a machine puts out (by the plan), or null when idle.</param>
        public Arrival Arriving(string machineKey, Func<string, string> productOf)
        {
            var result = new Arrival();
            if (!Nodes.TryGetValue(machineKey, out FeedNode machine))
            {
                return result;
            }
            var beltMemo = new Dictionary<string, HashSet<string>>();
            var chestMemo = new Dictionary<string, HashSet<string>>();
            var busy = new HashSet<string>();
            foreach (string p in machine.Parents)
            {
                result.Items.UnionWith(Give(p, machineKey, productOf, result, beltMemo, chestMemo, busy));
            }
            pushedOnly = true;
            try
            {
                var scratch = new Arrival();
                beltMemo = new Dictionary<string, HashSet<string>>();
                chestMemo = new Dictionary<string, HashSet<string>>();
                foreach (string p in machine.Parents)
                {
                    result.Pushed.UnionWith(Give(p, machineKey, productOf, scratch, beltMemo, chestMemo, busy));
                }
            }
            finally
            {
                pushedOnly = false;
            }
            return result;
        }

        /// <summary>Second pass of Arriving: demand sides give nothing.</summary>
        private bool pushedOnly;

        /// <summary>Items the node can hand to the child.</summary>
        private HashSet<string> Give(string key, string child, Func<string, string> productOf, Arrival result,
            Dictionary<string, HashSet<string>> beltMemo, Dictionary<string, HashSet<string>> chestMemo, HashSet<string> busy)
        {
            if (!Nodes.TryGetValue(key, out FeedNode node))
            {
                return new HashSet<string>();
            }
            switch (node.Kind)
            {
                case FeedKind.Machine:
                {
                    result.Upstream.Add(key);
                    string product = productOf(key);
                    var set = new HashSet<string>();
                    if (product != null)
                    {
                        set.Add(product);
                        if (!result.FromMachines.TryGetValue(product, out HashSet<string> from))
                        {
                            result.FromMachines[product] = from = new HashSet<string>();
                        }
                        from.Add(key);
                    }
                    return set;
                }
                case FeedKind.Belt:
                {
                    if (beltMemo.TryGetValue(key, out HashSet<string> known))
                    {
                        return known;
                    }
                    if (!busy.Add(key))
                    {
                        return new HashSet<string>();  // a loop of belts
                    }
                    var set = new HashSet<string>();
                    foreach (string p in node.Parents)
                    {
                        set.UnionWith(Give(p, key, productOf, result, beltMemo, chestMemo, busy));
                    }
                    busy.Remove(key);
                    return beltMemo[key] = set;
                }
                case FeedKind.Chest:
                case FeedKind.Source:
                {
                    if (pushedOnly && node.DemandFor.Contains(child))
                    {
                        return new HashSet<string>();
                    }
                    if (!chestMemo.TryGetValue(key, out HashSet<string> possible))
                    {
                        if (!busy.Add(key))
                        {
                            return new HashSet<string>();
                        }
                        possible = new HashSet<string>(node.Holds);
                        possible.UnionWith(node.Accepts);
                        if (!pushedOnly)
                        {
                            possible.UnionWith(node.Offered);
                        }
                        if (node.Kind == FeedKind.Chest)
                        {
                            foreach (string p in node.Parents)
                            {
                                possible.UnionWith(Give(p, key, productOf, result, beltMemo, chestMemo, busy));
                            }
                        }
                        busy.Remove(key);
                        chestMemo[key] = possible;
                    }
                    if (node.FilterFor.TryGetValue(child, out HashSet<string> only))
                    {
                        return new HashSet<string>(only);
                    }
                    if (node.RestFor.Contains(child))
                    {
                        var rest = new HashSet<string>(possible);
                        foreach (var filter in node.FilterFor)
                        {
                            rest.ExceptWith(filter.Value);
                        }
                        return rest;
                    }
                    return possible;
                }
                default:
                    return new HashSet<string>();
            }
        }

        /// <summary>
        /// Conveyor chests up the machine's input belts that can hand it the item (the side towards it lets the item
        /// out): where to put an ingredient brought from outside. Chests that already hold the item come first, then
        /// chests nothing feeds (filled by hand), then the nearest.
        /// </summary>
        public List<string> EntryChests(string machineKey, string item)
        {
            var found = new List<(string key, int rank, int order)>();
            if (!Nodes.TryGetValue(machineKey, out FeedNode machine))
            {
                return new List<string>();
            }
            var seen = new HashSet<string>();
            var queue = new Queue<(string key, string child)>();
            foreach (string p in machine.Parents)
            {
                queue.Enqueue((p, machineKey));
            }
            while (queue.Count > 0)
            {
                var (key, child) = queue.Dequeue();
                if (!seen.Add(key) || !Nodes.TryGetValue(key, out FeedNode node))
                {
                    continue;
                }
                if (node.Kind == FeedKind.Belt)
                {
                    foreach (string p in node.Parents)
                    {
                        queue.Enqueue((p, key));
                    }
                    continue;
                }
                if (node.Kind != FeedKind.Chest)
                {
                    continue;
                }
                bool lets;
                if (node.FilterFor.TryGetValue(child, out HashSet<string> only))
                {
                    lets = only.Contains(item);
                }
                else if (node.RestFor.Contains(child))
                {
                    lets = !node.FilterFor.Values.Any(f => f.Contains(item));
                }
                else
                {
                    lets = true;
                }
                if (lets)
                {
                    found.Add((key, node.Holds.Contains(item) ? 0 : node.Parents.Count == 0 ? 1 : 2, found.Count));
                }
            }
            return found.OrderBy(f => f.rank).ThenBy(f => f.order).Select(f => f.key).ToList();
        }

        /// <summary>Machines the machine's output reaches, through belts and chests.</summary>
        public HashSet<string> Downstream(string machineKey)
        {
            var found = new HashSet<string>();
            Dictionary<string, List<string>> kids = Children();
            var seen = new HashSet<string> { machineKey };
            var queue = new Queue<string>();
            queue.Enqueue(machineKey);
            while (queue.Count > 0)
            {
                string at = queue.Dequeue();
                if (!kids.TryGetValue(at, out List<string> next))
                {
                    continue;
                }
                foreach (string k in next)
                {
                    if (!seen.Add(k) || !Nodes.TryGetValue(k, out FeedNode n))
                    {
                        continue;
                    }
                    if (n.Kind == FeedKind.Machine)
                    {
                        found.Add(k);
                    }
                    else
                    {
                        queue.Enqueue(k);
                    }
                }
            }
            return found;
        }
    }
}
