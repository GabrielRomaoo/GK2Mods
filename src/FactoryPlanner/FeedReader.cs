using System;
using System.Collections.Generic;
using System.Linq;
using GK2Mods.FactoryPlanner.Core;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// Reads the factory's belt network into a Core.FeedGraph: every conveyor piece with the pieces that hand it
    /// items (ConveyorComponent.ParentsData; for a machine, its input belts), what chests and reserves hold, and the
    /// items each chest side lets out (ConveyorChestSlotData.slotItemId plus the mod's side rules, see Sides). A splitter on a machine's input is
    /// left out: the game lists it but never hands the item over (ConveyorSplitterComponent.Connect refuses benches).
    /// </summary>
    internal static class FeedReader
    {
        internal static FeedGraph Read(RecipeBook book)
        {
            var graph = new FeedGraph();
            ConveyorSystemData system = MainGame.Instance?.GameSave?.conveyorSystemData;
            if (system?.conveyorComponents == null)
            {
                return graph;
            }
            var ingredients = new HashSet<string>(book.ById.Values.SelectMany(r => r.Needs.Select(n => n.Item)));
            var pending = new Queue<ConveyorWgoData>();
            var seen = new HashSet<string>();
            foreach (ConveyorComponent component in system.conveyorComponents)
            {
                if (component?.WgoData is ConveyorWgoData data && !data.isTempObject)
                {
                    pending.Enqueue(data);
                }
            }
            int splitters = 0;
            var outputs = new List<(string machine, string belt)>();
            while (pending.Count > 0)
            {
                ConveyorWgoData data = pending.Dequeue();
                string key = data.UniqueId.ToString();
                if (!seen.Add(key) || data.ConveyorComponent == null)
                {
                    continue;
                }
                ConveyorComponent c = data.ConveyorComponent;
                FeedNode node = graph.Add(key, KindOf(c));
                if (c is ConveyorWorkbenchComponent bench)
                {
                    foreach (SGuid input in bench.connectedInWgoDataUniqueId ?? new List<SGuid>())
                    {
                        if (MainGame.WorldData.GetWgoData(input) is ConveyorWgoData belt)
                        {
                            if (belt.ConveyorComponent is ConveyorSplitterComponent)
                            {
                                splitters++;
                                continue;
                            }
                            node.Parents.Add(input.ToString());
                            pending.Enqueue(belt);
                        }
                    }
                    foreach (SGuid output in bench.connectedOutWgoDataUniqueId ?? new List<SGuid>())
                    {
                        if (MainGame.WorldData.GetWgoData(output) is ConveyorWgoData belt)
                        {
                            pending.Enqueue(belt);
                            outputs.Add((key, output.ToString()));
                        }
                    }
                    continue;
                }
                foreach (var kv in c.ParentsData)
                {
                    if (kv.Value == null)
                    {
                        continue;
                    }
                    node.Parents.Add(kv.Key.ToString());
                    pending.Enqueue(kv.Value);
                }
                if (node.Kind == FeedKind.Chest || node.Kind == FeedKind.Source)
                {
                    foreach (Item item in data.Inventory?.Data?.Inventory ?? new List<Item>())
                    {
                        if (item != null && item.Count > 0 && !string.IsNullOrEmpty(item.id))
                        {
                            node.Holds.Add(item.id);
                        }
                    }
                    if (data.id != null && data.id.IndexOf("_container_conveyor", StringComparison.Ordinal) >= 0)
                    {
                        foreach (string i in ingredients.Where(i => data.id.StartsWith(i + "_", StringComparison.Ordinal)))
                        {
                            node.Accepts.Add(i);
                        }
                    }
                    IEnumerable<ConveyorChestSlotData> slots = c is ConveyorChestComponent chest ? chest.SlotsData
                        : c is ConveyorChestOutComponent outChest ? outChest.SlotsData : null;
                    foreach (ConveyorChestSlotData slot in slots ?? Enumerable.Empty<ConveyorChestSlotData>())
                    {
                        if (slot == null || SGuid.IsNullOrEmpty(slot.conveyorWgoDataUniqueId))
                        {
                            continue;
                        }
                        string child = slot.conveyorWgoDataUniqueId.ToString();
                        switch (Sides.ModeOf(slot, out List<string> listed))
                        {
                            case SideMode.Items:
                                node.FilterFor[child] = new HashSet<string>(listed);
                                break;
                            case SideMode.Blocked:
                                node.FilterFor[child] = new HashSet<string>();
                                break;
                            case SideMode.Rest:
                                node.RestFor.Add(child);
                                break;
                            case SideMode.Demand:
                                node.DemandFor.Add(child);
                                break;
                        }
                    }
                }
            }
            // A machine's exit belts get the machine as a parent (the game may already list it).
            foreach (var (machine, belt) in outputs)
            {
                graph.Link(machine, belt);
            }
            try
            {
                Supply.Offer(graph, ingredients, book);
            }
            catch (Exception e)
            {
                Plugin.Debug($"Feed: supplies not counted: {e.Message}");
            }
            Plugin.Debug($"Feed: {graph.Nodes.Count} pieces, {graph.Nodes.Values.Count(n => n.Kind == FeedKind.Machine)} machines, " +
                $"{graph.Nodes.Values.Count(n => n.Kind == FeedKind.Source)} reserves, {splitters} splitter(s) on a machine input ignored");
            return graph;
        }

        private static FeedKind KindOf(ConveyorComponent c)
        {
            switch (c)
            {
                case ConveyorWorkbenchComponent _:
                    return FeedKind.Machine;
                case ConveyorChestComponent _:
                    return FeedKind.Chest;
                case ConveyorChestOutComponent _:
                    return FeedKind.Source;
                case ConveyorCellComponent _:
                case ConveyorSplitterComponent _:
                case ConveyorCellUndergroundComponent _:
                case ConveyorCellStationComponent _:
                    return FeedKind.Belt;
                default:
                    return FeedKind.Other;
            }
        }

        /// <summary>One line for the log: what reaches a machine.</summary>
        internal static string Describe(FeedGraph graph, string key, Func<string, string> productOf)
        {
            FeedGraph.Arrival a = graph.Arriving(key, productOf);
            return a.Items.Count == 0 ? "nothing" : string.Join(", ", a.Items.OrderBy(i => i, StringComparer.Ordinal));
        }
    }
}
