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
                Try("sprites", DumpSprites);
                StartCoroutine(new Studio(Logger, dumpDir).Run(Studio.DefaultIds));
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

        private static readonly System.Text.RegularExpressions.Regex SpriteMotifs =
            new System.Text.RegularExpressions.Regex("^i_b_|^i_bronze_gear$|^i_p-gearmaster$|^i_detail|^i_details",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>All sprite names, plus PNGs of building icons and a few motifs, for designing new icons.</summary>
        private void DumpSprites()
        {
            var collection = LazySingletonSO<EasySpritesCollection>.Instance;
            if (!EasySpritesCollection.isInitialized)
            {
                collection.Initialize();
            }
            List<string> names = collection.hash.Keys.OrderBy(n => n).ToList();
            File.WriteAllLines(Path.Combine(dumpDir, "sprite_names.txt"), names.Select(n => n + "	" + collection.hash[n]));

            string dir = Path.Combine(dumpDir, "sprites");
            Directory.CreateDirectory(dir);
            int ok = 0, failed = 0;
            var meta = new List<object>();
            foreach (string name in names.Where(n => SpriteMotifs.IsMatch(n)))
            {
                try
                {
                    Sprite sprite = collection.GetSprite(name);
                    if (sprite == null)
                    {
                        continue;
                    }
                    File.WriteAllBytes(Path.Combine(dir, name + ".png"), SpriteToPng(sprite));
                    meta.Add(new
                    {
                        name,
                        atlas = collection.hash[name],
                        rect = new[] { sprite.rect.x, sprite.rect.y, sprite.rect.width, sprite.rect.height },
                        textureRect = new[] { sprite.textureRect.x, sprite.textureRect.y, sprite.textureRect.width, sprite.textureRect.height },
                        pivot = new[] { sprite.pivot.x, sprite.pivot.y },
                        sprite.pixelsPerUnit,
                        border = new[] { sprite.border.x, sprite.border.y, sprite.border.z, sprite.border.w },
                        packed = sprite.packed,
                        rotation = sprite.packed ? sprite.packingRotation.ToString() : "",
                        atlasSize = new[] { sprite.texture.width, sprite.texture.height },
                        filter = sprite.texture.filterMode.ToString(),
                    });
                    ok++;
                }
                catch (Exception e)
                {
                    failed++;
                    if (failed <= 5)
                    {
                        Logger.LogWarning($"Sprite {name}: {e.Message}");
                    }
                }
            }
            // Sprites served by mods through GetSprite (not in the atlases): checks the whole path.
            foreach (string name in new[] { "gk2sn_icon_central_storage", "gk2sn_icon_zone_relay", "gk2sn_icon_connector" })
            {
                Sprite sprite = collection.GetSprite(name);
                Logger.LogInfo($"Mod sprite {name}: {(sprite == null ? "MISSING" : $"{sprite.rect.width}x{sprite.rect.height} ppu {sprite.pixelsPerUnit} bounds {sprite.bounds.size}")}");
                if (sprite != null)
                {
                    File.WriteAllBytes(Path.Combine(dir, name + ".png"), SpriteToPng(sprite));
                }
            }
            Write("sprites_meta.json", meta);
            Logger.LogInfo($"Sprites: {names.Count} names, {ok} exported, {failed} failed");
        }

        /// <summary>
        /// Copies exactly the sprite's texels: the blit scales/offsets the atlas so the sprite rect fills a
        /// render texture of the same pixel size, sampled with point filtering.
        /// </summary>
        private static byte[] SpriteToPng(Sprite sprite)
        {
            Texture2D atlas = sprite.texture;
            Rect r = sprite.textureRect;
            int w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.filterMode = FilterMode.Point;
            FilterMode oldFilter = atlas.filterMode;
            RenderTexture previous = RenderTexture.active;
            try
            {
                atlas.filterMode = FilterMode.Point;
                Graphics.Blit(atlas, rt, new Vector2(r.width / atlas.width, r.height / atlas.height), new Vector2(r.x / atlas.width, r.y / atlas.height));
                RenderTexture.active = rt;
                var output = new Texture2D(w, h, TextureFormat.RGBA32, false);
                output.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                output.Apply();
                byte[] png = output.EncodeToPNG();
                UnityEngine.Object.Destroy(output);
                return png;
            }
            finally
            {
                atlas.filterMode = oldFilter;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
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
