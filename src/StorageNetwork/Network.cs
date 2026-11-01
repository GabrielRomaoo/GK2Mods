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
        private static bool networkActive;
        private static Inventory centralInventory;
        private static readonly List<Inventory> connectorBoxes = new List<Inventory>();
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

        /// <summary>The finished Central Storage's own chest, or null while the network is inactive.</summary>
        internal static Inventory CentralInventory
        {
            get
            {
                EnsureFresh();
                return networkActive ? centralInventory : null;
            }
        }

        /// <summary>Inventories of the finished Connectors outside excluded zones (the courier's drop boxes).</summary>
        internal static List<Inventory> ConnectorBoxes
        {
            get
            {
                EnsureFresh();
                return connectorBoxes;
            }
        }

        /// <summary>
        /// The Keeper's construction in this zone can be paid from the network: zones with a Central Storage or
        /// Relay, and zones with a Connector (outside the factory) once a Central Storage is finished.
        /// </summary>
        internal static bool CanBuildFromNetwork(WorldZoneData zone)
        {
            if (zone == null)
            {
                return false;
            }
            EnsureFresh();
            return connectedZones.Contains(zone) || (networkActive && !IsExcludedZone(zone) && connectorsByZone.ContainsKey(zone));
        }

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

        /// <summary>
        /// A station is linked when a Connector stands close enough, in any zone outside the factory, once a Central
        /// Storage is finished. It then draws from the chests of the hub zones (Central Storage + Relays): Relays add
        /// stock to the network, Connectors draw from it.
        /// </summary>
        internal static bool IsStationConnected(WgoData station)
        {
            WorldZoneData zone = station?.WorldZoneData;
            if (zone == null)
            {
                return false;
            }
            EnsureFresh();
            if (!networkActive || IsExcludedZone(zone) || !connectorsByZone.TryGetValue(zone, out List<Vector2> connectors))
            {
                return false;
            }
            Vector2 reach = LinkReach;
            Vector2 pos = Flat(station.Position);
            foreach (Vector2 connector in connectors)
            {
                if (Mathf.Abs(connector.x - pos.x) <= reach.x && Mathf.Abs(connector.y - pos.y) <= reach.y)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Half-size (x, z) of the square around a station where a Connector links it: N build cells each way.
        /// The blue placement areas (LinkAreas) use the same square, so what shows blue is what works.
        /// </summary>
        internal static Vector2 LinkReach =>
            new Vector2(Plugin.ConnectorRadiusCells.Value * BuildConsts.CELL_SIZE.x, Plugin.ConnectorRadiusCells.Value * BuildConsts.CELL_SIZE.y);

        /// <summary>Objects a Connector can link: crafting stations (benches, furnaces, anvils, kilns, alchemy, morgue).</summary>
        internal static bool IsLinkableStation(WgoData wgo)
        {
            WGODef def = wgo?.Definition;
            if (def == null || wgo is ZombieWgoData || wgo.CraftComponent == null)
            {
                return false;
            }
            switch (def.interactionType)
            {
                case WGODef.InteractionType.Craft:
                case WGODef.InteractionType.Alchemy:
                case WGODef.InteractionType.Autopsy:
                case WGODef.InteractionType.Embalm:
                case WGODef.InteractionType.Crematorium:
                    break;
                default:
                    return false;
            }
            string id = wgo.id;
            // Factory machines (assembly benches, furnaces, kitchens, bioreactor) are fed by the conveyors only.
            return !id.StartsWith("test", StringComparison.Ordinal) && !id.StartsWith("conveyor_", StringComparison.Ordinal)
                && !id.Contains("broken")
                && !id.Contains("blockage") && !id.Contains("repairing") && !id.EndsWith("_place", StringComparison.Ordinal);
        }

        private static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        private static string excludedZonesText;
        private static readonly HashSet<string> excludedZoneIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Zones listed in ExcludedZones (by default the factory's two zones).</summary>
        internal static bool IsExcludedZone(WorldZoneData zone)
        {
            string text = Plugin.ExcludedZones.Value ?? "";
            if (text != excludedZonesText)
            {
                excludedZonesText = text;
                excludedZoneIds.Clear();
                foreach (string part in text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    excludedZoneIds.Add(part.Trim());
                }
            }
            string id = zone?.Definition?.id;
            return id != null && excludedZoneIds.Contains(id);
        }

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
            networkActive = false;
            centralInventory = null;
            connectorBoxes.Clear();
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
                        if (!IsExcludedZone(zone))
                        {
                            centralInventory = wgo.Inventory;
                        }
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
                        if (wgo.Inventory != null && !IsExcludedZone(zone))
                        {
                            connectorBoxes.Add(wgo.Inventory);
                        }
                        break;
                }
            }

            // Relays only work once a Central Storage is finished, and only outside the factory.
            if (builtArchives == 0 || centralInventory == null)
            {
                connectorBoxes.Clear();
                return;
            }
            networkActive = true;
            // Saves where it was finished before this rule existed learn the follow-ups quietly.
            Content.UnlockFollowUps(notify: false);
            connectedZones.UnionWith(hubZones);
            // The factory keeps its own logistics: a hub standing there links nothing.
            int excluded = connectedZones.RemoveWhere(IsExcludedZone);
            if (excluded > 0)
            {
                Plugin.Debug($"Network: {excluded} hub zone(s) ignored (ExcludedZones)");
            }

            // Same filter the game uses for a zone's MultiInventory.
            foreach (WorldZoneData zone in connectedZones)
            {
                foreach (SGuid guid in zone.wgoDataList)
                {
                    WgoData wgo = world.GetWgoData(guid);
                    if (wgo?.Definition != null && wgo.Definition.inventorySize != 0 && wgo.Definition.OpenInMultiInventory && wgo.Inventory != null
                        && !Fixtures.IsFixtureTag(wgo.CustomTag))
                    {
                        pool.Add(wgo.Inventory);
                    }
                }
            }
            Plugin.Debug($"Network: {archiveCount} archive(s), {connectedZones.Count} zone(s), {pool.Count} chest(s), connectors in {connectorsByZone.Count} zone(s)");
        }
    }
}
