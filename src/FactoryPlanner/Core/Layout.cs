using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GK2Mods.FactoryPlanner.Core
{
    /// <summary>
    /// Shape of a machine on the factory grid (cells of 0.64 x 0.6), relative to its anchor cell. Directions are the
    /// game's belt rotation indices: 0 south (-row), 1 west (-col), 2 north (+row), 3 east (+col).
    /// </summary>
    public sealed class MachineShape
    {
        public List<(int C, int R)> Body = new List<(int C, int R)>();
        /// <summary>Belt cells feeding the machine and the direction the belt there points (into the machine).</summary>
        public List<(int C, int R, int Dir)> Inputs = new List<(int C, int R, int Dir)>();
        /// <summary>The first belt after the machine and its direction (away from it).</summary>
        public (int C, int R, int Dir) Output;
        /// <summary>World offset of the machine's position from its anchor cell's centre.</summary>
        public float OffsetX, OffsetZ;
        public int Rotation = 1;
        /// <summary>Not seen in the game yet: assumed to be like the benches.</summary>
        public bool Presumed;

        /// <summary>
        /// Furnace I and benches I/II turned with the inputs south (rotation 1), as built in the player's factory:
        /// inputs on columns 0 and 2 of the anchor row, the body on the two rows above, the exit above column 0.
        /// </summary>
        public static MachineShape Bench(bool presumed = false)
        {
            var s = new MachineShape { OffsetX = -0.08f, Rotation = 1, Presumed = presumed, Output = (0, 3, 2) };
            for (int c = 0; c <= 2; c++)
            {
                s.Body.Add((c, 1));
                s.Body.Add((c, 2));
            }
            s.Inputs.Add((0, 0, 2));
            s.Inputs.Add((2, 0, 2));
            return s;
        }

        public IEnumerable<(int C, int R)> AllCells =>
            Body.Concat(Inputs.Select(i => (i.C, i.R))).Concat(new[] { (Output.C, Output.R) });

        public int MinC => AllCells.Min(c => c.C);
        public int MaxC => AllCells.Max(c => c.C);
        public int MinR => AllCells.Min(c => c.R);
        public int MaxR => AllCells.Max(c => c.R);
    }

    public enum PieceKind
    {
        Belt,
        Chest,
        Machine,
        Station,
    }

    public sealed class LayoutPiece
    {
        public PieceKind Kind;
        /// <summary>Machine station id for machines; null otherwise.</summary>
        public string Id;
        public int C, R;
        /// <summary>Belt direction; the machine's rotation for machines; -1 when it doesn't matter.</summary>
        public int Rot = -1;
        /// <summary>What a chest holds / a machine makes.</summary>
        public string Item;
        public Recipe Recipe;
        public MachineShape Shape;
        public int Stage;
    }

    public sealed class LayoutResult
    {
        public List<LayoutPiece> Pieces = new List<LayoutPiece>();
        /// <summary>Connections no belt could be drawn for: "item -> what needs it".</summary>
        public List<(string Item, string To)> Unrouted = new List<(string, string)>();
        /// <summary>Station ids laid with an assumed shape.</summary>
        public HashSet<string> Presumed = new HashSet<string>();
        /// <summary>Chests for raw materials, to be filled by hand.</summary>
        public List<LayoutPiece> EntryChests = new List<LayoutPiece>();

        public int Belts => Pieces.Count(p => p.Kind == PieceKind.Belt);
        public int Chests => Pieces.Count(p => p.Kind == PieceKind.Chest);
        public int Machines => Pieces.Count(p => p.Kind == PieceKind.Machine);

        public int MinC => Pieces.Count == 0 ? 0 : Pieces.Min(p => p.Kind == PieceKind.Machine ? p.C + p.Shape.MinC : p.C);
        public int MaxC => Pieces.Count == 0 ? 0 : Pieces.Max(p => p.Kind == PieceKind.Machine ? p.C + p.Shape.MaxC : p.C);
        public int MinR => Pieces.Count == 0 ? 0 : Pieces.Min(p => p.Kind == PieceKind.Machine ? p.R + p.Shape.MinR : p.R);
        public int MaxR => Pieces.Count == 0 ? 0 : Pieces.Max(p => p.Kind == PieceKind.Machine ? p.R + p.Shape.MaxR : p.R);
        public int Width => MaxC - MinC + 1;
        public int Height => MaxR - MinR + 1;

        /// <summary>Text drawing, north up: belts as arrows, B chests, S stations, machines by their first letter.</summary>
        public string Ascii()
        {
            var map = new Dictionary<(int, int), char>();
            foreach (LayoutPiece p in Pieces.Where(p => p.Kind == PieceKind.Machine))
            {
                char letter = Letter(p.Id);
                foreach (var b in p.Shape.Body)
                {
                    map[(p.C + b.C, p.R + b.R)] = letter;
                }
            }
            foreach (LayoutPiece p in Pieces.Where(p => p.Kind != PieceKind.Machine))
            {
                map[(p.C, p.R)] = p.Kind == PieceKind.Chest ? 'B' : p.Kind == PieceKind.Station ? 'S' : "v<^>"[p.Rot];
            }
            var sb = new StringBuilder();
            for (int r = MaxR; r >= MinR; r--)
            {
                sb.Append('|');
                for (int c = MinC; c <= MaxC; c++)
                {
                    sb.Append(map.TryGetValue((c, r), out char ch) ? ch : ' ');
                }
                sb.Append("|\n");
            }
            return sb.ToString();
        }

        private static char Letter(string id)
        {
            if (id == null) return 'M';
            if (id.Contains("furnace")) return 'F';
            if (id.Contains("kitchen")) return 'K';
            if (id.Contains("assembly")) return 'A';
            return 'M';
        }
    }

    /// <summary>
    /// Lays a stage plan out on an empty grid. Stages are horizontal bands, bottom to top: the entry chests
    /// (raw materials), stage 1's machines, the stock chests of stage 1, stage 2's machines, ... and the
    /// schedule's boxes on top, each ending in a station. A chest holds one item, so it needs no filter; every
    /// connection is one belt drawn by a path search that keeps belts from crossing, from touching a chest they
    /// don't serve (a chest pushes into any belt beside it) and from making U-turns.
    /// </summary>
    public static class LayoutPlanner
    {
        private static readonly (int dc, int dr)[] Step = { (0, -1), (-1, 0), (0, 1), (1, 0) };

        private const int MachineGap = 3;
        private const int ChannelRows = 3;
        private const int MaxPerRow = 6;

        private sealed class Instance
        {
            public StageStep Step;
            public int Stage;
            public MachineShape Shape;
            public int C, R;
            public LayoutPiece Piece;
        }

        private sealed class Chest
        {
            public string Item;
            public int Stage;   // 0 for the entry
            public int C, R;
            public int FreeSides;
            public LayoutPiece Piece;
            public Chest Next = null;  // overflow chest fed through a link belt (not used yet)
        }

        private sealed class Edge
        {
            public Chest From;          // chest -> machine input
            public Instance FromMachine; // machine output -> chest
            public Chest To;
            public Instance ToMachine;
            public int Input;
            public string Item;
            /// <summary>The chest sits right behind the input: only the input belt itself.</summary>
            public bool Direct;
        }

        public static LayoutResult Build(StagePlan plan, Func<string, MachineShape> shapeFor)
        {
            var result = new LayoutResult();
            var occupied = new HashSet<(int, int)>();
            var reserved = new HashSet<(int, int)>();   // chest sides: only their own belt may go there
            var ports = new HashSet<(int, int)>();      // machine inputs, outputs and the cell after each output

            // 1. Machines, band by band (a stage with many machines wraps onto more bands). Columns are then
            //    settled by sweeping up and down: each machine moves over the machines that feed it (up) or under
            //    those it feeds (down), so the chest between them and most belts run straight.
            // Physically a machine sits as late as it can: right under the first stage that takes its output
            // (glass made in stage 1 for a stage-4 box goes just below the box bench, not three bands lower).
            var level = new Dictionary<StageStep, int>();
            foreach (Stage stage in Enumerable.Reverse(plan.Stages))
            {
                foreach (StageStep step in stage.Steps)
                {
                    var users = plan.Stages.SelectMany(st => st.Steps).Where(x => x.Recipe.Needs.Any(n => n.Item == step.Recipe.OutItem) && level.ContainsKey(x)).ToList();
                    level[step] = stage.Final || users.Count == 0 ? stage.Index : Math.Max(stage.Index, users.Min(x => level[x]) - 1);
                }
            }
            var bands = new List<List<Instance>>();
            int row = ChannelRows;
            foreach (int lvl in level.Values.Distinct().OrderBy(v => v))
            {
                var list = new List<Instance>();
                foreach (StageStep step in plan.Stages.SelectMany(st => st.Steps).Where(x => level[x] == lvl))
                {
                    Stage stage = plan.Stages.First(st => st.Steps.Contains(step));
                    MachineShape shape = shapeFor(step.Recipe.Station) ?? MachineShape.Bench(presumed: true);
                    if (shape.Presumed)
                    {
                        result.Presumed.Add(step.Recipe.Station);
                    }
                    for (int i = 0; i < step.Machines; i++)
                    {
                        list.Add(new Instance { Step = step, Stage = stage.Index, Shape = shape });
                    }
                }
                for (int start = 0; start < list.Count; start += MaxPerRow)
                {
                    List<Instance> band = list.Skip(start).Take(MaxPerRow).ToList();
                    int height = band.Max(m => m.Shape.MaxR - m.Shape.MinR + 1);
                    foreach (Instance m in band)
                    {
                        m.R = row - m.Shape.MinR;
                    }
                    bands.Add(band);
                    row += height;
                    if (start + MaxPerRow < list.Count)
                    {
                        row += 3;  // a narrow corridor between two bands of the same stage
                    }
                }
                row += ChannelRows;
            }
            var machines = bands.SelectMany(b => b).ToList();
            var makers = machines.GroupBy(m => m.Step.Recipe.OutItem).ToDictionary(g => g.Key, g => g.ToList());
            var takersOf = new Dictionary<string, List<(Instance m, int k)>>();
            foreach (Instance m in machines)
            {
                for (int k = 0; k < m.Step.Recipe.Needs.Count && k < m.Shape.Inputs.Count; k++)
                {
                    string need = m.Step.Recipe.Needs[k].Item;
                    if (!takersOf.TryGetValue(need, out var l))
                    {
                        takersOf[need] = l = new List<(Instance, int)>();
                    }
                    l.Add((m, k));
                }
            }
            var placed = new HashSet<Instance>();
            double Up(Instance m)
            {
                var wants = new List<double>();
                for (int k = 0; k < m.Step.Recipe.Needs.Count && k < m.Shape.Inputs.Count; k++)
                {
                    if (makers.TryGetValue(m.Step.Recipe.Needs[k].Item, out List<Instance> ps))
                    {
                        wants.AddRange(ps.Where(placed.Contains).Select(pm => (double)(pm.C + pm.Shape.Output.C - m.Shape.Inputs[k].C)));
                    }
                }
                return wants.Count > 0 ? wants.Average() : double.NaN;
            }
            double Down(Instance m)
            {
                if (!takersOf.TryGetValue(m.Step.Recipe.OutItem, out var ts))
                {
                    return double.NaN;
                }
                var wants = ts.Where(t => placed.Contains(t.m)).Select(t => (double)(t.m.C + t.m.Shape.Inputs[t.k].C - m.Shape.Output.C)).ToList();
                return wants.Count > 0 ? wants.Average() : double.NaN;
            }
            void PlaceBand(List<Instance> band, Func<Instance, double> want)
            {
                var ordered = band.Select((m, i) => (m, w: want(m), i))
                    .OrderBy(x => double.IsNaN(x.w) ? 1 : 0).ThenBy(x => double.IsNaN(x.w) ? x.i : x.w).ToList();
                int? nextFree = null;
                foreach (var (m, w, _) in ordered)
                {
                    int c = double.IsNaN(w) ? (nextFree ?? 0) - m.Shape.MinC : (int)Math.Round(w);
                    if (nextFree.HasValue)
                    {
                        c = Math.Max(c, nextFree.Value - m.Shape.MinC);
                    }
                    m.C = c;
                    nextFree = c + m.Shape.MaxC + 1 + MachineGap;
                    placed.Add(m);
                }
                band.Sort((a, b) => a.C.CompareTo(b.C));
            }
            int Cost()
            {
                int sum = 0;
                foreach (var kv in takersOf)
                {
                    if (!makers.TryGetValue(kv.Key, out List<Instance> ps))
                    {
                        continue;
                    }
                    foreach (var (m, k) in kv.Value)
                    {
                        sum += ps.Min(pm => Math.Abs(pm.C + pm.Shape.Output.C - m.C - m.Shape.Inputs[k].C) + Math.Abs(pm.R - m.R));
                    }
                }
                return sum;
            }
            int bestCost = int.MaxValue;
            Dictionary<Instance, int> best = null;
            for (int pass = 0; pass < 6; pass++)
            {
                bool up = pass % 2 == 0;
                placed.Clear();
                if (up)
                {
                    foreach (List<Instance> band in bands)
                    {
                        PlaceBand(band, Up);
                    }
                }
                else
                {
                    placed.UnionWith(bands[bands.Count - 1]);
                    for (int b = bands.Count - 2; b >= 0; b--)
                    {
                        PlaceBand(bands[b], Down);
                    }
                }
                placed.UnionWith(machines);
                int cost = Cost();
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = machines.ToDictionary(m => m, m => m.C);
                }
            }
            foreach (Instance m in machines)
            {
                m.C = best[m];
            }

            foreach (Instance m in machines)
            {
                m.Piece = new LayoutPiece
                {
                    Kind = PieceKind.Machine, Id = m.Step.Recipe.Station, C = m.C, R = m.R, Rot = m.Shape.Rotation,
                    Item = m.Step.Recipe.OutItem, Recipe = m.Step.Recipe, Shape = m.Shape, Stage = m.Stage,
                };
                result.Pieces.Add(m.Piece);
                foreach (var b in m.Shape.Body)
                {
                    occupied.Add((m.C + b.C, m.R + b.R));
                }
                foreach (var p in m.Shape.Inputs)
                {
                    ports.Add((m.C + p.C, m.R + p.R));
                }
                ports.Add((m.C + m.Shape.Output.C, m.R + m.Shape.Output.R));
                var o = m.Shape.Output;
                ports.Add((m.C + o.C + Step[o.Dir].dc, m.R + o.R + Step[o.Dir].dr));
            }

            // 2. One chest per item (entry: raw materials; stock n: what stage n makes), with overflow chests when
            //    the item needs more than four belts.
            var edges = new List<Edge>();
            var finals = new HashSet<string>(plan.Stages.Where(s => s.Final).SelectMany(s => s.Output.Keys));
            var consumers = new Dictionary<string, List<(Instance m, int input)>>();
            Dictionary<string, List<Instance>> producersOf = makers;
            foreach (Instance m in machines)
            {
                List<Ingredient> needs = m.Step.Recipe.Needs;
                for (int k = 0; k < needs.Count; k++)
                {
                    if (k >= m.Shape.Inputs.Count)
                    {
                        result.Unrouted.Add((needs[k].Item, m.Step.Recipe.OutItem));
                        continue;
                    }
                    if (!consumers.TryGetValue(needs[k].Item, out var list))
                    {
                        consumers[needs[k].Item] = list = new List<(Instance, int)>();
                    }
                    list.Add((m, k));
                }
            }
            // A raw material gets its own chest right behind each input that takes it (filled by hand, no belt).
            // A made item gets one chest right behind the input of its nearest consumer; its makers and its other
            // consumers reach it by belt. A chest holds one item, so it needs no filter.
            var rowChests = new Dictionary<int, List<int>>();
            bool CanChest(int c, int r) => !occupied.Contains((c, r)) && !reserved.Contains((c, r)) && !ports.Contains((c, r)) &&
                !(rowChests.TryGetValue(r, out List<int> l) && l.Any(x => Math.Abs(x - c) < 2)) &&
                !(rowChests.TryGetValue(r - 1, out List<int> l2) && l2.Contains(c)) && !(rowChests.TryGetValue(r + 1, out List<int> l3) && l3.Contains(c));
            Chest NewChest(string item, int stage, int c, int r)
            {
                var chest = new Chest { Item = item, Stage = stage, C = c, R = r, FreeSides = 4 };
                chest.Piece = new LayoutPiece { Kind = PieceKind.Chest, C = c, R = r, Item = item, Stage = stage };
                result.Pieces.Add(chest.Piece);
                occupied.Add((c, r));
                for (int d = 0; d < 4; d++)
                {
                    var side = (c + Step[d].dc, r + Step[d].dr);
                    if (!ports.Contains(side))
                    {
                        reserved.Add(side);
                    }
                }
                if (!rowChests.TryGetValue(r, out List<int> list))
                {
                    rowChests[r] = list = new List<int>();
                }
                foreach (int other in list.Where(x => Math.Abs(x - c) == 2))
                {
                    occupied.Add(((other + c) / 2, r));  // between two chests: both would push into a belt there
                }
                list.Add(c);
                if (stage == 0)
                {
                    result.EntryChests.Add(chest.Piece);
                }
                return chest;
            }
            (int c, int r) Behind(Instance m, int input)
            {
                var port = m.Shape.Inputs[input];
                return (m.C + port.C - Step[port.Dir].dc, m.R + port.R - Step[port.Dir].dr);
            }
            (int c, int r) FreeNear((int c, int r) at)
            {
                for (int d = 1; d < 60; d++)
                {
                    foreach (int c in new[] { at.c + d, at.c - d })
                    {
                        if (CanChest(c, at.r))
                        {
                            return (c, at.r);
                        }
                    }
                }
                return at;
            }

            var items = consumers.Keys.Concat(machines.Select(m => m.Step.Recipe.OutItem).Where(i => !finals.Contains(i))).Distinct()
                .OrderBy(i => plan.MadeAt.TryGetValue(i, out int s) ? s : 0).ThenBy(i => i, StringComparer.Ordinal).ToList();
            // Raw materials first (they take the spots right behind the inputs), then the made items.
            foreach (string item in items.OrderBy(i => producersOf.ContainsKey(i) ? 1 : 0))
            {
                int stage = plan.MadeAt.TryGetValue(item, out int st) ? st : 0;
                List<Instance> producers = producersOf.TryGetValue(item, out List<Instance> pr) ? pr : new List<Instance>();
                List<(Instance m, int input)> takers = consumers.TryGetValue(item, out var t) ? t : new List<(Instance, int)>();
                if (producers.Count == 0)
                {
                    foreach (var (m, input) in takers)
                    {
                        var at = Behind(m, input);
                        if (CanChest(at.c, at.r))
                        {
                            edges.Add(new Edge { From = NewChest(item, 0, at.c, at.r), ToMachine = m, Input = input, Item = item, Direct = true });
                        }
                        else
                        {
                            var near = FreeNear(at);
                            edges.Add(new Edge { From = NewChest(item, 0, near.c, near.r), ToMachine = m, Input = input, Item = item });
                        }
                    }
                    continue;
                }
                var primary = takers.OrderBy(x => x.m.Stage).ThenBy(x => x.input).FirstOrDefault();
                Chest chest;
                if (primary.m == null)
                {
                    var o = producers[0].Shape.Output;
                    var spot = (producers[0].C + o.C, producers[0].R + o.R + 3);
                    chest = NewChest(item, stage, spot.Item1, spot.Item2);
                }
                else
                {
                    var at = Behind(primary.m, primary.input);
                    bool direct = CanChest(at.c, at.r);
                    if (!direct)
                    {
                        at = FreeNear(at);
                    }
                    chest = NewChest(item, stage, at.c, at.r);
                    edges.Add(new Edge { From = chest, ToMachine = primary.m, Input = primary.input, Item = item, Direct = direct });
                }
                foreach (Instance p in producers)
                {
                    edges.Add(new Edge { FromMachine = p, To = chest, Item = item });
                }
                foreach (var (m, input) in takers.Where(x => x.m != primary.m || x.input != primary.input))
                {
                    edges.Add(new Edge { From = chest, ToMachine = m, Input = input, Item = item });
                }
            }

            // 3. Stations for the schedule's boxes, right after the exit belt.
            foreach (Instance m in machines.Where(m => finals.Contains(m.Step.Recipe.OutItem)))
            {
                var o = m.Shape.Output;
                var exit = new LayoutPiece { Kind = PieceKind.Belt, C = m.C + o.C, R = m.R + o.R, Rot = o.Dir };
                var station = new LayoutPiece
                {
                    Kind = PieceKind.Station, C = exit.C + Step[o.Dir].dc, R = exit.R + Step[o.Dir].dr, Item = m.Step.Recipe.OutItem,
                };
                result.Pieces.Add(exit);
                result.Pieces.Add(station);
                occupied.Add((exit.C, exit.R));
                occupied.Add((station.C, station.R));
            }

            // 4. Belts, shortest connections first.
            int minC = result.MinC - 4, maxC = result.MaxC + 4, minR = result.MinR - 2, maxR = result.MaxR + 2;
            foreach (Edge e in edges.OrderBy(x => x.Direct ? 0 : 1).ThenBy(Length))
            {
                List<(int C, int R, int Dir)> path;
                if (e.Direct)
                {
                    var port = e.ToMachine.Shape.Inputs[e.Input];
                    path = new List<(int C, int R, int Dir)> { (e.ToMachine.C + port.C, e.ToMachine.R + port.R, port.Dir) };
                    e.From.FreeSides--;
                }
                else
                {
                    path = Route(e, occupied, reserved, ports, minC, maxC, minR, maxR);
                }
                if (path == null)
                {
                    result.Unrouted.Add((e.Item, e.ToMachine != null ? e.ToMachine.Step.Recipe.OutItem : StockLabel(e.To)));
                    continue;
                }
                foreach (var cell in path)
                {
                    result.Pieces.Add(new LayoutPiece { Kind = PieceKind.Belt, C = cell.C, R = cell.R, Rot = cell.Dir, Item = e.Item });
                    occupied.Add((cell.C, cell.R));
                }
            }
            return result;
        }

        /// <summary>
        /// One standard module: a chest right behind each input (raw material or the stock of an earlier stage),
        /// the machine, and its output straight into a stock chest (a station for a final product). Modules chain
        /// by laying one so that its input chest sits on the output chest of another: that chest is reused.
        /// </summary>
        public static LayoutResult Module(Recipe recipe, MachineShape shape, bool final)
        {
            var result = new LayoutResult();
            if (shape == null)
            {
                shape = MachineShape.Bench(presumed: true);
                result.Presumed.Add(recipe.Station);
            }
            else if (shape.Presumed)
            {
                result.Presumed.Add(recipe.Station);
            }
            result.Pieces.Add(new LayoutPiece { Kind = PieceKind.Machine, Id = recipe.Station, Rot = shape.Rotation, Item = recipe.OutItem, Recipe = recipe, Shape = shape });
            for (int k = 0; k < recipe.Needs.Count; k++)
            {
                string item = recipe.Needs[k].Item;
                if (k >= shape.Inputs.Count)
                {
                    result.Unrouted.Add((item, recipe.OutItem));
                    continue;
                }
                var port = shape.Inputs[k];
                result.Pieces.Add(new LayoutPiece { Kind = PieceKind.Belt, C = port.C, R = port.R, Rot = port.Dir, Item = item });
                var chest = new LayoutPiece { Kind = PieceKind.Chest, C = port.C - Step[port.Dir].dc, R = port.R - Step[port.Dir].dr, Item = item };
                result.Pieces.Add(chest);
                result.EntryChests.Add(chest);
            }
            var o = shape.Output;
            result.Pieces.Add(new LayoutPiece { Kind = PieceKind.Belt, C = o.C, R = o.R, Rot = o.Dir, Item = recipe.OutItem });
            result.Pieces.Add(new LayoutPiece
            {
                Kind = final ? PieceKind.Station : PieceKind.Chest, C = o.C + Step[o.Dir].dc, R = o.R + Step[o.Dir].dr, Item = recipe.OutItem,
            });
            return result;
        }

        private static string StockLabel(Chest chest) => chest.Stage == 0 ? "entry" : $"stock {chest.Stage}";

        private static int Nearest(int want, List<int> taken, int count)
        {
            for (int d = 0; d < 200; d++)
            {
                foreach (int c in new[] { want + d, want - d })
                {
                    bool free = true;
                    for (int i = 0; i < count && free; i++)
                    {
                        free = taken.All(t => Math.Abs(t - (c + 2 * i)) >= 2);
                    }
                    if (free)
                    {
                        return c;
                    }
                }
            }
            return want;
        }

        private static int Length(Edge e)
        {
            (int c, int r) a = e.From != null ? (e.From.C, e.From.R) : (e.FromMachine.C + e.FromMachine.Shape.Output.C, e.FromMachine.R + e.FromMachine.Shape.Output.R);
            (int c, int r) b = e.To != null ? (e.To.C, e.To.R)
                : (e.ToMachine.C + e.ToMachine.Shape.Inputs[e.Input].C, e.ToMachine.R + e.ToMachine.Shape.Inputs[e.Input].R);
            return Math.Abs(a.c - b.c) + Math.Abs(a.r - b.r);
        }

        /// <summary>
        /// A* over (cell, heading). Belts leave a chest or a machine straight; each belt points at the next one, the
        /// last at its target (a machine input belt keeps the machine's direction). Turns cost a little extra.
        /// </summary>
        private static List<(int C, int R, int Dir)> Route(Edge e, HashSet<(int, int)> occupied, HashSet<(int, int)> reserved,
            HashSet<(int, int)> ports, int minC, int maxC, int minR, int maxR)
        {
            var starts = new List<(List<(int C, int R, int Dir)> prefix, int c, int r, int heading)>();
            if (e.FromMachine != null)
            {
                var o = e.FromMachine.Shape.Output;
                int pc = e.FromMachine.C + o.C, pr = e.FromMachine.R + o.R;
                int nc = pc + Step[o.Dir].dc, nr = pr + Step[o.Dir].dr;
                if (!occupied.Contains((pc, pr)) && !occupied.Contains((nc, nr)))
                {
                    starts.Add((new List<(int, int, int)> { (pc, pr, o.Dir) }, nc, nr, o.Dir));
                }
            }
            else
            {
                for (Chest chest = e.From; chest != null; chest = chest.Next)
                {
                    if (chest.FreeSides <= 0)
                    {
                        continue;
                    }
                    for (int d = 0; d < 4; d++)
                    {
                        int sc = chest.C + Step[d].dc, sr = chest.R + Step[d].dr;
                        int nc = sc + Step[d].dc, nr = sr + Step[d].dr;
                        if (!occupied.Contains((sc, sr)) && !ports.Contains((sc, sr)) && Free(nc, nr, occupied, reserved, ports))
                        {
                            starts.Add((new List<(int, int, int)> { (sc, sr, d) }, nc, nr, d));
                        }
                    }
                }
            }
            if (starts.Count == 0)
            {
                return null;
            }

            // Goals: cell -> direction of the last belt there.
            var goals = new Dictionary<(int, int), int>();
            Chest target = null;
            if (e.ToMachine != null)
            {
                var p = e.ToMachine.Shape.Inputs[e.Input];
                goals[(e.ToMachine.C + p.C, e.ToMachine.R + p.R)] = p.Dir;
            }
            else
            {
                for (Chest chest = e.To; chest != null; chest = chest.Next)
                {
                    if (chest.FreeSides <= 0)
                    {
                        continue;
                    }
                    for (int d = 0; d < 4; d++)
                    {
                        var side = (chest.C + Step[d].dc, chest.R + Step[d].dr);
                        if (!occupied.Contains(side) && !ports.Contains(side))
                        {
                            goals[side] = (d + 2) % 4;  // pointing back into the chest
                        }
                    }
                    target = chest;
                    break;  // producers feed the head chest; overflow chests get items through the link
                }
            }
            if (goals.Count == 0)
            {
                return null;
            }

            var open = new SortedSet<(int f, int id)>();
            var nodes = new List<(int c, int r, int h, int g, int parent, int start)>();
            var best = new Dictionary<(int, int, int), int>();
            int H(int c, int r) => goals.Keys.Min(k => Math.Abs(k.Item1 - c) + Math.Abs(k.Item2 - r));
            for (int s = 0; s < starts.Count; s++)
            {
                var st = starts[s];
                nodes.Add((st.c, st.r, st.heading, 1, -1, s));
                open.Add((1 + H(st.c, st.r), nodes.Count - 1));
                best[(st.c, st.r, st.heading)] = 1;
            }
            int found = -1;
            int guard = 0;
            while (open.Count > 0 && guard++ < 60000)
            {
                var top = open.Min;
                open.Remove(top);
                var n = nodes[top.id];
                if (best.TryGetValue((n.c, n.r, n.h), out int bg) && bg < n.g)
                {
                    continue;
                }
                if (goals.TryGetValue((n.c, n.r), out int last) && last != (n.h + 2) % 4)
                {
                    found = top.id;
                    break;
                }
                for (int d = 0; d < 4; d++)
                {
                    if (d == (n.h + 2) % 4)
                    {
                        continue;  // no U-turns
                    }
                    int c = n.c + Step[d].dc, r = n.r + Step[d].dr;
                    if (c < minC || c > maxC || r < minR || r > maxR)
                    {
                        continue;
                    }
                    if (!goals.ContainsKey((c, r)) && !Free(c, r, occupied, reserved, ports))
                    {
                        continue;
                    }
                    if (goals.ContainsKey((c, r)) && occupied.Contains((c, r)))
                    {
                        continue;
                    }
                    if (OnPath(nodes, top.id, c, r))
                    {
                        continue;
                    }
                    int g = n.g + 1 + (d == n.h ? 0 : 1);
                    if (best.TryGetValue((c, r, d), out int old) && old <= g)
                    {
                        continue;
                    }
                    best[(c, r, d)] = g;
                    nodes.Add((c, r, d, g, top.id, n.start));
                    open.Add((g + H(c, r), nodes.Count - 1));
                }
            }
            if (found < 0)
            {
                return null;
            }
            var cells = new List<(int c, int r)>();
            for (int i = found; i >= 0; i = nodes[i].parent)
            {
                cells.Add((nodes[i].c, nodes[i].r));
            }
            cells.Reverse();
            var path = new List<(int C, int R, int Dir)>(starts[nodes[found].start].prefix);
            for (int i = 0; i < cells.Count; i++)
            {
                int dir = i + 1 < cells.Count ? DirTo(cells[i], cells[i + 1]) : goals[cells[i]];
                path.Add((cells[i].c, cells[i].r, dir));
            }
            // Use up the chest sides the path touches.
            if (e.From != null)
            {
                var first = path[0];
                for (Chest chest = e.From; chest != null; chest = chest.Next)
                {
                    if (Math.Abs(chest.C - first.C) + Math.Abs(chest.R - first.R) == 1)
                    {
                        chest.FreeSides--;
                    }
                }
            }
            if (target != null)
            {
                target.FreeSides--;
            }
            return path;
        }

        private static bool OnPath(List<(int c, int r, int h, int g, int parent, int start)> nodes, int id, int c, int r)
        {
            for (int i = id; i >= 0; i = nodes[i].parent)
            {
                if (nodes[i].c == c && nodes[i].r == r)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Free(int c, int r, HashSet<(int, int)> occupied, HashSet<(int, int)> reserved, HashSet<(int, int)> ports) =>
            !occupied.Contains((c, r)) && !reserved.Contains((c, r)) && !ports.Contains((c, r));

        private static int DirTo((int c, int r) a, (int c, int r) b)
        {
            for (int d = 0; d < 4; d++)
            {
                if (a.c + Step[d].dc == b.c && a.r + Step[d].dr == b.r)
                {
                    return d;
                }
            }
            return 2;
        }
    }
}
