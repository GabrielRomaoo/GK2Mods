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
                // Same scene (and physics world) as the station the grid is drawn around.
                Wgo view = GameScene.GetWgoViewGlobal(wgo.UniqueId);
                if (view != null)
                {
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, view.gameObject.scene);
                }
                go.transform.position = view != null ? view.transform.position : wgo.Position;
                var box = go.AddComponent<BoxCollider>();
                // Not a trigger: the build grid's overlap queries skip triggers, like the game's own BuildAreas.
                box.isTrigger = false;
                box.size = new Vector3(reach.x * 2f, Height, reach.y * 2f);
                var area = go.AddComponent<BuildArea>();
                area.collider = box;
                area.id = AreaId;
                areas.Add(go);
            }
            Physics.SyncTransforms();
            Plugin.Debug($"Link areas: {areas.Count} station(s) in {zoneData.id}");
            SelfCheck();
        }

        /// <summary>Runs the build grid's own overlap query next to the first station and logs what it sees.</summary>
        private static void SelfCheck()
        {
            if (areas.Count == 0)
            {
                return;
            }
            GameObject first = areas[0];
            Vector3 probe = first.transform.position + new Vector3(Network.LinkReach.x * 0.75f, 0f, 0f);
            var hits = new Collider[64];
            int n = Physics.OverlapBoxNonAlloc(probe, BuildConsts.CASTING_BOX_HALF_EXTENTS, hits, Quaternion.identity, 269091072);
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                found |= hits[i] != null && hits[i].gameObject == first;
            }
            Plugin.Log.LogInfo($"Link areas self-check: area at {first.transform.position} (scene {first.scene.name}), " +
                $"probe {probe} hits {n}, our area {(found ? "FOUND" : "NOT found")}, queriesHitTriggers={Physics.queriesHitTriggers}");
        }

        /// <summary>
        /// Diagnostics for the placement rules: how the game sets up its own extensions (Crucible) and the
        /// BuildAreas on the zone's stations, and what state the grid computes for a cell inside our area.
        /// </summary>
        internal static void Diagnose(BuildData buildData, WorldZone zone)
        {
            foreach (string id in new[] { "bucket_anvil_p", "furnace_bellows_1_p", "tool_rack_p", buildData?.Definition?.id })
            {
                BuildingDef d = id == null ? null : GameBalance.Me.GetDataOrNull<BuildingDef>(id);
                if (d != null)
                {
                    Plugin.Log.LogInfo($"Diag def {d.id}: areaType={d.chooseCustomBuildAreaType} areaId='{d.customBuildAreaId}' fullCover={d.fullCoveringCount} tab={d.tab}");
                }
            }
            WorldData world = MainGame.Instance?.GameSave?.WorldData;
            if (zone?.Data == null || world == null)
            {
                return;
            }
            int logged = 0;
            foreach (SGuid guid in zone.Data.wgoDataList)
            {
                Wgo view = GameScene.GetWgoViewGlobal(guid);
                if (view == null)
                {
                    continue;
                }
                foreach (BuildArea area in view.GetComponentsInChildren<BuildArea>(true))
                {
                    Collider c = area.Collider ?? area.GetComponent<Collider>();
                    Plugin.Log.LogInfo($"Diag area on {view.Data?.id}: id='{area.Id}' layer={area.gameObject.layer} active={area.gameObject.activeInHierarchy} " +
                        $"trigger={c?.isTrigger} bounds={c?.bounds.size} fullCover={area.fullCoveringMode} ignorePointer={area.ignoreForPointerPlacement} " +
                        $"forceShowBuff={area.foprceShowAsBuffAreaForPointerPlacement} path={Path(area.transform, view.transform)}");
                    if (++logged >= 25)
                    {
                        return;
                    }
                }
            }
            if (areas.Count > 0 && buildData?.Definition != null)
            {
                Vector3 probe = areas[0].transform.position + new Vector3(Network.LinkReach.x * 0.75f, 0f, 0f);
                BuildCellData cell = BuildCellData.GetData(probe, zone.Id, buildData.Definition);
                Plugin.Log.LogInfo($"Diag cell at {probe}: state={cell.State} (1 empty, 2 available, 4 busy, 8 buff, 16/32 wgo, 64 buff usage)");
            }
        }

        private static string Path(Transform t, Transform root)
        {
            string path = t.name;
            while (t.parent != null && t != root)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
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

    [HarmonyPatch(typeof(BuildController), nameof(BuildController.EnableBuildMode))]
    internal static class BuildController_EnableBuildMode_Diag
    {
        private static void Postfix(BuildData buildData, WorldZone worldZone)
        {
            if (buildData?.Definition?.customBuildAreaId != LinkAreas.AreaId)
            {
                return;
            }
            try
            {
                LinkAreas.Diagnose(buildData, worldZone);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Link areas diagnose failed: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(BuildController), nameof(BuildController.DisableBuildMode))]
    internal static class BuildController_DisableBuildMode
    {
        private static void Postfix() => LinkAreas.Clear();
    }
}
