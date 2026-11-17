using System;
using System.Linq;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// Keyboard shortcuts, only while the player walks free (no window open, no build mode, no text being typed,
    /// the game not paused):
    /// - "5 Hotkeys/BuildDesk" (B): opens the build menu of the zone the player stands in, as if they had used the
    ///   zone's build desk (WorldZoneDef.builderId; the game's BuildManager.TryEnable does the rest).
    /// - "5 Hotkeys/ArchitectDesk" (Shift+B): opens the nearest Architect's Desk.
    /// </summary>
    internal static class Hotkeys
    {
        private const int WorldZoneLayerMask = 1 << 17;  // the layer WgoExtensions.TryGetNearestBuilderWorldZone looks in
        private static readonly Collider[] hits = new Collider[32];

        internal static void Tick()
        {
            try
            {
                // Shift+B first, so it doesn't also count as B.
                if (Plugin.ArchitectKey.Value.IsDown() && Free())
                {
                    OpenArchitectDesk();
                }
                else if (Plugin.BuildKey.Value.IsDown() && Free())
                {
                    OpenZoneBuildMenu();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Hotkey failed: {e}");
            }
        }

        private static bool Free()
        {
            PlayerController player = MainGame.PlayerController;
            if (MainGame.Instance?.GameSave == null || player == null || MainGame.IsGamePaused || !player.IsControlsEnabled)
            {
                return false;
            }
            if (LazyWindowsStackController.ActiveWindow != null || LazyWindowsStackController.HasAnyModalWindowOpened)
            {
                return false;
            }
            GameObject focused = EventSystem.current?.currentSelectedGameObject;
            return focused == null || focused.GetComponent<TMP_InputField>() == null;
        }

        // ---------------------------------------------------------------- the zone's build menu

        private static void OpenZoneBuildMenu()
        {
            PlayerController player = MainGame.PlayerController;
            Vector3 at = player.transform.position;
            string builderId = ZoneBuilder(at, out string zoneId);
            if (builderId == null)
            {
                WeekDispatch.Notify("gk2fp_no_builder", Texts.Pt("Esta área não tem mesa de construção.", "This area has no build desk."), Icons.DraftingTable);
                Plugin.Debug($"Hotkey: no build desk for the zone at {at} ({zoneId ?? "no zone"})");
                return;
            }
            WorldData world = MainGame.Instance.GameSave.WorldData;
            WgoData data = world.Cache.wgoDataByUidCache.Values
                .Where(w => w != null && !w.isTempObject && w.id == builderId && GameScene.GetWgoViewGlobal(w.UniqueId) != null)
                .OrderBy(w => (w.Position - at).sqrMagnitude)
                .FirstOrDefault();
            Wgo desk = data != null ? GameScene.GetWgoViewGlobal(data.UniqueId) : null;
            if (desk == null)
            {
                WeekDispatch.Notify("gk2fp_no_builder", Texts.Pt("A mesa de construção desta área ainda não existe.", "This area's build desk isn't there yet."), Icons.DraftingTable);
                Plugin.Debug($"Hotkey: build desk {builderId} of {zoneId} not loaded");
                return;
            }
            BuildManager manager = LazySingleton<BuildManager>.Instance;
            bool fight = data.Definition?.interactionType == WGODef.InteractionType.FightBuilder;
            bool opened = fight
                ? manager.TryEnable(desk, MainGame.Instance.GameSave.militaryBaseData.CreateBaseBuildingsInventory)
                : manager.TryEnable(desk);
            Plugin.Debug($"Hotkey: build menu of {zoneId} via {builderId}: {(opened ? "open" : "refused")}");
        }

        /// <summary>The build desk id of the zone around the point (the one containing it, else the nearest).</summary>
        private static string ZoneBuilder(Vector3 at, out string zoneId)
        {
            zoneId = null;
            int n = Physics.OverlapBoxNonAlloc(at, new Vector3(2f, 6f, 2f), hits, Quaternion.identity, WorldZoneLayerMask, QueryTriggerInteraction.Collide);
            string best = null;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                Collider c = hits[i];
                if (c == null || !c.TryGetComponent(out WorldZone zone) || zone == null)
                {
                    continue;
                }
                string builder = GameBalance.Me.GetDataOrNull<WorldZoneDef>(zone.Id)?.builderId;
                if (string.IsNullOrEmpty(builder))
                {
                    continue;
                }
                Vector3 flat = new Vector3(at.x, c.bounds.center.y, at.z);
                // Inside: the smallest zone wins (a workshop inside a bigger area); outside: the nearest.
                float d = c.bounds.Contains(flat) ? -1e6f + c.bounds.size.x * c.bounds.size.z : c.bounds.SqrDistance(at);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = builder;
                    zoneId = zone.Id;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- the Architect's Desk

        private static void OpenArchitectDesk()
        {
            PlayerController player = MainGame.PlayerController;
            WorldData world = MainGame.Instance.GameSave.WorldData;
            Vector3 at = player.transform.position;
            WgoData desk = world.Cache.wgoDataByUidCache.Values
                .Where(w => w != null && !w.isTempObject && w.CustomTag == Content.ArchitectTag && w.id != null &&
                            !w.id.EndsWith("_place", StringComparison.Ordinal) && !w.id.EndsWith("_place_p", StringComparison.Ordinal))
                .OrderBy(w => (w.Position - at).sqrMagnitude)
                .FirstOrDefault();
            if (desk == null)
            {
                WeekDispatch.Notify("gk2fp_no_desk", Texts.Pt("Nenhuma Mesa de Arquitetura construída ainda.", "No Architect's Desk built yet."), Icons.ArchitectTable);
                return;
            }
            Plugin.Debug($"Hotkey: opening the Architect's Desk {desk.UniqueId}");
            ArchitectPanel.TryOpen(player, desk);
        }
    }
}
