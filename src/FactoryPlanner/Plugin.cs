using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace GK2Mods.FactoryPlanner
{
    [BepInPlugin(Guid, "Planejamento da Fábrica", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "gk2mods.factoryplanner";
        public const string Version = "0.3.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        internal static ConfigEntry<string> UnlockTech;
        internal static ConfigEntry<string> DraftingTableCost;
        internal static ConfigEntry<string> ArchitectTableCost;
        internal static ConfigEntry<int> Suggestions;
        internal static ConfigEntry<bool> ClassicBoard;
        internal static ConfigEntry<string> SelectedBlueprint;
        internal static ConfigEntry<bool> DebugLog;
        internal static ConfigEntry<bool> TestMode;
        internal static ConfigEntry<KeyboardShortcut> ArchitectKey;
        internal static ConfigEntry<KeyboardShortcut> BuildKey;
        internal static ConfigEntry<bool> SupplyEnabled;
        internal static ConfigEntry<int> SupplyPerZombie;
        internal static ConfigEntry<bool> SplitterByDemand;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            UnlockTech = Config.Bind("1 Unlock", "Tech", "tech_conv_box_empty",
                "Technology (id) that unlocks the Drafting Table. Default: Caixote (Building tree), the first factory step towards town orders.");
            DraftingTableCost = Config.Bind("2 Costs", "DraftingTable", "wooden_plank:6,nails_iron:6,clean_paper:4,bronze_gear:2",
                "Materials for the Drafting Table (item_id:count, comma separated). Read on game start.");
            ArchitectTableCost = Config.Bind("2 Costs", "ArchitectDesk", "wooden_plank:8,nails_iron:6,clean_paper:6,bronze_gear:2",
                "Materials for the Architect's Desk (item_id:count, comma separated). Read on game start.");
            Suggestions = Config.Bind("3 Planner", "Suggestions", 4,
                new ConfigDescription("How many orders the 'next expansion' section suggests.", new AcceptableValueRange<int>(1, 10)));
            ClassicBoard = Config.Bind("3 Planner", "ClassicWindow", false,
                "Open the Planning Board in the old chest-style window instead of its own window (no quantities nor dispatch there).");
            SelectedBlueprint = Config.Bind("4 Blueprints", "Selected", "",
                "Name of the last blueprint chosen on the Architect's Desk. Blueprint files: BepInEx/config/GK2FactoryPlanner/blueprints.");
            BuildKey = Config.Bind("5 Hotkeys", "BuildDesk", new KeyboardShortcut(UnityEngine.KeyCode.B),
                "Opens the build menu of the zone you stand in (as if you used its build desk), when no window is open. Clear it to turn it off.");
            ArchitectKey = Config.Bind("5 Hotkeys", "ArchitectDesk", new KeyboardShortcut(UnityEngine.KeyCode.B, UnityEngine.KeyCode.LeftShift),
                "Opens the nearest Architect's Desk from anywhere, when no window is open. Clear it to turn it off.");
            SupplyEnabled = Config.Bind("6 Supply", "Enabled", true,
                "After a week is dispatched, the factory's delivery zombies bring what the recipes need from outside (special wood, flax...) " +
                "from the Storage Network into the conveyor chests, once a day.");
            SupplyPerZombie = Config.Bind("6 Supply", "ItemsPerZombie", 120,
                new ConfigDescription("How many items one delivery zombie brings into the factory a day.", new AcceptableValueRange<int>(10, 2000)));
            // On while the mod is new: the log is how in-game problems get diagnosed.
            SplitterByDemand = Config.Bind("7 Splitter", "ByDemand", true,
                "A splitter down an 'on demand' chest side sends each item to the exit whose machines still lack it, instead of taking turns.");
            DebugLog = Config.Bind("9 Debug", "Log", true, "Extra log lines (BepInEx/LogOutput.log).");
            TestMode = Config.Bind("9 Debug", "TestMode", false,
                "Testing only: the Drafting Table needs no technology and costs 1 wooden plank. Use on a spare save slot. Read on game start.");

            Supply.Init();
            Content.TryInjectIntoLoadedBalance();
            int failed = ApplyPatches();
            Log.LogInfo(failed == 0
                ? $"Planejamento da Fábrica {Version} loaded"
                : $"Planejamento da Fábrica {Version} loaded with {failed} patch(es) inactive - see errors above");
        }

        private void Update()
        {
            Hotkeys.Tick();
            Lab.Tick();
            WeekDispatch.Tick();
        }

        /// <summary>Patches class by class, so one broken patch (e.g. after a game update) doesn't disable the rest.</summary>
        private static int ApplyPatches()
        {
            var harmony = new Harmony(Guid);
            int failed = 0;
            foreach (System.Type type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (System.Exception e)
                {
                    failed++;
                    Log.LogError($"Patch {type.Name} failed: {e.InnerException?.Message ?? e.Message}");
                }
            }
            return failed;
        }

        internal static void Debug(string message)
        {
            if (DebugLog.Value)
            {
                Log.LogInfo(message);
            }
        }
    }
}
