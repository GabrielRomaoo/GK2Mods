using System;
using System.Collections.Generic;
using UnityEngine;

namespace GK2Mods.StorageNetwork
{
    /// <summary>
    /// Snapshot of the network, rebuilt at most once per RefreshInterval (or right after something is
    /// built/replaced/removed). All state comes from the save's WgoData, so unloaded zones count too.
    /// </summary>
    internal static class Network
    {
        private const float RefreshInterval = 1f;

        private static float nextRefresh;
        private static object snapshotSave;

        private static int archiveCount;
        private static readonly HashSet<WorldZoneData> connectedZones = new HashSet<WorldZoneData>();
        private static readonly List<Inventory> pool = new List<Inventory>();
        private static readonly Dictionary<Inventory, string> tagByInventory = new Dictionary<Inventory, string>();
        private static readonly Dictionary<WorldZoneData, List<Vector2>> connectorsByZone = new Dictionary<WorldZoneData, List<Vector2>>();

        internal static int ArchiveCount
        {
            get
            {
                EnsureFresh();
                return archiveCount;
            }
        }

        internal static void Invalidate() => nextRefresh = 0f;

        internal static bool IsZoneConnected(WorldZoneData zone)
        {
            if (zone == null)
            {
                return false;
            }
            EnsureFresh();
            return connectedZones.Contains(zone);
        }

        /// <summary>Every chest inventory in every connected zone.</summary>
        internal static List<Inventory> Pool
        {
            get
            {
                EnsureFresh();
                return pool;
            }
        }

        internal static string TagFor(Inventory inventory)
        {
            if (inventory == null)
            {
                return null;
            }
            EnsureFresh();
            return tagByInventory.TryGetValue(inventory, out string tag) ? tag : null;
        }

        /// <summary>A station is linked when its zone is connected and a Connector stands close enough.</summary>
        internal static bool IsStationConnected(WgoData station)
        {
            WorldZoneData zone = station?.WorldZoneData;
            if (!IsZoneConnected(zone) || !connectorsByZone.TryGetValue(zone, out List<Vector2> connectors))
            {
                return false;
            }
            float radius = Plugin.ConnectorRadiusCells.Value * BuildConsts.CELL_SIZE.x;
            Vector2 pos = Flat(station.Position);
            foreach (Vector2 connector in connectors)
            {
                if ((connector - pos).sqrMagnitude <= radius * radius)
                {
                    return true;
                }
            }
            return false;
        }

        private static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        private static void EnsureFresh()
        {
            object save = MainGame.Instance?.GameSave;
            if (save == snapshotSave && Time.unscaledTime < nextRefresh)
            {
                return;
            }
            snapshotSave = save;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            try
            {
                Rebuild();
            }
            catch (Exception e)
            {
                Clear();
                Plugin.Log.LogError($"Network rebuild failed: {e}");
            }
        }

        private static void Clear()
        {
            archiveCount = 0;
            connectedZones.Clear();
            pool.Clear();
            tagByInventory.Clear();
            connectorsByZone.Clear();
        }

        private static void Rebuild()
        {
            Clear();
            WorldData world = MainGame.Instance?.GameSave?.WorldData;
            if (world == null)
            {
                return;
            }

            var hubZones = new HashSet<WorldZoneData>();
            int builtArchives = 0;
            foreach (WgoData wgo in world.Cache.wgoDataByUidCache.Values)
            {
                string tag = wgo?.CustomTag;
                if (string.IsNullOrEmpty(tag) || !Content.IsOurTag(tag))
                {
                    continue;
                }
                if (wgo.Inventory != null)
                {
                    tagByInventory[wgo.Inventory] = tag;
                }
                // A construction site ("..._place") counts for the one-Central-Storage limit but does nothing yet.
                bool built = !wgo.id.EndsWith("_place", StringComparison.Ordinal);
                if (tag == Content.ArchiveTag)
                {
                    archiveCount++;
                }
                WorldZoneData zone = wgo.WorldZoneData;
                if (zone == null || !built)
                {
                    continue;
                }
                switch (tag)
                {
                    case Content.ArchiveTag:
                        builtArchives++;
                        hubZones.Add(zone);
                        break;
                    case Content.RelayTag:
                        hubZones.Add(zone);
                        break;
                    case Content.ConnectorTag:
                        if (!connectorsByZone.TryGetValue(zone, out List<Vector2> list))
                        {
                            connectorsByZone[zone] = list = new List<Vector2>();
                        }
                        list.Add(Flat(wgo.Position));
                        break;
                }
            }

            // Relays only work once a Central Storage is finished.
            if (builtArchives == 0)
            {
                return;
            }
            // Saves where it was finished before this rule existed learn the follow-ups quietly.
            Content.UnlockFollowUps(notify: false);
            connectedZones.UnionWith(hubZones);

            // Same filter the game uses for a zone's MultiInventory.
            foreach (WorldZoneData zone in connectedZones)
            {
                foreach (SGuid guid in zone.wgoDataList)
                {
                    WgoData wgo = world.GetWgoData(guid);
                    if (wgo?.Definition != null && wgo.Definition.inventorySize != 0 && wgo.Definition.OpenInMultiInventory && wgo.Inventory != null)
                    {
                        pool.Add(wgo.Inventory);
                    }
                }
            }
            Plugin.Debug($"Network: {archiveCount} archive(s), {connectedZones.Count} zone(s), {pool.Count} chest(s), connectors in {connectorsByZone.Count} zone(s)");
        }
    }
}
