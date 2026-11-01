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
        public const string Version = "0.2.3";

        internal static ManualLogSource Log;

        internal static ConfigEntry<string> UnlockTech;
        internal static ConfigEntry<string> RelayTech;
        internal static ConfigEntry<string> ArchiveCost;
        internal static ConfigEntry<string> RelayCost;
        internal static ConfigEntry<string> ConnectorCost;
        internal static ConfigEntry<float> ConnectorRadiusCells;
        internal static ConfigEntry<bool> KeeperBuildsFromNetwork;
        internal static ConfigEntry<string> ExcludedZones;
        internal static ConfigEntry<string> BuildZonesWithoutConnector;
        internal static ConfigEntry<bool> DebugLog;
        internal static ConfigEntry<bool> TestMode;

        private void Awake()
        {
            Log = Logger;

            UnlockTech = Config.Bind("1 Unlock", "Tech", "wooden_joint",
                "Technology (id) that unlocks the Central Storage; finishing one teaches the Connector. Default: Articulação de Madeira (Building tree, mid game).");
            RelayTech = Config.Bind("1 Unlock", "RelayTech", "tech_carved_wood",
                "Technology (id) that unlocks the Zone Relay (linking zones is the late-game step). Default: Escultura em Madeira, the one that also unlocks the Big Chest.");
            ArchiveCost = Config.Bind("2 Costs", "CentralArchive", "wooden_joint:4,oiled_plank:8,bronze_gear:4,nails_iron:8",
                "Materials for the Central Storage (item_id:count, comma separated). Read on game start.");
            RelayCost = Config.Bind("2 Costs", "ZoneRelay", "carved_wood:2,oiled_plank:6,detail_1_iron:4,bronze_gear:2",
                "Materials for a Zone Relay.");
            ConnectorCost = Config.Bind("2 Costs", "Connector", "wooden_plank:4,nails_iron:4,bronze_gear:1",
                "Materials for a Connector.");
            ConnectorRadiusCells = Config.Bind("3 Network", "ConnectorRadiusCells", 4f,
                new ConfigDescription("How far (in build-grid cells, centre to centre) a Connector reaches to link a workstation.",
                    new AcceptableValueRange<float>(1f, 12f)));
            KeeperBuildsFromNetwork = Config.Bind("3 Network", "KeeperBuildsFromNetwork", true,
                "When the Keeper builds in a connected zone, construction costs can be paid from the whole network.");
            ExcludedZones = Config.Bind("3 Network", "ExcludedZones", "conveyor,conveyor_storage",
                "World zones (ids, comma separated) where a Central Storage or Relay links nothing, so the factory keeps its own logistics. " +
                "Its output still reaches the network through the game's porters.");
            BuildZonesWithoutConnector = Config.Bind("3 Network", "BuildZonesWithoutConnector", "town_guard_barracks",
                "World zones (ids, comma separated) whose build menu has no chests, so no Connector can stand there: once a Central Storage " +
                "is finished, the Keeper's construction there is paid from the network anyway (needs KeeperBuildsFromNetwork). Default: the Barracks.");
            DebugLog = Config.Bind("9 Debug", "Log", false, "Extra log lines about the network (BepInEx/LogOutput.log).");
            TestMode = Config.Bind("9 Debug", "TestMode", false,
                "Testing only: the structures need no technology and cost 1 wooden plank each. Use on a spare save slot. Read on game start.");

            Content.TryInjectIntoLoadedBalance();
            Courier.Init();
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
