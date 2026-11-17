using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GK2Mods.FactoryPlanner.Core;

// recipes.json: { "conv_x": { "st": [...], "ext": "", "needs": [[item, "n"]...], "dur": "8", "out": [[item, "n"]] } }
// orders.txt:   <order id> <box item> <count> <urgent 0/1> <happiness expression...>
// factory.txt:  one running recipe id per line
internal static class Program
{
    private static int failures;

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            // No game data given: only the checks that need none.
            return Synthetic.Run();
        }
        var recipes = new List<Recipe>();
        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(args[0])))
        {
            foreach (JsonProperty p in doc.RootElement.EnumerateObject())
            {
                JsonElement v = p.Value;
                var r = new Recipe
                {
                    Id = p.Name,
                    Stations = v.GetProperty("st").EnumerateArray().Select(s => s.GetString()).ToList(),
                    Extension = string.IsNullOrEmpty(v.GetProperty("ext").GetString()) ? null : v.GetProperty("ext").GetString(),
                    Duration = int.Parse(v.GetProperty("dur").GetString()),
                    Unlocked = true,
                };
                foreach (JsonElement n in v.GetProperty("needs").EnumerateArray())
                {
                    r.Needs.Add(new Ingredient(n[0].GetString(), int.Parse(n[1].GetString())));
                }
                JsonElement o = v.GetProperty("out")[0];
                r.OutItem = o[0].GetString();
                r.OutCount = int.TryParse(o[1].GetString().Split('+')[0], out int c) ? c : 1;
                recipes.Add(r);
            }
        }
        var book = new RecipeBook(recipes);
        var orders = File.ReadAllLines(args[1]).Where(l => l.Trim().Length > 0).Select(l => l.Split(' ')).Select(f => new Order
        {
            Id = f[0], BoxItem = f[1], Count = int.Parse(f[2]), Urgent = f[3] == "1",
            VendorId = f[0].Substring(0, f[0].IndexOf("_box_", StringComparison.Ordinal)),
            Happiness = float.Parse(f[4]), Available = true, Tier = 1,
        }).ToList();
        var factory = new FactoryState();
        if (args.Length > 2)
        {
            factory.RunningRecipes.UnionWith(File.ReadAllLines(args[2]).Select(l => l.Trim()).Where(l => l.Length > 0));
        }
        var options = new PlanOptions { Factory = factory };

        // 1. Ferramentas de Ferro: 10 boxes.
        LineResult tools = Planner.ForOrders(orders.Where(o => o.Id == "bakery_box_tools_iron"), book, options);
        Print("Ferramentas de Ferro x10", tools);
        Expect("tools: 10 box crafts", tools.Crafts["conv_box_tools_iron"], 10);
        Expect("tools: 5 Caixote crafts (2 per craft)", tools.Crafts["conv_box_empty"], 5);
        Expect("tools: 45 iron kits -> 12 crafts", tools.Crafts["conv_iron_kit_1"], 12);
        Expect("tools: 12 ingot crafts", tools.Crafts["conv_ingot_iron"], 12);
        Expect("tools: wood kit from the running hammer recipe", tools.Crafts.ContainsKey("conv_wooden_kit_1") ? 1 : 0, 1);
        Expect("tools: iron ore in", tools.Inputs["iron_ore"], 12);

        // 2. Ingots brought in ready-made.
        var external = new PlanOptions { Factory = factory, External = new HashSet<string> { "ingot_iron" } };
        LineResult toolsExt = Planner.ForOrders(orders.Where(o => o.Id == "bakery_box_tools_iron"), book, external);
        Expect("external ingots: no smelting", toolsExt.Crafts.ContainsKey("conv_ingot_iron") ? 1 : 0, 0);
        Expect("external ingots: 12 ingots in", toolsExt.Inputs["ingot_iron"], 12);

        // 3. Two orders sharing iron kits round once: cutlery (40 kits) + tools (45) = 85 -> 22 crafts.
        LineResult both = Planner.ForOrders(orders.Where(o => o.Id == "bakery_box_tools_iron" || o.Id == "pharmacy_box_cutlery_1"), book, options);
        Expect("shared kits summed before rounding", both.Crafts["conv_iron_kit_1"], 22);

        // 4. Kitchen utensils: tableware + glass containers, both furnace + bellows.
        LineResult utensils = Planner.ForOrders(orders.Where(o => o.Id == "pharmacy_box_kitchen_utensils"), book, options);
        Print("Utensílios x10", utensils);
        Expect("utensils: 40 tableware", utensils.Crafts["conv_tableware"], 40);
        Expect("utensils: sand 80", utensils.Inputs["sand"], 80);

        // 5. Expansion ranking from the current factory.
        var candidates = orders.Where(o => !o.Id.EndsWith("_one_time")).ToList();
        Console.WriteLine("\nPróximas expansões (fábrica atual):");
        foreach (Expansion e in Planner.RankExpansions(candidates, new LineResult(), book, options).Take(12))
        {
            Console.WriteLine($"  {e.Order.Id,-40} novas: {e.NewRecipes.Count} [{string.Join(", ", e.NewRecipes.Select(r => r.Id))}] +{e.Order.Happiness}");
        }

        // 6. Stages: tools + utensils + vegetables. Ingots in stage 1, iron kits 2, Caixote 3, boxes last.
        var mix = orders.Where(o => o.Id == "bakery_box_tools_iron" || o.Id == "pharmacy_box_kitchen_utensils" || o.Id == "vegetable_box_vegetables").ToList();
        LineResult mixLine = Planner.ForOrders(mix, book, options);
        StagePlan stages = Stager.Build(mixLine, book, mix.Select(o => o.BoxItem), (r, crafts) => crafts * r.Duration / 600.0,
            item => item.Contains("iron") ? "iron_ore" : item.Contains("wood") ? "wood" : null);
        Console.WriteLine("\nEtapas:");
        Console.WriteLine("  entrada: " + string.Join(", ", stages.Entry.Select(kv => $"{kv.Key} {kv.Value}")) + $" -> {stages.EntryChests} bau(s)");
        foreach (Stage st in stages.Stages)
        {
            Console.WriteLine($"  etapa {st.Index}{(st.Final ? " (final)" : "")}: " +
                string.Join(", ", st.Steps.Select(x => $"{x.Recipe.Id} x{x.Machines} [{x.Family}]")) + $" -> {stages.ChestsFor(st)} bau(s)");
            Console.WriteLine("     estoque: " + string.Join(", ", st.Output.Select(kv => $"{kv.Key} {kv.Value} -> etapa(s) " +
                (stages.UsedAt.TryGetValue(kv.Key, out var at) ? string.Join("/", at) : "-"))));
        }
        Expect("stages: ingot in stage 1", stages.MadeAt["ingot_iron"], 1);
        Expect("stages: iron kit in stage 2", stages.MadeAt["iron_kit_1"], 2);
        Expect("stages: boxes in the last stage", stages.MadeAt["box_tools_iron"], stages.Stages.Count);
        Expect("stages: vegetable box last too", stages.MadeAt["box_vegetables"], stages.Stages.Count);
        Expect("stages: only the last is final", stages.Stages.Count(x => x.Final), 1);
        Expect("stages: iron kit used later", stages.UsedAt["iron_kit_1"].Min > 2 ? 1 : 0, 1);

        // 7. Layout from scratch of the same plan: everything routed, nothing overlapping.
        foreach (var (title, plan) in new[] { ("tools+utensils+vegetables", stages),
            ("all level-1 orders", Stager.Build(Planner.ForOrders(orders.Where(o => o.Id.EndsWith("_easy")).GroupBy(o => o.BoxItem).Select(g => g.First()).Take(8), book, options),
                book, orders.Where(o => o.Id.EndsWith("_easy")).Select(o => o.BoxItem).Distinct().Take(8), (r, crafts) => crafts * r.Duration / 600.0)) })
        {
            LayoutResult layout = LayoutPlanner.Build(plan, station => station.Contains("kitchen") ? null : MachineShape.Bench());
            Console.WriteLine($"\nLayout {title}: {layout.Width}x{layout.Height} casas, {layout.Machines} máquinas, {layout.Chests} baús, {layout.Belts} esteiras");
            Console.WriteLine(layout.Ascii());
            foreach (var u in layout.Unrouted)
            {
                Console.WriteLine($"  sem esteira: {u.Item} -> {u.To}");
            }
            var cellsUsed = new Dictionary<(int, int), int>();
            foreach (LayoutPiece piece in layout.Pieces)
            {
                IEnumerable<(int, int)> cells = piece.Kind == PieceKind.Machine
                    ? piece.Shape.Body.Select(b => (piece.C + b.C, piece.R + b.R))
                    : new[] { (piece.C, piece.R) };
                foreach (var cell in cells)
                {
                    cellsUsed[cell] = cellsUsed.TryGetValue(cell, out int n) ? n + 1 : 1;
                }
            }
            Expect($"layout {title}: no overlaps", cellsUsed.Count(kv => kv.Value > 1), 0);
            if (title.StartsWith("tools"))
            {
                Expect("layout: every connection routed", layout.Unrouted.Count, 0);
            }
        }

        // 8. Catalog by stage over every recipe, and one module.
        StagePlan catalog = Stager.Catalog(book, factory);
        Console.WriteLine("\nCatálogo:");
        foreach (Stage st in catalog.Stages)
        {
            Console.WriteLine($"  etapa {st.Index}{(st.Final ? " (final)" : "")}: {st.Steps.Count} módulos: " + string.Join(", ", st.Steps.Take(8).Select(x => x.Recipe.OutItem)));
        }
        Expect("catalog: ingot in stage 1", catalog.MadeAt["ingot_iron"], 1);
        Expect("catalog: iron kit in stage 2", catalog.MadeAt["iron_kit_1"], 2);
        Expect("catalog: boxes final", catalog.MadeAt["box_tools_iron"], catalog.Stages.Count);
        LayoutResult module = LayoutPlanner.Module(book.ById["conv_iron_kit_1"], MachineShape.Bench(), final: false);
        Console.WriteLine("Módulo Kit de Ferro I:\n" + module.Ascii());
        Expect("module: 1 machine, 2 chests (ingot in, kit out)", module.Chests, 2);

        // 9. Dispatch: tools for the week on a small factory. The ingot furnace keeps its recipe, the idle bench
        //    takes the Caixote, the bench on stone kits (not needed) is repurposed; nothing makes iron kits.
        LineResult toolsWeek = Planner.ForOrders(orders.Where(o => o.Id == "bakery_box_tools_iron"), book, options);
        MachineInfo M(string key, string station, string current) => new MachineInfo
        {
            Key = key, Station = station, Current = current,
            CanRun = new HashSet<string>(book.ById.Values.Where(r => r.Stations.Contains(station)).Select(r => r.Id)),
        };
        var fleet = new List<MachineInfo>
        {
            M("f1", "conveyor_furnace_t1", "conv_ingot_iron"),
            M("a1", "conveyor_assemblybench_t1", null),
            M("a2", "conveyor_assemblybench_t1", "conv_stone_kit_1"),
            M("a3", "conveyor_assemblybench_t2", null),
        };
        DispatchPlan dispatch = Dispatcher.Plan(toolsWeek, book, fleet);
        Console.WriteLine("\nDisparo:");
        foreach (Assignment a in dispatch.Assignments)
        {
            Console.WriteLine($"  {a.Machine.Key} {a.Machine.Current ?? "(parada)"} -> {a.Recipe.Id} x{a.Crafts}{(a.Change ? "" : " (mantém)")}");
        }
        Console.WriteLine("  sem máquina: " + string.Join(", ", dispatch.Missing.Select(m => m.Recipe.Id)));
        Expect("dispatch: furnace keeps ingots", dispatch.Assignments.Count(a => a.Machine.Key == "f1" && !a.Change), 1);
        Expect("dispatch: every machine used at most once", dispatch.Assignments.GroupBy(a => a.Machine).Count(g => g.Count() > 1), 0);

        // 9b. One bench for two recipes of the week: the second queues behind the first instead of going unplaced.
        var oneBench = new List<MachineInfo> { M("f1", "conveyor_furnace_t1", "conv_ingot_iron"), M("f2", "conveyor_furnace_t1", null), M("a1", "conveyor_assemblybench_t1", null) };
        DispatchPlan queued = Dispatcher.Plan(toolsWeek, book, oneBench);
        Console.WriteLine("\nDisparo com fila:");
        foreach (Assignment a in queued.Assignments)
        {
            Console.WriteLine($"  {a.Machine.Key} -> {a.Recipe.Id} x{a.Crafts}{(a.After != null ? $" (depois de {a.After.Recipe.Id})" : "")}");
        }
        Console.WriteLine("  sem máquina: " + string.Join(", ", queued.Missing.Select(m => m.Recipe.Id)));
        int benchJobs = toolsWeek.Crafts.Count(kv => kv.Value > 0 && book.ById[kv.Key].Stations.Contains("conveyor_assemblybench_t1"));
        Expect("queue: the lone bench takes every bench recipe", queued.Assignments.Count(a => a.Machine.Key == "a1"), benchJobs);
        Expect("queue: the later ones wait behind the first", queued.Assignments.Count(a => a.Machine.Key == "a1" && a.After != null), benchJobs - 1);
        Expect("queue: nothing for the bench is left unplaced", queued.Missing.Count(m => m.Recipe.Stations.Contains("conveyor_assemblybench_t1")), 0);
        oneBench[2].Current = queued.Assignments.First(a => a.Machine.Key == "a1" && a.After == null).Recipe.Id;
        oneBench[2].Queued = queued.Assignments.Where(a => a.Machine.Key == "a1" && a.After != null).Select(a => a.Recipe.Id).ToList();
        Expect("queue: a bench already holding that queue changes nothing", Dispatcher.Plan(toolsWeek, book, oneBench).Assignments.Count(a => a.Machine.Key == "a1" && a.Change), 0);

        // 10. Quantities set by hand: more boxes pull more of everything below; a recipe at 0 asks for nothing.
        int boxes = toolsWeek.Crafts["conv_box_tools_iron"];
        int kits = toolsWeek.Crafts["conv_iron_kit_1"];
        var more = new PlanOptions { Factory = factory, External = options.External, Preferred = options.Preferred };
        more.Quantities["conv_box_tools_iron"] = boxes * 2;
        LineResult doubled = Planner.ForOrders(orders.Where(o => o.Id == "bakery_box_tools_iron"), book, more);
        Console.WriteLine($"\nQuantidade: caixas {boxes} -> {doubled.Crafts["conv_box_tools_iron"]}, kits {kits} -> {doubled.Crafts["conv_iron_kit_1"]}");
        Expect("quantity: boxes doubled", doubled.Crafts["conv_box_tools_iron"], boxes * 2);
        Expect("quantity: the week still asks for the old number", doubled.Needed["conv_box_tools_iron"], boxes);
        Expect("quantity: kits follow the boxes", doubled.Crafts["conv_iron_kit_1"] >= kits * 2 - 1 ? 1 : 0, 1);
        var none = new PlanOptions { Factory = factory, External = options.External, Preferred = options.Preferred };
        none.Quantities["conv_iron_kit_1"] = 0;
        LineResult noKits = Planner.ForOrders(orders.Where(o => o.Id == "bakery_box_tools_iron"), book, none);
        Expect("quantity: kits at 0 ask for no ingots", noKits.Crafts.TryGetValue("conv_ingot_iron", out int ing) ? ing : 0,
            toolsWeek.Crafts["conv_ingot_iron"] - toolsWeek.Crafts["conv_iron_kit_1"] * book.ById["conv_iron_kit_1"].Needs.Where(n => n.Item == "ingot_iron").Sum(n => n.Count));
        DispatchPlan skip = Dispatcher.Plan(noKits, book, fleet);
        Expect("quantity: a recipe at 0 takes no machine", skip.Assignments.Count(a => a.Recipe.Id == "conv_iron_kit_1") + skip.Missing.Count(m => m.Recipe.Id == "conv_iron_kit_1"), 0);

        // 11. Where a new machine goes: free ground between its two sources, the wall in the way counted.
        var query = new PlacementQuery { Area = new CellRect(0, 0, 29, 19), Shape = MachineShape.Bench() };
        for (int r = 0; r <= 14; r++)
        {
            query.Occupied.Add((15, r));  // a wall with a gap at the top
        }
        query.Inputs["urn"] = new List<CellRect> { new CellRect(2, 2, 4, 4) };
        query.Inputs["ingot_iron"] = new List<CellRect> { new CellRect(8, 2, 10, 4) };
        query.Inputs["nobody_makes_it"] = new List<CellRect>();
        query.Outputs.Add(new CellRect(6, 12, 8, 13));
        Placement spot = Placer.Best(query);
        Console.WriteLine(spot == null ? "\nLugar: nenhum" : $"\nLugar: ({spot.C},{spot.R}) caixa {spot.Box.MinC},{spot.Box.MinR}-{spot.Box.MaxC},{spot.Box.MaxR} " +
            string.Join(", ", spot.Belts.Select(kv => $"{kv.Key} {kv.Value}")) + $", saída {spot.OutBelts}, folga {spot.Roomy}");
        Expect("placement: found", spot != null ? 1 : 0, 1);
        if (spot != null)
        {
            Expect("placement: on free ground", query.Occupied.Count(o => o.C >= spot.Box.MinC && o.C <= spot.Box.MaxC && o.R >= spot.Box.MinR && o.R <= spot.Box.MaxR), 0);
            Expect("placement: left of the wall, near both sources", spot.Box.MaxC < 15 ? 1 : 0, 1);
            Expect("placement: short belts", spot.Score <= 14 ? 1 : 0, 1);
            Expect("placement: an ingredient nobody makes is not counted", spot.Belts.ContainsKey("nobody_makes_it") ? 1 : 0, 0);
            Expect("placement: room for belts", spot.Roomy ? 1 : 0, 1);
        }
        var tight = new PlacementQuery { Area = new CellRect(0, 0, 2, 3), Shape = MachineShape.Bench() };
        Placement squeezed = Placer.Best(tight);
        Expect("placement: a tight fit still found", squeezed != null && !squeezed.Roomy ? 1 : 0, 1);
        tight.Occupied.Add((1, 1));
        Expect("placement: no ground, no spot", Placer.Best(tight) == null ? 1 : 0, 1);

        // 12. Dispatch by the belts (the bug of 09/11: machines put on recipes whose ingredients never reach them).
        //     f1 gets ore + coal but is on urns; f2 is idle but only gets clay + coal; f3 gets f1's output.
        var feed = new FeedGraph();
        void Src(string key, string item) => feed.Add(key, FeedKind.Source).Accepts.Add(item);
        void Belt(string key, params string[] parents)
        {
            feed.Add(key, FeedKind.Belt);
            foreach (string p in parents) feed.Link(p, key);
        }
        void Mach(string key, params string[] inputs)
        {
            feed.Add(key, FeedKind.Machine);
            foreach (string p in inputs) feed.Link(p, key);
        }
        Src("ore", "iron_ore"); Src("coal", "coal"); Src("clay", "clay");
        Belt("b1", "ore"); Belt("b2", "coal"); Belt("b3", "clay"); Belt("b4", "coal");
        Mach("f1", "b1", "b2"); Mach("f2", "b3", "b4");
        Belt("b6", "f1"); feed.Add("k", FeedKind.Chest); feed.Link("b6", "k");
        Belt("b7", "k"); Belt("b8", "k");
        Mach("f3", "b7"); Mach("f4", "b8");
        Belt("b9", "f3"); Mach("a1", "b9");
        Belt("b10", "f4"); feed.Add("k2", FeedKind.Chest); feed.Link("b10", "k2");
        string urn = "conv_clay_molds";  // the clay urns (Forja II)
        var smelters = new List<MachineInfo>
        {
            M("f1", "conveyor_furnace_t2", urn), M("f2", "conveyor_furnace_t2", null),
            M("f3", "conveyor_furnace_t2", null), M("f4", "conveyor_furnace_t2", null),
            M("a1", "conveyor_assemblybench_t2", null),
        };
        DispatchPlan old = Dispatcher.Plan(toolsWeek, book, smelters);
        DispatchPlan byBelts = Dispatcher.Plan(toolsWeek, book, smelters, feed);
        Console.WriteLine("\nDisparo pelas esteiras:");
        foreach (Assignment a in byBelts.Assignments)
        {
            Console.WriteLine($"  {a.Machine.Key} -> {a.Recipe.Id} x{a.Crafts}" + (a.Extra.Count > 0 ? $" (chega também {string.Join(",", a.Extra)})" : ""));
        }
        foreach (Unplaced u in byBelts.Missing)
        {
            Console.WriteLine($"  sem máquina: {u.Recipe.Id}" + (u.NoMachine ? "" : $" (falta {string.Join(",", u.Lacking)} em {u.Closest?.Key})"));
        }
        string On(DispatchPlan p, string key) => p.Assignments.FirstOrDefault(a => a.Machine.Key == key)?.Recipe.Id;
        Expect("old dispatch: the idle clay furnace took the ingots (the bug)", On(old, "f2") == "conv_ingot_iron" ? 1 : 0, 1);
        Expect("belts: ingots on the furnace that gets ore and coal", On(byBelts, "f1") == "conv_ingot_iron" ? 1 : 0, 1);
        Expect("belts: the clay furnace gets nothing it can't make", On(byBelts, "f2") == null ? 1 : 0, 1);
        Expect("belts: iron kits where f1's ingots arrive", new[] { "f3", "f4" }.Contains(byBelts.Assignments.FirstOrDefault(a => a.Recipe.Id == "conv_iron_kit_1")?.Machine.Key) ? 1 : 0, 1);
        Expect("belts: every assignment fed", byBelts.Assignments.Count(a => a.Lacking.Count > 0), 0);
        var bareFeed = new FeedGraph();
        bareFeed.Add("f1", FeedKind.Machine);
        DispatchPlan starved = Dispatcher.Plan(toolsWeek, book, new List<MachineInfo> { M("f1", "conveyor_furnace_t2", null) }, bareFeed);
        Unplaced why = starved.Missing.FirstOrDefault(u => u.Recipe.Id == "conv_ingot_iron");
        Expect("belts: a furnace with no input belts is not used", starved.Assignments.Count, 0);
        Expect("belts: and the reason names the ingredients", why != null && !why.NoMachine && why.Lacking.Contains("iron_ore") ? 1 : 0, 1);
        Expect("belts: a chest filter limits what a side gives", Filtered(feed) ? 1 : 0, 1);
        var wheelRecipe = new Recipe { Id = "x_fabric", Stations = { "bench_t1", "bench_t2" }, Extension = "bench_wheel", OutItem = "box_fabric" };
        var wheelWeek = new LineResult();
        wheelWeek.RecipeOrder.Add("x_fabric");
        wheelWeek.Crafts["x_fabric"] = 3;
        DispatchPlan noWheel = Dispatcher.Plan(wheelWeek, new RecipeBook(new[] { wheelRecipe }), new List<MachineInfo> { M("b1", "bench_t2", null) });
        Expect("extension: a bench without the wheel is told apart from no bench", noWheel.Missing.Count(u => u.NoMachine && u.NeedsExtension), 1);
        Expect("supply: an outside ingredient goes to the hand-filled chest, a day's worth", SupplyCase() ? 1 : 0, 1);
        Expect("supply: what the zombies can bring reaches the machine but isn't pushed", Offered() ? 1 : 0, 1);
        Expect("supply: tops up to a day's worth", SupplyPlanner.Amount(20, 5, 100, 120), 15);
        Expect("supply: no more than the network has", SupplyPlanner.Amount(20, 0, 8, 120), 8);
        Expect("supply: no more than the zombies carry", SupplyPlanner.Amount(200, 0, 500, 120), 120);
        Expect("supply: a full chest asks for nothing", SupplyPlanner.Amount(20, 30, 100, 120), 0);
        Expect("sides: lists, the rest and blocked sides in the feed graph", SideRules() ? 1 : 0, 1);
        var inChest = new List<string> { "iron_ore", "coal", "clay" };
        var list = new List<string> { "coal", "clay" };
        Expect("sides: a list gives its first item held", SidePicker.Pick(SideMode.Items, inChest, list, null, null) == "coal" ? 1 : 0, 1);
        Expect("sides: a list takes turns", SidePicker.Pick(SideMode.Items, inChest, list, null, "coal") == "clay" ? 1 : 0, 1);
        Expect("sides: and wraps around", SidePicker.Pick(SideMode.Items, inChest, list, null, "clay") == "coal" ? 1 : 0, 1);
        Expect("sides: a list skips what the chest lacks", SidePicker.Pick(SideMode.Items, new List<string> { "clay" }, list, null, "clay") == "clay" ? 1 : 0, 1);
        Expect("sides: a list with nothing held gives nothing", SidePicker.Pick(SideMode.Items, new List<string> { "iron_ore" }, list, null, null) == null ? 1 : 0, 1);
        Expect("sides: the rest skips what other sides list", SidePicker.Pick(SideMode.Rest, new List<string> { "coal", "iron_ore" }, null, new HashSet<string>(list), null) == "iron_ore" ? 1 : 0, 1);
        Expect("sides: the rest with only listed items gives nothing", SidePicker.Pick(SideMode.Rest, new List<string> { "coal" }, null, new HashSet<string>(list), null) == null ? 1 : 0, 1);
        Expect("sides: blocked gives nothing", SidePicker.Pick(SideMode.Blocked, inChest, list, null, null) == null ? 1 : 0, 1);
        Expect("sides: any gives the first item", SidePicker.Pick(SideMode.Any, inChest, null, null, null) == "iron_ore" ? 1 : 0, 1);
        Expect("sides: picking twice takes the item out", SidePicker.Toggle(SidePicker.Toggle(list, "iron_ore"), "coal").SequenceEqual(new[] { "clay", "iron_ore" }) ? 1 : 0, 1);
        Expect("sides: right click goes any > demand > rest > blocked > any",
            SidePicker.Next(SideMode.Any) == SideMode.Demand && SidePicker.Next(SideMode.Demand) == SideMode.Rest
            && SidePicker.Next(SideMode.Rest) == SideMode.Blocked && SidePicker.Next(SideMode.Blocked) == SideMode.Any ? 1 : 0, 1);
        // A tool-box bench: 1 crate and 4 iron kits a craft, fed by one chest that also holds wood kits.
        var mixed = new List<string> { "wooden_kit_1", "iron_kit_1", "box_empty" };
        SidePicker.Taker Bench(int crates, int kits)
        {
            var t = new SidePicker.Taker();
            t.Needs["box_empty"] = 1;
            t.Needs["iron_kit_1"] = 4;
            t.Has["box_empty"] = crates;
            t.Has["iron_kit_1"] = kits;
            return t;
        }
        var noBelt = new Dictionary<string, int>();
        Expect("demand: the most lacking ingredient first", SidePicker.PickDemand(mixed, new[] { Bench(0, 0) }, noBelt) == "iron_kit_1" ? 1 : 0, 1);
        Expect("demand: what is on the belt counts", SidePicker.PickDemand(mixed, new[] { Bench(0, 0) }, new Dictionary<string, int> { { "iron_kit_1", 4 } }) == "box_empty" ? 1 : 0, 1);
        Expect("demand: nothing more once a craft's worth is on its way",
            SidePicker.PickDemand(mixed, new[] { Bench(0, 2) }, new Dictionary<string, int> { { "iron_kit_1", 2 }, { "box_empty", 1 } }) == null ? 1 : 0, 1);
        Expect("demand: never what the recipe doesn't take", SidePicker.PickDemand(new List<string> { "wooden_kit_1" }, new[] { Bench(0, 0) }, noBelt) == null ? 1 : 0, 1);
        Expect("demand: two benches down a splitter ask for both", SidePicker.PickDemand(mixed, new[] { Bench(1, 4), Bench(1, 3) }, noBelt) == "iron_kit_1" ? 1 : 0, 1);
        Expect("demand: a full bench asks for nothing", SidePicker.PickDemand(mixed, new[] { Bench(1, 4) }, noBelt) == null ? 1 : 0, 1);
        Expect("demand: a demand side doesn't count as blocking the belt", DemandSide() ? 1 : 0, 1);
        var other = new SidePicker.Taker();
        other.Needs["wooden_kit_1"] = 2;
        other.Needs["iron_kit_1"] = 1;
        var shared = new List<SidePicker.Taker> { Bench(0, 0), other };
        SidePicker.KeepCommonNeeds(shared);
        Expect("demand: past a splitter only what both machines take", shared.All(t => t.Needs.Count == 1 && t.Needs.ContainsKey("iron_kit_1")) ? 1 : 0, 1);

        // 12b. The factory as the bad dispatch left it: the clay furnace on ingots, the ore furnace on urns.
        var mess = new List<MachineInfo>
        {
            M("f1", "conveyor_furnace_t2", urn), M("f2", "conveyor_furnace_t2", "conv_ingot_iron"),
            M("f3", "conveyor_furnace_t2", null), M("f4", "conveyor_furnace_t2", null),
            M("a1", "conveyor_assemblybench_t2", null),
        };
        DispatchPlan fixedUp = Dispatcher.Plan(toolsWeek, book, mess, feed);
        Expect("mess: the unfed furnace isn't kept on ingots", fixedUp.Assignments.Count(a => a.Machine.Key == "f2" && a.Recipe.Id == "conv_ingot_iron"), 0);
        Expect("mess: ingots move to the fed furnace", On(fixedUp, "f1") == "conv_ingot_iron" ? 1 : 0, 1);
        Expect("mess: the stalled furnace is reported", fixedUp.Stalled.Count(s => s.Machine.Key == "f2" && s.Lacking.Contains("iron_ore")), 1);

        // 13. Own orders: any product, on a machine of its own, its ingredients counted below.
        var ownOptions = new PlanOptions { Factory = factory };
        ownOptions.Own["wooden_kit_2"] = 9;
        LineResult ownOnly = Planner.Compute(new KeyValuePair<string, int>[0], book, ownOptions);
        Expect("own: 9 wood kits II = 3 crafts", ownOnly.Own.TryGetValue("conv_wooden_kit_2", out int wk) ? wk : 0, 3);
        Expect("own: not a line recipe", ownOnly.Crafts.ContainsKey("conv_wooden_kit_2") ? 1 : 0, 0);
        Expect("own: its raw material counted", ownOnly.Inputs.TryGetValue("wood_2", out int w2) ? w2 : 0, 3);
        var withKits = new PlanOptions { Factory = factory };
        withKits.Own["iron_kit_1"] = 8;
        LineResult toolsOwn = Planner.ForOrders(orders.Where(o => o.Id == "bakery_box_tools_iron"), book, withKits);
        Expect("own + line: the line's kits unchanged", toolsOwn.Crafts["conv_iron_kit_1"], toolsWeek.Crafts["conv_iron_kit_1"]);
        Expect("own + line: 2 own kit crafts", toolsOwn.Own["conv_iron_kit_1"], 2);
        Expect("own + line: ingots for both", toolsOwn.Crafts["conv_ingot_iron"], toolsWeek.Crafts["conv_ingot_iron"] + 2);
        StagePlan ownStages = Stager.Build(toolsOwn, book, new[] { "box_tools_iron" }, (r, c) => 0.1);
        Stage kitStage = ownStages.Stages.FirstOrDefault(s => s.Steps.Any(x => x.Own));
        Expect("own: a step of its own", ownStages.Stages.Sum(s => s.Steps.Count(x => x.Own)), 1);
        Expect("own: in the kits' stage, next to the line's", kitStage != null && kitStage.Steps.Any(x => !x.Own && x.Recipe.Id == "conv_iron_kit_1") ? 1 : 0, 1);
        DispatchPlan ownPlan = Dispatcher.Plan(toolsOwn, book, smelters, feed);
        Console.WriteLine("\nDisparo com avulso:");
        foreach (Assignment a in ownPlan.Assignments)
        {
            Console.WriteLine($"  {a.Machine.Key} -> {a.Recipe.Id} x{a.Crafts}{(a.Own ? " (avulso)" : "")}");
        }
        Expect("own: the line's kits on the furnace that feeds the bench", ownPlan.Assignments.Count(a => !a.Own && a.Recipe.Id == "conv_iron_kit_1" && a.Machine.Key == "f3"), 1);
        Expect("own: the own kits on the dead-end furnace", ownPlan.Assignments.Count(a => a.Own && a.Machine.Key == "f4"), 1);
        smelters[2].Current = "conv_iron_kit_1";
        smelters[3].Current = "conv_iron_kit_1";
        DispatchPlan keptBoth = Dispatcher.Plan(toolsOwn, book, smelters, feed);
        Expect("own: two machines already on kits split by where they lead", keptBoth.Assignments.Count(a => a.Own && a.Machine.Key == "f4" && !a.Change) +
            keptBoth.Assignments.Count(a => !a.Own && a.Machine.Key == "f3" && !a.Change), 2);

        Console.WriteLine(failures == 0 ? "\nOK" : $"\n{failures} FALHA(S)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// A press bench fed by a hand-filled chest (special wood) and by the ore reserve's chest: only the special wood
    /// comes from outside, 12 crafts a week = 2 a day; a bench with no chest on its belts is reported.
    /// </summary>
    private static bool SupplyCase()
    {
        var g = new FeedGraph();
        g.Add("hand", FeedKind.Chest);
        FeedNode fed = g.Add("ore", FeedKind.Chest);
        fed.Holds.Add("iron_ore");
        foreach (string n in new[] { "b1", "b2" })
        {
            g.Add(n, FeedKind.Belt);
        }
        g.Link("hand", "b1");
        g.Link("ore", "b2");
        g.Add("m", FeedKind.Machine);
        g.Link("b1", "m");
        g.Link("b2", "m");
        g.Add("lonely", FeedKind.Machine);
        var kit = new Recipe { Id = "x_kit", Stations = { "bench" }, OutItem = "kit", Needs = { new Ingredient("wood_2", 1), new Ingredient("iron_ore", 1) } };
        var cloth = new Recipe { Id = "x_cloth", Stations = { "bench" }, OutItem = "cloth", Needs = { new Ingredient("flax", 16) } };
        var plan = new DispatchPlan();
        plan.Assignments.Add(new Assignment { Machine = new MachineInfo { Key = "m" }, Recipe = kit, Crafts = 12 });
        plan.Assignments.Add(new Assignment { Machine = new MachineInfo { Key = "lonely" }, Recipe = cloth, Crafts = 3 });
        SupplyPlan supply = SupplyPlanner.Plan(plan, g, new HashSet<string> { "iron_ore" });
        return supply.Lines.Count == 1 && supply.Lines[0].Item == "wood_2" && supply.Lines[0].Chest == "hand" && supply.Lines[0].PerDay == 2
            && supply.NoChest.Count == 1 && supply.NoChest[0].Item == "flax";
    }

    private static bool Offered()
    {
        var g = new FeedGraph();
        FeedNode hand = g.Add("hand", FeedKind.Chest);
        hand.Holds.Add("flax_seed");
        hand.Offered.Add("wood_2");
        g.Add("b", FeedKind.Belt);
        g.Link("hand", "b");
        g.Add("m", FeedKind.Machine);
        g.Link("b", "m");
        FeedGraph.Arrival a = g.Arriving("m", _ => null);
        return a.Items.SetEquals(new[] { "flax_seed", "wood_2" }) && a.Pushed.SetEquals(new[] { "flax_seed" });
    }

    /// <summary>A mixed chest on a demand side: everything can arrive, nothing is pushed.</summary>
    private static bool DemandSide()
    {
        var g = new FeedGraph();
        FeedNode chest = g.Add("c", FeedKind.Chest);
        chest.Holds.UnionWith(new[] { "iron_kit_1", "wooden_kit_1", "box_empty" });
        chest.DemandFor.Add("s1");
        foreach (string n in new[] { "1", "2" })
        {
            g.Add("s" + n, FeedKind.Belt);
            g.Link("c", "s" + n);
            g.Add("m" + n, FeedKind.Machine);
            g.Link("s" + n, "m" + n);
        }
        FeedGraph.Arrival asked = g.Arriving("m1", _ => null);
        FeedGraph.Arrival pushed = g.Arriving("m2", _ => null);
        return asked.Items.Count == 3 && asked.Pushed.Count == 0 && pushed.Items.Count == 3 && pushed.Pushed.Count == 3;
    }

    /// <summary>A chest with ore, coal and clay: one side lists two items, one takes the rest, one is blocked.</summary>
    private static bool SideRules()
    {
        var g = new FeedGraph();
        FeedNode chest = g.Add("c", FeedKind.Chest);
        chest.Holds.UnionWith(new[] { "iron_ore", "coal", "clay" });
        chest.FilterFor["s1"] = new HashSet<string> { "coal", "clay" };
        chest.RestFor.Add("s2");
        chest.FilterFor["s3"] = new HashSet<string>();
        foreach (string n in new[] { "1", "2", "3" })
        {
            g.Add("s" + n, FeedKind.Belt);
            g.Link("c", "s" + n);
            g.Add("m" + n, FeedKind.Machine);
            g.Link("s" + n, "m" + n);
        }
        return g.Arriving("m1", _ => null).Items.SetEquals(new[] { "coal", "clay" })
            && g.Arriving("m2", _ => null).Items.SetEquals(new[] { "iron_ore" })
            && g.Arriving("m3", _ => null).Items.Count == 0;
    }

    /// <summary>A chest holding ore and coal, one side filtered to coal: only coal goes down that side.</summary>
    private static bool Filtered(FeedGraph source)
    {
        var g = new FeedGraph();
        FeedNode chest = g.Add("c", FeedKind.Chest);
        chest.Holds.Add("iron_ore");
        chest.Holds.Add("coal");
        chest.FilterFor["s1"] = new HashSet<string> { "coal" };
        g.Add("s1", FeedKind.Belt);
        g.Link("c", "s1");
        g.Add("s2", FeedKind.Belt);
        g.Link("c", "s2");
        g.Add("m1", FeedKind.Machine);
        g.Link("s1", "m1");
        g.Add("m2", FeedKind.Machine);
        g.Link("s2", "m2");
        HashSet<string> one = g.Arriving("m1", _ => null).Items;
        HashSet<string> two = g.Arriving("m2", _ => null).Items;
        return one.SetEquals(new[] { "coal" }) && two.SetEquals(new[] { "iron_ore", "coal" }) && source.Nodes.Count > 0;
    }

    private static void Print(string title, LineResult r)
    {
        Console.WriteLine($"\n== {title}");
        foreach (string id in r.RecipeOrder)
        {
            Console.WriteLine($"   {id,-34} {r.Crafts[id],4} crafts");
        }
        Console.WriteLine("   entra: " + string.Join(", ", r.Inputs.Select(kv => $"{kv.Key} {kv.Value}")));
    }

    private static void Expect(string what, int got, int want)
    {
        if (got != want)
        {
            failures++;
            Console.WriteLine($"FALHOU {what}: {got} (esperado {want})");
        }
    }
}
