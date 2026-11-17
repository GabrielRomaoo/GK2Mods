using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using GK2Mods.FactoryPlanner.Core;
using LazyBearTechnology;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// Development only: an unattended run of the game on a spare save, to check the mod against the real conveyor
    /// system and take pictures of the factory. It does nothing unless BepInEx/config/GK2FactoryPlanner/lab.json
    /// exists when the game starts. The file is deleted as soon as it is read, so a run never repeats by itself.
    ///
    /// lab.json: { "slot": "Steam_3", "steps": [ { "do": "wait", "seconds": 10 }, { "do": "look", "x": -110, "z": -412 },
    /// { "do": "shot", "name": "before" }, { "do": "dump", "name": "before" },
    /// { "do": "side", "chest": "guid prefix", "belt": "guid prefix", "mode": "Demand" },
    /// { "do": "clear", "belts": ["guid prefix"] }, { "do": "recipe", "machine": "guid prefix", "id": "conv_x", "crafts": 10 },
    /// { "do": "speed", "scale": 4 }, { "do": "dispatch" }, { "do": "quit" } ] }
    /// Output goes to BepInEx/config/GK2FactoryPlanner/lab/. The run only ever loads the slot named in the file.
    /// </summary>
    internal static class Lab
    {
        private static string ConfigFolder => Path.Combine(Paths.ConfigPath, "GK2FactoryPlanner");
        private static string OutFolder => Path.Combine(ConfigFolder, "lab");

        private enum Phase { Off, Menu, Loading, Steps, Done }

        private static Phase phase = Phase.Off;
        private static bool read;
        private static string slotName;
        private static List<JObject> steps;
        private static int at;
        private static float waitUntil;
        private static float phaseSince;
        private static Transform lookTarget;
        private static bool saving;
        private static float pendingSince;
        private static int lastDx;
        private static int lastDz;

        internal static void Tick()
        {
            if (!read)
            {
                read = true;
                Start();
            }
            if (phase == Phase.Off || phase == Phase.Done)
            {
                return;
            }
            try
            {
                Run();
            }
            catch (Exception e)
            {
                Say($"FAILED at step {at}: {e}");
                Finish();
            }
        }

        private static void Start()
        {
            string file = Path.Combine(ConfigFolder, "lab.json");
            if (!File.Exists(file))
            {
                return;
            }
            string text = File.ReadAllText(file);
            File.Delete(file);
            JObject script = JObject.Parse(text);
            slotName = (string)script["slot"];
            steps = ((JArray)script["steps"]).Cast<JObject>().ToList();
            if (string.IsNullOrEmpty(slotName))
            {
                return;
            }
            Directory.CreateDirectory(OutFolder);
            File.WriteAllText(Path.Combine(OutFolder, "log.txt"), "");
            Application.runInBackground = true;
            phase = Phase.Menu;
            phaseSince = Time.realtimeSinceStartup;
            Say($"Lab run on slot {slotName}, {steps.Count} step(s)");
        }

        private static void Say(string line)
        {
            Plugin.Log.LogInfo("Lab: " + line);
            try
            {
                File.AppendAllText(Path.Combine(OutFolder, "log.txt"), $"{Time.realtimeSinceStartup:0.0} {line}\n");
            }
            catch (Exception)
            {
                // the game log has it
            }
        }

        private static void Finish()
        {
            phase = Phase.Done;
            Time.timeScale = 1f;
            File.WriteAllText(Path.Combine(OutFolder, "done.txt"), DateTime.Now.ToString("s"));
            Say("quitting");
            Application.Quit();
        }

        private static void Run()
        {
            float now = Time.realtimeSinceStartup;
            if (phase == Phase.Menu)
            {
                if (now - phaseSince > 300f)
                {
                    throw new TimeoutException("main menu never became ready");
                }
                if (now - phaseSince < 20f || MainGame.Instance == null)
                {
                    return;
                }
                SaveSlotData slot = SaveSystem.SaveSlotDataList?.FirstOrDefault(s => s != null && s.slotName == slotName);
                if (slot == null)
                {
                    throw new InvalidOperationException($"slot {slotName} not found");
                }
                Say($"loading {slot.slotName} (day {slot.day})");
                phase = Phase.Loading;
                phaseSince = now;
                UILoadingOverlay overlay = LazyUI.Get<UILoadingOverlay>();
                overlay.Draw(new LoadingWindowData(MainGame.EntrySceneToLoad, () =>
                {
                    SaveSystem.Load(slot, save =>
                    {
                        if (save == null)
                        {
                            Say("the save did not load");
                            Finish();
                            return;
                        }
                        MainGame.Instance.ContinueGame(slot, save);
                        LazyUI.GetWindow<UIMainMenuWindow>().Close();
                    });
                }));
                return;
            }
            if (phase == Phase.Loading)
            {
                if (now - phaseSince > 600f)
                {
                    throw new TimeoutException("the save never finished loading");
                }
                bool ready = MainGame.Instance?.GameSave?.conveyorSystemData != null && MainGame.PlayerController != null
                    && !LazyUI.Get<UILoadingOverlay>().IsShown && MainGame.PlayerController.IsControlsEnabled;
                if (!ready)
                {
                    waitUntil = 0f;
                    return;
                }
                if (waitUntil == 0f)
                {
                    waitUntil = now + 8f;
                    return;
                }
                if (now < waitUntil)
                {
                    return;
                }
                Say($"loaded; player at {MainGame.PlayerController.transform.position}");
                phase = Phase.Steps;
                at = 0;
                waitUntil = 0f;
                return;
            }
            if (now < waitUntil)
            {
                return;
            }
            if (at >= steps.Count)
            {
                Finish();
                return;
            }
            JObject step = steps[at++];
            string what = (string)step["do"];
            if (what != "waitPending" || pendingSince == 0f)
            {
                Say($"step {at}: {step.ToString(Formatting.None)}");
            }
            switch (what)
            {
                case "wait":
                    waitUntil = now + (float)step["seconds"];
                    break;
                case "speed":
                    Time.timeScale = (float)step["scale"];
                    break;
                case "look":
                    Look(new Vector3((float)step["x"], (float?)step["y"] ?? MainGame.PlayerController.transform.position.y, (float)step["z"]));
                    waitUntil = now + 2f;
                    break;
                case "shot":
                    ScreenCapture.CaptureScreenshot(Path.Combine(OutFolder, (string)step["name"] + ".png"));
                    waitUntil = now + 1.5f;
                    break;
                case "dump":
                    Dump((string)step["name"]);
                    break;
                case "side":
                    SetSide((string)step["chest"], (string)step["belt"], (string)step["mode"]);
                    break;
                case "clear":
                    foreach (string prefix in step["belts"].Select(t => (string)t))
                    {
                        ConveyorWgoData belt = Find(prefix);
                        int n = belt.Inventory.Data.RemoveAllItems().Count;
                        Say($"cleared {n} stack(s) from {belt.id} {prefix}");
                    }
                    break;
                case "returnRoute":
                {
                    ConveyorWgoData chest = Find((string)step["chest"]);
                    ConveyorWgoData from = Find((string)step["belt"]);
                    Say($"put {Demand.ReturnRoute(from, chest.Inventory)} item(s) from the belts back into {chest.id}");
                    break;
                }
                case "waitPending":
                    // Machines finishing a craft before they switch recipe: the switch only lives in memory.
                    if (pendingSince == 0f)
                    {
                        pendingSince = now;
                    }
                    if (WeekDispatch.PendingCount > 0 && now - pendingSince < ((float?)step["max"] ?? 180f))
                    {
                        at--;
                        waitUntil = now + 1f;
                    }
                    else
                    {
                        Say($"pending switches left: {WeekDispatch.PendingCount} after {now - pendingSince:0}s");
                        pendingSince = 0f;
                    }
                    break;
                case "save":
                    saving = true;
                    waitUntil = now + 120f;
                    SaveSystem.Save(MainGame.Instance.SaveSlotData, MainGame.Instance.GameSave,
                        () => { Say($"saved {MainGame.Instance.SaveSlotData?.slotName}"); saving = false; waitUntil = 0f; },
                        () => { Say("SAVE FAILED"); saving = false; waitUntil = 0f; });
                    break;
                case "clearRoute":
                {
                    ConveyorWgoData from = Find((string)step["belt"]);
                    int cleared = Demand.ClearRoute(from);
                    Say($"cleared {cleared} belt(s) down from {from.id}");
                    break;
                }
                case "recipe":
                {
                    ConveyorWgoData machine = Find((string)step["machine"]);
                    bool now2 = WeekDispatch.LabSetRecipe(machine, (string)step["id"], (int?)step["crafts"] ?? 10);
                    Say($"{machine.id} on {(string)step["id"]}: {(now2 ? "switched" : "waiting for its craft")}");
                    break;
                }
                case "plan":
                    GameData.SetInPlan((string)step["order"], (bool?)step["on"] ?? true);
                    break;
                case "own":
                    GameData.SetOwn((string)step["item"], (int)step["amount"]);
                    break;
                case "give":
                    Say($"gave the network {Supply.LabGive((string)step["item"], (int)step["count"])} {(string)step["item"]}");
                    break;
                case "stock":
                    foreach (string id in step["items"].Select(t => (string)t))
                    {
                        Say($"stock {id}: network {Supply.LabStock(id)}");
                    }
                    break;
                case "supply":
                    Say("supply: " + Supply.Describe(Supply.Run(WeekDispatch.Read())));
                    break;
                case "dispatch":
                    Say("dispatch: " + WeekDispatch.Describe(WeekDispatch.Dispatch()));
                    break;
                case "remove":
                    Remove((string)step["piece"]);
                    break;
                case "move":
                    Move((string)step["piece"], (int?)step["dx"] ?? 0, (int?)step["dz"] ?? 0);
                    break;
                case "moveBack":
                    Move((string)step["piece"], -lastDx, -lastDz);
                    break;
                case "board":
                {
                    WgoData board = MainGame.WorldData.GetWgoDataByCustomTag(Content.DraftingTag);
                    string pick = (string)step["pick"];
                    bool opened = board != null && BoardWindow.LabOpen(board, (bool?)step["map"] ?? true, pick != null ? Find(pick).UniqueId : default);
                    Say($"board: {(board == null ? "no Planning Board in this save" : opened ? "open" : "FAILED to open")}");
                    waitUntil = now + 2f;
                    break;
                }
                case "closeBoard":
                    BoardWindow.LabClose();
                    waitUntil = now + 1f;
                    break;
                case "splitters":
                    foreach (ConveyorSplitterComponent s in ConveyorSystem.Data.splitterElements)
                    {
                        int exit = Demand.Prefer(s);
                        var data = (ConveyorWgoData)s.WgoData;
                        Say($"splitter {data.UniqueId.ToString().Substring(0, 8)} at ({data.Position.x:0.00}, {data.Position.z:0.00}) holds [{string.Join(",", Items(data.Inventory).Keys)}] " +
                            $"exits [{string.Join(", ", s.ConnectedWgoData.Select(e => e == null ? "null" : $"{e.id} {e.UniqueId.ToString().Substring(0, 8)} [{string.Join(",", Items(e.Inventory).Keys)}]"))}] prefer {exit}");
                    }
                    break;
                case "quit":
                    Finish();
                    break;
                default:
                    Say($"unknown step '{what}'");
                    break;
            }
        }

        // ---------------------------------------------------------------- finding things

        private static IEnumerable<ConveyorWgoData> Pieces()
        {
            ConveyorSystemData system = MainGame.Instance?.GameSave?.conveyorSystemData;
            foreach (ConveyorComponent c in system?.conveyorComponents ?? new List<ConveyorComponent>())
            {
                if (c?.WgoData is ConveyorWgoData data && !data.isTempObject)
                {
                    yield return data;
                }
            }
        }

        private static ConveyorWgoData Find(string prefix)
        {
            List<ConveyorWgoData> found = Pieces().Where(p => p.UniqueId.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count != 1)
            {
                throw new InvalidOperationException($"'{prefix}' matches {found.Count} piece(s)");
            }
            return found[0];
        }

        private static void SetSide(string chestPrefix, string beltPrefix, string modeName)
        {
            ConveyorWgoData chest = Find(chestPrefix);
            List<ConveyorChestSlotData> slots = chest.ConveyorComponent is ConveyorChestComponent c ? c.SlotsData
                : chest.ConveyorComponent is ConveyorChestOutComponent o ? o.SlotsData : null;
            ConveyorChestSlotData slot = slots?.FirstOrDefault(s => !SGuid.IsNullOrEmpty(s.conveyorWgoDataUniqueId)
                && s.conveyorWgoDataUniqueId.ToString().StartsWith(beltPrefix, StringComparison.OrdinalIgnoreCase));
            if (slot == null)
            {
                throw new InvalidOperationException($"chest {chestPrefix} has no side on belt {beltPrefix}");
            }
            var mode = (SideMode)Enum.Parse(typeof(SideMode), modeName, true);
            Sides.LabSet(slot, mode);
            Say($"{chest.id} side {slot.slotPosDirection} -> {Sides.ModeOf(slot, out _)}");
        }

        // ---------------------------------------------------------------- removing and moving

        private static string Describe(ConveyorWgoData p)
        {
            ConveyorComponent c = p.ConveyorComponent;
            string recipe = p.CraftComponent?.CraftElementsQueue.FirstOrDefault()?.CraftId;
            int ins = c is ConveyorWorkbenchComponent bench ? (bench.connectedInWgoDataUniqueId?.Count ?? 0) : c.ParentsData.Count;
            return $"{p.id} at ({p.Position.x:0.00}, {p.Position.z:0.00}) rot {p.MainWgoPartData?.rotationIndex} recipe {recipe ?? "-"} "
                + $"worker {(p.Worker != null ? "yes" : "no")} inputs {ins} holds {Items(p.Inventory).Values.Sum()}";
        }

        // The game's own removal: instant pieces drop their cost, machines get a demolition craft someone has to do.
        private static void Remove(string prefix)
        {
            ConveyorWgoData piece = Find(prefix);
            Wgo view = GameScene.GetWgoViewGlobal(piece.UniqueId);
            if (view == null)
            {
                Say($"remove {prefix}: no view on the scene");
                return;
            }
            int before = Pieces().Count();
            Say($"remove {prefix}: {Describe(piece)}; removable {view.IsBuildRemovable()}");
            bool gone = view.DoBuildRemove();
            bool marked = piece.CraftComponent != null && piece.CraftComponent.IsDestroyingCraftActive;
            Say($"remove {prefix}: {(gone ? "removed at once" : marked ? "marked for demolition (needs work)" : "refused")}; pieces {before} -> {Pieces().Count()}");
        }

        // The game's own move (lift, relocate, reconnect by overlap). No free-ground check here: lab only.
        private static void Move(string prefix, int dx, int dz)
        {
            ConveyorWgoData piece = Find(prefix);
            if (dx == 0 && dz == 0)
            {
                // The nearest spot where the piece's own cells, with a cell of room around, are free.
                FactoryMap floor = FactoryMap.Read(GameData.Recipes(), default);
                CellRect cells = floor.Pieces.First(p => p.Id.Equals(piece.UniqueId)).Cells;
                bool Free(int ox, int oz)
                {
                    for (int c = cells.MinC - 1; c <= cells.MaxC + 1; c++)
                    {
                        for (int r = cells.MinR - 1; r <= cells.MaxR + 1; r++)
                        {
                            if (floor.Occupied.Contains((c + ox, r + oz)) || c + ox < floor.Area.MinC || c + ox > floor.Area.MaxC || r + oz < floor.Area.MinR || r + oz > floor.Area.MaxR)
                            {
                                return false;
                            }
                        }
                    }
                    return true;
                }
                var spot = Enumerable.Range(-14, 29).SelectMany(x => Enumerable.Range(-14, 29).Select(z => (x, z)))
                    .Where(o => (Math.Abs(o.x) > cells.Width + 1 || Math.Abs(o.z) > cells.Height + 1) && Free(o.x, o.z))
                    .OrderBy(o => Math.Abs(o.x) + Math.Abs(o.z)).FirstOrDefault();
                if (spot.x == 0 && spot.z == 0)
                {
                    Say($"move {prefix}: no free spot within 14 cells");
                    return;
                }
                (dx, dz) = spot;
                Say($"move {prefix}: free spot {dx}, {dz} cells away (piece covers {cells.Width}x{cells.Height})");
            }
            lastDx = dx;
            lastDz = dz;
            Wgo view = GameScene.GetWgoViewGlobal(piece.UniqueId);
            if (view == null)
            {
                Say($"move {prefix}: no view on the scene");
                return;
            }
            Say($"move {prefix}: before {Describe(piece)}; movable {view.IsBuildMovable()}");
            string problem = BoardWindow.LabMove(piece.UniqueId, dx, dz);
            Say($"move {prefix}: {problem ?? "placed"}; after {Describe(piece)}");
        }
        // ---------------------------------------------------------------- pictures

        private static void Look(Vector3 point)
        {
            if (lookTarget == null)
            {
                lookTarget = new GameObject("gk2fp_lab_look").transform;
            }
            lookTarget.position = point;
            CameraController.SetFollowTargetInstant(lookTarget);
        }

        // ---------------------------------------------------------------- state

        private static Dictionary<string, int> Items(Inventory inventory)
        {
            var result = new Dictionary<string, int>();
            foreach (Item item in inventory?.Data?.Inventory ?? new List<Item>())
            {
                if (item != null && !string.IsNullOrEmpty(item.id) && item.Count > 0)
                {
                    result[item.id] = (result.TryGetValue(item.id, out int n) ? n : 0) + item.Count;
                }
            }
            return result;
        }

        private static void Dump(string name)
        {
            RecipeBook book = GameData.Recipes();
            FeedGraph feed = FeedReader.Read(book);
            var machines = new JArray();
            var chests = new JArray();
            var onBelts = new Dictionary<string, int>();
            int belts = 0;
            foreach (ConveyorWgoData p in Pieces())
            {
                ConveyorComponent c = p.ConveyorComponent;
                string key = p.UniqueId.ToString();
                if (c is ConveyorWorkbenchComponent bench)
                {
                    CraftComponent craft = p.CraftComponent;
                    string recipe = craft?.CraftElementsQueue.FirstOrDefault()?.CraftId;
                    FeedGraph.Arrival arrival = feed.Arriving(key, k => null);
                    machines.Add(new JObject
                    {
                        ["key"] = key, ["id"] = p.id, ["x"] = p.Position.x, ["z"] = p.Position.z,
                        ["recipe"] = recipe, ["status"] = craft?.Status.ToString(), ["started"] = craft?.IsStarted,
                        ["queue"] = new JArray(craft?.CraftElementsQueue.Select(e => $"{e.CraftId} x{e.Count}{(e.IsInfinite ? " inf" : "")}") ?? new string[0]),
                        ["holds"] = JObject.FromObject(Items(p.CraftableObjectCraftInventory)),
                        ["inputBelts"] = new JArray((bench.connectedInWgoDataUniqueId ?? new List<SGuid>()).Select(g =>
                        {
                            var belt = MainGame.WorldData.GetWgoData(g) as ConveyorWgoData;
                            return (JToken)new JObject { ["belt"] = g.ToString(), ["items"] = JObject.FromObject(Items(belt?.Inventory)) };
                        })),
                        ["arrives"] = new JArray(arrival.Items.OrderBy(i => i)),
                        ["pushed"] = new JArray(arrival.Pushed.OrderBy(i => i)),
                    });
                }
                else if (c is ConveyorChestComponent || c is ConveyorChestOutComponent)
                {
                    List<ConveyorChestSlotData> slots = c is ConveyorChestComponent cc ? cc.SlotsData : ((ConveyorChestOutComponent)c).SlotsData;
                    chests.Add(new JObject
                    {
                        ["key"] = key, ["id"] = p.id, ["x"] = p.Position.x, ["z"] = p.Position.z,
                        ["holds"] = JObject.FromObject(Items(p.Inventory)),
                        ["parents"] = new JArray(c.ParentsData.Keys.Select(k => k.ToString())),
                        ["sides"] = new JArray(slots.Where(s => !SGuid.IsNullOrEmpty(s.conveyorWgoDataUniqueId)).Select(s =>
                        {
                            SideMode mode = Sides.ModeOf(s, out List<string> items);
                            return (JToken)new JObject
                            {
                                ["dir"] = s.slotPosDirection.ToString(), ["belt"] = s.conveyorWgoDataUniqueId.ToString(),
                                ["input"] = c.ParentsData.Keys.Any(k => k == s.conveyorWgoDataUniqueId),
                                ["mode"] = mode.ToString(), ["items"] = new JArray(items),
                            };
                        })),
                    });
                }
                else
                {
                    belts++;
                    foreach (var kv in Items(p.Inventory))
                    {
                        onBelts[kv.Key] = (onBelts.TryGetValue(kv.Key, out int n) ? n : 0) + kv.Value;
                    }
                }
            }
            // Every supply box in the world, by zone: the factory floor, its pallets, anywhere else.
            var boxes = new JObject();
            foreach (WgoData wgo in MainGame.Instance.GameSave.WorldData.Cache.wgoDataByUidCache.Values)
            {
                if (wgo == null || wgo.isTempObject)
                {
                    continue;
                }
                foreach (Inventory inv in new[] { wgo.Inventory, wgo is ZombieWgoData ? null : wgo.CraftableObjectCraftInventory })
                {
                    foreach (var kv in Items(inv))
                    {
                        if (!kv.Key.StartsWith("box_", StringComparison.Ordinal) || kv.Key == "box_empty")
                        {
                            continue;
                        }
                        string zone = string.IsNullOrEmpty(wgo.worldZoneDataId) ? "?" : wgo.worldZoneDataId;
                        var z = (JObject)(boxes[zone] ?? (boxes[zone] = new JObject()));
                        z[kv.Key] = ((int?)z[kv.Key] ?? 0) + kv.Value;
                    }
                }
            }
            var orders = new JArray(GameData.Orders().Select(o => (JToken)new JObject { ["id"] = o.Id, ["box"] = o.BoxItem, ["count"] = o.Count, ["urgent"] = o.Urgent }));
            var root = new JObject
            {
                ["boxes"] = boxes, ["orders"] = orders,
                ["time"] = Time.realtimeSinceStartup, ["timeScale"] = Time.timeScale, ["belts"] = belts,
                ["onBelts"] = JObject.FromObject(onBelts), ["machines"] = machines, ["chests"] = chests,
            };
            File.WriteAllText(Path.Combine(OutFolder, name + ".json"), root.ToString(Formatting.Indented));
            Say($"dump {name}: {machines.Count} machines, {chests.Count} chests, {belts} belts");
        }
    }
}
