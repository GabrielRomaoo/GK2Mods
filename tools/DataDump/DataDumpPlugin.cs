using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using LazyBearTechnology;
using Newtonsoft.Json;
using UnityEngine;

namespace GK2Mods.DataDump
{
    /// <summary>
    /// Dev-only plugin. Writes the game's balance data (buildings, techs, storage objects, items)
    /// to BepInEx/dumps once the main menu is up, and the current save's zones/chests on F9.
    /// </summary>
    [BepInPlugin("gk2mods.datadump", "GK2 Data Dump", "0.1.0")]
    public class DataDumpPlugin : BaseUnityPlugin
    {
        private string dumpDir;
        private bool balanceDumped;
        private float startTime;

        private void Awake()
        {
            dumpDir = Path.Combine(Paths.BepInExRootPath, "dumps");
            Directory.CreateDirectory(dumpDir);
            startTime = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            // Wait for the language to load so names come out translated.
            if (!balanceDumped && Time.realtimeSinceStartup - startTime > 25f)
            {
                balanceDumped = true;
                Try("balance", DumpBalance);
            }
            if (Input.GetKeyDown(KeyCode.F9))
            {
                Try("save", DumpSave);
            }
        }

        private void Try(string what, Action action)
        {
            try
            {
                action();
                Logger.LogInfo($"Dumped {what} to {dumpDir}");
            }
            catch (Exception e)
            {
                Logger.LogError($"Dump {what} failed: {e}");
            }
        }

        private static string Name(string id)
        {
            try { return LLBase.L(id); } catch { return null; }
        }

        private static List<object> Needs(List<NeedItemData> needs) =>
            needs.Select(n => (object)new { n.id, group = n.groupType.ToString(), count = n.count?.ToString() }).ToList();

        private void Write(string file, object data) =>
            File.WriteAllText(Path.Combine(dumpDir, file), JsonConvert.SerializeObject(data, Formatting.Indented));

        private void DumpBalance()
        {
            var gb = GameBalance.Me;

            Write("buildings.json", gb.buildingDefs.Select(b => new
            {
                b.id,
                name = Name(b.id),
                b.wgoId,
                mode = b.buildingMode.ToString(),
                b.tab,
                b.buildsIn,
                b.isNeedsUnlock,
                b.limitMax,
                limitExpr = b.currentLimitExpression?.ToString(),
                needs = Needs(b.needItems),
                icon = b.buildResultIcon,
                b.startCraft,
                b.customWgoPlacePreview,
                afterBuild = b.expressionAfterBuilding?.Select(e => e.ToString()).ToList(),
            }));

            Write("techs.json", gb.techDefs.Select(t => new
            {
                t.id,
                name = Name(t.id),
                tab = t.tab.ToString(),
                t.parents,
                lockType = t.techLockType.ToString(),
                defType = t.techDefType.ToString(),
                pos = new[] { t.posX, t.posY },
                red = t.redSpheresPrice,
                green = t.greenSpheresPrice,
                blue = t.blueSpheresPrice,
                t.availableAtStart,
                t.hiddenAtStart,
                t.customIconId,
                buildings = t.buildingsAfterUnlock,
                crafts = t.craftsAfterUnlock,
                perks = t.perksAfterUnlock,
            }));

            Write("storage_wgos.json", gb.wgoDefs.Where(w => w.inventorySize > 0).Select(w => new
            {
                w.id,
                name = Name(w.id),
                w.inventorySize,
                interaction = w.interactionType.ToString(),
                w.wgoGroup,
                multi = w.OpenInMultiInventory,
                w.hasCustomAssetId,
            }));

            Write("wgo_ids.json", gb.wgoDefs.Select(w => new { w.id, w.wgoGroup, interaction = w.interactionType.ToString() }));

            Write("items.json", gb.itemDefs.Select(i => new
            {
                i.id,
                name = Name(i.id),
                type = i.type.ToString(),
                i.stackCount,
            }));
        }

        private void DumpSave()
        {
            var world = MainGame.Instance?.GameSave?.WorldData;
            if (world == null)
            {
                Logger.LogWarning("No save loaded.");
                return;
            }
            var chests = world.Cache.wgoDataByUidCache.Values
                .Where(w => w.Definition != null && w.Definition.inventorySize > 0)
                .Select(w => new
                {
                    w.id,
                    uid = w.UniqueId.ToString(),
                    zone = w.WorldZoneData?.id,
                    multi = w.Definition.OpenInMultiInventory,
                    interaction = w.Definition.interactionType.ToString(),
                    w.CustomTag,
                })
                .ToList();
            Write("save_storages.json", chests);

            var ks = MainGame.Instance.GameSave.knowledgeSystem;
            Write("save_knowledge.json", new
            {
                techs = ks.unlockedTechs,
                buildings = ks.unlockedBuildings,
                locked = ks.lockedBuildings,
            });
        }
    }
}
