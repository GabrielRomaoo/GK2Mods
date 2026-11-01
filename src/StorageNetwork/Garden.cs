using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;

namespace GK2Mods.StorageNetwork
{
    /// <summary>
    /// Garden beds and vineyard rows near a Connector plant and fertilize from the network. Planting and fertilizing
    /// already take their items through the bed's GetCraftableMultiInventory (patched for every linked station);
    /// these patches make the bed window see the network too: the seed count, and the seed/fertilizer pickers.
    /// </summary>
    internal static class Garden
    {
        /// <summary>The bed whose window is opening a seed/fertilizer picker right now (set only during that call).</summary>
        internal static WgoData PickingFor;

        internal static bool IsLinkedBed(WgoData bed) => bed != null && Network.IsStationConnected(bed);

        /// <summary>Network chests not already in the player's own view (inventory + current zone).</summary>
        internal static List<Inventory> RemoteChests()
        {
            var local = new HashSet<Inventory>(new MultiInventory(MainGame.PlayerData).inventoryList);
            return Network.Pool.Where(i => !local.Contains(i)).ToList();
        }

        internal static bool HoldsMatching(Inventory inventory, Func<Item, bool> condition)
        {
            foreach (Item item in inventory.Data.Inventory)
            {
                if (item == null || item.IsEmpty)
                {
                    continue;
                }
                if (condition == null || condition(item))
                {
                    return true;
                }
                if (item.IsBag && item.Inventory.Any(inner => inner != null && !inner.IsEmpty && condition(inner)))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>The seed cell and the Plant button count seeds in the network as well.</summary>
    [HarmonyPatch(typeof(UIGardenBedWindow), nameof(UIGardenBedWindow.GetAvailableSeedCount))]
    internal static class UIGardenBedWindow_GetAvailableSeedCount
    {
        private static void Postfix(UIGardenBedWindow __instance, string itemId, ref int __result)
        {
            try
            {
                if (Garden.IsLinkedBed(__instance.data?.WgoData))
                {
                    __result += new MultiInventory(Garden.RemoteChests()).GetTotalCount(itemId);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Garden seed count failed: {e}");
            }
        }
    }

    /// <summary>Marks the bed while its window opens the seed picker or the fertilizer picker.</summary>
    [HarmonyPatch]
    internal static class UIGardenBedWindow_Pickers
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UIGardenBedWindow), nameof(UIGardenBedWindow.OnSeedItemCellPress));
            yield return AccessTools.Method(typeof(UIGardenBedWindow), nameof(UIGardenBedWindow.TryApplyFertilizer));
        }

        private static void Prefix(UIGardenBedWindow __instance)
        {
            Garden.PickingFor = __instance.data?.WgoData;
        }

        private static Exception Finalizer(Exception __exception)
        {
            Garden.PickingFor = null;
            return __exception;
        }
    }

    /// <summary>
    /// While a linked bed opens a picker, the picker also lists the network chests that hold something it accepts
    /// (empty or unrelated chests are left out, so the list stays short).
    /// </summary>
    [HarmonyPatch(typeof(UIMultiInventoryWindowData), MethodType.Constructor,
        typeof(PlayerData), typeof(Action<UIItemCell>), typeof(Func<Item, bool>), typeof(bool), typeof(string), typeof(Func<Item, string>))]
    internal static class UIMultiInventoryWindowData_Ctor
    {
        private static void Postfix(UIMultiInventoryWindowData __instance, Action<UIItemCell> onCellClicked,
            Func<Item, bool> itemsAvailableCondition, Func<Item, string> extraRedTooltipLocIdProvider)
        {
            try
            {
                if (!Garden.IsLinkedBed(Garden.PickingFor))
                {
                    return;
                }
                List<Inventory> remote = Garden.RemoteChests().Where(i => Garden.HoldsMatching(i, itemsAvailableCondition)).ToList();
                if (remote.Count == 0)
                {
                    return;
                }
                List<InventoryWidgetDataBase> widgets = InventoryWidgetDataHelper.GetWidgetsDataForMultiInventory(new MultiInventory(remote),
                    _ => LazyAudio.PlayAndForget("gui_hover_light"), null,
                    cell =>
                    {
                        onCellClicked?.Invoke(cell);
                        LazyAudio.PlayAndForget("gui_click");
                    }, null, null, itemsAvailableCondition);
                if (extraRedTooltipLocIdProvider != null)
                {
                    foreach (InventoryWidgetDataBase widget in widgets)
                    {
                        widget.ExtraRedTooltipLocIdProvider = extraRedTooltipLocIdProvider;
                    }
                }
                __instance.MultiInventoryWidgetData.AddRange(widgets);
                foreach (Inventory inventory in remote)
                {
                    __instance.MultiInventory.inventoryList.Add(inventory);
                }
                Plugin.Debug($"Garden picker: {remote.Count} network chest(s) added");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Garden picker failed: {e}");
            }
        }
    }
}
