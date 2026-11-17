using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GK2Mods.FactoryPlanner.Blueprints;
using GK2Mods.FactoryPlanner.Core;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// "Dispatch the week" on the Planning Board: puts the factory's machines on the week's recipes, with the
    /// quantity of each (Core/Dispatch.cs decides which machine takes what). The raw materials are already in the
    /// factory, in the reserves the zombies keep full; the machines pull them down the belts as they always do.
    /// A switch works like the game's craft window: a machine in the middle of a craft finishes it first.
    /// </summary>
    internal static class WeekDispatch
    {
        /// <summary>Machines still finishing a craft before switching: machine -> its queue of (recipe, crafts).</summary>
        private static readonly Dictionary<SGuid, List<(string recipe, int crafts)>> pending = new Dictionary<SGuid, List<(string recipe, int crafts)>>();
        private static bool loopRunning;

        internal static int PendingCount => pending.Count;

        // A waiting switch is also kept in the save (PlayerData res "gk2fp_pend_<machine>|<n>|<recipe>" = crafts):
        // saving and quitting right after a dispatch used to lose it.
        private const string PendRes = "gk2fp_pend_";
        private static PlayerData loadedFor;

        private static string PendKey(SGuid machine, int index, string recipe) => $"{PendRes}{machine}|{index}|{recipe}";

        private static void SetPending(SGuid machine, List<(string recipe, int crafts)> list)
        {
            DropPending(machine);
            pending[machine] = list;
            for (int i = 0; i < list.Count; i++)
            {
                MainGame.PlayerData.SetRes(PendKey(machine, i, list[i].recipe), Math.Max(1, list[i].crafts));
            }
        }

        private static void DropPending(SGuid machine)
        {
            if (pending.TryGetValue(machine, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    MainGame.PlayerData.SetRes(PendKey(machine, i, list[i].recipe), 0f);
                }
                pending.Remove(machine);
            }
        }

        /// <summary>Every frame: when another save is loaded, picks up the switches it left waiting.</summary>
        internal static void Tick()
        {
            if (MainGame.Instance?.GameSave?.conveyorSystemData == null)
            {
                return;
            }
            PlayerData player = MainGame.PlayerData;
            if (player?.res == null || ReferenceEquals(player, loadedFor))
            {
                return;
            }
            loadedFor = player;
            pending.Clear();
            try
            {
                var found = new Dictionary<string, SortedDictionary<int, (string recipe, int crafts)>>();
                foreach (GameResAtom atom in player.res.List)
                {
                    if (atom?.type == null || atom.value <= 0f || !atom.type.StartsWith(PendRes, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string[] parts = atom.type.Substring(PendRes.Length).Split('|');
                    if (parts.Length == 3 && int.TryParse(parts[1], out int index))
                    {
                        if (!found.TryGetValue(parts[0], out var list))
                        {
                            found[parts[0]] = list = new SortedDictionary<int, (string, int)>();
                        }
                        list[index] = (parts[2], Mathf.RoundToInt(atom.value));
                    }
                }
                foreach (WgoData wgo in Workbenches())
                {
                    if (found.TryGetValue(wgo.UniqueId.ToString(), out var list))
                    {
                        pending[wgo.UniqueId] = list.Values.ToList();
                    }
                }
                if (pending.Count > 0)
                {
                    Plugin.Debug($"Dispatch: {pending.Count} machine(s) still waiting to switch recipe in this save");
                    if (!loopRunning)
                    {
                        Plugin.Instance.StartCoroutine(PendingLoop());
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Dispatch: could not read the waiting switches: {e}");
            }
        }

        // ---------------------------------------------------------------- reading the factory

        internal sealed class Snapshot
        {
            public RecipeBook Book;
            public FactoryState Factory;
            public List<Order> Orders;
            public List<Order> Planned;
            public PlanOptions Options;
            /// <summary>The week with the player's quantities.</summary>
            public LineResult Week;
            public StagePlan Stages;
            public DispatchPlan Plan;
            public Dictionary<string, WgoData> MachineByKey = new Dictionary<string, WgoData>();
            /// <summary>Raw material -> what the factory's reserves and chests hold now.</summary>
            public Dictionary<string, int> Reserves = new Dictionary<string, int>();
            /// <summary>The factory's belts, to tell what reaches each machine.</summary>
            public FeedGraph Feed;

            public IEnumerable<Assignment> For(string recipeId, bool own = false) =>
                Plan.Assignments.Where(a => a.Recipe.Id == recipeId && a.Own == own);

            public Unplaced MissingFor(string recipeId, bool own = false) =>
                Plan.Missing.FirstOrDefault(m => m.Recipe.Id == recipeId && m.Own == own);
        }

        internal static Snapshot Read()
        {
            var snap = new Snapshot { Book = GameData.Recipes(), Factory = GameData.Factory(), Orders = GameData.Orders() };
            HashSet<string> plan = GameData.LoadPlan(snap.Orders);
            snap.Planned = snap.Orders.Where(o => plan.Contains(o.Id)).ToList();
            snap.Options = new PlanOptions
            {
                Factory = snap.Factory,
                External = GameData.LoadExternal(snap.Book),
                Quantities = GameData.LoadQuantities(snap.Book),
                Own = GameData.LoadOwn(snap.Book),
            };
            snap.Week = Planner.ForOrders(snap.Planned, snap.Book, snap.Options);
            float week = Math.Max(1f, GameData.WeekSeconds());
            snap.Stages = Stager.Build(snap.Week, snap.Book, snap.Planned.Select(o => o.BoxItem),
                (r, crafts) => crafts * r.Duration * GameData.TickSeconds(r.Station) / week,
                item => Clusters.MainRaw(new[] { item }, snap.Book, snap.Factory));

            var infos = new List<MachineInfo>();
            foreach (WgoData wgo in Workbenches())
            {
                CraftComponent cc = wgo.CraftComponent;
                string key = wgo.UniqueId.ToString();
                List<string> lined = QueueOf(wgo);
                var info = new MachineInfo { Key = key, Station = wgo.id, Current = lined.FirstOrDefault(), Queued = lined.Skip(1).ToList() };
                foreach (CraftDefBase def in cc.CraftsIn ?? new List<CraftDefBase>())
                {
                    if (def != null && snap.Book.ById.TryGetValue(def.id, out Recipe r) && r.Unlocked && cc.IsCraftAllowedByAttachedExtensions(def))
                    {
                        info.CanRun.Add(def.id);
                    }
                }
                infos.Add(info);
                snap.MachineByKey[key] = wgo;
            }
            try
            {
                snap.Feed = FeedReader.Read(snap.Book);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Feed: could not read the belts, dispatching without checking them: {e}");
                snap.Feed = null;
            }
            snap.Plan = Dispatcher.Plan(snap.Week, snap.Book, infos, snap.Feed);
            snap.Reserves = Reserves(snap.Week.Inputs.Keys);
            return snap;
        }

        private static IEnumerable<WgoData> Workbenches()
        {
            ConveyorSystemData system = MainGame.Instance?.GameSave?.conveyorSystemData;
            if (system?.conveyorComponents == null)
            {
                yield break;
            }
            foreach (ConveyorComponent component in system.conveyorComponents)
            {
                WgoData wgo = component?.WgoData;
                if (wgo != null && !wgo.isTempObject && wgo.CraftComponent != null && wgo.Definition?.conveyorType == ConveyorElementType.Workbench)
                {
                    yield return wgo;
                }
            }
        }

        /// <summary>The recipe a machine is on, counting a switch still waiting for the current craft.</summary>
        private static string RecipeOf(WgoData wgo) => QueueOf(wgo).FirstOrDefault();

        /// <summary>The recipes a machine has lined up, in order.</summary>
        private static List<string> QueueOf(WgoData wgo) =>
            pending.TryGetValue(wgo.UniqueId, out var p) ? p.Select(q => q.recipe).ToList()
                : (wgo.CraftComponent?.CraftElementsQueue.Select(e => e.CraftId) ?? new string[0]).Where(id => id != null).Distinct().ToList();

        /// <summary>
        /// What the factory's raw reserves (the *_container_conveyor stations the zombies fill) and conveyor chests
        /// hold of each item.
        /// </summary>
        private static Dictionary<string, int> Reserves(IEnumerable<string> items)
        {
            var wanted = new HashSet<string>(items);
            var result = wanted.ToDictionary(i => i, _ => 0);
            WorldData world = MainGame.Instance?.GameSave?.WorldData;
            if (world == null || wanted.Count == 0)
            {
                return result;
            }
            foreach (WgoData wgo in world.Cache.wgoDataByUidCache.Values)
            {
                if (wgo?.Inventory?.Data?.Inventory == null || wgo.isTempObject || wgo.id == null)
                {
                    continue;
                }
                bool reserve = wgo.id.IndexOf("_container_conveyor", StringComparison.Ordinal) >= 0;
                bool chest = wgo.Definition?.conveyorType == ConveyorElementType.Chest;
                if (!reserve && !chest)
                {
                    continue;
                }
                foreach (Item item in wgo.Inventory.Data.Inventory)
                {
                    if (item != null && wanted.Contains(item.id))
                    {
                        result[item.id] += item.Count;
                    }
                }
            }
            return result;
        }

        // ---------------------------------------------------------------- dispatching

        internal sealed class Result
        {
            public int Switched;
            public int Waiting;
            public int Counted;
            /// <summary>Recipes put behind another one on the same machine.</summary>
            public int Queued;
            public int Failed;
            public int Missing;
            /// <summary>What the delivery zombies brought (a whole-week dispatch only).</summary>
            public Supply.Result Supply;
        }

        /// <summary>Puts the machines on the week, or only on the given recipes (one stage).</summary>
        internal static Result Dispatch(ICollection<string> only = null)
        {
            Snapshot snap = Read();
            var result = new Result();
            foreach (var group in snap.Plan.Assignments.GroupBy(a => a.Machine))
            {
                // The machine's whole queue goes together, in order; a stage dispatch touches the machines it uses.
                List<Assignment> queue = group.ToList();
                if (only != null && !queue.Any(a => only.Contains(a.Recipe.Id)))
                {
                    continue;
                }
                if (!snap.MachineByKey.TryGetValue(group.Key.Key, out WgoData wgo))
                {
                    result.Failed += queue.Count;
                    continue;
                }
                try
                {
                    bool now = SetQueue(wgo, queue.Select(a => (a.Recipe.Id, a.Crafts)).ToList());
                    if (!now)
                    {
                        result.Waiting++;
                    }
                    else if (queue[0].Change)
                    {
                        result.Switched++;
                    }
                    else
                    {
                        result.Counted++;
                    }
                    result.Queued += queue.Count - 1;
                }
                catch (Exception e)
                {
                    result.Failed++;
                    Plugin.Log.LogError($"Dispatch: {wgo.id} -> {string.Join(", ", queue.Select(a => a.Recipe.Id))} failed: {e}");
                }
            }
            result.Missing = snap.Plan.Missing.Count(m => only == null || only.Contains(m.Recipe.Id));
            foreach (Unplaced m in snap.Plan.Missing.Where(m => only == null || only.Contains(m.Recipe.Id)))
            {
                Plugin.Log.LogInfo(m.NeedsExtension
                    ? $"Dispatch: {m.Recipe.Id}{(m.Own ? " (own)" : "")}: no machine has the extension {m.Recipe.Extension}"
                    : m.NoMachine
                    ? $"Dispatch: {m.Recipe.Id}{(m.Own ? " (own)" : "")} has no free {m.Recipe.Station}"
                    : $"Dispatch: {m.Recipe.Id}{(m.Own ? " (own)" : "")}: no free machine gets {string.Join(", ", m.Lacking)} " +
                      $"(closest {m.Closest?.Station} {m.Closest?.Key})");
            }
            foreach (var (m, lacking) in snap.Plan.Stalled)
            {
                Plugin.Log.LogInfo($"Dispatch: {m.Station} {m.Key} stays on {m.Current}, but its belts don't bring {string.Join(", ", lacking)}");
            }
            foreach (Assignment a in snap.Plan.Assignments.Where(a => (only == null || only.Contains(a.Recipe.Id)) && (a.Lacking.Count > 0 || a.Extra.Count > 0)))
            {
                Plugin.Log.LogInfo($"Dispatch: {a.Machine.Station} {a.Machine.Key} on {a.Recipe.Id}" +
                    (a.Lacking.Count > 0 ? $", its belts don't bring {string.Join(", ", a.Lacking)}" : "") +
                    (a.Extra.Count > 0 ? $", they also bring {string.Join(", ", a.Extra)}" : ""));
            }
            Plugin.Log.LogInfo($"Dispatch{(only != null ? $" ({only.Count} recipes)" : "")}: {result.Switched} switched, " +
                $"{result.Counted} kept with a new count, {result.Waiting} waiting for their craft, {result.Failed} failed, {result.Missing} without a machine");
            if (only == null)
            {
                try
                {
                    result.Supply = Supply.AfterDispatch(snap);
                    Plugin.Log.LogInfo("Dispatch: supplies: " + Supply.Describe(result.Supply));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Dispatch: supplies failed: {e}");
                }
            }
            return result;
        }

        /// <summary>Lab runs: one machine on one recipe.</summary>
        internal static bool LabSetRecipe(WgoData wgo, string recipe, int crafts) => SetQueue(wgo, new List<(string, int)> { (recipe, crafts) });

        /// <summary>
        /// Puts a machine on its recipes for the week, one after the other. True: done now. False: it is in the
        /// middle of a craft of another recipe (removing a started craft would lose its items, so the game doesn't
        /// allow it); that craft ends first, then the queue goes in.
        /// </summary>
        private static bool SetQueue(WgoData wgo, List<(string recipe, int crafts)> list)
        {
            CraftComponent cc = wgo.CraftComponent;
            CraftElementBase head = cc.CurrentCraftElement ?? cc.CraftElementsQueue.FirstOrDefault();
            if (head != null && head.CraftId == list[0].recipe)
            {
                // Already on the first recipe: only its quantity changes (the queue counts what is left to start).
                DropPending(wgo.UniqueId);
                head.IsInfinite = false;
                head.Count = Math.Max(1, list[0].crafts);
                foreach (CraftElementBase other in cc.CraftElementsQueue.Where(e => e != head).ToList())
                {
                    cc.RemoveFromQueue(other);
                }
                Append(wgo, list.Skip(1));
                Plugin.Debug($"Dispatch: {wgo.id} keeps {list[0].recipe}, {list[0].crafts} to go{Then(list)}");
                return true;
            }
            if (head != null && head.IsStarted)
            {
                head.IsInfinite = false;
                if (head.Count > 1)
                {
                    head.Count = 1;
                }
                foreach (CraftElementBase other in cc.CraftElementsQueue.Where(e => e != head).ToList())
                {
                    cc.RemoveFromQueue(other);
                }
                SetPending(wgo.UniqueId, list);
                Plugin.Debug($"Dispatch: {wgo.id} finishes {head.CraftId}, then {list[0].recipe} x{list[0].crafts}{Then(list)}");
                if (!loopRunning)
                {
                    Plugin.Instance.StartCoroutine(PendingLoop());
                }
                return false;
            }
            Replace(wgo, list);
            return true;
        }

        private static string Then(List<(string recipe, int crafts)> list) =>
            string.Concat(list.Skip(1).Select(q => $", then {q.recipe} x{q.crafts}"));

        private static void Append(WgoData wgo, IEnumerable<(string recipe, int crafts)> list)
        {
            foreach ((string recipe, int crafts) in list)
            {
                wgo.CraftComponent.AddToQueue(new ConveyorCraftElement(recipe, Math.Max(1, crafts), new CraftParamsData(recipe, wgo)));
            }
        }

        private static void Replace(WgoData wgo, List<(string recipe, int crafts)> list)
        {
            CraftComponent cc = wgo.CraftComponent;
            foreach (CraftElementBase element in cc.CraftElementsQueue.ToList())
            {
                if (element == cc.CurrentCraftElement)
                {
                    cc.RemoveCurNotStartedCraft();
                }
                else
                {
                    cc.RemoveFromQueue(element);
                }
            }
            Append(wgo, list);
            Plugin.Debug($"Dispatch: {wgo.id} now on {list[0].recipe} x{list[0].crafts}{Then(list)}");
        }

        private static IEnumerator PendingLoop()
        {
            loopRunning = true;
            var wait = new WaitForSecondsRealtime(2f);
            while (pending.Count > 0)
            {
                yield return wait;
                if (MainGame.Instance?.GameSave == null)
                {
                    continue;
                }
                foreach (var kv in pending.ToList())
                {
                    try
                    {
                        WgoData wgo = MainGame.WorldData.GetWgoData(kv.Key);
                        CraftComponent cc = wgo?.CraftComponent;
                        if (cc == null)
                        {
                            DropPending(kv.Key);
                            continue;
                        }
                        CraftElementBase current = cc.CurrentCraftElement;
                        if (current == null || !current.IsStarted)
                        {
                            DropPending(kv.Key);
                            Replace(wgo, kv.Value);
                        }
                    }
                    catch (Exception e)
                    {
                        DropPending(kv.Key);
                        Plugin.Log.LogError($"Dispatch: delayed switch failed: {e}");
                    }
                }
            }
            loopRunning = false;
        }
        internal static string Describe(Result r)
        {
            var parts = new List<string>();
            if (r.Switched > 0)
            {
                parts.Add(Texts.Pt($"{r.Switched} máquina(s) trocada(s)", $"{r.Switched} machine(s) switched"));
            }
            if (r.Counted > 0)
            {
                parts.Add(Texts.Pt($"{r.Counted} com nova quantidade", $"{r.Counted} with a new quantity"));
            }
            if (r.Queued > 0)
            {
                parts.Add(Texts.Pt($"{r.Queued} receita(s) na fila de uma máquina", $"{r.Queued} recipe(s) queued on a machine"));
            }
            if (r.Waiting > 0)
            {
                parts.Add(Texts.Pt($"{r.Waiting} terminando o craft antes", $"{r.Waiting} finishing a craft first"));
            }
            if (r.Missing > 0)
            {
                parts.Add(Texts.Pt($"{r.Missing} receita(s) sem máquina", $"{r.Missing} recipe(s) without a machine"));
            }
            if (r.Failed > 0)
            {
                parts.Add(Texts.Pt($"{r.Failed} falharam (veja o log)", $"{r.Failed} failed (see the log)"));
            }
            return parts.Count == 0 ? Texts.Pt("nada a mudar", "nothing to change") : string.Join(", ", parts);
        }

        internal static void Notify(string key, string text, string icon)
        {
            try
            {
                LazySingleton<UINotificator>.Instance.ShowSimpleTextWithIconNotification(Texts.Dynamic(key, text), icon);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Notification failed ({text}): {e.Message}");
            }
            Plugin.Debug(text);
        }
    }
}
