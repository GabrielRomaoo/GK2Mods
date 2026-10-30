using System;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;

namespace GK2Mods.StorageNetwork
{
    /// <summary>
    /// Zone Relay and Connector are fixtures, not chests: interacting shows what they do instead of a chest
    /// window, and their (template chest's) inventory is left out of the zone's storage, so neither zombies,
    /// stations nor the network put anything in it. One that still holds items opens normally, to empty it.
    /// </summary>
    internal static class Fixtures
    {
        internal const string RelayInfoKey = "gk2sn_relay_info";
        internal const string ConnectorInfoKey = "gk2sn_connector_info";

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
            if (data == null || !Fixtures.IsFixtureTag(data.CustomTag) || !Fixtures.IsEmpty(data.Inventory))
            {
                return true;
            }
            try
            {
                string key = data.CustomTag == Content.RelayTag ? Fixtures.RelayInfoKey : Fixtures.ConnectorInfoKey;
                LazySingleton<UINotificator>.Instance.ShowSimpleTextWithIconNotification(key, Content.IconForTag(data.CustomTag));
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
