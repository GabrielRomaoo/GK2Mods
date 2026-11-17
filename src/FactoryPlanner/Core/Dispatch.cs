using System;
using System.Collections.Generic;
using System.Linq;

namespace GK2Mods.FactoryPlanner.Core
{
    /// <summary>A machine standing in the factory, as the dispatcher sees it.</summary>
    public sealed class MachineInfo
    {
        /// <summary>Anything that identifies it to the caller (the game's unique id); also its key in the FeedGraph.</summary>
        public string Key;
        public string Station;
        /// <summary>Recipe it is set to now, or null when idle.</summary>
        public string Current;
        /// <summary>Recipes waiting in its queue behind the current one, in order.</summary>
        public List<string> Queued = new List<string>();
        /// <summary>Recipes it can run (station and attached add-ons allow them).</summary>
        public HashSet<string> CanRun = new HashSet<string>();
    }

    public sealed class Assignment
    {
        public MachineInfo Machine;
        public Recipe Recipe;
        /// <summary>Crafts this machine makes for the week.</summary>
        public int Crafts;
        /// <summary>The machine of a player's own order (its output stays in a chest).</summary>
        public bool Own;
        /// <summary>Ingredients no belt brings to this machine (only for a machine kept on its recipe).</summary>
        public List<string> Lacking = new List<string>();
        /// <summary>Items its belts bring that the recipe doesn't take: they stop at the input and block the belt.</summary>
        public List<string> Extra = new List<string>();
        /// <summary>The assignment this one waits behind on the same machine (null: the machine starts with it).</summary>
        public Assignment After;
        public bool Change => After == null ? Machine.Current != Recipe.Id : !Machine.Queued.Contains(Recipe.Id);
    }

    /// <summary>A recipe the week needs that no machine can take.</summary>
    public sealed class Unplaced
    {
        public Recipe Recipe;
        public int Crafts;
        public bool Own;
        /// <summary>No free machine of the right kind at all (false: there are some, but none gets the ingredients).</summary>
        public bool NoMachine;
        /// <summary>With NoMachine: machines of the right kind stand in the factory, but none has the extension the recipe asks for.</summary>
        public bool NeedsExtension;
        /// <summary>The closest free machine and what its belts don't bring, when NoMachine is false.</summary>
        public MachineInfo Closest;
        public List<string> Lacking = new List<string>();
    }

    public sealed class DispatchPlan
    {
        public List<Assignment> Assignments = new List<Assignment>();
        public List<Unplaced> Missing = new List<Unplaced>();
        /// <summary>Machines on a recipe of the week that their belts can't feed and that got nothing else to do.</summary>
        public List<(MachineInfo Machine, List<string> Lacking)> Stalled = new List<(MachineInfo, List<string>)>();

        public IEnumerable<Assignment> Changes => Assignments.Where(a => a.Change);
        public IEnumerable<Assignment> Kept => Assignments.Where(a => !a.Change);
    }

    /// <summary>
    /// Puts the factory's machines on the week's recipes. Machines already on a recipe the week needs keep it.
    /// The other recipes take a free machine that can run them and whose belts bring every ingredient: idle machines
    /// first, then machines on a recipe the week doesn't need, never one that feeds a machine the plan relies on.
    /// Recipes are handed out from the first stage up, so a machine's ingredients made in the factory are counted
    /// from the recipes its upstream machines get. A player's own order takes a machine of its own, preferably one
    /// whose output doesn't run into another machine.
    /// A recipe left without a machine queues behind another recipe on a machine the plan already uses (the game
    /// runs a machine's queue one recipe after the other), if that machine can run it and gets its ingredients.
    /// Without a FeedGraph the belts aren't checked (offline tests of the old behaviour).
    /// </summary>
    public static class Dispatcher
    {
        private sealed class Job
        {
            public Recipe Recipe;
            public int Crafts;
            public bool Own;
            public int Depth;
        }

        public static DispatchPlan Plan(LineResult week, RecipeBook book, IList<MachineInfo> machines, FeedGraph feed = null)
        {
            var plan = new DispatchPlan();
            var jobs = new List<Job>();
            foreach (string id in week.RecipeOrder)
            {
                if (week.Crafts.TryGetValue(id, out int c) && c > 0 && book.ById.TryGetValue(id, out Recipe r))
                {
                    jobs.Add(new Job { Recipe = r, Crafts = c });
                }
            }
            foreach (var kv in week.Own)
            {
                if (kv.Value > 0 && book.ById.TryGetValue(kv.Key, out Recipe r))
                {
                    jobs.Add(new Job { Recipe = r, Crafts = kv.Value, Own = true });
                }
            }
            var byOutput = new Dictionary<string, Recipe>();
            foreach (Job j in jobs.Where(j => !j.Own))
            {
                byOutput[j.Recipe.OutItem] = j.Recipe;
            }
            var depthMemo = new Dictionary<string, int>();
            int Depth(Recipe r, int guard)
            {
                if (depthMemo.TryGetValue(r.Id, out int d))
                {
                    return d;
                }
                depthMemo[r.Id] = 0;
                int deepest = guard > 24 ? 0 : r.Needs.Select(n => byOutput.TryGetValue(n.Item, out Recipe m) && m != r ? Depth(m, guard + 1) : 0).DefaultIfEmpty(0).Max();
                return depthMemo[r.Id] = deepest + 1;
            }
            foreach (Job j in jobs)
            {
                j.Depth = Depth(j.Recipe, 0);
            }
            // First stage first; within a stage, line before own orders, then the hardest to place.
            jobs = jobs.OrderBy(j => j.Depth).ThenBy(j => j.Own)
                .ThenBy(j => machines.Count(m => m.CanRun.Contains(j.Recipe.Id))).ThenBy(j => j.Recipe.Id, StringComparer.Ordinal).ToList();

            var wanted = new HashSet<string>(jobs.Select(j => j.Recipe.Id));
            var used = new HashSet<MachineInfo>();
            var protectedKeys = new HashSet<string>();
            var product = new Dictionary<string, string>();  // machine key -> item it puts out under the plan
            foreach (MachineInfo m in machines)
            {
                if (m.Current != null && book.ById.TryGetValue(m.Current, out Recipe r))
                {
                    product[m.Key] = r.OutItem;
                }
            }
            string ProductOf(string key) => product.TryGetValue(key, out string item) ? item : null;

            // Items that need the item made by the recipe, in the week (to tell a line machine from an own one).
            var consumers = new HashSet<string>(jobs.Where(j => !j.Own).SelectMany(j => j.Recipe.Needs.Select(n => n.Item)));
            bool FeedsAMachine(MachineInfo m) => feed != null && feed.Downstream(m.Key).Count > 0;

            // Machines already on a wanted recipe stay; split them between the line and an own order of the same
            // recipe by where their output goes. Their suppliers are protected from being switched away.
            var kept = new Dictionary<Job, List<MachineInfo>>();
            // A machine on a wanted recipe whose belts don't bring its ingredients isn't kept: it joins the free
            // machines (it may still win its recipe back once the machines upstream are set).
            var released = new HashSet<MachineInfo>();
            foreach (var group in jobs.GroupBy(j => j.Recipe.Id))
            {
                List<MachineInfo> already = machines.Where(m => m.Current == group.Key).ToList();
                if (feed != null)
                {
                    var needs0 = new HashSet<string>(group.First().Recipe.Needs.Select(n => n.Item));
                    foreach (MachineInfo m in already.ToList())
                    {
                        var probe = new Assignment { Machine = m, Recipe = group.First().Recipe };
                        Check(probe, feed, needs0, ProductOf);
                        if (probe.Lacking.Count > 0)
                        {
                            already.Remove(m);
                            released.Add(m);
                        }
                    }
                }
                if (already.Count == 0)
                {
                    continue;
                }
                Job line = group.FirstOrDefault(j => !j.Own);
                Job own = group.FirstOrDefault(j => j.Own);
                if (line != null && own != null && feed != null)
                {
                    List<MachineInfo> deadEnd = already.Where(m => !FeedsAMachine(m)).ToList();
                    if (deadEnd.Count > 0 && deadEnd.Count < already.Count)
                    {
                        kept[own] = deadEnd;
                        kept[line] = already.Except(deadEnd).ToList();
                    }
                    else
                    {
                        kept[line] = already;
                    }
                }
                else
                {
                    kept[line ?? own] = already;
                }
                used.UnionWith(already);
            }
            // Only the upstream machines that bring a consumer an ingredient it needs are held on their recipe.
            void Protect(MachineInfo m, Recipe r)
            {
                if (feed == null)
                {
                    return;
                }
                FeedGraph.Arrival arrival = feed.Arriving(m.Key, ProductOf);
                foreach (Ingredient need in r.Needs)
                {
                    if (arrival.FromMachines.TryGetValue(need.Item, out HashSet<string> from))
                    {
                        protectedKeys.UnionWith(from);
                    }
                }
            }
            foreach (var kv in kept)
            {
                foreach (MachineInfo m in kv.Value)
                {
                    Protect(m, kv.Key.Recipe);
                }
            }

            // Jobs no machine could take alone, with what to report if no queue takes them either.
            var deferred = new List<(Job Job, Unplaced Report)>();
            foreach (Job job in jobs)
            {
                var needs = new HashSet<string>(job.Recipe.Needs.Select(n => n.Item));
                if (kept.TryGetValue(job, out List<MachineInfo> list))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var a = new Assignment
                        {
                            Machine = list[i], Recipe = job.Recipe, Own = job.Own,
                            Crafts = Math.Max(1, job.Crafts / list.Count + (i < job.Crafts % list.Count ? 1 : 0)),
                        };
                        Check(a, feed, needs, ProductOf);
                        plan.Assignments.Add(a);
                    }
                    continue;
                }

                List<MachineInfo> free = machines
                    .Where(m => !used.Contains(m) && m.CanRun.Contains(job.Recipe.Id) && !protectedKeys.Contains(m.Key) &&
                                (m.Current == null || !wanted.Contains(m.Current) || released.Contains(m)))
                    .ToList();
                if (free.Count == 0)
                {
                    deferred.Add((job, new Unplaced
                    {
                        Recipe = job.Recipe, Crafts = job.Crafts, Own = job.Own, NoMachine = true,
                        NeedsExtension = job.Recipe.Extension != null && machines.Any(m => job.Recipe.Stations.Contains(m.Station))
                            && !machines.Any(m => m.CanRun.Contains(job.Recipe.Id)),
                    }));
                    continue;
                }
                MachineInfo pick = null;
                Assignment best = null;
                int bestScore = int.MaxValue;
                Unplaced closest = null;
                foreach (MachineInfo m in free.OrderBy(m => m.Key, StringComparer.Ordinal))
                {
                    var a = new Assignment { Machine = m, Recipe = job.Recipe, Own = job.Own, Crafts = Math.Max(1, job.Crafts) };
                    Check(a, feed, needs, ProductOf);
                    if (a.Lacking.Count > 0)
                    {
                        if (closest == null || a.Lacking.Count < closest.Lacking.Count)
                        {
                            closest = new Unplaced { Recipe = job.Recipe, Crafts = job.Crafts, Own = job.Own, Closest = m, Lacking = a.Lacking };
                        }
                        continue;
                    }
                    // Lower is better: nothing extra on the belts, the right kind of exit, idle first.
                    int score = a.Extra.Count * 100;
                    if (feed != null)
                    {
                        bool feeds = FeedsAMachine(m);
                        bool lineJob = !job.Own && consumers.Contains(job.Recipe.OutItem);
                        if (job.Own && feeds)
                        {
                            score += 20;
                        }
                        else if (lineJob && !feeds)
                        {
                            score += 20;
                        }
                    }
                    score += m.Current == job.Recipe.Id ? 0 : m.Current == null ? 1 : 5;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        pick = m;
                        best = a;
                    }
                }
                if (pick == null)
                {
                    deferred.Add((job, closest ?? new Unplaced { Recipe = job.Recipe, Crafts = job.Crafts, Own = job.Own, NoMachine = true }));
                    continue;
                }
                used.Add(pick);
                product[pick.Key] = job.Recipe.OutItem;
                Protect(pick, job.Recipe);
                plan.Assignments.Add(best);
            }

            // What is left queues behind the last recipe of a machine in use: one already holding it in its queue
            // first, then the shortest queue with nothing extra on the belts.
            foreach ((Job job, Unplaced report) in deferred)
            {
                var needs = new HashSet<string>(job.Recipe.Needs.Select(n => n.Item));
                Assignment best = null;
                int bestScore = int.MaxValue;
                foreach (MachineInfo m in machines.Where(m => m.CanRun.Contains(job.Recipe.Id)).OrderBy(m => m.Key, StringComparer.Ordinal))
                {
                    List<Assignment> queue = plan.Assignments.Where(a => a.Machine == m).ToList();
                    if (queue.Count == 0 || queue.Any(a => a.Recipe.Id == job.Recipe.Id))
                    {
                        continue;
                    }
                    var a = new Assignment { Machine = m, Recipe = job.Recipe, Own = job.Own, Crafts = Math.Max(1, job.Crafts), After = queue[queue.Count - 1] };
                    Check(a, feed, needs, ProductOf);
                    if (a.Lacking.Count > 0)
                    {
                        continue;
                    }
                    int score = a.Extra.Count * 100 + queue.Count * 10 + (m.Queued.Contains(job.Recipe.Id) ? 0 : 5);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = a;
                    }
                }
                if (best == null)
                {
                    plan.Missing.Add(report);
                }
                else
                {
                    plan.Assignments.Add(best);
                }
            }
            foreach (MachineInfo m in released.Where(m => !used.Contains(m)))
            {
                var probe = new Assignment { Machine = m, Recipe = book.ById[m.Current] };
                Check(probe, feed, new HashSet<string>(probe.Recipe.Needs.Select(n => n.Item)), ProductOf);
                plan.Stalled.Add((m, probe.Lacking));
            }
            return plan;
        }

        /// <summary>Fills in what the machine's belts don't bring and what they bring that the recipe doesn't take.</summary>
        private static void Check(Assignment a, FeedGraph feed, HashSet<string> needs, Func<string, string> productOf)
        {
            if (feed == null || !feed.Nodes.ContainsKey(a.Machine.Key))
            {
                return;
            }
            FeedGraph.Arrival arrival = feed.Arriving(a.Machine.Key, productOf);
            a.Lacking = needs.Where(n => !arrival.Items.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            a.Extra = arrival.Pushed.Where(i => !needs.Contains(i)).OrderBy(i => i, StringComparer.Ordinal).ToList();
        }
    }
}
