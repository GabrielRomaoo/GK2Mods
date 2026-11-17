using System;
using System.Collections.Generic;
using HarmonyLib;
using LazyBearTechnology;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// The Planning Board (first called Drafting Table, hence the ids): a plain chest underneath (it reuses the rough chest's objects and art), marked with a
    /// CustomTag that is stored in the save. Without the mod it stays an ordinary chest.
    /// </summary>
    internal static class Content
    {
        internal const string DraftingTag = "gk2fp_drafting_table";
        internal const string DraftingBuildingId = "gk2fp_drafting_table_p";
        private const string TemplateBuildingId = "chest_rough_place_p";

        // Ids keep the first name (Drafting Table) so boards already built stay boards.
        private const string NamePt = "Quadro de Planejamento";
        private const string NameEn = "Planning Board";
        private const string DescPt = "Planeje a fábrica a partir das encomendas da vila. Escolha os pedidos das lojas e o quadro mostra as máquinas, as extensões e os insumos da semana, o que a fábrica já faz e qual pedido sai mais barato acrescentar. Ele só planeja: construir é com você.";
        private const string DescEn = "Plan the factory from the town's orders. Pick the shops' orders and the board shows the machines, extensions and weekly inputs, what the factory already makes and which order is cheapest to add next. It only plans: building is up to you.";

        // The Architect's Desk: a second tagged rough chest, unlocked by a standing Planning Board. Blueprints are
        // traced and laid from it, next to the week's plan.
        internal const string ArchitectTag = "gk2fp_architect_table";
        internal const string ArchitectBuildingId = "gk2fp_architect_table_p";
        private const string ArchitectNamePt = "Mesa de Arquitetura";
        private const string ArchitectNameEn = "Architect's Desk";
        private const string ArchitectDescPt = "O cavalete do arquiteto da fábrica. Faça Decalques de trechos da fábrica que funcionam e ponha as plantas no chão: cada peça vira uma obra normal, paga com o que você e os baús da fábrica têm. A mesa lê o cronograma do Quadro de Planejamento e mostra quais plantas fazem as máquinas novas da semana.";
        private const string ArchitectDescEn = "The factory architect's easel. Trace parts of the factory that work and lay the blueprints on the floor: every piece becomes a normal construction, paid from you and the factory's chests. The desk reads the Planning Board's schedule and shows which blueprints make the week's new machines.";

        // Blueprint tools, started from the Architect's Desk (no build menu of their own). Their pointer is a hidden
        // conveyor chest preview: the mod draws the blueprint around it and never lets the game build the chest itself.
        internal const string BlueprintPlaceId = "gk2fp_blueprint_place_p";
        internal const string BlueprintCopyId = "gk2fp_blueprint_copy_p";
        internal const string FactoryBuilder = "builder_conveyor";
        private const string BlueprintTemplateId = "conveyor_chest_t1_place_p";

        private static BuildingDef injected;
        private static readonly List<BuildingDef> all = new List<BuildingDef>();

        internal static bool Injected => injected != null;

        internal static bool IsOurTag(string tag) => tag == DraftingTag || tag == ArchitectTag;

        /// <summary>The tag a building def stamps on what it builds, or null.</summary>
        internal static string TagForDef(string defId) =>
            defId == DraftingBuildingId ? DraftingTag : defId == ArchitectBuildingId ? ArchitectTag : null;

        internal static string IconForTag(string tag) =>
            tag == ArchitectTag ? Icons.ArchitectTable : Icons.DraftingTable;

        internal static bool IsBlueprintDef(string id) => id == BlueprintPlaceId || id == BlueprintCopyId;

        /// <summary>Adds the building defs to the balance lists. Runs before the id caches are built.</summary>
        internal static void InjectDefs(GameBalance gb)
        {
            if (Injected)
            {
                return;
            }
            BuildingDef template = gb.buildingDefs.Find(d => d.id == TemplateBuildingId);
            if (template == null)
            {
                Plugin.Log.LogError($"Template building '{TemplateBuildingId}' not found; the Planning Board is not available.");
                return;
            }
            BuildingDef def = CloneDef(template);
            def.id = DraftingBuildingId;
            def.isNeedsUnlock = !Plugin.TestMode.Value;
            def.needItems = ParseCost(Plugin.TestMode.Value ? "wooden_plank:1" : Plugin.DraftingTableCost.Value);
            def.expressionAfterBuilding = new List<LazyExpression>();
            def.buildResultIcon = Icons.DraftingTable;
            def.moveMod = BuildingDef.MoveMod.None;
            def.limitMax = 0;
            def.currentLimitExpression = new LazyExpression();
            gb.buildingDefs.Add(def);
            injected = def;
            all.Add(def);

            TechDef tech = gb.techDefs.Find(t => t.id == Plugin.UnlockTech.Value);
            if (tech == null)
            {
                Plugin.Log.LogError($"Unlock tech '{Plugin.UnlockTech.Value}' not found: the Planning Board can't be unlocked.");
            }
            else
            {
                if (tech.buildingsAfterUnlock.Count + tech.craftsAfterUnlock.Count >= 3)
                {
                    Plugin.Log.LogWarning($"Tech '{tech.id}' already shows 3 unlocks; the Planning Board will be unlocked but its icon may not show.");
                }
                if (!tech.buildingsAfterUnlock.Contains(DraftingBuildingId))
                {
                    tech.buildingsAfterUnlock.Add(DraftingBuildingId);
                }
            }
            Plugin.Log.LogInfo(Plugin.TestMode.Value
                ? "Registered the Planning Board in TEST MODE (no tech needed, 1 plank)."
                : $"Registered the Planning Board, unlocked by '{Plugin.UnlockTech.Value}'.");

            // Architect's Desk: built where chests are built, and in the factory itself.
            BuildingDef desk = CloneDef(template);
            desk.id = ArchitectBuildingId;
            if (!desk.buildsIn.Contains(FactoryBuilder))
            {
                desk.buildsIn.Add(FactoryBuilder);
            }
            // Shown only once a Planning Board stands (BuildingDef_GetBuildingsInBuilder), no tech of its own.
            desk.isNeedsUnlock = false;
            desk.needItems = ParseCost(Plugin.TestMode.Value ? "wooden_plank:1" : Plugin.ArchitectTableCost.Value);
            desk.expressionAfterBuilding = new List<LazyExpression>();
            desk.buildResultIcon = Icons.ArchitectTable;
            desk.moveMod = BuildingDef.MoveMod.None;
            desk.limitMax = 0;
            desk.currentLimitExpression = new LazyExpression();
            gb.buildingDefs.Add(desk);
            all.Add(desk);
            Plugin.Log.LogInfo("Registered the Architect's Desk, shown once a Planning Board stands.");


            BuildingDef bpTemplate = gb.buildingDefs.Find(d => d.id == BlueprintTemplateId);
            if (bpTemplate == null)
            {
                Plugin.Log.LogError($"Template building '{BlueprintTemplateId}' not found; blueprints are not available.");
                return;
            }
            foreach (var (id, icon) in new[] { (BlueprintPlaceId, Icons.Blueprint), (BlueprintCopyId, Icons.BlueprintCopy) })
            {
                BuildingDef bp = CloneDef(bpTemplate);
                bp.id = id;
                // In no build menu: the Architect's Desk starts the build mode with them.
                bp.buildsIn = new List<string>();
                bp.isNeedsUnlock = false;
                bp.needItems = new List<NeedItemData>();
                bp.expressionAfterBuilding = new List<LazyExpression>();
                bp.buildResultIcon = icon;
                bp.moveMod = BuildingDef.MoveMod.None;
                bp.limitMax = 0;
                bp.currentLimitExpression = new LazyExpression();
                bp.customGridStep = 1;
                gb.buildingDefs.Add(bp);
                all.Add(bp);
            }
            Plugin.Log.LogInfo("Registered the blueprint tools (started from the Architect's Desk).");
        }

        internal static BuildingDef BlueprintPlaceDef => all.Find(d => d.id == BlueprintPlaceId);

        /// <summary>
        /// Our defs share their wgoId with their templates, and CreateBuildCache keys several dictionaries by
        /// wgoId (Dictionary.Add would throw). Hide them from it, then add them to the build menus ourselves.
        /// </summary>
        internal static void HideFromBuildCache()
        {
            foreach (BuildingDef def in all)
            {
                def.buildingMode = BuildingDef.BuildingMode.None;
                def.moveMod = BuildingDef.MoveMod.None;
            }
        }

        internal static void RestoreAfterBuildCache(GameBalance gb)
        {
            foreach (BuildingDef def in all)
            {
                def.buildingMode = BuildingDef.BuildingMode.Place;
            }
            AddToBuildCache(gb);
        }

        private static void AddToBuildCache(GameBalance gb)
        {
            foreach (BuildingDef def in all)
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

        /// <summary>If the balance was already loaded when the plugin started, add the defs now and refresh the caches.</summary>
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
            Plugin.Log.LogInfo("GameBalance was already loaded; the Planning Board was added late.");
        }

        internal static void AddTranslations(LLBase lang)
        {
            if (lang == null)
            {
                return;
            }
            bool pt = Texts.IsPortuguese(lang);
            string name = pt ? NamePt : NameEn;
            string desc = pt ? DescPt : DescEn;
            Texts.Set(lang, DraftingBuildingId, name);
            Texts.Set(lang, DraftingBuildingId + "_d", desc);
            Texts.Set(lang, DraftingTag, name);
            Texts.Set(lang, DraftingTag + "_d", desc);
            string deskName = pt ? ArchitectNamePt : ArchitectNameEn;
            string deskDesc = pt ? ArchitectDescPt : ArchitectDescEn;
            Texts.Set(lang, ArchitectBuildingId, deskName);
            Texts.Set(lang, ArchitectBuildingId + "_d", deskDesc);
            Texts.Set(lang, ArchitectTag, deskName);
            Texts.Set(lang, ArchitectTag + "_d", deskDesc);
            Blueprints.BlueprintTexts.Apply(lang);
        }

        private static BuildingDef CloneDef(BuildingDef template)
        {
            var clone = (BuildingDef)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(template, null);
            clone.buildsIn = new List<string>(template.buildsIn);
            clone.excludeWgoGroups = new List<string>(template.excludeWgoGroups);
            clone.alwaysBlockedByWgoGroups = new List<string>(template.alwaysBlockedByWgoGroups);
            return clone;
        }

        private static List<NeedItemData> ParseCost(string text)
        {
            var result = new List<NeedItemData>();
            foreach (string part in (text ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = part.Split(':');
                if (pair.Length != 2 || !int.TryParse(pair[1].Trim(), out int count) || count <= 0)
                {
                    Plugin.Log.LogWarning($"Cost: ignoring entry '{part}' (expected item_id:count).");
                    continue;
                }
                result.Add(new NeedItemData { id = pair[0].Trim(), count = new LazyExpression(count.ToString()) });
            }
            return result;
        }
    }

    internal static class Texts
    {
        internal static bool IsPortuguese(LLBase lang) =>
            lang?.id != null && lang.id.StartsWith("pt", StringComparison.OrdinalIgnoreCase);

        internal static bool Portuguese => IsPortuguese(LLBase.currentLang);

        /// <summary>
        /// LLBase.L reads idsToMetaInfo[key] for every key found in dictionary (KeyNotFoundException otherwise,
        /// which breaks whole windows such as the tech tree). Every text needs both entries.
        /// </summary>
        internal static void Set(LLBase lang, string key, string text)
        {
            if (lang == null)
            {
                return;
            }
            lang.dictionary[key] = text;
            lang.idsToMetaInfo[key] = new NestedLocalesMetaInfo();
        }

        /// <summary>Registers (or updates) a runtime text, e.g. a section title that shows live numbers.</summary>
        internal static string Dynamic(string key, string text)
        {
            Set(LLBase.currentLang, key, text);
            return key;
        }

        /// <summary>The game's name for an id, or the id itself when it has none.</summary>
        internal static string Name(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "";
            }
            try
            {
                if (LLBase.HasL(id))
                {
                    return LLBase.L(id);
                }
                int colon = id.IndexOf(':');
                if (colon > 0 && LLBase.HasL(id.Substring(0, colon)))
                {
                    return LLBase.L(id.Substring(0, colon)) + Stars(id.Substring(colon + 1));
                }
            }
            catch (Exception)
            {
                // fall through to the id
            }
            return id;
        }

        private static string Stars(string quality) =>
            int.TryParse(quality, out int q) ? " " + new string('I', Math.Max(1, Math.Min(q, 3))) : "";

        internal static string Pt(string pt, string en) => Portuguese ? pt : en;
    }
}
