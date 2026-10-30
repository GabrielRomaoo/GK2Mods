using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LazyBearTechnology;

namespace GK2Mods.StorageNetwork
{
    // ---------------------------------------------------------------- balance data

    [HarmonyPatch(typeof(GameBalance), nameof(GameBalance.InitCache))]
    internal static class GameBalance_InitCache
    {
        private static void Prefix(GameBalance __instance) => Content.InjectDefs(__instance);
    }

    [HarmonyPatch(typeof(GameBalance), nameof(GameBalance.CreateBuildCache))]
    internal static class GameBalance_CreateBuildCache
    {
        private static void Prefix() => Content.HideFromBuildCache();

        private static Exception Finalizer(GameBalance __instance, Exception __exception)
        {
            Content.RestoreAfterBuildCache(__instance);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(LLBase), nameof(LLBase.LoadLanguageResource))]
    internal static class LLBase_LoadLanguageResource
    {
        private static void Postfix() => Content.AddTranslations(LLBase.currentLang);
    }

    /// <summary>The Archive's limit expression counts Archives that exist in the save.</summary>
    [HarmonyPatch(typeof(LazyExpression), nameof(LazyExpression.EvaluateInt), new Type[0])]
    internal static class LazyExpression_EvaluateInt
    {
        private static void Postfix(LazyExpression __instance, ref int __result)
        {
            if (Content.ArchiveLimitExpression != null && ReferenceEquals(__instance, Content.ArchiveLimitExpression))
            {
                __result = Network.ArchiveCount;
            }
        }
    }

    // ---------------------------------------------------------------- tagging built structures

    /// <summary>Tag handed to the next WgoData added to a scene (set around a build or a replace).</summary>
    internal static class PendingTag
    {
        internal static string Value;
    }

    [HarmonyPatch(typeof(WgoBuildPointer), nameof(WgoBuildPointer.TryDoBuildAction))]
    internal static class WgoBuildPointer_TryDoBuildAction
    {
        private static bool Prefix(BuildingDef ___buildingDef, ref bool __result)
        {
            string tag = Content.TagForBuilding(___buildingDef?.id);
            if (tag == null)
            {
                return true;
            }
            if (tag == Content.ArchiveTag && Network.ArchiveCount >= 1)
            {
                __result = false;
                return false;
            }
            PendingTag.Value = tag;
            return true;
        }

        private static Exception Finalizer(Exception __exception)
        {
            PendingTag.Value = null;
            Network.Invalidate();
            return __exception;
        }
    }

    /// <summary>A finished construction site is replaced by the final chest: carry the tag over.</summary>
    [HarmonyPatch(typeof(WorldData), nameof(WorldData.ReplaceWgoData), typeof(WgoData), typeof(string))]
    internal static class WorldData_ReplaceWgoData
    {
        private static void Prefix(WgoData wgoData)
        {
            if (Content.IsOurTag(wgoData?.CustomTag))
            {
                PendingTag.Value = wgoData.CustomTag;
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            PendingTag.Value = null;
            Network.Invalidate();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(GameSceneData), nameof(GameSceneData.AddWgoData), typeof(WgoData), typeof(bool))]
    internal static class GameSceneData_AddWgoData
    {
        private static void Prefix(WgoData wgoData)
        {
            if (PendingTag.Value != null && wgoData != null && string.IsNullOrEmpty(wgoData.CustomTag))
            {
                wgoData.CustomTag = PendingTag.Value;
                PendingTag.Value = null;
                Plugin.Debug($"Tagged {wgoData.id} as {wgoData.CustomTag}");
            }
        }
    }

    [HarmonyPatch(typeof(WorldData), nameof(WorldData.RemoveWgoDataFromGameScene), typeof(WgoData), typeof(bool))]
    internal static class WorldData_RemoveWgoDataFromGameScene
    {
        private static void Postfix() => Network.Invalidate();
    }

    // ---------------------------------------------------------------- material sources

    /// <summary>
    /// Inventories appended from the network. Remove/check operations see them; add operations don't,
    /// so crafting output never spills into a chest in another zone.
    /// </summary>
    internal static class RemoteInventories
    {
        private static readonly ConditionalWeakTable<MultiInventory, HashSet<Inventory>> remote =
            new ConditionalWeakTable<MultiInventory, HashSet<Inventory>>();

        private static readonly HashSet<MultiInventory> swapped = new HashSet<MultiInventory>();

        internal static void Append(MultiInventory multi, List<Inventory> pool)
        {
            if (multi == null || pool.Count == 0)
            {
                return;
            }
            var existing = new HashSet<Inventory>(multi.inventoryList);
            var added = new HashSet<Inventory>();
            foreach (Inventory inventory in pool)
            {
                if (existing.Add(inventory))
                {
                    added.Add(inventory);
                    multi.inventoryList.Add(inventory);
                }
            }
            if (added.Count > 0)
            {
                remote.Remove(multi);
                remote.Add(multi, added);
            }
        }

        internal static List<Inventory> HideRemote(MultiInventory multi)
        {
            if (multi == null || swapped.Contains(multi) || !remote.TryGetValue(multi, out HashSet<Inventory> added))
            {
                return null;
            }
            List<Inventory> original = multi.inventoryList;
            multi.inventoryList = original.Where(i => !added.Contains(i)).ToList();
            swapped.Add(multi);
            return original;
        }

        internal static void Restore(MultiInventory multi, List<Inventory> original)
        {
            if (original == null)
            {
                return;
            }
            multi.inventoryList = original;
            swapped.Remove(multi);
        }
    }

    [HarmonyPatch(typeof(WgoData), nameof(WgoData.GetCraftableMultiInventory))]
    internal static class WgoData_GetCraftableMultiInventory
    {
        private static void Postfix(WgoData __instance, MultiInventory __result)
        {
            // Zombies (porters, suppliers) keep the game's own logistics.
            if (__instance is ZombieWgoData || !Network.IsStationConnected(__instance))
            {
                return;
            }
            RemoteInventories.Append(__result, Network.Pool);
        }
    }

    [HarmonyPatch]
    internal static class MultiInventory_AddMethods
    {
        private static readonly string[] Names =
        {
            nameof(MultiInventory.CanAddItems), nameof(MultiInventory.CanAddItem),
            nameof(MultiInventory.TryAddItems), nameof(MultiInventory.TryAddItem),
            nameof(MultiInventory.TryAddItemsPerOne), nameof(MultiInventory.AddItems),
            nameof(MultiInventory.AddItem),
        };

        private static IEnumerable<MethodBase> TargetMethods() =>
            typeof(MultiInventory).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => Names.Contains(m.Name));

        private static void Prefix(MultiInventory __instance, out List<Inventory> __state) =>
            __state = RemoteInventories.HideRemote(__instance);

        private static Exception Finalizer(MultiInventory __instance, List<Inventory> __state, Exception __exception)
        {
            RemoteInventories.Restore(__instance, __state);
            return __exception;
        }
    }

    /// <summary>The Keeper's own construction in a connected zone can be paid from the network.</summary>
    [HarmonyPatch(typeof(BuildManager), nameof(BuildManager.RefreshAdditionalInventories))]
    internal static class BuildManager_RefreshAdditionalInventories
    {
        private static void Postfix(BuildManager __instance)
        {
            if (!Plugin.KeeperBuildsFromNetwork.Value)
            {
                return;
            }
            WorldZoneData zone = __instance.worldZone?.Data;
            if (!Network.IsZoneConnected(zone))
            {
                return;
            }
            // The player's MultiInventory already holds the player's zone; skip those chests.
            var skip = new HashSet<Inventory>(new MultiInventory(MainGame.PlayerData).inventoryList);
            List<Inventory> list = __instance.additionalInventories ?? new List<Inventory>();
            skip.UnionWith(list);
            foreach (Inventory inventory in Network.Pool)
            {
                if (skip.Add(inventory))
                {
                    list.Add(inventory);
                }
            }
            __instance.additionalInventories = list;
        }
    }

    // ---------------------------------------------------------------- names

    [HarmonyPatch(typeof(InventoryHeaderWidgetData), MethodType.Constructor,
        typeof(Inventory), typeof(string), typeof(string), typeof(bool), typeof(Action))]
    internal static class InventoryHeaderWidgetData_Ctor
    {
        private static void Postfix(InventoryHeaderWidgetData __instance, Inventory inventory)
        {
            string tag = Network.TagFor(inventory);
            if (tag != null && string.IsNullOrEmpty(__instance.CustomHeaderId))
            {
                __instance.CustomHeaderId = tag;
            }
        }
    }
}
