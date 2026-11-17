using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GK2Mods.FactoryPlanner.Core;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// What each side of a conveyor chest lets out. The game gives a side one item or "?" (the first item in the
    /// chest). Here a side can also list several items, give "on demand" (only what the machines down its belt lack
    /// for their recipe), take "the rest" (whatever no other side lists) or be blocked.
    ///
    /// The first listed item stays in the game's own slot (ConveyorChestSlotData.slotItemId), so a save opened
    /// without the mod still has that filter. Everything else lives in the save's PlayerData res, keyed by the belt
    /// on that side ("gk2fp_side_&lt;belt guid&gt;|&lt;item id | #demand | #rest | #block&gt;" = 1): a side's rule goes away
    /// with its belt, as the game's filter does.
    /// </summary>
    internal static class Sides
    {
        private const string Prefix = "gk2fp_side_";
        private const string RestToken = "#rest";
        private const string DemandToken = "#demand";
        private const string BlockToken = "#block";

        private sealed class Rule
        {
            internal SideMode Mode = SideMode.Any;
            internal readonly List<string> Extra = new List<string>();
        }

        private static PlayerData loadedFor;
        private static readonly Dictionary<string, Rule> rules = new Dictionary<string, Rule>();
        /// <summary>Belt -> the item its side gave last, so a list takes turns. Not saved.</summary>
        private static readonly Dictionary<string, string> lastGiven = new Dictionary<string, string>();

        private static Dictionary<string, Rule> Rules()
        {
            PlayerData player = MainGame.PlayerData;
            if (!ReferenceEquals(player, loadedFor))
            {
                Load(player);
            }
            return rules;
        }

        private static void Load(PlayerData player)
        {
            loadedFor = player;
            rules.Clear();
            lastGiven.Clear();
            GameRes res = player?.res;
            if (res == null)
            {
                return;
            }
            foreach (GameResAtom atom in res.List)
            {
                if (atom?.type == null || atom.value <= 0f || !atom.type.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                int bar = atom.type.IndexOf('|', Prefix.Length);
                if (bar < 0)
                {
                    continue;
                }
                string belt = atom.type.Substring(Prefix.Length, bar - Prefix.Length);
                string token = atom.type.Substring(bar + 1);
                if (!rules.TryGetValue(belt, out Rule rule))
                {
                    rules[belt] = rule = new Rule();
                }
                if (token == RestToken)
                {
                    rule.Mode = SideMode.Rest;
                }
                else if (token == DemandToken)
                {
                    rule.Mode = SideMode.Demand;
                }
                else if (token == BlockToken)
                {
                    rule.Mode = SideMode.Blocked;
                }
                else if (token.Length > 0 && !rule.Extra.Contains(token))
                {
                    rule.Extra.Add(token);
                }
            }
            Plugin.Debug($"Chest sides: {rules.Count} side rule(s) in this save");
        }

        private static string KeyOf(ConveyorChestSlotData slot) =>
            slot == null || SGuid.IsNullOrEmpty(slot.conveyorWgoDataUniqueId) ? null : slot.conveyorWgoDataUniqueId.ToString();

        /// <summary>The side's mode, with its items when it lists any.</summary>
        internal static SideMode ModeOf(ConveyorChestSlotData slot, out List<string> items)
        {
            items = new List<string>();
            string key = KeyOf(slot);
            if (key == null)
            {
                return SideMode.Any;
            }
            if (!string.IsNullOrEmpty(slot.slotItemId))
            {
                items.Add(slot.slotItemId);
            }
            if (Rules().TryGetValue(key, out Rule rule))
            {
                foreach (string id in rule.Extra)
                {
                    if (!items.Contains(id))
                    {
                        items.Add(id);
                    }
                }
                if (items.Count == 0)
                {
                    return rule.Mode;
                }
            }
            return items.Count > 0 ? SideMode.Items : SideMode.Any;
        }

        /// <summary>Adds the item to the side's list, or takes it out when it is already there.</summary>
        internal static void Toggle(ConveyorChestSlotData slot, string itemId)
        {
            ModeOf(slot, out List<string> items);
            Store(slot, SideMode.Any, SidePicker.Toggle(items, itemId));
        }

        /// <summary>A side with no item: Any, then Demand, Rest, Blocked.</summary>
        internal static SideMode Cycle(ConveyorChestSlotData slot)
        {
            SideMode next = SidePicker.Next(ModeOf(slot, out _));
            Store(slot, next, new List<string>());
            return next;
        }

        private static void Store(ConveyorChestSlotData slot, SideMode mode, List<string> items)
        {
            string key = KeyOf(slot);
            if (key == null)
            {
                return;
            }
            slot.SetItemSlotId(items.Count > 0 ? items[0] : string.Empty);
            Forget(key);
            var rule = new Rule();
            if (items.Count > 1)
            {
                rule.Extra.AddRange(items.Skip(1));
            }
            else if (items.Count == 0 && (mode == SideMode.Rest || mode == SideMode.Blocked || mode == SideMode.Demand))
            {
                rule.Mode = mode;
            }
            else
            {
                return;  // one item or "?": the game's own slot says it all
            }
            rules[key] = rule;
            PlayerData player = MainGame.PlayerData;
            foreach (string id in rule.Extra)
            {
                player.SetRes(Prefix + key + "|" + id, 1f);
            }
            if (rule.Mode == SideMode.Rest)
            {
                player.SetRes(Prefix + key + "|" + RestToken, 1f);
            }
            else if (rule.Mode == SideMode.Demand)
            {
                player.SetRes(Prefix + key + "|" + DemandToken, 1f);
            }
            else if (rule.Mode == SideMode.Blocked)
            {
                player.SetRes(Prefix + key + "|" + BlockToken, 1f);
            }
        }

        private static IEnumerable<ConveyorChestSlotData> AllSlots()
        {
            foreach (ConveyorComponent c in ConveyorSystem.Data?.conveyorComponents ?? new List<ConveyorComponent>())
            {
                List<ConveyorChestSlotData> slots = c is ConveyorChestComponent chest ? chest.SlotsData : c is ConveyorChestOutComponent source ? source.SlotsData : null;
                foreach (ConveyorChestSlotData slot in slots ?? new List<ConveyorChestSlotData>())
                {
                    if (KeyOf(slot) != null)
                    {
                        yield return slot;
                    }
                }
            }
        }

        /// <summary>Every side's setting, by belt: a piece that is moved loses its links, and the sides with them.</summary>
        internal static Dictionary<string, (SideMode Mode, List<string> Items)> Capture()
        {
            var all = new Dictionary<string, (SideMode, List<string>)>();
            foreach (ConveyorChestSlotData slot in AllSlots())
            {
                SideMode mode = ModeOf(slot, out List<string> items);
                if (mode != SideMode.Any)
                {
                    all[KeyOf(slot)] = (mode, items);
                }
            }
            return all;
        }

        /// <summary>Puts back the settings of the sides that are linked to the same belt again. Returns how many.</summary>
        internal static int Reapply(Dictionary<string, (SideMode Mode, List<string> Items)> captured)
        {
            int n = 0;
            foreach (ConveyorChestSlotData slot in AllSlots().ToList())
            {
                string key = KeyOf(slot);
                if (captured.TryGetValue(key, out var was) && (ModeOf(slot, out List<string> now) != was.Mode || !now.SequenceEqual(was.Items)))
                {
                    Store(slot, was.Mode, new List<string>(was.Items));
                    n++;
                }
                captured.Remove(key);  // linked again: from here on the side is the player's to change
            }
            return n;
        }

        /// <summary>The belts of the sides set to give on demand.</summary>
        internal static IEnumerable<string> DemandBelts() => Rules().Where(kv => kv.Value.Mode == SideMode.Demand).Select(kv => kv.Key);

        internal static bool Any => Rules().Count > 0;

        /// <summary>Lab runs: puts a side with no list straight into a mode.</summary>
        internal static void LabSet(ConveyorChestSlotData slot, SideMode mode) => Store(slot, mode, new List<string>());

        /// <summary>Drops what the mod keeps for the side fed by this belt (the game's own slot is not touched).</summary>
        internal static void Forget(SGuid belt)
        {
            if (!SGuid.IsNullOrEmpty(belt))
            {
                Forget(belt.ToString());
            }
        }

        private static void Forget(string key)
        {
            lastGiven.Remove(key);
            if (!Rules().Remove(key))
            {
                return;
            }
            GameRes res = MainGame.PlayerData?.res;
            if (res == null)
            {
                return;
            }
            string start = Prefix + key + "|";
            if (res.resType.Count != res.resValues.Count)
            {
                foreach (string type in res.resType.Where(t => t != null && t.StartsWith(start, StringComparison.Ordinal)).ToList())
                {
                    res.SetWithoutSystemsCheck(type, 0f);
                }
                return;
            }
            for (int i = res.resType.Count - 1; i >= 0; i--)
            {
                if (res.resType[i] != null && res.resType[i].StartsWith(start, StringComparison.Ordinal))
                {
                    res.resType.RemoveAt(i);
                    res.resValues.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// The mod's answer to "what does the chest give this belt". False = not ours (one item or "?"): the game
        /// decides as always.
        /// </summary>
        internal static bool TryPick(ConveyorWgoData chest, List<ConveyorChestSlotData> slots, ConveyorComponent belt, out string itemId)
        {
            itemId = null;
            if (Rules().Count == 0 || chest == null || slots == null || belt?.WgoData == null)
            {
                return false;
            }
            SGuid beltId = belt.WgoData.UniqueId;
            ConveyorChestSlotData slot = slots.Find(x => x.conveyorWgoDataUniqueId == beltId);
            if (slot == null || !rules.ContainsKey(beltId.ToString()))
            {
                return false;
            }
            SideMode mode = ModeOf(slot, out List<string> items);
            if (mode == SideMode.Any || (mode == SideMode.Items && items.Count < 2))
            {
                return false;
            }
            var held = new List<string>();
            foreach (Item item in chest.Inventory.Data.Inventory)
            {
                if (item != null && !string.IsNullOrEmpty(item.id) && item.Count > 0 && !held.Contains(item.id))
                {
                    held.Add(item.id);
                }
            }
            if (mode == SideMode.Demand)
            {
                itemId = Demand.Pick(belt.WgoData as ConveyorWgoData, held);
                return true;
            }
            HashSet<string> claimed = null;
            if (mode == SideMode.Rest)
            {
                claimed = new HashSet<string>();
                foreach (ConveyorChestSlotData other in slots)
                {
                    if (other != null && !ReferenceEquals(other, slot) && ModeOf(other, out List<string> theirs) == SideMode.Items)
                    {
                        claimed.UnionWith(theirs);
                    }
                }
            }
            lastGiven.TryGetValue(beltId.ToString(), out string last);
            itemId = SidePicker.Pick(mode, held, items, claimed, last);
            return true;
        }

        /// <summary>Remembers what a list side gave, so the next item is another one of the list.</summary>
        internal static void Gave(ConveyorComponent belt, List<Item> given)
        {
            if (rules.Count == 0 || given == null || given.Count == 0 || belt?.WgoData == null)
            {
                return;
            }
            string key = belt.WgoData.UniqueId.ToString();
            if (rules.TryGetValue(key, out Rule rule) && rule.Extra.Count > 0)
            {
                lastGiven[key] = given[0].id;
            }
        }

        // ---------------------------------------------------------------- texts

        internal static string TooltipHeader => Texts.Pt("Saída deste lado", "This side's output");

        internal static string Tooltip(SideMode mode, List<string> items)
        {
            string what;
            switch (mode)
            {
                case SideMode.Items:
                    what = Texts.Pt("Só sai: ", "Only gives: ") + string.Join(", ", items.Select(Texts.Name)) + ".";
                    break;
                case SideMode.Demand:
                    what = Texts.Pt("Sob demanda: sai só o que a máquina no fim desta esteira ainda precisa para a receita dela, um craft por vez.",
                        "On demand: gives only what the machine at the end of this belt still lacks for its recipe, one craft at a time.");
                    break;
                case SideMode.Rest:
                    what = Texts.Pt("O resto: sai tudo o que os outros lados não listam.",
                        "The rest: gives whatever the other sides don't list.");
                    break;
                case SideMode.Blocked:
                    what = Texts.Pt("Bloqueado: nada sai por aqui.", "Blocked: nothing leaves this way.");
                    break;
                default:
                    what = Texts.Pt("Qualquer item: sai o primeiro do baú.", "Any item: gives the first one in the chest.");
                    break;
            }
            return what + "\n\n" + Texts.Pt(
                "Clique e escolha um item do baú para pôr na lista deste lado (escolher de novo tira).\n" +
                "Botão direito: limpa a lista; sem lista, alterna Qualquer > Sob demanda > O resto > Bloqueado.",
                "Click and pick an item in the chest to add it to this side's list (pick it again to take it out).\n" +
                "Right click: clears the list; with no list, switches Any > On demand > The rest > Blocked.");
        }
    }

    /// <summary>
    /// A demand side: follows the side's belt down to the machines it reaches (through belts, splitters and
    /// undergrounds; another chest ends the way) and asks what their current recipe still lacks. The game's bench
    /// only takes an ingredient off its input belt while it lacks it (ConveyorWorkbenchComponent.GetItemFromConveyor),
    /// so sending one craft's worth at a time lets a single belt carry every ingredient of a recipe.
    /// </summary>
    internal static class Demand
    {
        private sealed class Route
        {
            internal readonly List<ConveyorWgoData> Belts = new List<ConveyorWgoData>();
            internal readonly List<ConveyorWgoData> Machines = new List<ConveyorWgoData>();
            /// <summary>A splitter on the way (steered by Prefer below when the side gives on demand).</summary>
            internal bool Split;
            /// <summary>The way also ends somewhere that isn't a machine: a chest, a station, a belt going nowhere.</summary>
            internal bool Open;
        }

        private const float Lifetime = 2f;
        private static float builtAt = -100f;
        private static PlayerData builtFor;
        private static readonly Dictionary<string, List<ConveyorWgoData>> children = new Dictionary<string, List<ConveyorWgoData>>();
        private static readonly Dictionary<string, Route> routes = new Dictionary<string, Route>();
        private static readonly Dictionary<string, ConveyorWgoData> pieces = new Dictionary<string, ConveyorWgoData>();
        /// <summary>Splitters down a demand side: the ones that send by need.</summary>
        private static readonly HashSet<string> steered = new HashSet<string>();
        private static readonly HashSet<string> reported = new HashSet<string>();

        /// <summary>The belts change rarely: the map of who feeds whom is rebuilt every couple of seconds.</summary>
        private static void Refresh()
        {
            if (Time.unscaledTime - builtAt < Lifetime && ReferenceEquals(builtFor, MainGame.PlayerData))
            {
                return;
            }
            builtAt = Time.unscaledTime;
            builtFor = MainGame.PlayerData;
            children.Clear();
            routes.Clear();
            pieces.Clear();
            steered.Clear();
            ConveyorSystemData system = MainGame.Instance?.GameSave?.conveyorSystemData;
            if (system?.conveyorComponents == null)
            {
                return;
            }
            foreach (ConveyorComponent component in system.conveyorComponents)
            {
                if (!(component?.WgoData is ConveyorWgoData data) || data.isTempObject)
                {
                    continue;
                }
                pieces[data.UniqueId.ToString()] = data;
                if (component is ConveyorWorkbenchComponent bench)
                {
                    foreach (SGuid input in bench.connectedInWgoDataUniqueId ?? new List<SGuid>())
                    {
                        Link(input, data);
                    }
                    continue;
                }
                foreach (SGuid parent in component.ParentsData.Keys)
                {
                    Link(parent, data);
                }
            }
            foreach (string belt in Sides.DemandBelts())
            {
                if (!pieces.TryGetValue(belt, out ConveyorWgoData start))
                {
                    continue;
                }
                foreach (ConveyorWgoData b in RouteFrom(start).Belts)
                {
                    if (b.ConveyorComponent is ConveyorSplitterComponent)
                    {
                        steered.Add(b.UniqueId.ToString());
                    }
                }
            }
        }

        private static void Link(SGuid parent, ConveyorWgoData child)
        {
            if (SGuid.IsNullOrEmpty(parent))
            {
                return;
            }
            string key = parent.ToString();
            if (!children.TryGetValue(key, out List<ConveyorWgoData> list))
            {
                children[key] = list = new List<ConveyorWgoData>();
            }
            list.Add(child);
        }

        private static Route RouteFrom(ConveyorWgoData belt)
        {
            string start = belt.UniqueId.ToString();
            if (routes.TryGetValue(start, out Route known))
            {
                return known;
            }
            var route = new Route();
            var seen = new HashSet<string>();
            var queue = new Queue<ConveyorWgoData>();
            queue.Enqueue(belt);
            while (queue.Count > 0)
            {
                ConveyorWgoData at = queue.Dequeue();
                string key = at.UniqueId.ToString();
                if (!seen.Add(key))
                {
                    continue;
                }
                ConveyorComponent c = at.ConveyorComponent;
                if (c is ConveyorWorkbenchComponent)
                {
                    route.Machines.Add(at);
                    continue;
                }
                if (!(c is ConveyorCellComponent || c is ConveyorSplitterComponent || c is ConveyorCellUndergroundComponent || c is ConveyorCellStationComponent))
                {
                    route.Open = true;
                    continue;
                }
                route.Belts.Add(at);
                route.Split |= c is ConveyorSplitterComponent;
                if (children.TryGetValue(key, out List<ConveyorWgoData> next))
                {
                    foreach (ConveyorWgoData n in next)
                    {
                        queue.Enqueue(n);
                    }
                }
                else
                {
                    route.Open = true;
                }
            }
            return routes[start] = route;
        }

        private static void Count(Dictionary<string, int> into, Inventory inventory)
        {
            List<Item> items = inventory?.Data?.Inventory;
            if (items == null)
            {
                return;
            }
            foreach (Item item in items)
            {
                if (item != null && !string.IsNullOrEmpty(item.id) && item.Count > 0)
                {
                    into[item.id] = (into.TryGetValue(item.id, out int n) ? n : 0) + item.Count;
                }
            }
        }

        /// <summary>Lab runs: empties the belts between a side and its machines. Returns how many held something.</summary>
        internal static int ClearRoute(ConveyorWgoData belt)
        {
            builtAt = -100f;
            Refresh();
            int cleared = 0;
            foreach (ConveyorWgoData b in RouteFrom(belt).Belts)
            {
                if (b.Inventory?.Data?.Inventory != null && b.Inventory.Data.Inventory.Count > 0)
                {
                    b.Inventory.Data.RemoveAllItems();
                    cleared++;
                }
            }
            return cleared;
        }

        /// <summary>
        /// Puts what sits on the belts between a side and its machines back into the chest (what the side pushed
        /// there before it was set to give on demand). Returns how many items went back; what doesn't fit stays.
        /// </summary>
        internal static int ReturnRoute(ConveyorWgoData belt, Inventory chest)
        {
            builtAt = -100f;
            Refresh();
            int back = 0;
            foreach (ConveyorWgoData b in RouteFrom(belt).Belts)
            {
                foreach (Item item in (b.Inventory?.Data?.Inventory ?? new List<Item>()).ToList())
                {
                    if (item == null || string.IsNullOrEmpty(item.id) || item.Count <= 0)
                    {
                        continue;
                    }
                    if (chest.AddItemToInventory(new Item(item.id, item.Count), out List<Item> added, null, true))
                    {
                        int moved = added.Sum(i => i.Count);
                        if (moved > 0)
                        {
                            b.Inventory.RemoveItemById(item.id, moved, null, null, true);
                            back += moved;
                        }
                    }
                }
            }
            return back;
        }

        /// <summary>The item the chest should put on this belt now, or null.</summary>
        internal static string Pick(ConveyorWgoData belt, List<string> held)
        {
            if (belt == null || held.Count == 0)
            {
                return null;
            }
            Refresh();
            Route route = RouteFrom(belt);
            if (route.Machines.Count == 0)
            {
                return null;
            }
            List<SidePicker.Taker> takers = Takers(route);
            if (takers.Count == 0)
            {
                return null;
            }
            if (route.Split && takers.Count > 1 && !Plugin.SplitterByDemand.Value)
            {
                // A splitter left to the game takes turns whatever the item: only what every machine takes is safe.
                SidePicker.KeepCommonNeeds(takers);
            }
            return SidePicker.PickDemand(held, takers, Coming(route));
        }

        /// <summary>
        /// What is riding the way's belts towards a machine that takes it. An item stranded on a branch where no
        /// machine uses it isn't coming to anyone: counting it would keep the side from ever sending another.
        /// </summary>
        private static Dictionary<string, int> Coming(Route route)
        {
            var coming = new Dictionary<string, int>();
            foreach (ConveyorWgoData b in route.Belts)
            {
                foreach (Item item in b.Inventory?.Data?.Inventory ?? new List<Item>())
                {
                    if (item != null && !string.IsNullOrEmpty(item.id) && item.Count > 0 && Takes(RouteFrom(b), item.id))
                    {
                        coming[item.id] = (coming.TryGetValue(item.id, out int n) ? n : 0) + item.Count;
                    }
                }
            }
            return coming;
        }

        /// <summary>A machine at the end of the way has the item in its current recipe.</summary>
        private static bool Takes(Route route, string item)
        {
            foreach (ConveyorWgoData machine in route.Machines)
            {
                CraftComponent craft = machine.CraftComponent;
                List<NeedItemData> requirements = craft != null && craft.HasCraftsInQueue ? craft.CraftElementsQueue[0]?.Requirements : null;
                if (requirements != null && requirements.Any(n => n != null && n.id == item))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The machines at the end of a way, with what their current recipe takes and what they hold.</summary>
        private static List<SidePicker.Taker> Takers(Route route)
        {
            var takers = new List<SidePicker.Taker>();
            foreach (ConveyorWgoData machine in route.Machines)
            {
                CraftComponent craft = machine.CraftComponent;
                if (craft == null || !craft.HasCraftsInQueue)
                {
                    continue;
                }
                List<NeedItemData> requirements = craft.CraftElementsQueue[0]?.Requirements;
                if (requirements == null)
                {
                    continue;
                }
                var taker = new SidePicker.Taker();
                foreach (NeedItemData need in requirements)
                {
                    if (need != null && !string.IsNullOrEmpty(need.id))
                    {
                        taker.Needs[need.id] = (taker.Needs.TryGetValue(need.id, out int n) ? n : 0) + Math.Max(1, need.GetCount(machine));
                    }
                }
                Count(taker.Has, machine.CraftableObjectCraftInventory);
                takers.Add(taker);
            }
            return takers;
        }

        /// <summary>
        /// The exit (0 or 1) a splitter down a demand side should give its item to, or -1 to leave it to the game
        /// (which takes turns). Called once a conveyor tick, for the item the splitter holds.
        /// </summary>
        internal static int Prefer(ConveyorSplitterComponent splitter)
        {
            if (!Plugin.SplitterByDemand.Value || !Sides.Any || !(splitter?.WgoData is ConveyorWgoData data))
            {
                return -1;
            }
            Refresh();
            string key = data.UniqueId.ToString();
            if (!steered.Contains(key))
            {
                return -1;
            }
            List<ConveyorWgoData> exits = splitter.ConnectedWgoData;
            if (exits == null || exits.Count != 2 || exits[0] == null || exits[1] == null)
            {
                return -1;
            }
            string item = null;
            foreach (Item held in data.Inventory?.Data?.Inventory ?? new List<Item>())
            {
                if (held != null && !string.IsNullOrEmpty(held.id) && held.Count > 0)
                {
                    item = held.id;
                    break;
                }
            }
            if (item == null)
            {
                return -1;
            }
            var ranks = new SidePicker.ExitRank[2];
            for (int i = 0; i < 2; i++)
            {
                Route route = RouteFrom(exits[i]);
                Coming(route).TryGetValue(item, out int coming);
                ranks[i] = SidePicker.RankExit(item, Takers(route), coming, route.Open);
            }
            int pick = SidePicker.PickExit(ranks[0], ranks[1]);
            if (pick >= 0 && reported.Add(key + "|" + item + "|" + pick))
            {
                Plugin.Debug($"Splitter {key.Substring(0, 8)}: {item} goes to exit {pick} ({ranks[pick]} against {ranks[1 - pick]})");
            }
            return pick;
        }
    }

    /// <summary>
    /// The game flips every splitter to its other exit at the end of each conveyor tick (ConveyorSystem.CustomUpdate,
    /// which then zeroes its timer). Right after that, a splitter down a demand side is pointed at the exit that has
    /// a use for the item it holds. Patched on the big caller: SwitchDirection is small enough to be inlined.
    /// </summary>
    [HarmonyPatch(typeof(ConveyorSystem), nameof(ConveyorSystem.CustomUpdate))]
    internal static class ConveyorSystem_CustomUpdate
    {
        private static void Postfix()
        {
            try
            {
                ConveyorSystemData data = ConveyorSystem.Data;
                if (data == null || data.timer != 0f || data.splitterElements == null || !Plugin.SplitterByDemand.Value || !Sides.Any)
                {
                    return;
                }
                foreach (ConveyorSplitterComponent splitter in data.splitterElements)
                {
                    int exit = Demand.Prefer(splitter);
                    if (exit >= 0)
                    {
                        splitter.currentChildTransferIndex = exit;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Splitter by demand failed, the game decides: {e}");
            }
        }
    }
    // ---------------------------------------------------------------- what the chest gives

    [HarmonyPatch]
    internal static class ConveyorChests_TryGetItemIdToGive
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ConveyorChestComponent), "TryGetItemIdToGive");
            yield return AccessTools.Method(typeof(ConveyorChestOutComponent), "TryGetItemIdToGive");
        }

        private static bool Prefix(ConveyorComponent __instance, ConveyorComponent conveyorComponent, ref string itemId, ref bool __result)
        {
            try
            {
                List<ConveyorChestSlotData> slots = __instance is ConveyorChestComponent chest ? chest.SlotsData
                    : __instance is ConveyorChestOutComponent source ? source.SlotsData : null;
                if (!Sides.TryPick(__instance.WgoData, slots, conveyorComponent, out string picked))
                {
                    return true;
                }
                itemId = picked;
                __result = picked != null;
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Chest sides: pick failed, the game decides: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch]
    internal static class ConveyorChests_GiveItem
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ConveyorChestComponent), nameof(ConveyorChestComponent.GiveItem));
            yield return AccessTools.Method(typeof(ConveyorChestOutComponent), nameof(ConveyorChestOutComponent.GiveItem));
        }

        private static void Postfix(ConveyorComponent conveyorComponent, List<Item> __result) => Sides.Gave(conveyorComponent, __result);
    }

    /// <summary>A belt taken off a chest side clears the game's filter for that side; the mod's rule goes with it.</summary>
    [HarmonyPatch]
    internal static class ConveyorChests_Disconnect
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ConveyorChestComponent), nameof(ConveyorChestComponent.Disconnect));
            yield return AccessTools.Method(typeof(ConveyorChestOutComponent), nameof(ConveyorChestOutComponent.Disconnect));
        }

        private static void Prefix(ConveyorWgoData conveyorWgoData) => Sides.Forget(conveyorWgoData?.UniqueId);
    }

    [HarmonyPatch]
    internal static class ConveyorChests_DisconnectChilds
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ConveyorChestComponent), nameof(ConveyorChestComponent.DisconnectChilds));
            yield return AccessTools.Method(typeof(ConveyorChestOutComponent), nameof(ConveyorChestOutComponent.DisconnectChilds));
        }

        private static void Prefix(ConveyorComponent __instance)
        {
            List<ConveyorChestSlotData> slots = __instance is ConveyorChestComponent chest ? chest.SlotsData
                : __instance is ConveyorChestOutComponent source ? source.SlotsData : null;
            foreach (ConveyorChestSlotData slot in slots ?? new List<ConveyorChestSlotData>())
            {
                Sides.Forget(slot?.conveyorWgoDataUniqueId);
            }
        }
    }

    // ---------------------------------------------------------------- the chest window

    /// <summary>Picking an item for a side adds it to the side's list (or takes it out) instead of replacing it.</summary>
    [HarmonyPatch(typeof(UIConveyorChestWindow), "SetItemSlot")]
    internal static class UIConveyorChestWindow_SetItemSlot
    {
        private static bool Prefix(UIConveyorChestWindow __instance, UIItemCell cell, ConveyorChestSlotData slotData)
        {
            try
            {
                string id = cell?.DisplayingItem?.id;
                if (slotData == null || string.IsNullOrEmpty(id) || !(__instance.data?.Chest is ConveyorWgoData chest)
                    || !(chest.ConveyorComponent is ConveyorChestComponent || chest.ConveyorComponent is ConveyorChestOutComponent))
                {
                    return true;
                }
                Sides.Toggle(slotData, id);
                Sides.ModeOf(slotData, out List<string> items);
                Plugin.Debug($"Chest sides: {chest.id} side {slotData.slotPosDirection} now lists {(items.Count == 0 ? "nothing" : string.Join(", ", items))}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Chest sides: list change failed, the game sets the item: {e}");
                return true;
            }
            __instance.SetChestItemSettingMode(false);
            __instance.RedrawChestSlots();
            return false;
        }
    }

    /// <summary>Right click on a side with an item clears it (the game); the rest of the list goes too.</summary>
    [HarmonyPatch(typeof(UIConveyorChestWindow), "RemoveItemFromConveyorSlot")]
    internal static class UIConveyorChestWindow_RemoveItemFromConveyorSlot
    {
        private static void Prefix(UIConveyorChestWindow __instance, UIConveyorChestSlot conveyorChestSlot)
        {
            if (__instance.data?.Chest is ConveyorWgoData chest && chest.ConveyorComponent is ConveyorChestComponent)
            {
                Sides.Forget(conveyorChestSlot?.SlotData?.conveyorWgoDataUniqueId);
            }
        }
    }

    /// <summary>Right click on an output side with no item: Any, On demand, The rest, Blocked.</summary>
    [HarmonyPatch(typeof(UIConveyorChestSlot), "OnCellPressed2")]
    internal static class UIConveyorChestSlot_OnCellPressed2
    {
        private static bool Prefix(UIConveyorChestSlot __instance)
        {
            try
            {
                UIConveyorChestWindow window = __instance.GetComponentInParent<UIConveyorChestWindow>(true);
                ConveyorChestSlotData slot = __instance.slotData;
                if (window == null || window.IsItemSelectionModActive || slot == null || !__instance.isChild || __instance.isParent
                    || !string.IsNullOrEmpty(slot.slotItemId))
                {
                    return true;
                }
                SideMode mode = Sides.Cycle(slot);
                Plugin.Debug($"Chest sides: side {slot.slotPosDirection} is now {mode}");
                window.RedrawChestSlots();
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Chest sides: mode change failed: {e}");
                return true;
            }
        }

        /// <summary>
        /// The game leaves the slot in its "choosing" state after clearing it, and a slot in that state ignores
        /// clicks until something redraws the window.
        /// </summary>
        private static void Postfix(UIConveyorChestSlot __instance)
        {
            try
            {
                UIConveyorChestWindow window = __instance.GetComponentInParent<UIConveyorChestWindow>(true);
                if (window != null && !window.IsItemSelectionModActive && __instance.state == UIConveyorChestSlot.State.OutDuringSelection)
                {
                    window.RedrawChestSlots();
                }
            }
            catch (Exception e)
            {
                Plugin.Debug($"Chest sides: redraw after right click failed: {e.Message}");
            }
        }
    }

    /// <summary>Shows a side's rule on its cell: "+n" for a list, green on demand, gold for the rest, red for blocked, and the tooltip.</summary>
    [HarmonyPatch(typeof(UIConveyorChestSlot), "DrawCurrentState")]
    internal static class UIConveyorChestSlot_DrawCurrentState
    {
        private static readonly Color RestTint = new Color(1f, 0.82f, 0.35f, 1f);
        private static readonly Color DemandTint = new Color(0.5f, 0.9f, 0.5f, 1f);
        private static readonly Color BlockedTint = new Color(1f, 0.35f, 0.3f, 1f);
        private static readonly Dictionary<int, Color> normal = new Dictionary<int, Color>();

        private static void Postfix(UIConveyorChestSlot __instance)
        {
            try
            {
                UIItemCell cell = __instance.itemCell;
                if (cell == null || __instance.GetComponentInParent<UIConveyorChestWindow>(true) == null)
                {
                    return;
                }
                if (cell.Background != null)
                {
                    int id = cell.Background.GetInstanceID();
                    if (!normal.TryGetValue(id, out Color usual))
                    {
                        normal[id] = usual = cell.Background.color;
                    }
                    cell.Background.color = usual;
                }
                ConveyorChestSlotData slot = __instance.slotData;
                bool output = __instance.state == UIConveyorChestSlot.State.EmptyOut || __instance.state == UIConveyorChestSlot.State.FilledOut;
                if (slot == null || !output)
                {
                    return;
                }
                SideMode mode = Sides.ModeOf(slot, out List<string> items);
                if (mode == SideMode.Items && items.Count > 1 && cell.countLabel != null)
                {
                    cell.countLabel.text = "+" + (items.Count - 1);
                    cell.countLabel.gameObject.SetActive(true);
                }
                else if (mode == SideMode.Rest && cell.Background != null)
                {
                    cell.Background.color = RestTint;
                }
                else if (mode == SideMode.Demand && cell.Background != null)
                {
                    cell.Background.color = DemandTint;
                }
                else if (mode == SideMode.Blocked)
                {
                    if (cell.Background != null)
                    {
                        cell.Background.color = BlockedTint;
                    }
                    __instance.questionMark?.SetActive(false);
                    __instance.directionImage?.gameObject.SetActive(false);
                }
                string body = Sides.Tooltip(mode, items);
                cell.CustomTooltipShowAction = shown => UITooltip.ShowSimpleInfo(shown.transform, body, default, Sides.TooltipHeader);
            }
            catch (Exception e)
            {
                Plugin.Debug($"Chest sides: slot drawing failed: {e.Message}");
            }
        }
    }
}
