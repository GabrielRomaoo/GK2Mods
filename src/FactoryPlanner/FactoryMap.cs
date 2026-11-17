using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GK2Mods.FactoryPlanner.Blueprints;
using GK2Mods.FactoryPlanner.Core;
using UnityEngine;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// The factory floor as the board's chain view draws it: every piece on the factory's grid, what each machine
    /// makes and what each chest or reserve holds, so an ingredient can be traced to where it comes out today and a
    /// new machine placed close to it (Core/Placement.cs picks the spot).
    /// </summary>
    internal sealed class FactoryMap
    {
        internal enum Kind
        {
            Machine,
            Belt,
            Chest,
            Reserve,
            Other,
        }

        internal sealed class Piece
        {
            public SGuid Id;
            public string WgoId;
            public Kind Kind;
            public CellRect Cells;
            /// <summary>Recipe the machine is on now (null if idle or not a machine).</summary>
            public string Recipe;
            public string Product;
            /// <summary>Item -> count, for chests and reserves.</summary>
            public Dictionary<string, int> Holds = new Dictionary<string, int>();
            /// <summary>What a machine is doing (Idle for anything else).</summary>
            public Work State;
            /// <summary>A machine's queue, in order: recipe and crafts left (-1: endless).</summary>
            public List<(string Recipe, int Left)> Queue = new List<(string, int)>();
            /// <summary>Item -> count inside the machine, waiting for its craft.</summary>
            public Dictionary<string, int> Inside = new Dictionary<string, int>();
            public bool HasWorker;
            /// <summary>A machine: no belt takes its product away.</summary>
            public bool NoExit;
            /// <summary>A machine: what its exit belts lead to (wgo ids of chests, stations, other machines).</summary>
            public List<string> ExitEnds = new List<string>();
            /// <summary>A machine: every end of its exit is a conveyor chest.</summary>
            public bool EndsInChestsOnly;
            /// <summary>The item riding a belt now.</summary>
            public string Carrying;
            public Vector3 Position;
        }

        /// <summary>What a machine is doing, as the map colours it.</summary>
        internal enum Work
        {
            Idle,
            Working,
            /// <summary>On a recipe, waiting for ingredients.</summary>
            Waiting,
            /// <summary>Its product can't leave: the exit belt is full or missing.</summary>
            Blocked,
            /// <summary>Marked to be taken down.</summary>
            Demolishing,
        }

        private static Work WorkOf(CraftComponent craft)
        {
            if (craft == null)
            {
                return Work.Idle;
            }
            if (craft.IsDestroyingCraftActive)
            {
                return Work.Demolishing;
            }
            if (!craft.HasCraftsInQueue)
            {
                return Work.Idle;
            }
            if (craft.Status == CraftComponentStatus.WaitingForOutputDrop)
            {
                return Work.Blocked;
            }
            return craft.IsStarted || craft.Status == CraftComponentStatus.Started ? Work.Working : Work.Waiting;
        }

        private static void Count(Dictionary<string, int> into, Inventory inventory)
        {
            foreach (Item item in inventory?.Data?.Inventory ?? new List<Item>())
            {
                if (item != null && !string.IsNullOrEmpty(item.id) && item.Count > 0)
                {
                    into[item.id] = (into.TryGetValue(item.id, out int n) ? n : 0) + item.Count;
                }
            }
        }

        public readonly List<Piece> Pieces = new List<Piece>();
        public readonly HashSet<(int C, int R)> Occupied = new HashSet<(int, int)>();
        public CellRect Area;
        public Piece Board;
        /// <summary>Screen directions on the ground: the map is drawn the way the camera looks at the factory.</summary>
        public Vector2 Up = new Vector2(0f, 1f);

        public bool Empty => Pieces.Count == 0;

        internal static FactoryMap Read(RecipeBook book, SGuid board)
        {
            var map = new FactoryMap();
            WorldZoneData zone = MainGame.Instance?.conveyorSystem?.ConveyorWorldZone;
            bool any = false;
            foreach (WgoData data in FactoryPieces.ZoneObjects(zone))
            {
                if (data?.id == null)
                {
                    continue;
                }
                var piece = new Piece { Id = data.UniqueId, WgoId = data.id, Kind = KindOf(data), Cells = CellsOf(data), Position = data.Position };
                if (piece.Kind == Kind.Belt)
                {
                    piece.Carrying = data.Inventory?.Data?.Inventory?.FirstOrDefault(i => i != null && !string.IsNullOrEmpty(i.id) && i.Count > 0)?.id;
                }
                if (piece.Kind == Kind.Machine)
                {
                    piece.State = WorkOf(data.CraftComponent);
                    piece.HasWorker = data.Worker != null;
                    foreach (CraftElementBase e in data.CraftComponent.CraftElementsQueue)
                    {
                        if (e?.CraftId == null)
                        {
                            continue;
                        }
                        // The game keeps one entry per order of the same recipe: shown as one line.
                        int last = piece.Queue.Count - 1;
                        int left = e.IsInfinite ? -1 : e.Count;
                        if (last >= 0 && piece.Queue[last].Recipe == e.CraftId)
                        {
                            piece.Queue[last] = (e.CraftId, piece.Queue[last].Left < 0 || left < 0 ? -1 : piece.Queue[last].Left + left);
                        }
                        else
                        {
                            piece.Queue.Add((e.CraftId, left));
                        }
                    }
                    Count(piece.Inside, data.CraftableObjectCraftInventory);
                    piece.Recipe = data.CraftComponent?.CraftElementsQueue.FirstOrDefault()?.CraftId;
                    piece.Product = piece.Recipe != null && book.ById.TryGetValue(piece.Recipe, out Recipe r) ? r.OutItem : null;
                }
                else if ((piece.Kind == Kind.Chest || piece.Kind == Kind.Reserve) && data.Inventory?.Data?.Inventory != null)
                {
                    foreach (Item item in data.Inventory.Data.Inventory)
                    {
                        if (item != null && item.Count > 0)
                        {
                            piece.Holds[item.id] = (piece.Holds.TryGetValue(item.id, out int n) ? n : 0) + item.Count;
                        }
                    }
                }
                if (data.UniqueId.Equals(board))
                {
                    map.Board = piece;
                }
                map.Pieces.Add(piece);
                for (int c = piece.Cells.MinC; c <= piece.Cells.MaxC; c++)
                {
                    for (int r = piece.Cells.MinR; r <= piece.Cells.MaxR; r++)
                    {
                        map.Occupied.Add((c, r));
                    }
                }
                map.Area = any ? map.Area.Union(piece.Cells) : piece.Cells;
                any = true;
            }
            Exits(map);
            map.Up = CameraUp();
            Plugin.Debug($"Factory map: {map.Pieces.Count} pieces, area {map.Area.Width}x{map.Area.Height} cells, {map.Occupied.Count} taken, up {map.Up}");
            return map;
        }

        /// <summary>Follows each machine's exit belts to where its product ends up.</summary>
        private static void Exits(FactoryMap map)
        {
            var children = new Dictionary<SGuid, List<ConveyorWgoData>>();
            foreach (ConveyorComponent c in ConveyorSystem.Data?.conveyorComponents ?? new List<ConveyorComponent>())
            {
                if (!(c?.WgoData is ConveyorWgoData data) || data.isTempObject || c is ConveyorWorkbenchComponent)
                {
                    continue;
                }
                foreach (SGuid parent in c.ParentsData.Keys)
                {
                    if (!children.TryGetValue(parent, out List<ConveyorWgoData> list))
                    {
                        children[parent] = list = new List<ConveyorWgoData>();
                    }
                    list.Add(data);
                }
            }
            foreach (Piece machine in map.Pieces.Where(p => p.Kind == Kind.Machine))
            {
                if (!((MainGame.WorldData.GetWgoData(machine.Id) as ConveyorWgoData)?.ConveyorComponent is ConveyorWorkbenchComponent bench))
                {
                    continue;
                }
                var queue = new Queue<ConveyorWgoData>((bench.connectedOutWgoDataUniqueId ?? new List<SGuid>())
                    .Select(id => MainGame.WorldData.GetWgoData(id) as ConveyorWgoData).Where(d => d != null));
                machine.NoExit = queue.Count == 0;
                var seen = new HashSet<SGuid>();
                bool onlyChests = queue.Count > 0;
                while (queue.Count > 0)
                {
                    ConveyorWgoData at = queue.Dequeue();
                    if (!seen.Add(at.UniqueId))
                    {
                        continue;
                    }
                    ConveyorComponent c = at.ConveyorComponent;
                    bool belt = c is ConveyorCellComponent || c is ConveyorSplitterComponent || c is ConveyorCellUndergroundComponent;
                    if (!belt)
                    {
                        machine.ExitEnds.Add(at.id);
                        onlyChests &= c is ConveyorChestComponent;
                        continue;
                    }
                    if (children.TryGetValue(at.UniqueId, out List<ConveyorWgoData> next))
                    {
                        next.ForEach(queue.Enqueue);
                    }
                    else
                    {
                        // A belt with nothing after it: either it feeds a machine (which holds the link) or it ends there.
                        Piece fed = map.Pieces.FirstOrDefault(p => p.Kind == Kind.Machine &&
                            ((MainGame.WorldData.GetWgoData(p.Id) as ConveyorWgoData)?.ConveyorComponent as ConveyorWorkbenchComponent)?.connectedInWgoDataUniqueId?.Contains(at.UniqueId) == true);
                        if (fed != null)
                        {
                            machine.ExitEnds.Add(fed.WgoId);
                        }
                        onlyChests = false;
                    }
                }
                machine.EndsInChestsOnly = onlyChests && machine.ExitEnds.Count > 0;
            }
        }

        private static Kind KindOf(WgoData data)
        {
            ConveyorElementType? type = data.Definition?.conveyorType;
            if (data.id.IndexOf("_container_conveyor", StringComparison.Ordinal) >= 0)
            {
                return Kind.Reserve;
            }
            if (type == ConveyorElementType.Workbench && data.CraftComponent != null)
            {
                return Kind.Machine;
            }
            if (type == ConveyorElementType.Chest || Clusters.IsChest(data.id))
            {
                return Kind.Chest;
            }
            // Only what carries items is a belt: the floor's pins and decorations ("conveyor_place_...") are not.
            return data is ConveyorWgoData && type != null && type != ConveyorElementType.None ? Kind.Belt : Kind.Other;
        }

        /// <summary>
        /// The cells of a machine's rectangle where its inputs are: the line through the cell it is anchored on.
        /// The body doesn't stand there; belts do.
        /// </summary>
        internal static IEnumerable<(int C, int R)> EntryLine(Piece machine)
        {
            CellRect rect = machine.Cells;
            int pc = Mathf.RoundToInt(machine.Position.x / FromScratch.CellX), pr = Mathf.RoundToInt(machine.Position.z / FromScratch.CellZ);
            if (machine.Kind != Kind.Machine || rect.Width < 2 || rect.Height < 2)
            {
                yield break;
            }
            if (pr == rect.MinR || pr == rect.MaxR)
            {
                for (int c = rect.MinC; c <= rect.MaxC; c++)
                {
                    yield return (c, pr);
                }
            }
            else if (pc == rect.MinC || pc == rect.MaxC)
            {
                for (int r = rect.MinR; r <= rect.MaxR; r++)
                {
                    yield return (pc, r);
                }
            }
        }

        /// <summary>The cells under a piece: its build colliders, else its visible meshes, else the cell it stands on.</summary>
        private static CellRect CellsOf(WgoData data)
        {
            int pc = Mathf.RoundToInt(data.Position.x / FromScratch.CellX), pr = Mathf.RoundToInt(data.Position.z / FromScratch.CellZ);
            Wgo view = GameScene.GetWgoViewGlobal(data.UniqueId);
            if (view == null)
            {
                return CellRect.At(pc, pr);
            }
            bool any = false;
            Bounds all = default;
            foreach (Collider c in view.GetComponentsInChildren<Collider>(true))
            {
                Bounds b = c.bounds;
                if (c.gameObject.layer != 19 || (b.size.x <= 0f && b.size.z <= 0f))
                {
                    continue;
                }
                if (any) { all.Encapsulate(b); } else { all = b; any = true; }
            }
            if (!any)
            {
                foreach (MeshRenderer r in view.GetComponentsInChildren<MeshRenderer>())
                {
                    if (r.enabled)
                    {
                        if (any) { all.Encapsulate(r.bounds); } else { all = r.bounds; any = true; }
                    }
                }
            }
            if (!any || all.size.x > 30f || all.size.z > 30f)
            {
                return CellRect.At(pc, pr);  // nothing to measure, or the room itself
            }
            // Cells whose centre is inside the rectangle (cell centres sit on multiples of the cell size).
            int minC = Mathf.CeilToInt((all.min.x + 0.05f) / FromScratch.CellX), maxC = Mathf.FloorToInt((all.max.x - 0.05f) / FromScratch.CellX);
            int minR = Mathf.CeilToInt((all.min.z + 0.05f) / FromScratch.CellZ), maxR = Mathf.FloorToInt((all.max.z - 0.05f) / FromScratch.CellZ);
            if (minC > maxC)
            {
                minC = maxC = Mathf.RoundToInt(all.center.x / FromScratch.CellX);
            }
            if (minR > maxR)
            {
                minR = maxR = Mathf.RoundToInt(all.center.z / FromScratch.CellZ);
            }
            return new CellRect(minC, minR, maxC, maxR);
        }

        /// <summary>The ground direction that is "up" on the screen, snapped to the grid.</summary>
        private static Vector2 CameraUp()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return new Vector2(0f, 1f);
            }
            Vector3 f = cam.transform.forward;
            return Mathf.Abs(f.z) >= Mathf.Abs(f.x)
                ? new Vector2(0f, f.z >= 0f ? 1f : -1f)
                : new Vector2(f.x >= 0f ? 1f : -1f, 0f);
        }

        // ---------------------------------------------------------------- questions

        /// <summary>Machines making the item now.</summary>
        internal IEnumerable<Piece> Makers(string item) => Pieces.Where(p => p.Kind == Kind.Machine && p.Product == item);

        /// <summary>Machines on the recipe now.</summary>
        internal IEnumerable<Piece> Running(string recipeId) => Pieces.Where(p => p.Kind == Kind.Machine && p.Recipe == recipeId);

        /// <summary>Chests and reserves holding the item, or reserves meant for it (empty ones too).</summary>
        internal IEnumerable<Piece> Stocks(string item) => Pieces.Where(p =>
            (p.Kind == Kind.Chest || p.Kind == Kind.Reserve) &&
            (p.Holds.ContainsKey(item) || (p.Kind == Kind.Reserve && p.WgoId.StartsWith(item + "_", StringComparison.Ordinal))));

        internal int Held(string item) => Stocks(item).Sum(p => p.Holds.TryGetValue(item, out int n) ? n : 0);

        /// <summary>Machines that use the item now.</summary>
        internal IEnumerable<Piece> Users(string item, RecipeBook book) => Pieces.Where(p =>
            p.Kind == Kind.Machine && p.Recipe != null && book.ById.TryGetValue(p.Recipe, out Recipe r) && r.Needs.Any(n => n.Item == item));

        /// <summary>The best free spot for one more machine on the recipe, near its ingredients and its users.</summary>
        internal Placement Suggest(Recipe recipe, RecipeBook book)
        {
            if (Empty)
            {
                return null;
            }
            var query = new PlacementQuery
            {
                Area = Area,
                Shape = FromScratch.ShapeFor(recipe.Station) ?? MachineShape.Bench(presumed: true),
            };
            query.Occupied.UnionWith(Occupied);
            foreach (Ingredient need in recipe.Needs)
            {
                query.Inputs[need.Item] = Makers(need.Item).Concat(Stocks(need.Item)).Select(p => p.Cells).ToList();
            }
            query.Outputs.AddRange(Users(recipe.OutItem, book).Select(p => p.Cells));
            Placement spot = Placer.Best(query);
            Plugin.Debug(spot == null
                ? $"Factory map: no free spot for {recipe.Station} ({recipe.Id})"
                : $"Factory map: {recipe.Id} best at {spot.Box.MinC},{spot.Box.MinR}-{spot.Box.MaxC},{spot.Box.MaxR}, belts " +
                  string.Join(", ", spot.Belts.Select(kv => $"{kv.Key} {kv.Value}")) + $", out {spot.OutBelts}, roomy {spot.Roomy}");
            return spot;
        }

        // ---------------------------------------------------------------- showing it in the world

        private static readonly List<SGuid> tinted = new List<SGuid>();
        private static int tintRun;

        /// <summary>Tints the given pieces in the world for a while (the board's "show in the factory").</summary>
        internal static void Tint(IEnumerable<(SGuid id, Color color)> pieces, float seconds)
        {
            ClearTint();
            foreach (var (id, color) in pieces)
            {
                Wgo view = GameScene.GetWgoViewGlobal(id);
                if (view == null)
                {
                    continue;
                }
                try
                {
                    view.SetSelectionTint(color, 0.75f);
                    tinted.Add(id);
                }
                catch (Exception e)
                {
                    Plugin.Debug($"Factory map: no tint on {id}: {e.Message}");
                }
            }
            int run = ++tintRun;
            Plugin.Instance.StartCoroutine(Untint(run, seconds));
        }

        private static IEnumerator Untint(int run, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (run == tintRun)
            {
                ClearTint();
            }
        }

        internal static void ClearTint()
        {
            foreach (SGuid id in tinted)
            {
                try
                {
                    GameScene.GetWgoViewGlobal(id)?.ClearSelectionTint();
                }
                catch (Exception)
                {
                    // the piece is gone
                }
            }
            tinted.Clear();
        }
    }
}
