using System;
using System.Collections.Generic;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Mods.StorageNetwork
{
    /// <summary>
    /// Shows, while a Connector is being placed, where it links stations — the way the game shows a workbench
    /// extension's reach (the Crucible's blue area). The build grid draws blue the cells covered by a layer-28
    /// "buff" collider that belongs to an object (its Wgo), for the objects the pointer allows. So each station
    /// in the zone gets a temporary buff collider as a child, the size of the link reach, and the Connector's
    /// pointer allows those stations. Placement itself follows the template chest's normal rules.
    /// Everything is removed when build mode ends; nothing is saved.
    /// </summary>
    internal static class LinkAreas
    {
        internal const string ConnectorBuildingId = "gk2sn_connector_p";
        private const int BuffLayer = 28;
        private const float Height = 6f;

        private static readonly List<GameObject> areas = new List<GameObject>();
        private static readonly HashSet<string> stationIds = new HashSet<string>();

        internal static bool Active => areas.Count > 0;

        internal static IReadOnlyCollection<string> StationIds => stationIds;

        internal static void Create(WorldZone zone)
        {
            Clear();
            WorldZoneData zoneData = zone?.Data;
            WorldData world = MainGame.Instance?.GameSave?.WorldData;
            if (zoneData == null || world == null || Network.IsExcludedZone(zoneData))
            {
                return;
            }
            Vector2 reach = Network.LinkReach;
            foreach (SGuid guid in zoneData.wgoDataList)
            {
                WgoData wgo = world.GetWgoData(guid);
                Wgo view = Network.IsLinkableStation(wgo) ? GameScene.GetWgoViewGlobal(wgo.UniqueId) : null;
                if (view == null)
                {
                    continue;
                }
                var go = new GameObject("GK2SN_LinkReach") { layer = BuffLayer };
                go.transform.SetParent(view.transform, false);
                go.transform.position = view.transform.position;
                go.transform.rotation = Quaternion.identity;
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(reach.x * 2f, Height, reach.y * 2f);
                areas.Add(go);
                stationIds.Add(wgo.id);
            }
            Physics.SyncTransforms();
            Plugin.Debug($"Link reach: {areas.Count} station(s) in {zoneData.id}");
        }

        internal static void Clear()
        {
            foreach (GameObject go in areas)
            {
                if (go != null)
                {
                    go.SetActive(false);
                    UnityEngine.Object.Destroy(go);
                }
            }
            areas.Clear();
            stationIds.Clear();
        }

        /// <summary>Logs the grid state of a cell inside the first station's reach (dev check).</summary>
        internal static void Diagnose(BuildData buildData, WorldZone zone)
        {
            if (areas.Count == 0 || buildData?.Definition == null || zone == null)
            {
                return;
            }
            Vector3 probe = areas[0].transform.position + new Vector3(Network.LinkReach.x * 0.75f, 0f, 0f);
            BuildCellData cell = BuildCellData.GetData(probe, zone.Id, buildData.Definition);
            Plugin.Log.LogInfo($"Link reach check: {areas.Count} station(s); cell at {probe} state={cell.State} " +
                $"(2 available, 4 busy, 8 blue reach) linked stations={cell.ExtensionList.Count}");
        }
    }

    [HarmonyPatch(typeof(BuildController), nameof(BuildController.EnableBuildMode))]
    internal static class BuildController_EnableBuildMode
    {
        // Prefix: the pointer and the grid are set up inside EnableBuildMode, so the reach must exist first.
        private static void Prefix(BuildData buildData, WorldZone worldZone)
        {
            try
            {
                if (buildData?.Definition?.id == LinkAreas.ConnectorBuildingId)
                {
                    LinkAreas.Create(worldZone);
                }
                else
                {
                    LinkAreas.Clear();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Link reach failed: {e}");
            }
        }

        private static void Postfix(BuildData buildData, WorldZone worldZone)
        {
            if (buildData?.Definition?.id != LinkAreas.ConnectorBuildingId)
            {
                return;
            }
            try
            {
                LinkAreas.Diagnose(buildData, worldZone);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Link reach check failed: {e}");
            }
        }
    }

    /// <summary>For a Connector the pointer would allow no buff areas (it is a plain chest underneath).</summary>
    [HarmonyPatch(typeof(WgoBuildPointer), nameof(WgoBuildPointer.DoBuffDrawingLogic))]
    internal static class WgoBuildPointer_DoBuffDrawingLogic
    {
        private static void Postfix(BuildingDef ___buildingDef)
        {
            if (___buildingDef?.id != LinkAreas.ConnectorBuildingId || !LinkAreas.Active)
            {
                return;
            }
            BuildGrid3D grid = LazySingleton<BuildManager>.Instance?.BuildController?.BuildLayout?.BuildGrid3D;
            grid?.SetAllowedExtensionWgoIds(new HashSet<string>(LinkAreas.StationIds));
        }
    }

    [HarmonyPatch(typeof(BuildController), nameof(BuildController.DisableBuildMode))]
    internal static class BuildController_DisableBuildMode
    {
        private static void Postfix() => LinkAreas.Clear();
    }
}
