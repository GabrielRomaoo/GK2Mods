using System;
using System.Collections.Generic;
using System.Linq;
using GK2Mods.FactoryPlanner.Blueprints;
using GK2Mods.FactoryPlanner.Core;
using UnityEngine;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// What the board's map does to the factory, through the game's own build code: moving a piece (the game lifts
    /// it with its add-ons, hard-linked belts and zombie, and relinks the belts where it lands) and taking one down
    /// (a belt goes at once and drops what it cost; a machine gets the game's demolition job, which someone works).
    /// </summary>
    internal static class FloorActions
    {
        /// <summary>
        /// Chest sides by belt, as they were before a move: a side loses its setting when its belt is unlinked, and
        /// gets it back if a later move links the two again (the piece put back, or set down beside the same belt).
        /// </summary>
        private static readonly Dictionary<string, (SideMode Mode, List<string> Items)> parked = new Dictionary<string, (SideMode, List<string>)>();

        /// <summary>Moves a piece by whole cells. Null when done, else why not.</summary>
        internal static string Move(FactoryMap floor, SGuid id, int dc, int dr)
        {
            Wgo view = GameScene.GetWgoViewGlobal(id);
            if (view == null || view.Data == null)
            {
                return Texts.Pt("A peça não está carregada: abra o Quadro dentro da fábrica.", "The piece isn't loaded: open the board inside the factory.");
            }
            if (dc == 0 && dr == 0)
            {
                return Texts.Pt("É o lugar onde ela já está.", "That is where it already stands.");
            }
            if (!view.IsBuildMovable())
            {
                return Texts.Pt("O jogo não deixa mover esta peça.", "The game doesn't let this piece be moved.");
            }
            foreach (var kv in Sides.Capture())
            {
                parked[kv.Key] = kv.Value;
            }
            if (!BuildMoveHelper.TryLift(view))
            {
                return Texts.Pt("O jogo não conseguiu levantar a peça.", "The game couldn't lift the piece.");
            }
            try
            {
                BuildMoveData move = BuildMoveHelper.ActiveMove;
                var members = new HashSet<SGuid>(move.Members.Select(m => m.Data.UniqueId));
                List<FactoryMap.Piece> real = floor.Pieces.Where(p => p.Kind != FactoryMap.Kind.Other).ToList();
                CellRect yard = real.Skip(1).Aggregate(real[0].Cells, (a, p) => a.Union(p.Cells));
                // What stands in the way: bodies stop everything; belts stop bodies; a machine's input line takes belts only.
                var bodies = new HashSet<(int, int)>();
                var belts = new HashSet<(int, int)>();
                var inputs = new HashSet<(int, int)>();
                foreach (FactoryMap.Piece p in floor.Pieces.Where(p => !members.Contains(p.Id) && !p.WgoId.StartsWith("conveyor_place_", StringComparison.Ordinal)))
                {
                    var line = new HashSet<(int, int)>(FactoryMap.EntryLine(p));
                    inputs.UnionWith(line);
                    (p.Kind == FactoryMap.Kind.Belt ? belts : bodies).UnionWith(Cells(p.Cells).Where(c => !line.Contains(c)));
                }
                foreach (FactoryMap.Piece p in floor.Pieces.Where(p => members.Contains(p.Id)))
                {
                    var line = new HashSet<(int, int)>(FactoryMap.EntryLine(p));
                    bool belt = p.Kind == FactoryMap.Kind.Belt;
                    foreach ((int c, int r) in Cells(p.Cells))
                    {
                        (int, int) to = (c + dc, r + dr);
                        if (to.Item1 < yard.MinC || to.Item1 > yard.MaxC || to.Item2 < yard.MinR || to.Item2 > yard.MaxR)
                        {
                            BuildMoveHelper.Cancel();
                            return Texts.Pt("Fica fora da área da fábrica.", "That is outside the factory floor.");
                        }
                        bool soft = line.Contains((c, r));
                        if (bodies.Contains(to) || (!soft && belts.Contains(to)) || (!soft && !belt && inputs.Contains(to)))
                        {
                            BuildMoveHelper.Cancel();
                            return Texts.Pt("O lugar está ocupado.", "That spot is taken.");
                        }
                    }
                }                var delta = new Vector3(dc * FromScratch.CellX, 0f, dr * FromScratch.CellZ);
                var placements = move.Members.Select(m => new BuildMovePlacement
                {
                    Id = m.Data.UniqueId, Position = m.OriginalPosition + delta, VariationId = m.VariationId, RotationIndex = m.RotationIndex,
                }).ToList();
                if (!BuildMoveHelper.TryPlace(placements, null, restoreOriginalLinks: false))
                {
                    BuildMoveHelper.Cancel();
                    return Texts.Pt("O jogo recusou o lugar novo.", "The game refused the new spot.");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Floor: moving {view.Data.id} failed, putting it back: {e}");
                BuildMoveHelper.Cancel();
                return Texts.Pt("Não deu para mover (veja o log).", "Couldn't move it (see the log).");
            }
            int kept = Sides.Reapply(parked);
            Plugin.Debug($"Floor: moved {view.Data.id} by {dc}, {dr} cells; {kept} chest side(s) set again");
            return null;
        }

        private static IEnumerable<(int, int)> Cells(CellRect rect)
        {
            for (int c = rect.MinC; c <= rect.MaxC; c++)
            {
                for (int r = rect.MinR; r <= rect.MaxR; r++)
                {
                    yield return (c, r);
                }
            }
        }

        internal enum Removal { Refused, Gone, Marked, Unmarked }

        internal static bool CanRemove(SGuid id)
        {
            Wgo view = GameScene.GetWgoViewGlobal(id);
            return view != null && view.Data != null && view.IsBuildRemovable();
        }

        /// <summary>The game's own removal. A machine is marked (or, marked already, unmarked).</summary>
        internal static Removal Remove(SGuid id)
        {
            Wgo view = GameScene.GetWgoViewGlobal(id);
            if (view == null || view.Data == null || !view.IsBuildRemovable())
            {
                return Removal.Refused;
            }
            bool was = view.Data.CraftComponent != null && view.Data.CraftComponent.IsDestroyingCraftActive;
            string what = view.Data.id;
            if (view.DoBuildRemove())
            {
                Plugin.Debug($"Floor: removed {what}");
                return Removal.Gone;
            }
            bool now = view.Data.CraftComponent != null && view.Data.CraftComponent.IsDestroyingCraftActive;
            Plugin.Debug($"Floor: {what} demolition {(now ? "marked" : was ? "unmarked" : "refused")}");
            return now ? Removal.Marked : was ? Removal.Unmarked : Removal.Refused;
        }
    }
}
