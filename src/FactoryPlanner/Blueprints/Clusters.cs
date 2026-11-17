using System;
using System.Collections.Generic;
using System.Linq;
using GK2Mods.FactoryPlanner.Core;

namespace GK2Mods.FactoryPlanner.Blueprints
{
    /// <summary>One production line of the player's factory: machines joined by belts, with the chests at its ends.</summary>
    internal sealed class FactoryCluster
    {
        /// <summary>Main raw material the line eats (an item id), the "family" it is listed under; null if unknown.</summary>
        public string RawItem;
        public List<WgoData> Objects = new List<WgoData>();
        public List<string> Products = new List<string>();
        public List<string> Machines = new List<string>();
        public int Belts;
        public int Chests;
        /// <summary>The line copied as a blueprint, ready to lay elsewhere (not saved unless the player asks).</summary>
        public Blueprint Blueprint;

        public string MainProduct => Products.FirstOrDefault();
    }

    /// <summary>
    /// Finds the factory's production lines. Pieces are linked through the belts' own connections (each conveyor
    /// element knows its parents) and the belts a machine spawns with itself. Chests are boundaries: a chest belongs
    /// to every line it touches, but a line doesn't continue through it, so one shared chest doesn't merge the
    /// whole factory into a single line.
    /// </summary>
    internal static class Clusters
    {
        internal static List<FactoryCluster> Find(RecipeBook book, FactoryState factory)
        {
            var result = new List<FactoryCluster>();
            ConveyorSystemData system = MainGame.Instance?.GameSave?.conveyorSystemData;
            WorldZoneData zone = MainGame.Instance?.conveyorSystem?.ConveyorWorldZone;
            if (system?.conveyorComponents == null || zone == null)
            {
                return result;
            }

            var nodes = new Dictionary<SGuid, WgoData>();
            var edges = new List<(SGuid a, SGuid b)>();
            foreach (ConveyorComponent component in system.conveyorComponents)
            {
                ConveyorWgoData data = component?.WgoData;
                if (data == null || data.isTempObject)
                {
                    continue;
                }
                nodes[data.UniqueId] = data;
                foreach (SGuid up in component.ParentsData.Keys)
                {
                    edges.Add((data.UniqueId, up));
                }
                foreach (SGuid cell in data.HardConnectedWGOs ?? new List<SGuid>())
                {
                    edges.Add((data.UniqueId, cell));
                }
            }

            // Union the non-chest pieces; remember which chests touch which pieces.
            var parent = nodes.Keys.ToDictionary(k => k, k => k);
            SGuid Root(SGuid x)
            {
                while (!parent[x].Equals(x))
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }
            var chestLinks = new Dictionary<SGuid, HashSet<SGuid>>();
            foreach (var (a, b) in edges)
            {
                if (!nodes.TryGetValue(a, out WgoData da) || !nodes.TryGetValue(b, out WgoData db))
                {
                    continue;
                }
                bool ca = IsChest(da.id), cb = IsChest(db.id);
                if (ca && cb)
                {
                    continue;
                }
                if (ca || cb)
                {
                    SGuid chest = ca ? a : b, other = ca ? b : a;
                    if (!chestLinks.TryGetValue(chest, out HashSet<SGuid> set))
                    {
                        chestLinks[chest] = set = new HashSet<SGuid>();
                    }
                    set.Add(other);
                    continue;
                }
                SGuid ra = Root(a), rb = Root(b);
                if (!ra.Equals(rb))
                {
                    parent[ra] = rb;
                }
            }

            var groups = new Dictionary<SGuid, FactoryCluster>();
            foreach (var kv in nodes.Where(kv => !IsChest(kv.Value.id)))
            {
                SGuid root = Root(kv.Key);
                if (!groups.TryGetValue(root, out FactoryCluster cluster))
                {
                    groups[root] = cluster = new FactoryCluster();
                }
                cluster.Objects.Add(kv.Value);
            }
            foreach (var kv in chestLinks)
            {
                foreach (SGuid root in kv.Value.Select(Root).Distinct())
                {
                    if (groups.TryGetValue(root, out FactoryCluster cluster))
                    {
                        cluster.Objects.Add(nodes[kv.Key]);
                    }
                }
            }

            foreach (FactoryCluster cluster in groups.Values)
            {
                var products = new Dictionary<string, int>();
                foreach (WgoData data in cluster.Objects)
                {
                    if (data.Definition?.conveyorType == ConveyorElementType.Workbench)
                    {
                        cluster.Machines.Add(data.id);
                        string product = FactoryPieces.ProductOf(data, book);
                        if (product != null)
                        {
                            products[product] = products.TryGetValue(product, out int n) ? n + 1 : 1;
                        }
                    }
                    else if (IsChest(data.id))
                    {
                        cluster.Chests++;
                    }
                    else
                    {
                        cluster.Belts++;
                    }
                }
                if (cluster.Machines.Count == 0)
                {
                    continue;  // belts and chests only: nothing to recommend
                }
                // The line's last products first: those no other machine of the line consumes.
                var used = new HashSet<string>(products.Keys.SelectMany(p => book.Choose(p, factory)?.Needs.Select(n => n.Item) ?? Enumerable.Empty<string>()));
                cluster.Products = products.Keys.OrderBy(p => used.Contains(p)).ThenByDescending(p => products[p]).ToList();
                cluster.RawItem = MainRaw(cluster.Products, book, factory);
                try
                {
                    cluster.Blueprint = FactoryPieces.Capture(zone, cluster.Objects);
                    cluster.Blueprint.Name = Texts.Pt("Linha: ", "Line: ") + (cluster.MainProduct != null ? Texts.Name(cluster.MainProduct) : Texts.Name(cluster.Machines[0]));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Cluster could not be copied: {e.Message}");
                }
                if (cluster.Blueprint != null && cluster.Blueprint.Pieces.Count > 0)
                {
                    result.Add(cluster);
                }
            }
            Plugin.Debug($"Clusters: {result.Count} line(s) from {nodes.Count} conveyor pieces");
            return result;
        }

        internal static bool IsChest(string id) =>
            id != null && (id.Contains("chest") || id.Contains("storage") || id.Contains("pallet"));

        /// <summary>The raw material (not made in the factory) the items need most of, following the recipes down.</summary>
        internal static string MainRaw(IEnumerable<string> items, RecipeBook book, FactoryState factory)
        {
            var raw = new Dictionary<string, double>();
            foreach (string item in items)
            {
                AddRaw(item, 1.0, book, factory, raw, 0);
            }
            return raw.Count == 0 ? null : raw.OrderByDescending(kv => kv.Value).First().Key;
        }

        private static void AddRaw(string item, double amount, RecipeBook book, FactoryState factory, Dictionary<string, double> raw, int depth)
        {
            Recipe recipe = depth < 10 && book.IsMade(item) ? book.Choose(item, factory) : null;
            if (recipe == null)
            {
                if (depth > 0)
                {
                    raw[item] = (raw.TryGetValue(item, out double r) ? r : 0) + amount;
                }
                return;
            }
            double crafts = amount / Math.Max(1, recipe.OutCount);
            foreach (Ingredient need in recipe.Needs)
            {
                AddRaw(need.Item, crafts * need.Count, book, factory, raw, depth + 1);
            }
        }
    }
}
