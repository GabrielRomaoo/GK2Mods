using System;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;

namespace GK2Mods.StorageNetwork
{
    /// <summary>
    /// Zone Relay and Connector are fixtures, not chests: their (template chest's) inventory is left out of the
    /// zone's storage, so neither zombies, stations nor the network put anything in it or take from it.
    /// Interacting with a Relay shows what it does (one that still holds items opens, to empty it); a Connector
    /// opens as the courier's drop box.
    /// </summary>
    internal static class Fixtures
    {
        internal const string RelayInfoKey = "gk2sn_relay_info";

        internal static bool IsFixtureTag(string tag) => tag == Content.RelayTag || tag == Content.ConnectorTag;

        internal static bool IsFixtureInventory(Inventory inventory) => IsFixtureTag(Network.TagFor(inventory));

        internal static bool IsEmpty(Inventory inventory) =>
            inventory?.Data?.Inventory == null || inventory.Data.Inventory.All(i => i == null || i.IsEmpty);
    }

    [HarmonyPatch(typeof(ChestInteractionHandler), nameof(ChestInteractionHandler.Interact))]
    internal static class ChestInteractionHandler_Interact
    {
        private static bool Prefix(ChestInteractionHandler __instance, ref bool __result)
        {
            WgoData data = __instance.assignedWgo?.Data;
            // The Connector opens as its drop box (see Courier); only an empty Relay explains itself instead.
            if (data == null || data.CustomTag != Content.RelayTag || !Fixtures.IsEmpty(data.Inventory))
            {
                return true;
            }
            try
            {
                LazySingleton<UINotificator>.Instance.ShowSimpleTextWithIconNotification(Fixtures.RelayInfoKey, Content.IconForTag(data.CustomTag));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Fixture notice not shown: {e.Message}");
            }
            __result = true;
            return false;
        }
    }

    /// <summary>The zone's chest list (porters, gardeners, stations, chest windows) skips the fixtures.</summary>
    [HarmonyPatch(typeof(MultiInventory), MethodType.Constructor, typeof(WorldZoneData), typeof(WgoData), typeof(bool))]
    internal static class MultiInventory_ZoneCtor
    {
        private static void Postfix(MultiInventory __instance)
        {
            if (__instance.inventoryList.Count > 0)
            {
                __instance.inventoryList.RemoveAll(Fixtures.IsFixtureInventory);
            }
        }
    }
}
