using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;

namespace GK2Mods.StorageNetwork
{
    internal enum StructureKind
    {
        Archive,
        Relay,
        Connector,
    }

    /// <summary>
    /// The three structures. Each one is a regular chest underneath (it reuses that chest's objects and art),
    /// marked with a CustomTag that is stored in the save. Without the mod they stay ordinary chests.
    /// </summary>
    internal static class Content
    {
        internal const string ArchiveTag = "gk2sn_archive";
        internal const string RelayTag = "gk2sn_relay";
        internal const string ConnectorTag = "gk2sn_connector";

        private class Structure
        {
            public StructureKind Kind;
            public string BuildingId;
            public string Tag;
            public string TemplateBuildingId;
            public Func<string> Cost;
            public string NamePt, NameEn, DescPt, DescEn;
            public string Icon;
        }

        private static readonly Structure[] Structures =
        {
            new Structure
            {
                Kind = StructureKind.Archive, BuildingId = ArchiveBuildingId, Tag = ArchiveTag,
                TemplateBuildingId = "chest_good_place_p", Cost = () => Plugin.ArchiveCost.Value,
                Icon = Icons.CentralStorage,
                NamePt = "Armazenamento Central", NameEn = "Central Storage",
                DescPt = "Coração da Rede de Armazenamento. Os baús desta zona viram o estoque da rede, que as bancadas com Conector usam de qualquer zona. Ao terminar a obra, você aprende a fazer Conectores. Só pode existir um. Não funciona na fábrica. Também é um baú.",
                DescEn = "Heart of the Storage Network. Chests in this zone become the network's stock, which stations with a Connector use from any zone. Finishing it teaches Connectors. Only one can exist. Doesn't work in the factory. Also works as a chest.",
            },
            new Structure
            {
                Kind = StructureKind.Relay, BuildingId = RelayBuildingId, Tag = RelayTag,
                TemplateBuildingId = "chest_place_p", Cost = () => Plugin.RelayCost.Value,
                Icon = Icons.ZoneRelay,
                NamePt = "Relé de Zona", NameEn = "Zone Relay",
                DescPt = "Coloca os baús desta zona no estoque da rede do Armazenamento Central. Sem um Armazenamento Central construído, não faz nada. Não funciona na fábrica: o que ela produz chega à rede pelos carregadores.",
                DescEn = "Adds this zone's chests to the Central Storage network's stock. Does nothing until a Central Storage exists. Doesn't work in the factory: its output reaches the network through porters.",
            },
            new Structure
            {
                Kind = StructureKind.Connector, BuildingId = "gk2sn_connector_p", Tag = ConnectorTag,
                TemplateBuildingId = "chest_rough_place_p", Cost = () => Plugin.ConnectorCost.Value,
                Icon = Icons.Connector,
                NamePt = "Conector", NameEn = "Connector",
                DescPt = "Coloque ao lado de uma bancada, estação ou canteiro, em qualquer zona: eles passam a usar materiais do estoque da rede (baús da zona do Armazenamento Central e das zonas com Relé). Construções nesta zona também podem ser pagas com a rede. Também é uma caixa de envio: o que você guardar nele vai para o Armazenamento Central uma vez por dia. Precisa de um Armazenamento Central construído. Não funciona na fábrica.",
                DescEn = "Place next to a workbench, station or garden bed, in any zone, so it can use materials from the network's stock (chests in the Central Storage's zone and in zones with a Relay). Construction in this zone can be paid from the network too. Also a drop box: whatever you store in it goes to the Central Storage once a day. Needs a finished Central Storage. Doesn't work in the factory.",
            },
        };

        private static readonly List<BuildingDef> injectedDefs = new List<BuildingDef>();

        /// <summary>Recognised by reference in the EvaluateInt patch: returns how many Archives exist.</summary>
        internal static LazyExpression ArchiveLimitExpression;

        internal static bool Injected => injectedDefs.Count > 0;

        internal static IReadOnlyList<BuildingDef> InjectedDefs => injectedDefs;

        internal static string TagForBuilding(string buildingId) =>
            Structures.FirstOrDefault(s => s.BuildingId == buildingId)?.Tag;

        internal static string IconForTag(string tag) =>
            Structures.FirstOrDefault(s => s.Tag == tag)?.Icon;

        internal const string ArchiveBuildingId = "gk2sn_archive_p";
        internal const string UnlockNoticeKey = "gk2sn_unlock_notice";
        internal const string RelayBuildingId = "gk2sn_relay_p";
        // The Relay comes from its own (later) technology, see Plugin.RelayTech.
        private static readonly string[] FollowUpBuildingIds = { "gk2sn_connector_p" };

        /// <summary>Teaches the Connector once a Central Storage is finished. Returns true if anything was new.</summary>
        internal static bool UnlockFollowUps(bool notify)
        {
            KnowledgeSystem knowledge = MainGame.Instance?.GameSave?.knowledgeSystem;
            if (knowledge == null)
            {
                return false;
            }
            bool learned = false;
            foreach (string id in FollowUpBuildingIds)
            {
                if (!knowledge.unlockedBuildings.Contains(id))
                {
                    knowledge.UnlockBuilding(id);
                    learned = true;
                }
            }
            if (learned && notify)
            {
                try
                {
                    LazySingleton<UINotificator>.Instance.ShowSimpleTextWithIconNotification(UnlockNoticeKey, Icons.CentralStorage);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Unlock notice not shown: {e.Message}");
                }
            }
            return learned;
        }

        internal static bool IsOurTag(string tag) =>
            tag == ArchiveTag || tag == RelayTag || tag == ConnectorTag;

        /// <summary>Adds the building defs to the balance lists. Runs before the id caches are built.</summary>
        internal static void InjectDefs(GameBalance gb)
        {
            if (Injected)
            {
                return;
            }
            ArchiveLimitExpression = new LazyExpression("0");
            foreach (Structure s in Structures)
            {
                BuildingDef template = gb.buildingDefs.Find(d => d.id == s.TemplateBuildingId);
                if (template == null)
                {
                    Plugin.Log.LogError($"Template building '{s.TemplateBuildingId}' not found; '{s.BuildingId}' skipped.");
                    continue;
                }
                BuildingDef def = CloneDef(template);
                def.id = s.BuildingId;
                def.isNeedsUnlock = !Plugin.TestMode.Value;
                def.needItems = ParseCost(Plugin.TestMode.Value ? "wooden_plank:1" : s.Cost(), s.BuildingId);
                def.expressionAfterBuilding = new List<LazyExpression>();
                if (s.Icon != null)
                {
                    def.buildResultIcon = s.Icon;
                }
                def.moveMod = BuildingDef.MoveMod.None;
                if (s.Kind == StructureKind.Archive)
                {
                    def.limitMax = 1;
                    def.currentLimitExpression = ArchiveLimitExpression;
                }
                else
                {
                    def.limitMax = 0;
                    def.currentLimitExpression = new LazyExpression();
                }
                gb.buildingDefs.Add(def);
                injectedDefs.Add(def);
            }

            // A tech node shows at most 3 unlock icons, so each structure hangs on a node with room for it:
            // Central Storage on the mid-game tech, Relay on the late one. The Connector is learned by finishing
            // a Central Storage (UnlockFollowUps). Saves that already own a tech get its rewards on load
            // (KnowledgeSystem.RebuildUnlockedTechRewards).
            bool archiveOk = HangOnTech(gb, Plugin.UnlockTech.Value, ArchiveBuildingId);
            bool relayOk = HangOnTech(gb, Plugin.RelayTech.Value, RelayBuildingId);
            Plugin.Log.LogInfo(Plugin.TestMode.Value
                ? $"Registered {injectedDefs.Count} structures in TEST MODE (no tech needed, 1 plank each)."
                : $"Registered {injectedDefs.Count} structures: Central Storage by '{Plugin.UnlockTech.Value}'{(archiveOk ? "" : " (MISSING)")}, " +
                  $"Zone Relay by '{Plugin.RelayTech.Value}'{(relayOk ? "" : " (MISSING)")}.");
        }

        private static bool HangOnTech(GameBalance gb, string techId, string buildingId)
        {
            TechDef tech = gb.techDefs.Find(t => t.id == techId);
            if (tech == null)
            {
                Plugin.Log.LogError($"Unlock tech '{techId}' not found: '{buildingId}' can't be unlocked.");
                return false;
            }
            if (tech.buildingsAfterUnlock.Count + tech.craftsAfterUnlock.Count >= 3)
            {
                Plugin.Log.LogWarning($"Tech '{techId}' already shows 3 unlocks; '{buildingId}' will be unlocked but its icon may not show.");
            }
            if (!tech.buildingsAfterUnlock.Contains(buildingId))
            {
                tech.buildingsAfterUnlock.Add(buildingId);
            }
            return true;
        }

        /// <summary>
        /// Our defs share their wgoId with the template chest, and CreateBuildCache keys several dictionaries by
        /// wgoId (Dictionary.Add would throw). Hide them from it, then add them to the build menu ourselves.
        /// </summary>
        internal static void HideFromBuildCache()
        {
            foreach (BuildingDef def in injectedDefs)
            {
                def.buildingMode = BuildingDef.BuildingMode.None;
                def.moveMod = BuildingDef.MoveMod.None;
            }
        }

        internal static void RestoreAfterBuildCache(GameBalance gb)
        {
            foreach (BuildingDef def in injectedDefs)
            {
                def.buildingMode = BuildingDef.BuildingMode.Place;
            }
            AddToBuildCache(gb);
        }

        /// <summary>Adds the defs to the per-builder lists that the build menu reads.</summary>
        internal static void AddToBuildCache(GameBalance gb)
        {
            foreach (BuildingDef def in injectedDefs)
            {
                foreach (string builder in def.buildsIn)
                {
                    if (!gb.buildDefsInBuilder.TryGetValue(builder, out List<BuildingDef> list))
                    {
                        list = new List<BuildingDef>();
                        gb.buildDefsInBuilder.Add(builder, list);
                    }
                    if (!list.Contains(def))
                    {
                        list.Add(def);
                    }
                }
            }
        }

        /// <summary>
        /// Normally the defs go in through the GameBalance.InitCache prefix. If the balance was already
        /// loaded when the plugin started, add them now and refresh the caches by hand.
        /// </summary>
        internal static void TryInjectIntoLoadedBalance()
        {
            GameBalance gb = GameBalance.instance;
            if (gb == null || !gb.cacheCreated)
            {
                return;
            }
            InjectDefs(gb);
            gb.CreateIDsCache();
            AddToBuildCache(gb);
            Plugin.Log.LogInfo("GameBalance was already loaded; structures added late.");
        }

        internal static void AddTranslations(LLBase lang)
        {
            if (lang == null)
            {
                return;
            }
            bool pt = lang.id != null && lang.id.StartsWith("pt", StringComparison.OrdinalIgnoreCase);
            foreach (Structure s in Structures)
            {
                string name = pt ? s.NamePt : s.NameEn;
                string desc = pt ? s.DescPt : s.DescEn;
                AddText(lang, s.BuildingId, name);
                AddText(lang, s.BuildingId + "_d", desc);
                AddText(lang, s.Tag, name);
                AddText(lang, s.Tag + "_d", desc);
            }
            AddText(lang, UnlockNoticeKey, pt
                ? "Nova construção: Conector"
                : "New building: Connector");
            AddText(lang, Fixtures.RelayInfoKey, pt
                ? "Relé de Zona: coloca os baús desta zona no estoque da rede."
                : "Zone Relay: adds this zone's chests to the network's stock.");
            AddText(lang, Courier.NoticeKey, pt
                ? "O mensageiro da rede levou o conteúdo dos Conectores ao Armazenamento Central"
                : "The network courier took the Connectors' contents to the Central Storage");
            AddText(lang, CentralPanel.HeaderKey, pt ? "Estoque da Rede" : "Network Stock");
            foreach (Categories.Category c in Categories.All)
            {
                AddText(lang, c.Key, pt ? c.Pt : c.En);
            }
        }

        /// <summary>
        /// LLBase.L reads idsToMetaInfo[key] for every key found in dictionary (KeyNotFoundException otherwise,
        /// which breaks whole windows such as the tech tree). Every text needs both entries.
        /// </summary>
        private static void AddText(LLBase lang, string key, string text)
        {
            lang.dictionary[key] = text;
            lang.idsToMetaInfo[key] = new NestedLocalesMetaInfo();
        }

        private static BuildingDef CloneDef(BuildingDef template)
        {
            var clone = (BuildingDef)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(template, null);
            clone.buildsIn = new List<string>(template.buildsIn);
            clone.excludeWgoGroups = new List<string>(template.excludeWgoGroups);
            clone.alwaysBlockedByWgoGroups = new List<string>(template.alwaysBlockedByWgoGroups);
            return clone;
        }

        private static List<NeedItemData> ParseCost(string text, string buildingId)
        {
            var result = new List<NeedItemData>();
            foreach (string part in (text ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = part.Split(':');
                if (pair.Length != 2 || !int.TryParse(pair[1].Trim(), out int count) || count <= 0)
                {
                    Plugin.Log.LogWarning($"{buildingId}: ignoring cost entry '{part}' (expected item_id:count).");
                    continue;
                }
                result.Add(new NeedItemData { id = pair[0].Trim(), count = new LazyExpression(count.ToString()) });
            }
            return result;
        }
    }
}
