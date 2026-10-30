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
