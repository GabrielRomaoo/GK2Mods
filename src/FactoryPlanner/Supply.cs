using System;
using System.Collections.Generic;
using System.Linq;
using GK2Mods.FactoryPlanner.Core;
using HarmonyLib;
using LazyBearTechnology;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// Supplies from outside: the game only brings seven raw materials into the factory (the *_container_conveyor
    /// reserves) plus the garden depot and the wine pallet. Whatever else a recipe takes (special wood, flax, steel
    /// ingots, zombie power) had to be carried into a conveyor chest by hand. Once a week is dispatched, the factory's
    /// delivery zombies (Zumbi Entregador de Suprimentos, the ones that take the boxes out) bring those items back
    /// from the Storage Network's stock: on the dispatch and then once every new day, a day's worth into the conveyor
    /// chest that feeds the machine. No delivery zombie or no Storage Network: nothing moves.
    /// </summary>
    internal static class Supply
    {
        private const string ActiveRes = "gk2fp_supply_on";

        internal sealed class Result
        {
            public bool Off;
            public bool NoNetwork;
            /// <summary>Lines the plan wanted delivered (0: the week takes nothing from outside).</summary>
            public int Wanted;
            public int Zombies;
            public Dictionary<string, int> Moved = new Dictionary<string, int>();
            /// <summary>Item -> how much the network lacked.</summary>
            public Dictionary<string, int> Short = new Dictionary<string, int>();
            public List<string> NoChest = new List<string>();
            /// <summary>Wanted more than the zombies carry in a day.</summary>
            public bool OverCarry;
        }

        internal static void Init()
        {
            EnvironmentEngine.OnNewDayStarted += OnNewDay;
        }

        private static void OnNewDay(int day)
        {
            try
            {
                if (MainGame.PlayerData == null || MainGame.PlayerData.GetRes(ActiveRes) <= 0f)
                {
                    return;
                }
                Result r = Run(WeekDispatch.Read());
                Plugin.Debug($"Supply, day {day}: {Describe(r)}");
                if (r.Moved.Count > 0 || r.Short.Count > 0)
                {
                    WeekDispatch.Notify("gk2fp_supply_day", Texts.Pt("Suprimentos da fábrica: ", "Factory supplies: ") + Describe(r), Icons.DraftingTable);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Supply failed: {e}");
            }
        }

        /// <summary>The week was dispatched: from now on the zombies bring a day's worth every day, starting now.</summary>
        internal static Result AfterDispatch(WeekDispatch.Snapshot snap)
        {
            MainGame.PlayerData.SetRes(ActiveRes, 1f);
            return Run(snap);
        }

        /// <summary>The Storage Network's chests (its mod is a separate plugin, so it is read by reflection), or null.</summary>
        private static List<Inventory> NetworkPool()
        {
            try
            {
                Type network = AccessTools.TypeByName("GK2Mods.StorageNetwork.Network");
                if (network == null)
                {
                    return null;
                }
                object central = AccessTools.Property(network, "CentralInventory")?.GetValue(null);
                if (central == null)
                {
                    return null;
                }
                return (AccessTools.Property(network, "Pool")?.GetValue(null) as IEnumerable<Inventory>)?.Where(i => i != null).ToList();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Supply: the Storage Network could not be read: {e.Message}");
                return null;
            }
        }

        private static int DeliveryZombies()
        {
            WorldData world = MainGame.Instance?.GameSave?.WorldData;
            if (world == null)
            {
                return 0;
            }
            return world.Cache.wgoDataByUidCache.Values.OfType<ZombieWgoData>()
                .Count(z => z.ZombieType == ZombieType.ConveyorTransporter && z.AttachedWgoData != null);
        }

        /// <summary>What the factory's own reserves give: the raw reserves, the garden depot, the wine pallet.</summary>
        private static HashSet<string> InFactory()
        {
            var items = new HashSet<string>();
            ConveyorSystemData system = MainGame.Instance?.GameSave?.conveyorSystemData;
            foreach (ConveyorComponent c in system?.conveyorComponents ?? new List<ConveyorComponent>())
            {
                if (!(c?.WgoData is ConveyorWgoData data) || data.isTempObject || !(c is ConveyorPalletComponent || c is ConveyorChestOutComponent))
                {
                    continue;
                }
                foreach (Item item in data.Inventory?.Data?.Inventory ?? new List<Item>())
                {
                    if (item != null && !string.IsNullOrEmpty(item.id))
                    {
                        items.Add(item.id);
                    }
                }
                int cut = data.id?.IndexOf("_container_conveyor", StringComparison.Ordinal) ?? -1;
                if (cut > 0)
                {
                    string raw = data.id.Substring(0, cut);
                    items.Add(raw);
                    items.Add("1h_" + raw);  // stone and marble come as one-handed blocks
                }
            }
            return items;
        }

        internal static Result Run(WeekDispatch.Snapshot snap)
        {
            var result = new Result();
            if (!Plugin.SupplyEnabled.Value)
            {
                result.Off = true;
                return result;
            }
            HashSet<string> inFactory = InFactory();
            inFactory.UnionWith(snap.Book.ById.Values.Select(r => r.OutItem));  // what the factory makes is not fetched
            SupplyPlan plan = SupplyPlanner.Plan(snap.Plan, snap.Feed, inFactory);
            result.NoChest.AddRange(plan.NoChest.Select(n => n.Item).Distinct());
            result.Wanted = plan.Lines.Count;
            if (plan.Lines.Count == 0)
            {
                return result;
            }
            result.Zombies = DeliveryZombies();
            List<Inventory> pool = NetworkPool();
            if (pool == null)
            {
                result.NoNetwork = true;
                return result;
            }
            if (result.Zombies == 0)
            {
                return result;
            }
            int carry = result.Zombies * Math.Max(1, Plugin.SupplyPerZombie.Value);
            foreach (SupplyLine line in plan.Lines)
            {
                Inventory chest;
                try
                {
                    chest = MainGame.WorldData.GetWgoData(new SGuid(line.Chest))?.Inventory;
                }
                catch (Exception)
                {
                    chest = null;
                }
                if (chest == null)
                {
                    continue;
                }
                int inChest = chest.Data.GetTotalCountInInventory(line.Item);
                int wanted = Math.Max(0, line.PerDay - inChest);
                if (wanted == 0)
                {
                    continue;
                }
                int stock = pool.Sum(i => i.Data.GetTotalCountInInventory(line.Item));
                int amount = SupplyPlanner.Amount(line.PerDay, inChest, stock, carry);
                result.OverCarry |= wanted > carry && stock > carry;
                int moved = 0;
                if (amount > 0 && chest.AddItemToInventory(new Item(line.Item, amount), out List<Item> added, null, true))
                {
                    moved = added.Sum(i => i.Count);
                }
                int left = moved;
                foreach (Inventory from in pool)
                {
                    if (left <= 0)
                    {
                        break;
                    }
                    int has = from.Data.GetTotalCountInInventory(line.Item);
                    int take = Math.Min(has, left);
                    if (take > 0)
                    {
                        from.RemoveItemById(line.Item, take, null, null, true);
                        left -= take;
                    }
                }
                carry -= moved;
                if (moved > 0)
                {
                    result.Moved[line.Item] = (result.Moved.TryGetValue(line.Item, out int m) ? m : 0) + moved;
                }
                if (stock < wanted)
                {
                    result.Short[line.Item] = (result.Short.TryGetValue(line.Item, out int s) ? s : 0) + wanted - stock;
                }
                Plugin.Debug($"Supply: {line.Item} wanted {wanted} (a day {line.PerDay}, chest has {inChest}), network has {stock}, moved {moved}");
            }
            return result;
        }

        private static float offerAt = -100f;
        private static PlayerData offerFor;
        private static HashSet<string> offer = new HashSet<string>();

        /// <summary>
        /// Tells the belt graph what the delivery zombies can bring: a conveyor chest nothing feeds (one filled by
        /// hand) counts as able to give any ingredient the Storage Network has in stock. Without this the dispatcher
        /// would refuse a machine whose only missing ingredient is exactly the one the zombies are about to bring.
        /// </summary>
        internal static void Offer(FeedGraph graph, ICollection<string> ingredients, RecipeBook book)
        {
            if (!Plugin.SupplyEnabled.Value)
            {
                return;
            }
            if (UnityEngine.Time.unscaledTime - offerAt > 5f || !ReferenceEquals(offerFor, MainGame.PlayerData))
            {
                offerAt = UnityEngine.Time.unscaledTime;
                offerFor = MainGame.PlayerData;
                offer = new HashSet<string>();
                List<Inventory> pool = DeliveryZombies() > 0 ? NetworkPool() : null;
                if (pool != null)
                {
                    HashSet<string> own = InFactory();
                    foreach (string item in ingredients)
                    {
                        // Only what no factory recipe makes: kits and boxes stay the factory's own business.
                        if (!own.Contains(item) && !book.IsMade(item) && pool.Any(i => i.Data.GetTotalCountInInventory(item) > 0))
                        {
                            offer.Add(item);
                        }
                    }
                }
            }
            if (offer.Count == 0)
            {
                return;
            }
            foreach (FeedNode node in graph.Nodes.Values)
            {
                if (node.Kind == FeedKind.Chest && node.Parents.Count == 0)
                {
                    node.Offered.UnionWith(offer);
                }
            }
        }

        /// <summary>Lab runs: puts items into the Storage Network's Central Storage (a test save only).</summary>
        internal static int LabGive(string item, int count)
        {
            offerAt = -100f;
            int left = count;
            foreach (Inventory chest in NetworkPool() ?? new List<Inventory>())
            {
                if (left <= 0)
                {
                    break;
                }
                if (chest.AddItemToInventory(new Item(item, left), out List<Item> added, null, true))
                {
                    left -= added.Sum(i => i.Count);
                }
            }
            return count - left;
        }

        /// <summary>Lab runs: how much of an item the Storage Network holds (-1 without a network).</summary>
        internal static int LabStock(string item) => NetworkPool()?.Sum(i => i.Data.GetTotalCountInInventory(item)) ?? -1;

        internal static string Describe(Result r)
        {
            if (r.Off)
            {
                return Texts.Pt("suprimentos desligados na configuração", "supplies turned off in the config");
            }
            var parts = new List<string>();
            if (r.Moved.Count > 0)
            {
                parts.Add(Texts.Pt("trouxeram ", "brought ") + string.Join(", ", r.Moved.Select(kv => $"{kv.Value} {Texts.Name(kv.Key)}")));
            }
            if (r.Short.Count > 0)
            {
                parts.Add(Texts.Pt("falta na rede: ", "the network lacks: ") + string.Join(", ", r.Short.Select(kv => $"{kv.Value} {Texts.Name(kv.Key)}")));
            }
            if (r.NoChest.Count > 0)
            {
                parts.Add(Texts.Pt("sem baú de esteira na entrada para: ", "no conveyor chest on the input for: ") + string.Join(", ", r.NoChest.Select(Texts.Name)));
            }
            if (r.NoNetwork)
            {
                parts.Add(Texts.Pt("sem Armazenamento Central para buscar", "no Central Storage to fetch from"));
            }
            else if (r.Wanted > 0 && r.Zombies == 0)
            {
                parts.Add(Texts.Pt("sem Zumbi Entregador trabalhando na fábrica", "no delivery zombie at work in the factory"));
            }
            if (r.OverCarry)
            {
                parts.Add(Texts.Pt("os entregadores não carregam tudo num dia", "the zombies can't carry it all in a day"));
            }
            return parts.Count == 0 ? Texts.Pt("nada a buscar", "nothing to fetch") : string.Join("; ", parts);
        }
    }
}
