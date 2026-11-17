using System;
using System.Collections.Generic;
using System.Linq;
using GK2Mods.FactoryPlanner.Core;
using UnityEngine;

namespace GK2Mods.FactoryPlanner.Blueprints
{
    /// <summary>
    /// Turns the from-scratch layout (Core/Layout.cs) into a blueprint for the Blueprint tool. Machine shapes come
    /// from what was seen in the player's factory: Furnace I and Benches I/II are known; other machines are
    /// learned from one already built (where its belts are), else assumed to be like the benches.
    /// </summary>
    internal static class FromScratch
    {
        internal const float CellX = 0.64f;
        internal const float CellZ = 0.6f;

        private static readonly HashSet<string> Known = new HashSet<string>
        {
            "conveyor_furnace_t1", "conveyor_assemblybench_t1", "conveyor_assemblybench_t2",
        };

        private static readonly Dictionary<string, MachineShape> learned = new Dictionary<string, MachineShape>();

        internal static MachineShape ShapeFor(string station)
        {
            if (station == null)
            {
                return null;
            }
            if (Known.Contains(station))
            {
                return MachineShape.Bench();
            }
            if (!learned.TryGetValue(station, out MachineShape shape))
            {
                try
                {
                    shape = Learn(station);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"From scratch: could not read the shape of {station}: {e.Message}");
                    shape = null;
                }
                if (shape != null)
                {
                    learned[station] = shape;  // only keep successes: a machine built later can still teach it
                }
            }
            return shape;
        }

        /// <summary>The shape of a machine of this kind standing in the factory: its body and the belts touching it.</summary>
        private static MachineShape Learn(string station)
        {
            ConveyorSystemData system = MainGame.Instance?.GameSave?.conveyorSystemData;
            if (system?.conveyorComponents == null)
            {
                return null;
            }
            foreach (ConveyorComponent component in system.conveyorComponents)
            {
                ConveyorWgoData data = component?.WgoData;
                if (data == null || data.isTempObject || data.id != station)
                {
                    continue;
                }
                List<ConveyorWgoData> ins = component.ParentsData.Values.Where(v => v != null && IsBelt(v)).ToList();
                List<ConveyorWgoData> outs = system.conveyorComponents
                    .Where(c => c?.WgoData != null && c.ParentsData.ContainsKey(data.UniqueId) && IsBelt(c.WgoData))
                    .Select(c => c.WgoData).ToList();
                Wgo view = GameScene.GetWgoViewGlobal(data.UniqueId);
                if (ins.Count == 0 || outs.Count == 0 || view == null)
                {
                    continue;
                }
                int ac = Mathf.RoundToInt(data.Position.x / CellX), ar = Mathf.RoundToInt(data.Position.z / CellZ);
                var shape = new MachineShape
                {
                    OffsetX = data.Position.x - ac * CellX,
                    OffsetZ = data.Position.z - ar * CellZ,
                    Rotation = data.MainWgoPartData?.rotationIndex ?? -1,
                };
                (int, int) CellOf(Vector3 p) => (Mathf.RoundToInt(p.x / CellX) - ac, Mathf.RoundToInt(p.z / CellZ) - ar);
                var portCells = new HashSet<(int, int)>(ins.Concat(outs).Select(b => CellOf(b.Position)));
                foreach (Collider c in view.GetComponentsInChildren<Collider>(true))
                {
                    if (c.gameObject.layer != 19)
                    {
                        continue;
                    }
                    Bounds b = c.bounds;
                    for (int x = Mathf.FloorToInt(b.min.x / CellX) - 1; x <= Mathf.CeilToInt(b.max.x / CellX) + 1; x++)
                    {
                        for (int z = Mathf.FloorToInt(b.min.z / CellZ) - 1; z <= Mathf.CeilToInt(b.max.z / CellZ) + 1; z++)
                        {
                            float cx = x * CellX, cz = z * CellZ;
                            if (cx > b.min.x + 0.05f && cx < b.max.x - 0.05f && cz > b.min.z + 0.05f && cz < b.max.z - 0.05f)
                            {
                                var cell = (x - ac, z - ar);
                                if (!portCells.Contains(cell) && !shape.Body.Contains(cell))
                                {
                                    shape.Body.Add(cell);
                                }
                            }
                        }
                    }
                }
                if (shape.Body.Count == 0)
                {
                    continue;
                }
                var body = new HashSet<(int, int)>(shape.Body);
                foreach (ConveyorWgoData belt in ins)
                {
                    var (pc, pr) = CellOf(belt.Position);
                    int dir = DirInto(pc, pr, body);
                    if (dir >= 0 && !shape.Inputs.Any(i => i.C == pc && i.R == pr))
                    {
                        shape.Inputs.Add((pc, pr, dir));
                    }
                }
                var (oc, orow) = CellOf(outs[0].Position);
                int odir = DirInto(oc, orow, body);
                if (shape.Inputs.Count == 0 || odir < 0)
                {
                    continue;
                }
                shape.Output = (oc, orow, (odir + 2) % 4);
                shape.Inputs.Sort((a, b) => a.C != b.C ? a.C.CompareTo(b.C) : a.R.CompareTo(b.R));
                Plugin.Debug($"From scratch: learned {station}: body {shape.Body.Count} cells, inputs " +
                    string.Join(" ", shape.Inputs.Select(i => $"({i.C},{i.R})>{i.Dir}")) + $", output ({shape.Output.C},{shape.Output.R})>{shape.Output.Dir}, rot {shape.Rotation}");
                return shape;
            }
            return null;
        }

        private static bool IsBelt(ConveyorWgoData data)
        {
            ConveyorElementType? type = data.Definition?.conveyorType;
            return type == ConveyorElementType.Cell || type == ConveyorElementType.UndergroundCell;
        }

        /// <summary>The direction from a belt cell into the machine body next to it, or -1.</summary>
        private static int DirInto(int c, int r, HashSet<(int, int)> body)
        {
            (int dc, int dr)[] step = { (0, -1), (-1, 0), (0, 1), (1, 0) };
            for (int d = 0; d < 4; d++)
            {
                if (body.Contains((c + step[d].dc, r + step[d].dr)))
                {
                    return d;
                }
            }
            return -1;
        }

        internal static Blueprint ToBlueprint(LayoutResult layout, string name)
        {
            var blueprint = new Blueprint { Name = name, OnFactoryGrid = true };
            int minC = layout.MinC, minR = layout.MinR;
            int step = 2;
            foreach (LayoutPiece piece in layout.Pieces.OrderBy(p => p.Kind == PieceKind.Machine ? 0 : p.Kind == PieceKind.Chest ? 1 : p.Kind == PieceKind.Station ? 2 : 3))
            {
                string wgo = piece.Kind == PieceKind.Machine ? piece.Id
                    : piece.Kind == PieceKind.Chest ? "conveyor_chest_t1"
                    : piece.Kind == PieceKind.Station ? "conveyor_cell_station"
                    : "conveyor_cell";
                BuildingDef def = FactoryPieces.DefFor(wgo);
                if (def == null)
                {
                    blueprint.Skipped[wgo] = blueprint.Skipped.TryGetValue(wgo, out int n) ? n + 1 : 1;
                    continue;
                }
                step = Math.Max(step, def.customGridStep);
                float x = (piece.C - minC) * CellX, z = (piece.R - minR) * CellZ;
                int rotation = piece.Kind == PieceKind.Belt ? piece.Rot : -1;
                if (piece.Kind == PieceKind.Machine)
                {
                    x += piece.Shape.OffsetX;
                    z += piece.Shape.OffsetZ;
                    rotation = piece.Shape.Rotation;
                    if (piece.Recipe?.Extension != null)
                    {
                        string ext = piece.Recipe.Extension;
                        blueprint.AddOns[ext] = blueprint.AddOns.TryGetValue(ext, out int e) ? e + 1 : 1;
                    }
                }
                blueprint.Pieces.Add(new BlueprintPiece
                {
                    Def = def.id,
                    Wgo = FactoryPieces.FinishedId(def),
                    X = x,
                    Z = z,
                    Rotation = rotation,
                    Product = piece.Kind == PieceKind.Machine ? piece.Item : null,
                });
            }
            blueprint.GridStep = step;
            return blueprint;
        }

        /// <summary>Puts a point on the factory's cell lattice (belts stand on multiples of 0.64 x 0.6).</summary>
        internal static Vector3 Snap(Vector3 p) =>
            new Vector3(Mathf.Round(p.x / CellX) * CellX, p.y, Mathf.Round(p.z / CellZ) * CellZ);
    }
}
