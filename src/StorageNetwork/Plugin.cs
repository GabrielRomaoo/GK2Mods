using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace GK2Mods.StorageNetwork
{
    [BepInPlugin(Guid, "Rede de Armazenamento", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "gk2mods.storagenetwork";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<string> UnlockTech;
        internal static ConfigEntry<string> ArchiveCost;
        internal static ConfigEntry<string> RelayCost;
        internal static ConfigEntry<string> ConnectorCost;
        internal static ConfigEntry<float> ConnectorRadiusCells;
        internal static ConfigEntry<bool> KeeperBuildsFromNetwork;
        internal static ConfigEntry<bool> DebugLog;
        internal static ConfigEntry<bool> TestMode;

        private void Awake()
        {
            Log = Logger;

            UnlockTech = Config.Bind("1 Unlock", "Tech", "tech_carved_wood",
                "Technology (id) that unlocks the three structures. Default: Escultura em Madeira (Building tree), the one that also unlocks the Big Chest.");
            ArchiveCost = Config.Bind("2 Costs", "CentralArchive", "carved_wood:8,oiled_beam:4,detail_2_iron:4,bronze_gear:4",
                "Materials for the Central Storage (item_id:count, comma separated). Read on game start.");
            RelayCost = Config.Bind("2 Costs", "ZoneRelay", "oiled_plank:6,wooden_beam:2,detail_1_iron:4,bronze_gear:2",
                "Materials for a Zone Relay.");
            ConnectorCost = Config.Bind("2 Costs", "Connector", "wooden_plank:4,nails_iron:4,bronze_gear:1",
                "Materials for a Connector.");
            ConnectorRadiusCells = Config.Bind("3 Network", "ConnectorRadiusCells", 4f,
                new ConfigDescription("How far (in build-grid cells, centre to centre) a Connector reaches to link a workstation.",
                    new AcceptableValueRange<float>(1f, 12f)));
            KeeperBuildsFromNetwork = Config.Bind("3 Network", "KeeperBuildsFromNetwork", true,
                "When the Keeper builds in a connected zone, construction costs can be paid from the whole network.");
            DebugLog = Config.Bind("9 Debug", "Log", false, "Extra log lines about the network (BepInEx/LogOutput.log).");
            TestMode = Config.Bind("9 Debug", "TestMode", false,
                "Testing only: the structures need no technology and cost 1 wooden plank each. Use on a spare save slot. Read on game start.");

            Content.TryInjectIntoLoadedBalance();
            int failed = ApplyPatches();
            Log.LogInfo(failed == 0
                ? $"Rede de Armazenamento {Version} loaded"
                : $"Rede de Armazenamento {Version} loaded with {failed} patch(es) inactive - see errors above");
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
