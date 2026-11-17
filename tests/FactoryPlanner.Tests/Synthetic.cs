using System;
using System.Collections.Generic;
using System.Linq;
using GK2Mods.FactoryPlanner.Core;

// Checks that need no game data: a made-up factory of one furnace and one bench. Run with no arguments.
internal static class Synthetic
{
    internal static int Run()
    {
        int failures = 0;
        void Expect(string what, int got, int want)
        {
            Console.WriteLine($"{(got == want ? "ok  " : "FAIL")} {what}: {got} (want {want})");
            if (got != want) failures++;
        }

        // ---- a machine's queue: two recipes of the week on one bench
        Recipe R(string id, string station, string outItem, params string[] needs)
        {
            var r = new Recipe { Id = id, Stations = { station }, OutItem = outItem, Duration = 8, Unlocked = true };
            r.Needs.AddRange(needs.Select(n => new Ingredient(n, 1)));
            return r;
        }
        var book = new RecipeBook(new[]
        {
            R("conv_ingot", "furnace", "ingot", "ore"),
            R("conv_box_a", "bench", "box_a", "ingot"),
            R("conv_box_b", "bench", "box_b", "ingot"),
        });
        LineResult week = Planner.Compute(new[] { new KeyValuePair<string, int>("box_a", 10), new KeyValuePair<string, int>("box_b", 10) }, book,
            new PlanOptions { Factory = new FactoryState() });
        MachineInfo M(string key, string station, string current) => new MachineInfo
        {
            Key = key, Station = station, Current = current,
            CanRun = new HashSet<string>(book.ById.Values.Where(r => r.Stations.Contains(station)).Select(r => r.Id)),
        };
        var fleet = new List<MachineInfo> { M("f1", "furnace", "conv_ingot"), M("b1", "bench", "conv_box_a") };
        DispatchPlan plan = Dispatcher.Plan(week, book, fleet);
        Expect("queue: one bench, both boxes placed", plan.Missing.Count, 0);
        Expect("queue: box_b waits behind box_a", plan.Assignments.Count(a => a.Recipe.Id == "conv_box_b" && a.After?.Recipe.Id == "conv_box_a"), 1);
        Expect("queue: queuing box_b is the only change", plan.Assignments.Count(a => a.Change), 1);
        fleet[1].Queued.Add("conv_box_b");
        Expect("queue: already queued, nothing changes", Dispatcher.Plan(week, book, fleet).Assignments.Count(a => a.Change), 0);
        fleet = new List<MachineInfo> { M("f1", "furnace", "conv_ingot"), M("b1", "bench", "conv_box_a"), M("b2", "bench", null) };
        Expect("queue: a free bench is used before any queue", Dispatcher.Plan(week, book, fleet).Assignments.Count(a => a.After != null), 0);
        fleet = new List<MachineInfo> { M("b1", "bench", null) };
        plan = Dispatcher.Plan(week, book, fleet);
        Expect("queue: no furnace, the ingot stays unplaced", plan.Missing.Count(m => m.Recipe.Id == "conv_ingot"), 1);
        Expect("queue: an idle lone bench takes both", plan.Assignments.Count(a => a.Machine.Key == "b1"), 2);

        // ---- a splitter down a demand side: which exit gets the item
        SidePicker.Taker T(string need, int count, int has) => new SidePicker.Taker { Needs = { [need] = count }, Has = { [need] = has } };
        var mech = new[] { T("iron_kit_2", 2, 0) };
        var other = new[] { T("wooden_kit_1", 1, 0) };
        SidePicker.ExitRank lacks = SidePicker.RankExit("iron_kit_2", mech, 0, false);
        SidePicker.ExitRank useless = SidePicker.RankExit("iron_kit_2", other, 0, false);
        Expect("splitter: the kit goes to the machine that lacks it", SidePicker.PickExit(lacks, useless), 0);
        Expect("splitter: same, exits swapped", SidePicker.PickExit(useless, lacks), 1);
        Expect("splitter: both lack it, take turns", SidePicker.PickExit(lacks, SidePicker.RankExit("iron_kit_2", new[] { T("iron_kit_2", 1, 0) }, 0, false)), -1);
        SidePicker.ExitRank served = SidePicker.RankExit("iron_kit_2", new[] { T("iron_kit_2", 2, 2) }, 0, false);
        Expect("splitter: a served machine still beats a useless exit", SidePicker.PickExit(served, useless), 0);
        SidePicker.ExitRank coming = SidePicker.RankExit("iron_kit_2", mech, 2, false);
        SidePicker.ExitRank chest = SidePicker.RankExit("iron_kit_2", new SidePicker.Taker[0], 0, true);
        Expect("splitter: enough on the belt already, the open exit gets it", SidePicker.PickExit(coming, chest), 1);
        Expect("splitter: a lacking machine beats an open exit", SidePicker.PickExit(lacks, chest), 0);
        Expect("splitter: useless both ways, take turns", SidePicker.PickExit(useless, SidePicker.RankExit("iron_kit_2", other, 0, false)), -1);

        Console.WriteLine(failures == 0 ? "\nSynthetic checks passed." : $"\n{failures} synthetic check(s) FAILED.");
        return failures;
    }
}
