using System;
using System.Collections.Generic;
using System.Linq;
using GK2Mods.FactoryPlanner.Core;
using UnityEngine;

namespace GK2Mods.FactoryPlanner.Blueprints
{
    /// <summary>
    /// Which build-menu def places which factory object. Machines are built through a construction site
    /// ("x_place_p" places "x_place", which turns into "x" when finished); belts, splitters and the like are placed
    /// directly ("x_cp" places "x"). The hidden instant defs of machines ("x_cp" without a build menu) are never used.
    /// </summary>
    internal static class FactoryPieces
    {
        private static Dictionary<string, BuildingDef> byWgo;

        private static Dictionary<string, BuildingDef> ByWgo
        {
            get
            {
                if (byWgo == null)
                {
                    byWgo = new Dictionary<string, BuildingDef>();
                    foreach (BuildingDef def in GameBalance.Me.buildingDefs)
                    {
                        bool placing = def.buildingMode == BuildingDef.BuildingMode.Place || def.buildingMode == BuildingDef.BuildingMode.ConveyorPlace;
                        if (!placing || def.buildsIn == null || !def.buildsIn.Contains(Content.FactoryBuilder) ||
                            Content.IsBlueprintDef(def.id) || string.IsNullOrEmpty(def.wgoId) || byWgo.ContainsKey(def.wgoId))
                        {
                            continue;
                        }
                        byWgo[def.wgoId] = def;
                    }
                    Plugin.Debug($"Blueprints: {byWgo.Count} factory build defs");
                }
                return byWgo;
            }
        }

        /// <summary>The def that builds this object (or its construction site), or null if the menu can't.</summary>
        internal static BuildingDef DefFor(string wgoId)
        {
            if (string.IsNullOrEmpty(wgoId))
            {
                return null;
            }
            if (ByWgo.TryGetValue(wgoId + "_place", out BuildingDef site))
            {
                return site;
            }
            return ByWgo.TryGetValue(wgoId, out BuildingDef def) ? def : null;
        }

        internal static BuildingDef Def(string defId) => GameBalance.Me.buildingDefs.Find(d => d.id == defId);

        /// <summary>The finished object of a def: the site's result for "x_place", the object itself otherwise.</summary>
        internal static string FinishedId(BuildingDef def) =>
            def.wgoId.EndsWith("_place", StringComparison.Ordinal) ? def.wgoId.Substring(0, def.wgoId.Length - "_place".Length) : def.wgoId;

        /// <summary>What the game spawns to preview a def (the same object the build pointer shows).</summary>
        internal static string PreviewId(BuildingDef def) =>
            string.IsNullOrEmpty(def.customWgoPlacePreview) ? def.wgoId : def.customWgoPlacePreview;

        internal static bool IsSite(BuildingDef def) => def.wgoId.EndsWith("_place", StringComparison.Ordinal);

        /// <summary>Order of building: machines first, then chests and splitters, belts last.</summary>
        internal static int BuildOrder(BuildingDef def)
        {
            if (IsSite(def))
            {
                return 0;
            }
            string id = def.wgoId;
            if (id.Contains("chest"))
            {
                return 1;
            }
            if (id.Contains("splitter"))
            {
                return 2;
            }
            if (id.Contains("underground"))
            {
                return 3;
            }
            return 4;
        }

        internal static IEnumerable<WgoData> ZoneObjects(WorldZoneData zone)
        {
            if (zone?.wgoDataList == null)
            {
                yield break;
            }
            foreach (SGuid guid in zone.wgoDataList.ToList())
            {
                WgoData data = MainGame.WorldData.GetWgoData(guid);
                if (data != null && !data.isTempObject)
                {
                    yield return data;
                }
            }
        }

        /// <summary>Objects already standing in the zone, by (id, position), to recognise pieces built before.</summary>
        internal static HashSet<string> Standing(WorldZoneData zone)
        {
            var set = new HashSet<string>();
            foreach (WgoData data in ZoneObjects(zone))
            {
                set.Add(Key(data.id, data.Position));
            }
            return set;
        }

        internal static string Key(string id, Vector3 position) =>
            $"{id}@{Mathf.RoundToInt(position.x * 100f)},{Mathf.RoundToInt(position.z * 100f)}";

        // ------------------------------------------------------------------ copying

        /// <summary>Copies everything between two corners (world positions) into a new blueprint.</summary>
        internal static Blueprint Capture(WorldZoneData zone, Vector3 a, Vector3 b) => Capture(zone, InArea(zone, a, b));

        /// <summary>Copies the given objects (and the add-ons attached to their machines) into a new blueprint.</summary>
        internal static Blueprint Capture(WorldZoneData zone, IEnumerable<WgoData> objects)
        {
            var blueprint = new Blueprint();
            var autoCells = new HashSet<SGuid>();
            var inside = new List<WgoData>();
            var seen = new HashSet<SGuid>();
            foreach (WgoData data in objects)
            {
                if (data == null || !seen.Add(data.UniqueId))
                {
                    continue;
                }
                inside.Add(data);
                foreach (SGuid ext in data.AttachedWorkbenchExtensions ?? new List<SGuid>())
                {
                    WgoData addOn = MainGame.WorldData.GetWgoData(ext);
                    if (addOn != null && seen.Add(addOn.UniqueId))
                    {
                        inside.Add(addOn);
                    }
                }
            }
            foreach (WgoData data in ZoneObjects(zone))
            {
                if (data is ConveyorWgoData conveyor && conveyor.HardConnectedWGOs != null)
                {
                    autoCells.UnionWith(conveyor.HardConnectedWGOs);
                }
            }
            RecipeBook book = GameData.Recipes();
            var picked = new List<(WgoData data, BuildingDef def)>();
            foreach (WgoData data in inside)
            {
                if (autoCells.Contains(data.UniqueId))
                {
                    continue;  // the machine spawns this belt itself when it is finished
                }
                if (GameBalance.Me.IsWorkbenchExtensionId(data.id))
                {
                    Count(blueprint.AddOns, data.id);
                    continue;
                }
                BuildingDef def = DefFor(data.id);
                if (def == null)
                {
                    if (data.Definition?.conveyorType != null && data.Definition.conveyorType != ConveyorElementType.None)
                    {
                        Count(blueprint.Skipped, data.id);
                    }
                    continue;
                }
                picked.Add((data, def));
            }
            if (picked.Count == 0)
            {
                return blueprint;
            }
            blueprint.GridStep = Math.Max(1, picked.Max(p => p.def.customGridStep));
            Vector3 min = new Vector3(picked.Min(p => p.data.Position.x), picked.Min(p => p.data.Position.y), picked.Min(p => p.data.Position.z));
            Vector3 origin = VisualConsts.GetRoundedPosXZ(min, BuildConsts.BUILD_GRID_SIZE * blueprint.GridStep);
            origin.y = min.y;
            foreach (var (data, def) in picked.OrderBy(p => FactoryPieces.BuildOrder(p.def)).ThenBy(p => p.data.Position.z).ThenBy(p => p.data.Position.x))
            {
                WgoPartData part = data.MainWgoPartData;
                blueprint.Pieces.Add(new BlueprintPiece
                {
                    Def = def.id,
                    Wgo = FinishedId(def),
                    X = data.Position.x - origin.x,
                    Y = data.Position.y - origin.y,
                    Z = data.Position.z - origin.z,
                    Variation = part?.variationId,
                    Rotation = part?.rotationIndex ?? -1,
                    Product = ProductOf(data, book),
                });
            }
            string main = blueprint.Products.GroupBy(p => p).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            blueprint.Name = BlueprintStore.NewName(main);
            return blueprint;
        }

        internal static IEnumerable<WgoData> InArea(WorldZoneData zone, Vector3 a, Vector3 b)
        {
            // Corners are pointer positions; an object counts when its position is within half a cell of the box.
            float minX = Mathf.Min(a.x, b.x) - 0.16f, maxX = Mathf.Max(a.x, b.x) + 0.16f;
            float minZ = Mathf.Min(a.z, b.z) - 0.15f, maxZ = Mathf.Max(a.z, b.z) + 0.15f;
            return ZoneObjects(zone).Where(d => d.Position.x >= minX && d.Position.x <= maxX && d.Position.z >= minZ && d.Position.z <= maxZ);
        }

        internal static string ProductOf(WgoData data, RecipeBook book)
        {
            try
            {
                if (data.Definition?.conveyorType != ConveyorElementType.Workbench || data.CraftComponent == null)
                {
                    return null;
                }
                string craft = data.CraftComponent.CraftElementsQueue.FirstOrDefault()?.Def?.id
                    ?? data.CraftComponent.LastStartedCraftWithRequirements?.Def?.id;
                return craft != null && book.ById.TryGetValue(craft, out Recipe recipe) ? recipe.OutItem : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Count(Dictionary<string, int> counts, string id) =>
            counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;
    }
}
