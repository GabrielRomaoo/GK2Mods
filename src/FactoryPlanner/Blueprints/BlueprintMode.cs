using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Mods.FactoryPlanner.Blueprints
{
    /// <summary>
    /// The two blueprint tools of the factory's build menu, both running inside the game's own build mode.
    ///
    /// Blueprint: the pointer (a hidden chest preview) carries chalk-blue ghosts of every piece; red ghosts sit on
    /// something. On click the pieces are built one by one through the game's own build pointer, exactly as if
    /// the player had picked each one from the menu, rotated it and clicked: the game checks the spot, takes the
    /// materials (player + the zone's chests), makes construction sites for machines and connects the belts.
    /// Nothing is placed by hand-made data. Pieces already standing are recognised and skipped, so laying the
    /// same blueprint on the same spot again finishes it.
    ///
    /// Trace: click two corners; the factory pieces inside become a new blueprint.
    /// </summary>
    internal static class BlueprintMode
    {
        private sealed class Ghost
        {
            public BlueprintPiece Piece;
            public BuildingDef Def;
            public Wgo View;
            public readonly List<Collider> Footprint = new List<Collider>();
            public bool Standing;
            public bool Blocked;
        }

        private static readonly Color Free = new Color(0.35f, 0.65f, 1f);
        private static readonly Color Blocked = new Color(1f, 0.3f, 0.25f);
        private static readonly Color Picked = new Color(1f, 0.85f, 0.3f);
        private const int ObstacleMask = (1 << 8) | (1 << 16) | (1 << 19) | (1 << 29);

        private static bool active;
        private static bool copyMode;
        private static bool committing;
        private static bool restoring;

        private static Blueprint blueprint;
        private static readonly List<Ghost> ghosts = new List<Ghost>();
        private static HashSet<string> standing = new HashSet<string>();
        private static Vector3 origin;
        private static bool hasOrigin;
        private static bool pinned;
        private static Vector3 pointerPos;
        private static readonly Dictionary<string, Vector3> lastOrigins = new Dictionary<string, Vector3>();

        private static Vector3? cornerA;
        // What the Trace view shows right now, per object: a tint (3D objects) or a ground cell (belts and the like,
        // which the game's tint doesn't reach). Only changes are applied, so moving the mouse stays cheap.
        private static readonly Dictionary<SGuid, (Color color, float alpha)> shown = new Dictionary<SGuid, (Color, float)>();
        private static readonly Dictionary<SGuid, BuildSelectionCell> markers = new Dictionary<SGuid, BuildSelectionCell>();
        private static readonly Stack<BuildSelectionCell> markerPool = new Stack<BuildSelectionCell>();

        private sealed class TracePiece
        {
            public WgoData Data;
            public bool Single;
            public bool RectDone;
            public Rect Rect;
        }

        private static List<TracePiece> tracePieces;
        private static Vector3 cellOffset;   // pointer position -> centre of the cell under it (same for every 1x1 piece)
        private static Vector3 cellCenter;
        private static readonly Color Hover = new Color(1f, 1f, 0.85f);
        private static readonly Color Unpick = new Color(1f, 0.5f, 0.2f);

        // Trace: pieces marked one by one (or by rectangle), saved with R.
        private static readonly HashSet<SGuid> picks = new HashSet<SGuid>();
        private static readonly Dictionary<SGuid, Rect> footprints = new Dictionary<SGuid, Rect>();
        private static WgoData hovered;
        private static Vector3 hoverPos = new Vector3(float.NaN, 0f, 0f);

        private static readonly Collider[] overlap = new Collider[64];

        private static BuildController Controller => BuildController.Instance;

        internal static bool IsOurs(BuildData data) => data?.Definition != null && Content.IsBlueprintDef(data.Definition.id);

        internal static bool Committing => committing;

        // ---------------------------------------------------------------- started from the Architect's Desk

        private static bool pendingLaunch;
        private static bool launchedFromDesk;

        /// <summary>Set by BuildManager.EnableBuildMode: whether this build mode was started by the desk.</summary>
        internal static void OnBuildManagerEnable()
        {
            launchedFromDesk = pendingLaunch;
            pendingLaunch = false;
        }

        /// <summary>
        /// Leaving a build mode the desk started: the game would open the factory's build menu (the "build desk" it
        /// was started with). Give the controls back instead, so Esc returns to walking around.
        /// </summary>
        internal static bool TryLeaveToWorld()
        {
            if (!launchedFromDesk)
            {
                return false;
            }
            launchedFromDesk = false;
            MainGame.PlayerController.SetControlTakenType(TakenControlType.ByBuilding, isEnabled: true);
            return true;
        }

        /// <summary>Opens the factory's build mode straight on the Trace or Blueprint tool (a frame after the desk's window closed).</summary>
        internal static IEnumerator StartFromDesk(bool copy)
        {
            yield return null;
            yield return null;
            try
            {
                BuildingDef def = FactoryPieces.Def(copy ? Content.BlueprintCopyId : Content.BlueprintPlaceId);
                WgoData builderData = MainGame.WorldData.GetWgoData(Content.FactoryBuilder);
                Wgo builder = builderData != null ? GameScene.GetWgoViewGlobal(builderData.UniqueId) : null;
                if (def == null || builder == null || !builder.TryGetNearestBuilderWorldZone(out WorldZone zone))
                {
                    Plugin.Log.LogWarning($"Blueprint tool: factory builder not reachable (def {def != null}, builder {builderData != null}/{builder != null}).");
                    Notify("gk2fp_bp_far", Texts.Pt("A fábrica precisa estar carregada: use a Mesa de Arquitetura dentro da fábrica ou perto dela.",
                        "The factory must be loaded: use the Architect's Desk inside the factory or near it."), Icons.ArchitectTable);
                    yield break;
                }
                BuildManager manager = LazySingleton<BuildManager>.Instance;
                manager.WorldZone = zone;
                pendingLaunch = true;
                manager.EnableBuildMode(builder, BuildData.GetDataForBuild(def), new List<NeedItemData>());
                Plugin.Debug($"Blueprint tool started from the desk: {def.id} in zone {zone.Id}");
            }
            catch (Exception e)
            {
                pendingLaunch = false;
                launchedFromDesk = false;
                Plugin.Log.LogError($"Blueprint tool could not start: {e}");
            }
        }

        // ---------------------------------------------------------------- lifecycle

        internal static void OnPointerEnabled(BuildPointer pointer)
        {
            if (committing && !restoring)
            {
                return;
            }
            bool copy = ((BuildPointerObject)pointer.PointerObject).BuildData.Definition.id == Content.BlueprintCopyId;
            HideAnchor(pointer);
            if (restoring && active && copy == copyMode)
            {
                standing = FactoryPieces.Standing(Controller.CurrentWorldZone?.Data);
                SpawnGhosts();
                return;
            }
            Exit();
            active = true;
            copyMode = copy;
            if (copyMode)
            {
                Notify("gk2fp_bp_copy_start", Texts.Pt("Decalque: clique nas peças para marcar (amarelo). Clique no chão vazio e depois noutro ponto para marcar um retângulo. R salva a planta, Esc sai.",
                    "Trace: click pieces to mark them (yellow). Click empty floor, then another spot, to mark a rectangle. R saves the blueprint, Esc leaves."), Icons.BlueprintCopy);
                return;
            }
            blueprint = BlueprintStore.Selected;
            if (blueprint == null)
            {
                Notify("gk2fp_bp_none", Texts.Pt("Ainda não há plantas: faça um Decalque na Mesa de Arquitetura.",
                    "No blueprints yet: Trace a part of the factory at the Architect's Desk."), Icons.Blueprint);
                return;
            }
            pinned = false;
            standing = FactoryPieces.Standing(Controller.CurrentWorldZone?.Data);
            SpawnGhosts();
            Notify("gk2fp_bp_start", Texts.Pt($"{blueprint.Name}: {blueprint.Pieces.Count} peças. Clique para construir.",
                $"{blueprint.Name}: {blueprint.Pieces.Count} pieces. Click to build."), Icons.Blueprint);
        }

        internal static void OnOtherPointerEnabled()
        {
            if (active && !committing)
            {
                Exit();
            }
        }

        internal static void Exit()
        {
            DestroyGhosts();
            ClearTints();
            active = false;
            blueprint = null;
            cornerA = null;
            hasOrigin = false;
            picks.Clear();
            footprints.Clear();
            tracePieces = null;
            hovered = null;
            hoverPos = new Vector3(float.NaN, 0f, 0f);
        }

        /// <summary>The pointer's own object (a chest preview) is only an anchor: hide it and its selection cells.</summary>
        private static void HideAnchor(BuildPointer pointer)
        {
            if (pointer.PointerObject is WgoBuildPointer wp && wp.Target != null)
            {
                foreach (Renderer r in wp.Target.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = false;
                }
                HideCells(wp);
            }
        }

        internal static void HideCellsOf(WgoBuildPointer wp) => HideCells(wp);

        private static void HideCells(WgoBuildPointer wp)
        {
            foreach (BuildSelectionCell cell in wp.cells)
            {
                if (cell != null)
                {
                    cell.gameObject.SetActive(false);
                }
            }
        }

        // ---------------------------------------------------------------- moving

        internal static void OnMoved(WgoBuildPointer wp, Vector3 position)
        {
            HideCells(wp);
            pointerPos = position;
            if (copyMode)
            {
                BuildSelectionCell cell = wp.cells?.FirstOrDefault(c => c != null);
                cellOffset = cell != null ? cell.transform.position - position : Vector3.zero;
                cellOffset.y = 0f;
                cellCenter = position + cellOffset;
                UpdateCopyHighlight();
                return;
            }
            if (blueprint == null)
            {
                return;
            }
            Vector3 o = pinned && lastOrigins.TryGetValue(blueprint.Name, out Vector3 last) ? last : position;
            if (blueprint.OnFactoryGrid)
            {
                o = FromScratch.Snap(o);
            }
            if (hasOrigin && (o - origin).sqrMagnitude < 1e-8f)
            {
                return;
            }
            origin = o;
            hasOrigin = true;
            MoveGhosts();
        }

        /// <summary>R in blueprint mode: pin the drawing to the last construction of this blueprint, or release it.</summary>
        internal static void TogglePin()
        {
            if (copyMode)
            {
                SaveSelection();
                return;
            }
            if (blueprint == null)
            {
                return;
            }
            if (!lastOrigins.ContainsKey(blueprint.Name))
            {
                Notify("gk2fp_bp_nopin", Texts.Pt("Esta planta ainda não foi construída nesta sessão: não há obra para prender.",
                    "This blueprint hasn't been built this session: no construction to pin to."), Icons.Blueprint);
                return;
            }
            pinned = !pinned;
            hasOrigin = false;
            OnMovedAgain();
        }

        private static void OnMovedAgain()
        {
            if (Controller?.buildPointer?.PointerObject is WgoBuildPointer wp)
            {
                OnMoved(wp, pointerPos);
            }
        }

        // ---------------------------------------------------------------- ghosts

        private static void SpawnGhosts()
        {
            DestroyGhosts();
            if (blueprint == null)
            {
                return;
            }
            GameScene scene = MainGame.PlayerController.CurrentGameScene;
            var missing = new HashSet<string>();
            foreach (BlueprintPiece piece in blueprint.Pieces)
            {
                BuildingDef def = FactoryPieces.Def(piece.Def);
                if (def == null)
                {
                    missing.Add(piece.Def);
                    continue;
                }
                var ghost = new Ghost { Piece = piece, Def = def };
                try
                {
                    var data = new WgoData(FactoryPieces.PreviewId(def), origin + Offset(piece), scene.Id, string.Empty, isTempObject: true);
                    ghost.View = Wgo.Spawn(data, scene.transform, registerInChunkManagerIfStatic: true, ignoreChunkRegistration: true, applyDefaultWgoPartState: true);
                    ghost.View.UpdateChunkVisibility(isVisible: true);
                    ApplyState(ghost.View.MainWgoPart, piece.Variation, piece.Rotation);
                    foreach (Collider c in ghost.View.GetComponentsInChildren<Collider>(true))
                    {
                        if (c.gameObject.layer == 19)
                        {
                            ghost.Footprint.Add(c);
                        }
                        else
                        {
                            c.enabled = false;
                        }
                    }
                    foreach (NavMeshCutBoxCustom cut in ghost.View.GetComponentsInChildren<NavMeshCutBoxCustom>(true))
                    {
                        cut.gameObject.SetActive(false);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Blueprint ghost {piece.Def} failed: {e.Message}");
                }
                ghosts.Add(ghost);
            }
            if (missing.Count > 0)
            {
                Plugin.Log.LogWarning($"Blueprint '{blueprint.Name}': unknown defs {string.Join(", ", missing)}");
            }
            hasOrigin = false;
            OnMovedAgain();
        }

        private static void MoveGhosts()
        {
            foreach (Ghost g in ghosts)
            {
                if (g.View != null)
                {
                    g.View.Data.Position = origin + Offset(g.Piece);
                }
            }
            Physics.SyncTransforms();
            foreach (Ghost g in ghosts)
            {
                Vector3 pos = origin + Offset(g.Piece);
                g.Standing = standing.Contains(FactoryPieces.Key(g.Piece.Wgo, pos)) || standing.Contains(FactoryPieces.Key(g.Def.wgoId, pos));
                g.Blocked = !g.Standing && Overlaps(g);
                if (g.View == null)
                {
                    continue;
                }
                if (g.View.gameObject.activeSelf == g.Standing)
                {
                    g.View.gameObject.SetActive(!g.Standing);
                }
                if (!g.Standing)
                {
                    g.View.SetSelectionTint(g.Blocked ? Blocked : Free, g.Blocked ? 0.8f : 0.7f);
                }
            }
        }

        private static bool Overlaps(Ghost g)
        {
            foreach (Collider c in g.Footprint)
            {
                if (c == null)
                {
                    continue;
                }
                Bounds b = c.bounds;
                Vector3 half = Vector3.Max(new Vector3(0.01f, 0.05f, 0.01f), b.extents - new Vector3(0.03f, 0f, 0.03f));
                int n = Physics.OverlapBoxNonAlloc(b.center, half, overlap, Quaternion.identity, ObstacleMask);
                for (int i = 0; i < n; i++)
                {
                    Collider hit = overlap[i];
                    if (hit == null || g.Footprint.Contains(hit))
                    {
                        continue;
                    }
                    if (hit.gameObject.layer == 29)
                    {
                        return true;
                    }
                    Wgo w = hit.GetComponentInParent<Wgo>();
                    if (w == null)
                    {
                        if (hit.gameObject.layer == 8 || hit.gameObject.layer == 16)
                        {
                            return true;
                        }
                        continue;
                    }
                    if (w.Data != null && !w.Data.isTempObject)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void DestroyGhosts()
        {
            foreach (Ghost g in ghosts)
            {
                if (g.View != null)
                {
                    UnityEngine.Object.Destroy(g.View.gameObject);
                }
            }
            ghosts.Clear();
        }

        private static Vector3 Offset(BlueprintPiece p) => new Vector3(p.X, p.Y, p.Z);

        /// <summary>Puts a part into one of its authored states (variation + rotation), as rotating it would.</summary>
        internal static bool ApplyState(WgoPart part, string variation, int rotation)
        {
            if (part == null || rotation < 0)
            {
                return true;
            }
            WgoPartData now = part.WgoPartData;
            if (now != null && now.rotationIndex == rotation && (variation == null || now.variationId == variation))
            {
                return true;
            }
            WgoPartState state = part.variations.FirstOrDefault(s => s.rotationIndex == rotation && s.variationId == variation)
                ?? part.variations.FirstOrDefault(s => s.rotationIndex == rotation && s.variationId == now?.variationId)
                ?? part.variations.FirstOrDefault(s => s.rotationIndex == rotation);
            if (state == null)
            {
                return false;
            }
            if (now != null)
            {
                now.variationId = state.variationId;
                now.rotationIndex = state.rotationIndex;
            }
            part.ApplyWgoPartState(state);
            return true;
        }

        // ---------------------------------------------------------------- tracing

        private static bool IsPiece(WgoData data) =>
            FactoryPieces.DefFor(data.id) != null || GameBalance.Me.IsWorkbenchExtensionId(data.id);

        /// <summary>Belts, splitters and underground belts: one cell each, drawn without 3D objects.</summary>
        private static bool IsSingleCell(string id) =>
            id.StartsWith("conveyor_cell", StringComparison.Ordinal) || id.Contains("splitter") || id.Contains("underground");

        /// <summary>The zone's factory pieces, listed once per Trace session (the factory doesn't change meanwhile).</summary>
        private static List<TracePiece> Pieces()
        {
            if (tracePieces == null)
            {
                tracePieces = FactoryPieces.ZoneObjects(Controller.CurrentWorldZone?.Data).Where(IsPiece)
                    .Select(d => new TracePiece { Data = d, Single = IsSingleCell(d.id) }).ToList();
                Plugin.Debug($"Trace: {tracePieces.Count} factory pieces in the zone");
            }
            return tracePieces;
        }

        /// <summary>
        /// The piece under the pointer. A one-cell piece must sit exactly on the pointer's cell; a bigger one must
        /// cover it with its footprint (the nearest wins when footprints overlap).
        /// </summary>
        private static WgoData PieceAt()
        {
            WgoData best = null;
            float bestScore = float.MaxValue;
            foreach (TracePiece piece in Pieces())
            {
                Vector3 p = piece.Data.Position;
                if (piece.Single)
                {
                    Vector3 c = p + cellOffset;
                    if (Mathf.Abs(c.x - cellCenter.x) < 0.1f && Mathf.Abs(c.z - cellCenter.z) < 0.1f)
                    {
                        return piece.Data;
                    }
                    continue;
                }
                if (!piece.RectDone)
                {
                    piece.RectDone = true;
                    Footprint(piece.Data, out piece.Rect);
                }
                if (piece.Rect.width <= 0f || !piece.Rect.Contains(new Vector2(cellCenter.x, cellCenter.z)))
                {
                    continue;
                }
                float dx = p.x - cellCenter.x, dz = p.z - cellCenter.z;
                float score = dx * dx + dz * dz;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = piece.Data;
                }
            }
            return best;
        }

        /// <summary>Ground rectangle of an object: its build colliders, or its visible meshes when those are off.</summary>
        private static bool Footprint(WgoData data, out Rect rect)
        {
            if (footprints.TryGetValue(data.UniqueId, out rect))
            {
                return rect.width > 0f;
            }
            rect = default;
            Wgo view = GameScene.GetWgoViewGlobal(data.UniqueId);
            if (view != null)
            {
                bool any = false;
                Bounds all = default;
                foreach (Collider c in view.GetComponentsInChildren<Collider>(true))
                {
                    Bounds b = c.bounds;
                    if (c.gameObject.layer != 19 || (b.size.x <= 0f && b.size.z <= 0f))
                    {
                        continue;
                    }
                    if (any) { all.Encapsulate(b); } else { all = b; any = true; }
                }
                if (!any)
                {
                    foreach (MeshRenderer r in view.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (!r.enabled)
                        {
                            continue;
                        }
                        if (any) { all.Encapsulate(r.bounds); } else { all = r.bounds; any = true; }
                    }
                }
                if (any)
                {
                    rect = Rect.MinMaxRect(all.min.x, all.min.z, all.max.x, all.max.z);
                }
            }
            footprints[data.UniqueId] = rect;
            return rect.width > 0f;
        }

        private static bool InRectangle(Vector3 p, Vector3 a, Vector3 b) =>
            p.x >= Mathf.Min(a.x, b.x) - 0.16f && p.x <= Mathf.Max(a.x, b.x) + 0.16f &&
            p.z >= Mathf.Min(a.z, b.z) - 0.15f && p.z <= Mathf.Max(a.z, b.z) + 0.15f;

        /// <summary>Marked pieces yellow, the rectangle being drawn lighter, the piece under the pointer pale (orange if a click unmarks it).</summary>
        private static void UpdateCopyHighlight()
        {
            if ((pointerPos - hoverPos).sqrMagnitude > 1e-6f || float.IsNaN(hoverPos.x))
            {
                hoverPos = pointerPos;
                hovered = cornerA == null ? PieceAt() : null;
            }
            var want = new Dictionary<SGuid, (Color color, float alpha)>();
            foreach (SGuid id in picks)
            {
                want[id] = (Picked, 0.6f);
            }
            if (cornerA != null)
            {
                foreach (TracePiece piece in Pieces())
                {
                    if (!want.ContainsKey(piece.Data.UniqueId) && InRectangle(piece.Data.Position, cornerA.Value, pointerPos))
                    {
                        want[piece.Data.UniqueId] = (Picked, 0.4f);
                    }
                }
            }
            if (hovered != null)
            {
                want[hovered.UniqueId] = picks.Contains(hovered.UniqueId) ? (Unpick, 0.6f) : (Hover, 0.4f);
            }
            foreach (SGuid id in shown.Keys.Where(id => !want.ContainsKey(id)).ToList())
            {
                Unshow(id);
            }
            foreach (var kv in want)
            {
                if (!shown.TryGetValue(kv.Key, out var now) || now != kv.Value)
                {
                    Show(kv.Key, kv.Value.color, kv.Value.alpha);
                }
            }
        }

        private static void Show(SGuid id, Color color, float alpha)
        {
            shown[id] = (color, alpha);
            WgoData data = MainGame.WorldData.GetWgoData(id);
            if (data == null)
            {
                return;
            }
            if (IsSingleCell(data.id))
            {
                if (!markers.TryGetValue(id, out BuildSelectionCell marker) || marker == null)
                {
                    marker = NewMarker();
                    if (marker == null)
                    {
                        return;
                    }
                    markers[id] = marker;
                }
                marker.transform.position = data.Position + cellOffset + new Vector3(0f, 0.01f, 0f);
                marker.spriteRenderer.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha + 0.2f));
                return;
            }
            Wgo view = GameScene.GetWgoViewGlobal(id);
            if (view != null)
            {
                view.SetSelectionTint(color, alpha);
            }
        }

        private static void Unshow(SGuid id)
        {
            shown.Remove(id);
            if (markers.TryGetValue(id, out BuildSelectionCell marker))
            {
                markers.Remove(id);
                if (marker != null)
                {
                    marker.gameObject.SetActive(false);
                    markerPool.Push(marker);
                }
                return;
            }
            Wgo view = GameScene.GetWgoViewGlobal(id);
            if (view != null)
            {
                view.ClearSelectionTint();
            }
        }

        /// <summary>A copy of the build pointer's own selection cell (the green/red squares), recoloured.</summary>
        private static BuildSelectionCell NewMarker()
        {
            while (markerPool.Count > 0)
            {
                BuildSelectionCell pooled = markerPool.Pop();
                if (pooled != null)
                {
                    pooled.gameObject.SetActive(true);
                    return pooled;
                }
            }
            BuildSelectionCell prefab = Controller?.buildPointer?.selectionCellPrefab;
            if (prefab == null)
            {
                return null;
            }
            BuildSelectionCell cell = UnityEngine.Object.Instantiate(prefab);
            cell.name = "GK2FP_TraceMarker";
            cell.gameObject.SetActive(true);
            cell.SetVariation(BuildSelectionCellVariation.Central);
            return cell;
        }

        private static void ClearTints()
        {
            foreach (SGuid id in shown.Keys.ToList())
            {
                Unshow(id);
            }
            while (markerPool.Count > 0)
            {
                BuildSelectionCell marker = markerPool.Pop();
                if (marker != null)
                {
                    UnityEngine.Object.Destroy(marker.gameObject);
                }
            }
        }

        private static void CopyClick()
        {
            WorldZoneData zone = Controller.CurrentWorldZone?.Data;
            if (cornerA != null)
            {
                int before = picks.Count;
                foreach (WgoData data in FactoryPieces.InArea(zone, cornerA.Value, pointerPos))
                {
                    if (IsPiece(data))
                    {
                        picks.Add(data.UniqueId);
                    }
                }
                cornerA = null;
                hoverPos = new Vector3(float.NaN, 0f, 0f);
                Notify("gk2fp_bp_copy_rect", Texts.Pt($"+{picks.Count - before} peça(s): {picks.Count} marcada(s). R salva a planta.",
                    $"+{picks.Count - before} piece(s): {picks.Count} marked. R saves the blueprint."), Icons.BlueprintCopy);
                UpdateCopyHighlight();
                return;
            }
            WgoData piece = PieceAt();
            if (piece != null)
            {
                if (!picks.Remove(piece.UniqueId))
                {
                    picks.Add(piece.UniqueId);
                }
                LazyAudio.PlayAndForget("gui_hover_light");
                Plugin.Debug($"Trace: {(picks.Contains(piece.UniqueId) ? "marked" : "unmarked")} {piece.id}, {picks.Count} marked");
                hoverPos = new Vector3(float.NaN, 0f, 0f);
                UpdateCopyHighlight();
                return;
            }
            cornerA = pointerPos;
            Notify("gk2fp_bp_copy_a", Texts.Pt("Retângulo: clique no canto oposto. O que ficar amarelo é marcado.",
                "Rectangle: click the opposite corner. Whatever turns yellow gets marked."), Icons.BlueprintCopy);
            UpdateCopyHighlight();
        }

        /// <summary>R in Trace: the marked pieces become a new blueprint on the Architect's Desk.</summary>
        private static void SaveSelection()
        {
            if (picks.Count == 0)
            {
                Notify("gk2fp_bp_copy_none", Texts.Pt("Nada marcado: clique nas peças que quer na planta.", "Nothing marked: click the pieces you want in the blueprint."),
                    Icons.BlueprintCopy);
                return;
            }
            Blueprint captured = FactoryPieces.Capture(Controller.CurrentWorldZone?.Data, picks.Select(id => MainGame.WorldData.GetWgoData(id)));
            picks.Clear();
            cornerA = null;
            hoverPos = new Vector3(float.NaN, 0f, 0f);
            UpdateCopyHighlight();
            if (captured.Pieces.Count == 0)
            {
                Notify("gk2fp_bp_copy_empty", Texts.Pt("As peças marcadas não entram em planta (só extensões ou peças sem item no menu).",
                    "The marked pieces can't go in a blueprint (only add-ons, or pieces with no build menu item)."), Icons.BlueprintCopy);
                return;
            }
            BlueprintStore.Save(captured);
            BlueprintStore.Select(captured);
            var text = new StringBuilder(Texts.Pt($"Planta \"{captured.Name}\" salva na Mesa de Arquitetura: {captured.Pieces.Count} peças.",
                $"Blueprint \"{captured.Name}\" saved on the Architect's Desk: {captured.Pieces.Count} pieces."));
            if (captured.AddOns.Count > 0)
            {
                text.Append(Texts.Pt(" Extensões ficam para pôr à mão: ", " Add-ons are placed by hand: ") + CountList(captured.AddOns));
            }
            Notify("gk2fp_bp_copy_done", text.ToString(), Icons.Blueprint);
        }

        // ---------------------------------------------------------------- building

        internal static void BuildClick()
        {
            if (copyMode)
            {
                CopyClick();
                return;
            }
            if (blueprint == null || committing)
            {
                return;
            }
            Plugin.Instance.StartCoroutine(Commit());
        }

        private static IEnumerator Commit()
        {
            committing = true;
            BuildController ctl = Controller;
            ctl.isBuildModeInputLocked = true;
            var report = new CommitReport { Origin = origin, Blueprint = blueprint };
            IEnumerator steps = CommitSteps(report);
            while (true)
            {
                bool more;
                try
                {
                    more = steps.MoveNext();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Blueprint build stopped: {e}");
                    report.Error = true;
                    more = false;
                }
                if (!more)
                {
                    break;
                }
                yield return steps.Current;
            }
            try
            {
                Restore(report);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Blueprint mode could not be restored: {e}");
                try
                {
                    ctl.DisableBuildMode();
                    LazySingleton<BuildManager>.Instance.Disable();
                }
                catch (Exception)
                {
                    // nothing more to do
                }
                Exit();
            }
            finally
            {
                committing = false;
                restoring = false;
                if (ctl != null)
                {
                    ctl.isBuildModeInputLocked = false;
                }
            }
        }

        private sealed class CommitReport
        {
            public Blueprint Blueprint;
            public Vector3 Origin;
            public int Built;
            public int Remaining;
            public readonly Dictionary<string, int> NoRoom = new Dictionary<string, int>();
            public readonly Dictionary<string, int> NoItems = new Dictionary<string, int>();
            public readonly Dictionary<string, int> Failed = new Dictionary<string, int>();
            public bool Error;
        }

        private static IEnumerator CommitSteps(CommitReport report)
        {
            BuildController ctl = Controller;
            BuildPointer bp = ctl.buildPointer;
            Vector3 o = report.Origin;
            List<Ghost> todo = ghosts.Where(g => !g.Standing).OrderBy(g => FactoryPieces.BuildOrder(g.Def)).ThenBy(g => g.Def.id).ToList();
            List<BlueprintPiece> pieces = todo.Select(g => g.Piece).ToList();
            List<BuildingDef> defs = todo.Select(g => g.Def).ToList();
            DestroyGhosts();
            MultiInventory inventory = NewInventory();
            BuildingDef current = null;
            WaitForFixedUpdate fixedStep = new WaitForFixedUpdate();
            Plugin.Debug($"Blueprint '{report.Blueprint.Name}': building {pieces.Count} pieces at {o}");
            for (int i = 0; i < pieces.Count; i++)
            {
                if (!ctl.IsBuildModeActive)
                {
                    Plugin.Log.LogWarning("Blueprint build interrupted: build mode closed.");
                    yield break;
                }
                BlueprintPiece piece = pieces[i];
                BuildingDef def = defs[i];
                string name = Texts.Name(FactoryPieces.FinishedId(def));
                if (current != def)
                {
                    SwitchPointer(ctl, bp, def, inventory, o + Offset(piece));
                    current = def;
                    yield return fixedStep;
                }
                if (!(bp.PointerObject is WgoBuildPointer wp) || wp.Target == null)
                {
                    Add(report.Failed, name);
                    continue;
                }
                if (!SetPointerState(bp, wp, piece))
                {
                    Plugin.Log.LogWarning($"Blueprint: {def.id} has no state {piece.Variation}/{piece.Rotation}");
                    Add(report.Failed, name);
                    continue;
                }
                yield return fixedStep;
                Vector3 target = o + Offset(piece);
                ctl.UpdatePointerObjectPosition(target - wp.PreviewOffset + bp.ShiftToVisualCenter);
                Physics.SyncTransforms();
                bp.UpdateAvailability();
                Vector3 got = wp.Target.Data.Position;
                if (Mathf.Abs(got.x - target.x) > 0.01f || Mathf.Abs(got.z - target.z) > 0.01f)
                {
                    Plugin.Log.LogWarning($"Blueprint: {def.id} landed at {got} instead of {target}; skipped.");
                    Add(report.Failed, name);
                    continue;
                }
                if (!wp.shownAsActive)
                {
                    bool hasItems = wp.canTakeResources == null || wp.canTakeResources();
                    Add(hasItems ? report.NoRoom : report.NoItems, name);
                    continue;
                }
                if (bp.TryBuildActionInput())
                {
                    report.Built++;
                }
                else
                {
                    Add(report.Failed, name);
                }
                yield return fixedStep;
            }
        }

        private static void Restore(CommitReport report)
        {
            BuildController ctl = Controller;
            report.Remaining = report.NoRoom.Values.Sum() + report.NoItems.Values.Sum() + report.Failed.Values.Sum();
            if (report.Built > 0)
            {
                lastOrigins[report.Blueprint.Name] = report.Origin;
            }
            if (ctl.IsBuildModeActive)
            {
                ctl.buildLayout.UpdateBuildingMode();
                BuildingDef def = FactoryPieces.Def(Content.BlueprintPlaceId);
                origin = report.Origin;
                pinned = report.Remaining > 0 && report.Built > 0;
                restoring = true;
                SwitchPointer(ctl, ctl.buildPointer, def, NewInventory(), report.Origin);
                ctl.UpdatePointerObjectPosition(ctl.lastSnappedCursorPos);
                ctl.DrawBuildingHud();
            }
            else
            {
                Exit();
            }
            var text = new StringBuilder(Texts.Pt($"{report.Blueprint.Name}: {report.Built} peça(s) postas.",
                $"{report.Blueprint.Name}: {report.Built} piece(s) placed."));
            if (report.NoItems.Count > 0)
            {
                text.Append(Texts.Pt(" Sem material: ", " Missing materials: ") + CountList(report.NoItems) + ".");
            }
            if (report.NoRoom.Count > 0)
            {
                text.Append(Texts.Pt(" Sem espaço: ", " No room: ") + CountList(report.NoRoom) + ".");
            }
            if (report.Failed.Count > 0)
            {
                text.Append(Texts.Pt(" Não deu: ", " Failed: ") + CountList(report.Failed) + ".");
            }
            if (pinned)
            {
                text.Append(Texts.Pt(" O desenho ficou preso na obra; R solta.", " The drawing stays pinned to the construction; R releases it."));
            }
            if (report.Blueprint.AddOns.Count > 0 && report.Built > 0)
            {
                text.Append(Texts.Pt(" Extensões à mão: ", " Add-ons by hand: ") + CountList(report.Blueprint.AddOns) + ".");
            }
            Plugin.Log.LogInfo($"Blueprint '{report.Blueprint.Name}': built {report.Built}, remaining {report.Remaining}" +
                (report.Error ? " (stopped by an error)" : ""));
            Notify("gk2fp_bp_done", text.ToString(), Icons.Blueprint);
        }

        /// <summary>Switches the build pointer to another def, with its cost, exactly like picking it in the menu.</summary>
        private static void SwitchPointer(BuildController ctl, BuildPointer bp, BuildingDef def, MultiInventory inventory, Vector3 position)
        {
            BuildData data = BuildData.GetDataForBuild(def);
            ctl.currentBuildData = data;
            ctl.gridStep = BuildConsts.BUILD_GRID_SIZE * def.customGridStep;
            bp.Disable();
            bp.Enable(data, ctl.CurrentWorldZone.Id, position, ResolveNeeds(def, inventory), inventory);
        }

        /// <summary>Same as BuildPointer.Rotate, but straight to the wanted state instead of the next one.</summary>
        private static bool SetPointerState(BuildPointer bp, WgoBuildPointer wp, BlueprintPiece piece)
        {
            WgoPart part = wp.Target.MainWgoPart;
            if (part == null || piece.Rotation < 0 || !wp.Target.CanBeRotated())
            {
                return true;
            }
            WgoPartData now = part.WgoPartData;
            bool belt = (piece.Wgo ?? "").StartsWith("conveyor_cell", StringComparison.Ordinal);
            if (now.rotationIndex == piece.Rotation && (belt || piece.Variation == null || now.variationId == piece.Variation))
            {
                return true;
            }
            bp.transform.localPosition = Vector3.zero;
            wp.transform.localPosition = Vector3.zero;
            wp.ClearSelectionCells();
            // Belts' variation (start/centre/end) only follows their connections: keep the preview's own.
            bool ok = ApplyState(part, belt ? now.variationId : piece.Variation, piece.Rotation);
            wp.SetupSelectionCells(new[] { bp.selectionCellPrefab, bp.buffCellPrefab }, wp.transform);
            wp.UpdateCollider();
            wp.ApplySelectionCellsVisuals();
            wp.UpdateSelectionCellsState();
            bp.CalculateShiftToVisualCenter();
            return ok && part.WgoPartData.rotationIndex == piece.Rotation;
        }

        private static MultiInventory NewInventory()
        {
            var inventory = new MultiInventory(MainGame.PlayerController.PlayerData);
            List<Inventory> extra = LazySingleton<BuildManager>.Instance.additionalInventories;
            if (extra != null)
            {
                foreach (Inventory inv in extra)
                {
                    inventory.Add(inv);
                }
            }
            return inventory;
        }

        /// <summary>Costs that name a group ("any plank") resolve to an item the player has, as the build menu does.</summary>
        private static List<NeedItemData> ResolveNeeds(BuildingDef def, MultiInventory inventory)
        {
            var list = new List<NeedItemData>();
            foreach (NeedItemData need in def.needItems ?? new List<NeedItemData>())
            {
                list.Add(need.groupType == ItemGroup.None ? need : new UICraftItemCellData(need, inventory, null).currentItem);
            }
            return list;
        }

        // ---------------------------------------------------------------- helpers

        private static void Add(Dictionary<string, int> counts, string key) =>
            counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;

        private static string CountList(Dictionary<string, int> counts) =>
            string.Join(", ", counts.Select(kv => kv.Value > 1 ? $"{Texts.Name(kv.Key)} ×{kv.Value}" : Texts.Name(kv.Key)));

        private static void Notify(string key, string text, string icon)
        {
            try
            {
                LazySingleton<UINotificator>.Instance.ShowSimpleTextWithIconNotification(Texts.Dynamic(key, text), icon);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Notification failed ({text}): {e.Message}");
            }
            Plugin.Debug(text);
        }
    }

    // -------------------------------------------------------------------- patches

    /// <summary>The blueprint moves in steps of its largest piece grid, so every piece lands on its own grid.</summary>
    [HarmonyPatch(typeof(BuildController), nameof(BuildController.EnableBuildMode))]
    internal static class BuildController_EnableBuildMode
    {
        private static void Prefix(BuildData buildData)
        {
            if (buildData?.Definition?.id == Content.BlueprintPlaceId)
            {
                buildData.Definition.customGridStep = Math.Max(1, BlueprintStore.Selected?.GridStep ?? 1);
            }
        }
    }

    [HarmonyPatch(typeof(BuildPointer), nameof(BuildPointer.Enable))]
    internal static class BuildPointer_Enable
    {
        private static void Postfix(BuildPointer __instance, BuildData buildData)
        {
            try
            {
                if (BlueprintMode.IsOurs(buildData))
                {
                    BlueprintMode.OnPointerEnabled(__instance);
                }
                else
                {
                    BlueprintMode.OnOtherPointerEnabled();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Blueprint pointer failed: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(BuildController), nameof(BuildController.DisableBuildMode))]
    internal static class BuildController_DisableBuildMode
    {
        private static void Prefix()
        {
            if (!BlueprintMode.Committing)
            {
                BlueprintMode.Exit();
            }
        }
    }

    [HarmonyPatch(typeof(WgoBuildPointer), nameof(WgoBuildPointer.UpdatePosition))]
    internal static class WgoBuildPointer_UpdatePosition
    {
        private static void Postfix(WgoBuildPointer __instance, Vector3 position)
        {
            if (!BlueprintMode.IsOurs(__instance.BuildData) || BlueprintMode.Committing)
            {
                return;
            }
            try
            {
                BlueprintMode.OnMoved(__instance, position);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Blueprint move failed: {e}");
            }
        }
    }

    /// <summary>Our tools never build their anchor chest: a click lays the blueprint or marks a corner.</summary>
    [HarmonyPatch(typeof(WgoBuildPointer), nameof(WgoBuildPointer.TryDoBuildAction))]
    internal static class WgoBuildPointer_TryDoBuildAction_Blueprint
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(WgoBuildPointer __instance, ref bool __result)
        {
            if (!BlueprintMode.IsOurs(__instance.BuildData))
            {
                return true;
            }
            __result = false;
            try
            {
                BlueprintMode.BuildClick();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Blueprint click failed: {e}");
            }
            return false;
        }
    }

    /// <summary>R pins the drawing to the last construction (Blueprint) or saves the marked pieces (Trace), never rotating the anchor.</summary>
    [HarmonyPatch(typeof(BuildPointer), nameof(BuildPointer.Rotate))]
    internal static class BuildPointer_Rotate
    {
        private static bool Prefix(BuildPointer __instance)
        {
            if (!BlueprintMode.IsOurs((__instance.PointerObject as BuildPointerObject)?.BuildData) || BlueprintMode.Committing)
            {
                return true;
            }
            try
            {
                BlueprintMode.TogglePin();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Blueprint pin failed: {e}");
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(WgoBuildPointer), nameof(WgoBuildPointer.HasRotation))]
    internal static class WgoBuildPointer_HasRotation
    {
        private static bool Prefix(WgoBuildPointer __instance, ref bool __result)
        {
            if (!BlueprintMode.IsOurs(__instance.BuildData))
            {
                return true;
            }
            __result = true;  // R: pins the drawing (Blueprint) or saves the marked pieces (Trace)
            return false;
        }
    }

    /// <summary>The game shows the pointer's selection cells again after each click or R; the anchor's stay hidden.</summary>
    [HarmonyPatch(typeof(BuildPointer), nameof(BuildPointer.SetVisibleSelectionCells))]
    internal static class BuildPointer_SetVisibleSelectionCells
    {
        private static void Postfix(BuildPointer __instance)
        {
            if (__instance.PointerObject is WgoBuildPointer wp && BlueprintMode.IsOurs(wp.BuildData))
            {
                BlueprintMode.HideCellsOf(wp);
            }
        }
    }

    [HarmonyPatch(typeof(BuildManager), nameof(BuildManager.EnableBuildMode))]
    internal static class BuildManager_EnableBuildMode
    {
        private static void Prefix() => BlueprintMode.OnBuildManagerEnable();
    }

    [HarmonyPatch(typeof(BuildManager), nameof(BuildManager.Disable))]
    internal static class BuildManager_Disable
    {
        private static bool Prefix()
        {
            try
            {
                return !BlueprintMode.TryLeaveToWorld();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Leaving the blueprint tool failed: {e}");
                return true;
            }
        }
    }

    /// <summary>The blueprint tools are in no build menu; the Architect's Desk shows once a Planning Board stands.</summary>
    [HarmonyPatch(typeof(BuildingDef), nameof(BuildingDef.GetBuildingsInBuilder))]
    internal static class BuildingDef_GetBuildingsInBuilder
    {
        private static void Postfix(List<BuildData> __result)
        {
            if (__result == null)
            {
                return;
            }
            __result.RemoveAll(d => d?.Definition != null && Content.IsBlueprintDef(d.Definition.id));
            if (Plugin.TestMode.Value)
            {
                return;
            }
            bool hasBoard = false;
            try
            {
                hasBoard = MainGame.WorldData.GetWgoDataByCustomTag(Content.DraftingTag) != null;
            }
            catch (Exception)
            {
                // no save loaded
            }
            if (!hasBoard)
            {
                __result.RemoveAll(d => d?.Definition?.id == Content.ArchitectBuildingId);
            }
        }
    }
}
