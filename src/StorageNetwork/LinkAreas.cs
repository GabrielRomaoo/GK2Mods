using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GK2Mods.StorageNetwork
{
    /// <summary>
    /// While a Connector is being placed, every station in the zone gets a temporary BuildArea around it, the
    /// way a furnace carries the Crucible's area: build cells inside it are available and drawn blue (layer 28
    /// marks "buff" cells), cells elsewhere are not. The areas are removed when build mode ends.
    /// </summary>
    internal static class LinkAreas
    {
        internal const string AreaId = "gk2sn_link";
        private const int BuffAreaLayer = 28;
        private const float Height = 6f;

        private static readonly List<GameObject> areas = new List<GameObject>();

        internal static void Create(WorldZone zone)
        {
            Clear();
            WorldZoneData zoneData = zone?.Data;
            WorldData world = MainGame.Instance?.GameSave?.WorldData;
            if (zoneData == null || world == null)
            {
                return;
            }
            Vector2 reach = Network.LinkReach;
            foreach (SGuid guid in zoneData.wgoDataList)
            {
                WgoData wgo = world.GetWgoData(guid);
                if (!Network.IsLinkableStation(wgo))
                {
                    continue;
                }
                var go = new GameObject("GK2SN_LinkArea " + wgo.id) { layer = BuffAreaLayer };
                go.transform.position = wgo.Position;
                var box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(reach.x * 2f, Height, reach.y * 2f);
                var area = go.AddComponent<BuildArea>();
                area.collider = box;
                area.id = AreaId;
                areas.Add(go);
            }
            Physics.SyncTransforms();
            Plugin.Debug($"Link areas: {areas.Count} station(s) in {zoneData.id}");
        }

        internal static void Clear()
        {
            foreach (GameObject go in areas)
            {
                if (go != null)
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
            areas.Clear();
        }
    }

    [HarmonyPatch(typeof(BuildController), nameof(BuildController.EnableBuildMode))]
    internal static class BuildController_EnableBuildMode
    {
        // Prefix: the grid is formed inside EnableBuildMode, so the areas must exist before it runs.
        private static void Prefix(BuildData buildData, WorldZone worldZone)
        {
            try
            {
                if (buildData?.Definition?.customBuildAreaId == LinkAreas.AreaId)
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
                Plugin.Log.LogError($"Link areas failed: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(BuildController), nameof(BuildController.DisableBuildMode))]
    internal static class BuildController_DisableBuildMode
    {
        private static void Postfix() => LinkAreas.Clear();
    }
}
