using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;

namespace GK2Mods.StorageNetwork
{
    /// <summary>
    /// The Central Storage opens the game's chest window with the whole network's stock on the right: one
    /// section per category, each item summed over every connected chest. The sections are display-only
    /// ("virtual") inventories: taking from them takes from the real chests, and anything put in goes to the
    /// Central Storage itself (then the network's other chests if it is full).
    /// </summary>
    internal static class CentralPanel
    {
        internal const string HeaderKey = "gk2sn_panel_header";

        private static readonly Dictionary<Inventory, Categories.Category> virtuals = new Dictionary<Inventory, Categories.Category>();
        private static readonly Dictionary<string, Inventory> byCategory = new Dictionary<string, Inventory>();
        private static Inventory central;

        internal static bool IsVirtual(Inventory inventory) => inventory != null && virtuals.ContainsKey(inventory);

        internal static bool TryOpen(PlayerController interactor, WgoData centralWgo)
        {
            List<Inventory> pool = Network.Pool;
            if (pool.Count == 0)
            {
                return false;  // network not running yet: behave as a plain chest
            }
            virtuals.Clear();
            byCategory.Clear();
            central = centralWgo.Inventory;

            // Sum every item over the network (items inside bags included).
            var totals = new Dictionary<string, int>();
            foreach (Inventory inventory in pool)
            {
                foreach (Item item in inventory.Data.Inventory)
                {
                    if (item == null || item.IsEmpty)
                    {
                        continue;
                    }
                    if (item.IsBag)
                    {
                        foreach (Item inner in item.Inventory.Where(i => i != null && !i.IsEmpty && !i.IsBag))
                        {
                            totals[inner.id] = totals.TryGetValue(inner.id, out int a) ? a + inner.Count : inner.Count;
                        }
                        continue;
                    }
                    totals[item.id] = totals.TryGetValue(item.id, out int n) ? n + item.Count : item.Count;
                }
            }

            // One virtual inventory per category, items in the game's own order.
            var grouped = totals
                .Select(t => (id: t.Key, count: t.Value, def: GameBalance.Me.GetDataOrNull<ItemDef>(t.Key)))
                .GroupBy(t => Categories.Classify(t.id, t.def))
                .ToDictionary(g => g.Key, g => g.OrderBy(t => t.def?.sortOrder ?? int.MaxValue).ToList());

            var data = new UIBaseChestWindowData(interactor.PlayerData.inventory, null, centralWgo);
            var handler = new InventoryUIItemMoveOpHandler(
                () => data.FirstMultiInventoryData.SelectedWidgetData.Inventory,
                () => data.SecondMultiInventoryData.SelectedWidgetData.Inventory);
            data.SecondMultiInventoryData.inventoriesData.Clear();
            data.SecondMultiInventoryData.HeaderLocaleId = HeaderKey;
            foreach (Categories.Category category in Categories.All)
            {
                if (!grouped.TryGetValue(category, out var items))
                {
                    continue;
                }
                int stacks = items.Sum(t => (t.count + Math.Max(1, t.def?.stackCount ?? 1) - 1) / Math.Max(1, t.def?.stackCount ?? 1));
                var inventory = new Inventory(category.Key, Math.Max(stacks, 1));
                foreach (var (id, count, _) in items)
                {
                    inventory.AddItemToInventory(new Item(id, count), out _, null, true);
                }
                virtuals[inventory] = category;
                byCategory[category.Key] = inventory;
                data.SecondMultiInventoryData.inventoriesData.AddRange(InventoryWidgetDataHelper.GetWidgetsDataForInventory(
                    inventory, _ => LazyAudio.PlayAndForget("gui_hover_light"), null,
                    handler.OnInventory2ItemPress1, handler.OnInventory2ItemPress2, null, null, addBags: false));
            }
            if (data.SecondMultiInventoryData.inventoriesData.Count == 0)
            {
                return false;
            }

            // "Move all similar": everything the player carries that the network already stocks.
            data.FirstMultiInventoryData.OnMoveAllSimilarPressed = () => DepositAllSimilar(interactor.PlayerData.inventory);
            data.FirstMultiInventoryData.GetMoveAllSimilarTargetInventory = () => central;

            LazyUI.GetWindow<UIChestWindow>().Open(data);
            Plugin.Debug($"Central panel: {totals.Count} item kinds in {virtuals.Count} categories from {pool.Count} chests");
            return true;
        }

        /// <summary>Takes from the network's real chests into <paramref name="to"/>.</summary>
        internal static bool Withdraw(string itemId, int count, Inventory from, Inventory to)
        {
            if (to == null || IsVirtual(to))
            {
                return false;
            }
            int available = from.Data.GetTotalCountInInventory(itemId);
            int wanted = Math.Min(count, available);
            if (wanted <= 0 || !to.AddItemToInventory(new Item(itemId, wanted), out List<Item> added, null, true))
            {
                return false;
            }
            int moved = added.Sum(i => i.Count);
            if (moved <= 0)
            {
                return false;
            }
            int removed = new MultiInventory(Network.Pool).RemoveItemByCount(itemId, moved).Sum(i => i.Count);
            if (removed < moved)
            {
                // Someone (a zombie, a station) used some in the meantime: give back what the network didn't have.
                to.RemoveItemById(itemId, moved - removed);
                moved = removed;
            }
            from.RemoveItemById(itemId, moved);
            return moved > 0;
        }

        /// <summary>Puts items from the player into the Central Storage (then other network chests).</summary>
        internal static bool Deposit(UIItemCell cell, int count, Inventory from)
        {
            Item shown = cell.DisplayingItem;
            if (shown == null || shown.IsBag || central == null)
            {
                return false;
            }
            int moved = AddToNetwork(shown.id, count);
            if (moved <= 0)
            {
                return false;
            }
            from.RemoveItemById(shown.id, moved, null, from.TryFindSourceBagForItem(shown), true);
            ShowInPanel(shown.id, moved);
            return true;
        }

        private static void DepositAllSimilar(Inventory player)
        {
            var stocked = new HashSet<string>(virtuals.Keys.SelectMany(v => v.Data.Inventory.Where(i => !i.IsEmpty).Select(i => i.id)));
            foreach (Item item in player.Data.Inventory.ToList())
            {
                if (item == null || item.IsEmpty || item.IsBag || !stocked.Contains(item.id))
                {
                    continue;
                }
                int moved = AddToNetwork(item.id, item.Count);
                if (moved > 0)
                {
                    player.RemoveItemById(item.id, moved, null, null, true);
                    ShowInPanel(item.id, moved);
                }
            }
        }

        private static int AddToNetwork(string itemId, int count)
        {
            int moved = 0;
            foreach (Inventory target in new[] { central }.Concat(Network.Pool.Where(i => i != central)))
            {
                if (moved >= count)
                {
                    break;
                }
                if (target.AddItemToInventory(new Item(itemId, count - moved), out List<Item> added, null, true))
                {
                    moved += added.Sum(i => i.Count);
                }
            }
            return moved;
        }

        private static void ShowInPanel(string itemId, int count)
        {
            ItemDef def = GameBalance.Me.GetDataOrNull<ItemDef>(itemId);
            Categories.Category category = Categories.Classify(itemId, def);
            if (byCategory.TryGetValue(category.Key, out Inventory inventory))
            {
                int stack = Math.Max(1, def?.stackCount ?? 1);
                inventory.Data.InventorySize += (count + stack - 1) / stack;
                inventory.AddItemToInventory(new Item(itemId, count), out _, null, true);
            }
        }
    }

    /// <summary>Every item move in an inventory window goes through TryMoveItem (click, shift, ctrl, count window).</summary>
    [HarmonyPatch(typeof(InventoryUIItemMoveOpHandler), nameof(InventoryUIItemMoveOpHandler.TryMoveItem))]
    internal static class InventoryUIItemMoveOpHandler_TryMoveItem
    {
        private static bool Prefix(UIItemCell itemCell, int count, Inventory from, Inventory to, ref bool __result)
        {
            try
            {
                if (CentralPanel.IsVirtual(from))
                {
                    __result = itemCell?.DisplayingItem != null && CentralPanel.Withdraw(itemCell.DisplayingItem.id, count, from, to);
                    return false;
                }
                if (CentralPanel.IsVirtual(to))
                {
                    __result = CentralPanel.Deposit(itemCell, count, from);
                    return false;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Central panel move failed: {e}");
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ChestInteractionHandler), nameof(ChestInteractionHandler.Interact))]
    internal static class ChestInteractionHandler_Interact_Central
    {
        private static bool Prefix(ChestInteractionHandler __instance, PlayerController interactor, ref bool __result)
        {
            WgoData data = __instance.assignedWgo?.Data;
            if (data?.CustomTag != Content.ArchiveTag || data.id.EndsWith("_place", StringComparison.Ordinal))
            {
                return true;
            }
            try
            {
                if (CentralPanel.TryOpen(interactor, data))
                {
                    __result = true;
                    return false;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Central panel failed, opening as a chest: {e}");
            }
            return true;
        }
    }
}
