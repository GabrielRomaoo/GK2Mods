using System;
using System.Collections.Generic;
using System.Linq;

namespace GK2Mods.FactoryPlanner.Core
{
    /// <summary>A rectangle of factory cells (inclusive), the ground a piece takes.</summary>
    public struct CellRect
    {
        public int MinC, MinR, MaxC, MaxR;

        public CellRect(int minC, int minR, int maxC, int maxR)
        {
            MinC = Math.Min(minC, maxC);
            MinR = Math.Min(minR, maxR);
            MaxC = Math.Max(minC, maxC);
            MaxR = Math.Max(minR, maxR);
        }

        public static CellRect At(int c, int r) => new CellRect(c, r, c, r);

        public int Width => MaxC - MinC + 1;
        public int Height => MaxR - MinR + 1;

        /// <summary>Belt cells from (c, r) to the cell next to the rectangle (0 when touching or inside).</summary>
        public int Distance(int c, int r)
        {
            int dc = Math.Max(0, Math.Max(MinC - c, c - MaxC));
            int dr = Math.Max(0, Math.Max(MinR - r, r - MaxR));
            return Math.Max(0, dc + dr - 1);
        }

        public CellRect Union(CellRect o) =>
            new CellRect(Math.Min(MinC, o.MinC), Math.Min(MinR, o.MinR), Math.Max(MaxC, o.MaxC), Math.Max(MaxR, o.MaxR));
    }

    public sealed class PlacementQuery
    {
        /// <summary>Cells something stands on.</summary>
        public HashSet<(int C, int R)> Occupied = new HashSet<(int, int)>();
        /// <summary>The ground the machine may go on (the factory's extent).</summary>
        public CellRect Area;
        public MachineShape Shape;
        /// <summary>Ingredient -> where it can be picked up today (machines making it, chests and reserves holding it).</summary>
        public Dictionary<string, List<CellRect>> Inputs = new Dictionary<string, List<CellRect>>();
        /// <summary>Where the product goes (the machines that use it, a chest that keeps it); may be empty.</summary>
        public List<CellRect> Outputs = new List<CellRect>();
    }

    public sealed class Placement
    {
        /// <summary>Anchor cell of the machine's shape.</summary>
        public int C, R;
        /// <summary>Ground the machine and its belt ports take.</summary>
        public CellRect Box;
        /// <summary>Ingredient -> belt cells to its nearest source.</summary>
        public Dictionary<string, int> Belts = new Dictionary<string, int>();
        /// <summary>Belt cells from the exit to the nearest user of the product, or -1 when there is none.</summary>
        public int OutBelts = -1;
        public int Score;
        /// <summary>A free ring around it was found (room for the belts); false when it only just fits.</summary>
        public bool Roomy;
    }

    /// <summary>
    /// Where a new machine should stand in the factory: on free ground, as close as it gets to the places its
    /// ingredients come out of and to the machines that use what it makes, counted in belt cells.
    /// </summary>
    public static class Placer
    {
        public static Placement Best(PlacementQuery q)
        {
            if (q?.Shape == null || q.Area.Width <= 0 || q.Area.Height <= 0)
            {
                return null;
            }
            // A ring of free cells around it leaves room for the belts; a packed factory gets the tight fit.
            return Search(q, margin: 1) ?? Search(q, margin: 0);
        }

        private static Placement Search(PlacementQuery q, int margin)
        {
            MachineShape shape = q.Shape;
            var cells = shape.AllCells.ToList();
            var ins = shape.Inputs.Select(i => (i.C, i.R)).ToList();
            var outCell = (shape.Output.C, shape.Output.R);
            Placement best = null;
            for (int r = q.Area.MinR - shape.MinR + margin; r + shape.MaxR + margin <= q.Area.MaxR; r++)
            {
                for (int c = q.Area.MinC - shape.MinC + margin; c + shape.MaxC + margin <= q.Area.MaxC; c++)
                {
                    if (!Free(q, c + shape.MinC - margin, r + shape.MinR - margin, c + shape.MaxC + margin, r + shape.MaxR + margin))
                    {
                        continue;
                    }
                    var p = new Placement { C = c, R = r, Roomy = margin > 0, Box = new CellRect(c + shape.MinC, r + shape.MinR, c + shape.MaxC, r + shape.MaxR) };
                    foreach (var kv in q.Inputs)
                    {
                        if (kv.Value == null || kv.Value.Count == 0)
                        {
                            continue;
                        }
                        int d = int.MaxValue;
                        foreach (CellRect src in kv.Value)
                        {
                            foreach (var (ic, ir) in ins.Count > 0 ? ins : cells)
                            {
                                d = Math.Min(d, src.Distance(c + ic, r + ir));
                            }
                        }
                        p.Belts[kv.Key] = d;
                        p.Score += d;
                    }
                    if (q.Outputs.Count > 0)
                    {
                        p.OutBelts = q.Outputs.Min(o => o.Distance(c + outCell.Item1, r + outCell.Item2));
                        p.Score += p.OutBelts;
                    }
                    if (best == null || p.Score < best.Score)
                    {
                        best = p;
                    }
                }
            }
            return best;
        }

        private static bool Free(PlacementQuery q, int minC, int minR, int maxC, int maxR)
        {
            for (int r = minR; r <= maxR; r++)
            {
                for (int c = minC; c <= maxC; c++)
                {
                    if (q.Occupied.Contains((c, r)))
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
