using System;
using System.Collections.Generic;
using System.Linq;
using GK2Mods.FactoryPlanner.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UButton = UnityEngine.UI.Button;
using UImage = UnityEngine.UI.Image;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// The board's factory map: the whole floor as it stands now, every piece clickable. Machines are coloured by
    /// what they are doing (working, waiting for ingredients, exit blocked, no recipe); the left column tells the
    /// picked piece's story: its queue, what its belts bring and what they don't, what a chest holds and how its
    /// sides are set.
    /// </summary>
    internal sealed partial class BoardWindow
    {
        private static readonly Color MapWorking = BoardUi.Ok;
        private static readonly Color MapWaiting = BoardUi.Gold;
        private static readonly Color MapBlocked = BoardUi.Short;
        private static readonly Color MapIdle = BoardUi.Hex("6b7285");
        private static readonly Color MapDemolish = BoardUi.Hex("b06ad6");
        private static readonly Color MapLoaded = BoardUi.Hex("596072");

        private RectTransform modeHolder;
        private bool mapMode;
        private SGuid picked;
        /// <summary>The picked piece is waiting for a click on its new spot.</summary>
        private bool moving;
        private string removeArmed;

        /// <summary>Takes the clicks on the floor itself (the empty ground between the pieces).</summary>
        private sealed class FloorClick : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
        {
            internal Action<Vector2> Clicked;

            public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
            {
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, eventData.position, eventData.pressEventCamera, out Vector2 local))
                {
                    BoardUi.Pressed(() => Clicked?.Invoke(new Vector2(local.x, -local.y)));
                }
            }
        }

        private void MoveTo(FactoryMap floor, FactoryMap.Piece piece, int c, int r)
        {
            int dc = c - (piece.Cells.MinC + piece.Cells.MaxC) / 2, dr = r - (piece.Cells.MinR + piece.Cells.MaxR) / 2;
            string problem = FloorActions.Move(floor, piece.Id, dc, dr);
            moving = problem != null;
            message = problem ?? Texts.Pt($"{Texts.Name(piece.WgoId)} mudou de lugar. As esteiras encostadas no lugar novo já estão ligadas.",
                $"{Texts.Name(piece.WgoId)} moved. The belts touching its new spot are linked.");
            Refill();
        }

        private void RemoveClick(FactoryMap.Piece piece)
        {
            string key = piece.Id.ToString();
            if (removeArmed != key && piece.State != FactoryMap.Work.Demolishing)
            {
                removeArmed = key;
                message = piece.Kind == FactoryMap.Kind.Machine
                    ? Texts.Pt("Remover marca a máquina para demolição: alguém precisa trabalhar nela, e os materiais caem no chão ao lado. Clique de novo para confirmar.",
                        "Removing marks the machine to be taken down: someone has to work on it, and the materials drop beside it. Click again to confirm.")
                    : Texts.Pt("Remover tira a peça na hora; o que ela custou e o que ela guarda caem no chão ao lado. Clique de novo para confirmar.",
                        "Removing takes the piece away at once; what it cost and what it holds drop beside it. Click again to confirm.");
                Refill();
                return;
            }
            removeArmed = null;
            FloorActions.Removal result = FloorActions.Remove(piece.Id);
            message = result == FloorActions.Removal.Gone ? Texts.Pt("Peça removida; os materiais estão no chão, no lugar dela.", "Piece removed; its materials are on the ground where it stood.")
                : result == FloorActions.Removal.Marked ? Texts.Pt("Máquina marcada para demolição.", "Machine marked to be taken down.")
                : result == FloorActions.Removal.Unmarked ? Texts.Pt("Demolição cancelada.", "Demolition called off.")
                : Texts.Pt("O jogo não deixa remover esta peça.", "The game doesn't let this piece be removed.");
            if (result == FloorActions.Removal.Gone)
            {
                picked = default;
            }
            Refill();
        }

        private static Color ColorOf(FactoryMap.Piece p)
        {
            switch (p.Kind)
            {
                case FactoryMap.Kind.Machine:
                    switch (p.State)
                    {
                        case FactoryMap.Work.Working: return MapWorking;
                        case FactoryMap.Work.Waiting: return MapWaiting;
                        case FactoryMap.Work.Blocked: return MapBlocked;
                        case FactoryMap.Work.Demolishing: return MapDemolish;
                        default: return MapIdle;
                    }
                case FactoryMap.Kind.Belt:
                    return p.Carrying != null ? MapLoaded : MapBelt;
                case FactoryMap.Kind.Other:
                    return MapOther;
                default:
                    return MapStock;
            }
        }

        private static bool Touches(CellRect a, CellRect b) => a.MinC <= b.MaxC && b.MinC <= a.MaxC && a.MinR <= b.MaxR && b.MinR <= a.MaxR;

        private static string StateText(FactoryMap.Work state)
        {
            switch (state)
            {
                case FactoryMap.Work.Working: return Texts.Pt("produzindo", "working");
                case FactoryMap.Work.Waiting: return Texts.Pt("esperando insumo", "waiting for ingredients");
                case FactoryMap.Work.Blocked: return Texts.Pt("saída travada", "exit blocked");
                case FactoryMap.Work.Demolishing: return Texts.Pt("marcada para demolir", "marked to be taken down");
                default: return Texts.Pt("sem receita", "no recipe");
            }
        }

        private string ProductName(string recipeId) =>
            recipeId != null && snap.Book.ById.TryGetValue(recipeId, out Recipe r) ? Texts.Name(r.OutItem) : recipeId ?? "-";

        private string Brief(FactoryMap.Piece p)
        {
            switch (p.Kind)
            {
                case FactoryMap.Kind.Machine:
                    return StateText(p.State) + (p.Recipe != null ? $": {ProductName(p.Recipe)}" : "");
                case FactoryMap.Kind.Belt:
                    return p.Carrying != null ? Texts.Pt("levando ", "carrying ") + Texts.Name(p.Carrying) : Texts.Pt("vazia", "empty");
                case FactoryMap.Kind.Chest:
                case FactoryMap.Kind.Reserve:
                    return p.Holds.Count == 0 ? Texts.Pt("vazio", "empty")
                        : string.Join(", ", p.Holds.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Value} × {Texts.Name(kv.Key)}"));
                default:
                    return null;
            }
        }

        private void FillFloor()
        {
            FactoryMap floor = Map;
            FactoryMap.Piece chosen = floor.Pieces.FirstOrDefault(p => p.Id.Equals(picked));
            List<FactoryMap.Piece> machines = floor.Pieces.Where(p => p.Kind == FactoryMap.Kind.Machine).ToList();
            rightTitle.text = Texts.Pt($"Fábrica agora · {machines.Count} máquinas", $"The factory now · {machines.Count} machines");
            leftTitle.text = chosen != null ? Texts.Name(chosen.WgoId) : Texts.Pt("Visão geral", "Overview");

            BoardUi.Clear(weekList);
            float h = ListH - 8f;
            DrawFloor(floor, chosen, 2f, h);
            weekList.sizeDelta = new Vector2(0f, h + 6f);

            BoardUi.Clear(orderList);
            float y = chosen != null ? PieceCard(floor, chosen, 0f) : Overview(floor, machines, 0f);
            y = Legend(y);
            orderList.sizeDelta = new Vector2(0f, y + 4f);
        }

        private void Pick(SGuid id)
        {
            picked = picked.Equals(id) ? default : id;
            moving = false;
            removeArmed = null;
            message = "";
            UITooltip.Hide();
            Refill();
            orderList.anchoredPosition = Vector2.zero;
        }

        // ---- the floor

        private void DrawFloor(FactoryMap floor, FactoryMap.Piece chosen, float y, float h)
        {
            float w = RightW - 12f;
            RectTransform panel = BoardUi.Rect(weekList, "Floor", 6f, y, w, h);
            UImage bg = panel.gameObject.AddComponent<UImage>();
            BoardUi.SetSprite(bg, "comm-value_frame_1", BoardUi.Field);
            bg.raycastTarget = true;
            if (floor.Empty || floor.Pieces.All(p => p.Kind == FactoryMap.Kind.Other))
            {
                BoardUi.Text(panel, "Empty", BoardUi.StyleSmall, 8f, 0f, w - 16f, h, BoardUi.Muted, TextAlignmentOptions.Center, true).text =
                    Texts.Pt("A fábrica não está carregada: abra o Quadro dentro dela.", "The factory isn't loaded: open the board inside it.");
                return;
            }
            bool placing = moving && chosen != null;
            // Only the pieces of the factory set the frame: decoration far away would shrink everything.
            List<FactoryMap.Piece> real = floor.Pieces.Where(p => p.Kind != FactoryMap.Kind.Other).ToList();
            CellRect frame = real.Count > 0 ? real.Skip(1).Aggregate(real[0].Cells, (a, p) => a.Union(p.Cells)) : floor.Area;
            frame = new CellRect(frame.MinC - 2, frame.MinR - 2, frame.MaxC + 2, frame.MaxR + 2);

            Vector2 up = floor.Up;
            var right = new Vector2(up.y, -up.x);
            Vector2 Uv(int c, int r) => new Vector2(c * right.x + r * right.y, c * up.x + r * up.y);
            Rect UvRect(CellRect cells)
            {
                Vector2 a = Uv(cells.MinC, cells.MinR), b = Uv(cells.MaxC, cells.MaxR);
                return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            }
            Rect area = UvRect(frame);
            float pad = 6f;
            float scale = Mathf.Min(Mathf.Min((w - 2f * pad) / (area.width + 1f), (h - 2f * pad) / (area.height + 1f)), 22f);
            float ox = (w - (area.width + 1f) * scale) / 2f, oy = (h - (area.height + 1f) * scale) / 2f;
            float inset = scale >= 4f ? 0.5f : 0f;

            RectTransform Box(string name, CellRect cells, Color color, float gap)
            {
                Rect r = UvRect(cells);
                float px = ox + (r.xMin - area.xMin) * scale + gap, py = oy + (area.yMax - r.yMax) * scale + gap;
                RectTransform rect = BoardUi.Rect(panel, name, px, py, Mathf.Max(1f, (r.width + 1f) * scale - 2f * gap), Mathf.Max(1f, (r.height + 1f) * scale - 2f * gap));
                rect.gameObject.AddComponent<UImage>().color = color;
                return rect;
            }

            // Belts first, then chests, machines on top: a click lands on the piece that matters.
            foreach (FactoryMap.Piece piece in floor.Pieces.OrderBy(p => p.Kind == FactoryMap.Kind.Other ? 0 : p.Kind == FactoryMap.Kind.Belt ? 1 : p.Kind == FactoryMap.Kind.Machine ? 3 : 2))
            {
                // Big things that aren't part of the line (rooms, floors) would only bury the map.
                if (!Touches(frame, piece.Cells) || (piece.Kind == FactoryMap.Kind.Other && piece.Cells.Width * piece.Cells.Height > 12))
                {
                    continue;
                }
                RectTransform rect = Box("P", piece.Cells, ColorOf(piece), inset);
                UImage img = rect.GetComponent<UImage>();
                if (piece.Kind == FactoryMap.Kind.Other || placing)
                {
                    img.raycastTarget = false;  // while a piece is being placed, every click is a spot on the floor
                    if (piece.Kind == FactoryMap.Kind.Other)
                    {
                        continue;
                    }
                }
                if (placing)
                {
                    continue;
                }
                img.raycastTarget = true;
                UButton button = rect.gameObject.AddComponent<UButton>();
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                SGuid id = piece.Id;
                button.onClick.AddListener(() => BoardUi.Pressed(() => Pick(id)));
                BoardUi.Tip(rect.gameObject, Texts.Name(piece.WgoId), Brief(piece));
                if (piece.Kind == FactoryMap.Kind.Machine && piece.Product != null && rect.sizeDelta.x >= 18f && rect.sizeDelta.y >= 18f)
                {
                    float size = Mathf.Min(Mathf.Min(rect.sizeDelta.x, rect.sizeDelta.y) - 4f, 28f);
                    UImage icon = BoardUi.ItemIcon(rect, piece.Product, (rect.sizeDelta.x - size) / 2f, (rect.sizeDelta.y - size) / 2f, size);
                    if (icon != null)
                    {
                        icon.raycastTarget = false;
                    }
                }
            }
            if (floor.Board != null && Touches(frame, floor.Board.Cells))
            {
                RectTransform board = Box("Board", floor.Board.Cells, MapBoard, 0f);
                board.GetComponent<UImage>().raycastTarget = true;
                BoardUi.Tip(board.gameObject, Texts.Pt("Você está aqui", "You are here"), Texts.Pt("O Quadro de Planejamento.", "The Planning Board."));
            }
            if (placing)
            {
                FloorClick click = panel.gameObject.AddComponent<FloorClick>();
                click.Clicked = at =>
                {
                    // Back from the panel to the grid: the square under the pointer, then the cell it stands for.
                    int u = Mathf.FloorToInt((at.x - ox) / scale + area.xMin), v = Mathf.CeilToInt(area.yMax - (at.y - oy) / scale);
                    int c = Mathf.RoundToInt(u * right.x + v * up.x), r = Mathf.RoundToInt(u * right.y + v * up.y);
                    MoveTo(floor, chosen, c, r);
                };
                BoardUi.Text(panel, "Hint", BoardUi.StyleSmall, 8f, 4f, w - 16f, 18f, BoardUi.Gold, TextAlignmentOptions.Center).text =
                    Texts.Pt("Clique no chão livre onde a peça deve ficar", "Click the free ground where the piece should go");
            }
            if (chosen != null)
            {
                RectTransform mark = Box("Picked", chosen.Cells, new Color(1f, 1f, 1f, 0f), -2f);
                mark.GetComponent<UImage>().raycastTarget = false;
                UImage ring = BoardUi.Sliced(mark, "Frame", 0f, 0f, 10f, 10f, BoardUi.FrameSprite, BoardUi.Gold);
                BoardUi.Stretch(ring.rectTransform);
                ring.raycastTarget = false;
            }
        }

        // ---- the left column

        private float Line(float y, string text, Color color, float h = 20f)
        {
            RectTransform row = BoardUi.Row(orderList, "Line", y, h, null, false, null);
            row.GetComponent<UImage>().raycastTarget = false;
            BoardUi.Text(row, "Text", BoardUi.StyleSmall, 8f, 0f, LeftW - 16f, h, color, TextAlignmentOptions.Left, true).text = text;
            return y + h;
        }

        private float ItemLine(float y, string item, string text, Color color)
        {
            RectTransform row = BoardUi.Row(orderList, "Item", y, 22f, null, false, null);
            row.GetComponent<UImage>().raycastTarget = false;
            BoardUi.ItemIcon(row, item, 8f, 2f, 18f);
            BoardUi.Text(row, "Text", BoardUi.StyleSmall, 30f, 0f, LeftW - 38f, 22f, color).text = text;
            return y + 22f;
        }

        private float Overview(FactoryMap floor, List<FactoryMap.Piece> machines, float y)
        {
            y = Group(orderList, y, Texts.Pt("Máquinas", "Machines"));
            foreach (FactoryMap.Work state in new[] { FactoryMap.Work.Working, FactoryMap.Work.Waiting, FactoryMap.Work.Blocked, FactoryMap.Work.Idle, FactoryMap.Work.Demolishing })
            {
                int n = machines.Count(m => m.State == state);
                if (n > 0)
                {
                    y = Line(y, $"{n} · {StateText(state)}", ColorOf(new FactoryMap.Piece { Kind = FactoryMap.Kind.Machine, State = state }));
                }
            }
            bool power = MainGame.Instance?.conveyorSystem?.HasEnoughPower ?? true;
            y = Line(y + 4f, power ? Texts.Pt("Energia: suficiente", "Power: enough") : Texts.Pt("SEM ENERGIA: a fábrica toda está parada", "NO POWER: the whole factory is stopped"),
                power ? BoardUi.Muted : BoardUi.Short);
            List<FactoryMap.Piece> trouble = machines.Where(m => m.State == FactoryMap.Work.Blocked || m.State == FactoryMap.Work.Waiting).ToList();
            if (trouble.Count > 0)
            {
                y = Group(orderList, y + 4f, Texts.Pt("Paradas (clique para ver)", "Stopped (click to see)"));
                foreach (FactoryMap.Piece m in trouble.OrderBy(m => m.State == FactoryMap.Work.Blocked ? 0 : 1).Take(12))
                {
                    SGuid id = m.Id;
                    RectTransform row = BoardUi.Row(orderList, "Trouble", y + 2f, 36f, BoardUi.PlateSprite, false, () => Pick(id));
                    BoardUi.ItemIcon(row, m.Product, 6f, 6f, 24f);
                    BoardUi.Text(row, "Name", BoardUi.StyleText, 36f, 2f, LeftW - 44f, 16f, BoardUi.Ink).text = ProductName(m.Recipe);
                    BoardUi.Text(row, "Why", BoardUi.StyleSmall, 36f, 18f, LeftW - 44f, 16f, ColorOf(m)).text = Why(m);
                    y += 40f;
                }
            }
            RectTransform again = BoardUi.Row(orderList, "Refresh", y + 4f, 26f, null, false, null);
            BoardUi.Button(again, "Go", 4f, 2f, LeftW - 16f, 22f, Texts.Pt("Atualizar o mapa", "Refresh the map"), false, Refill);
            y += 32f;
            y = Line(y + 4f, Texts.Pt("Clique numa peça do mapa para ver o que ela faz, o que recebe e o que falta.",
                "Click a piece on the map to see what it does, what reaches it and what is missing."), BoardUi.Muted, 44f);
            return y;
        }

        /// <summary>What reaches a machine by belt, under the recipes the machines are on now.</summary>
        private FeedGraph.Arrival Reaching(FactoryMap floor, FactoryMap.Piece machine)
        {
            if (snap.Feed == null || !snap.Feed.Nodes.ContainsKey(machine.Id.ToString()))
            {
                return null;
            }
            Dictionary<string, string> product = floor.Pieces.Where(p => p.Kind == FactoryMap.Kind.Machine && p.Product != null)
                .GroupBy(p => p.Id.ToString()).ToDictionary(g => g.Key, g => g.First().Product);
            return snap.Feed.Arriving(machine.Id.ToString(), k => product.TryGetValue(k, out string item) ? item : null);
        }

        /// <summary>One line on why a machine isn't working.</summary>
        private string Why(FactoryMap.Piece m)
        {
            if (m.State == FactoryMap.Work.Blocked)
            {
                if (m.NoExit)
                {
                    return Texts.Pt("sem esteira de saída: o produto fica preso na máquina", "no exit belt: its product stays inside");
                }
                if (m.EndsInChestsOnly && m.Product != null && m.Product.StartsWith("box_", StringComparison.Ordinal))
                {
                    return Texts.Pt("a saída termina num Baú de Esteira, que não aceita caixa: ligue a uma estação de entrega",
                        "the exit ends in a Conveyor Chest, which takes no boxes: link it to a delivery station");
                }
                return Texts.Pt("o produto não sai: a esteira de saída está cheia", "its product can't leave: the exit belt is full");
            }
            if (m.State != FactoryMap.Work.Waiting || m.Recipe == null || !snap.Book.ById.TryGetValue(m.Recipe, out Recipe recipe))
            {
                return StateText(m.State);
            }
            FeedGraph.Arrival arrival = Reaching(Map, m);
            List<Ingredient> lacking = recipe.Needs.Where(n => (m.Inside.TryGetValue(n.Item, out int has) ? has : 0) < n.Count).ToList();
            List<string> never = arrival == null ? new List<string>() : lacking.Where(n => !arrival.Items.Contains(n.Item)).Select(n => n.Item).ToList();
            if (never.Count > 0)
            {
                return Texts.Pt("nenhuma esteira traz ", "no belt brings ") + Names(never);
            }
            return lacking.Count > 0 ? Texts.Pt("falta chegar ", "still to arrive: ") + Names(lacking.Select(n => n.Item)) : StateText(m.State);
        }

        private float PieceCard(FactoryMap floor, FactoryMap.Piece p, float y)
        {
            RectTransform back = BoardUi.Row(orderList, "Back", y + 2f, 26f, null, false, null);
            BoardUi.Button(back, "Overview", 4f, 2f, 110f, 22f, Texts.Pt("< Visão geral", "< Overview"), false, () => Pick(p.Id));
            y += 30f;
            if (p.Kind == FactoryMap.Kind.Machine)
            {
                y = Line(y, StateText(p.State).ToUpperInvariant(), ColorOf(p));
                if (p.State == FactoryMap.Work.Waiting || p.State == FactoryMap.Work.Blocked)
                {
                    y = Line(y, Why(p), ColorOf(p), 34f);
                }
                if (p.ExitEnds.Count > 0)
                {
                    y = Line(y, Texts.Pt("Sai para: ", "Goes to: ") + string.Join(", ", p.ExitEnds.Distinct().Take(3).Select(Texts.Name)), BoardUi.Muted, 34f);
                }
                y = Group(orderList, y, Texts.Pt("Fila", "Queue"));
                if (p.Queue.Count == 0)
                {
                    y = Line(y, Texts.Pt("vazia", "empty"), BoardUi.Muted);
                }
                for (int i = 0; i < p.Queue.Count; i++)
                {
                    (string id, int left) = p.Queue[i];
                    string item = snap.Book.ById.TryGetValue(id, out Recipe q) ? q.OutItem : null;
                    y = ItemLine(y, item, $"{ProductName(id)} · {(left < 0 ? Texts.Pt("sem fim", "endless") : Texts.Pt($"faltam {left}", $"{left} to go"))}",
                        i == 0 ? BoardUi.Ink : BoardUi.Muted);
                }
                if (p.Recipe != null && snap.Book.ById.TryGetValue(p.Recipe, out Recipe recipe))
                {
                    FeedGraph.Arrival arrival = Reaching(floor, p);
                    y = Group(orderList, y, Texts.Pt("Insumos da receita", "The recipe's ingredients"));
                    foreach (Ingredient need in recipe.Needs)
                    {
                        int has = p.Inside.TryGetValue(need.Item, out int n) ? n : 0;
                        bool arrives = arrival == null || arrival.Items.Contains(need.Item);
                        y = ItemLine(y, need.Item, $"{has}/{need.Count} {Texts.Name(need.Item)}" + (arrives ? "" : Texts.Pt(" · não chega", " · doesn't arrive")),
                            !arrives ? BoardUi.Short : has >= need.Count ? BoardUi.Ok : BoardUi.Gold);
                    }
                    if (arrival != null)
                    {
                        List<string> extra = arrival.Pushed.Where(i => recipe.Needs.All(n => n.Item != i)).ToList();
                        if (extra.Count > 0)
                        {
                            y = Line(y, Texts.Pt("Chega também (trava a esteira): ", "Also arrives (blocks the belt): ") + Names(extra), BoardUi.Short, 34f);
                        }
                    }
                }
                y = Line(y + 2f, p.HasWorker ? Texts.Pt("Zumbi no posto", "A zombie works it") : Texts.Pt("Sem zumbi", "No zombie"), BoardUi.Muted);
                if (p.Recipe != null)
                {
                    RectTransform act = BoardUi.Row(orderList, "Chain", y + 2f, 26f, null, false, null);
                    string recipeId = p.Recipe;
                    BoardUi.Button(act, "Open", 4f, 2f, LeftW - 16f, 22f, Texts.Pt("Ver a cadeia desta receita", "See this recipe's chain"), false, () =>
                    {
                        mapMode = false;
                        OpenChain(recipeId);
                    });
                    y += 30f;
                }
            }
            else if (p.Kind == FactoryMap.Kind.Belt)
            {
                y = Line(y, Brief(p), BoardUi.Ink);
            }
            else
            {
                y = Group(orderList, y, Texts.Pt("Guarda", "Holds"));
                if (p.Holds.Count == 0)
                {
                    y = Line(y, Texts.Pt("vazio", "empty"), BoardUi.Muted);
                }
                foreach (var kv in p.Holds.OrderByDescending(kv => kv.Value).Take(12))
                {
                    y = ItemLine(y, kv.Key, $"{kv.Value} × {Texts.Name(kv.Key)}", BoardUi.Ink);
                }
                y = SidesOf(p, y);
            }
            if (p.Kind != FactoryMap.Kind.Other)
            {
                bool removable = FloorActions.CanRemove(p.Id);
                float half = (LeftW - 20f) / 2f;
                RectTransform acts = BoardUi.Row(orderList, "Acts", y + 2f, 26f, null, false, null);
                BoardUi.ButtonParts move = BoardUi.Button(acts, "Move", 4f, 2f, half, 22f, moving ? Texts.Pt("Cancelar", "Cancel") : Texts.Pt("Mover", "Move"), false, () =>
                {
                    moving = !moving;
                    removeArmed = null;
                    message = moving ? Texts.Pt("Clique no mapa, num chão livre, para onde a peça vai. Ela leva junto extensões, zumbi e receita.",
                        "Click free ground on the map for the piece's new spot. Its add-ons, zombie and recipe go with it.") : "";
                    Refill();
                }, removable && p.State != FactoryMap.Work.Demolishing);
                BoardUi.Tip(move.Root.gameObject, Texts.Pt("Mover", "Move"),
                    Texts.Pt("Muda a peça de lugar como a ferramenta Mover do jogo: sem custo, mantém receita, estoque, extensões e zumbi. As esteiras só se ligam de novo se encostarem no lugar novo.",
                        "Moves the piece like the game's Move tool: no cost; recipe, stock, add-ons and zombie stay. Belts link again only if they touch the new spot."));
                BoardUi.ButtonParts remove = BoardUi.Button(acts, "Remove", 8f + half, 2f, half, 22f,
                    p.State == FactoryMap.Work.Demolishing ? Texts.Pt("Não demolir", "Keep it") : removeArmed == p.Id.ToString() ? Texts.Pt("Confirmar?", "Confirm?") : Texts.Pt("Remover", "Remove"),
                    false, () => RemoveClick(p), removable);
                BoardUi.Tip(remove.Root.gameObject, Texts.Pt("Remover", "Remove"),
                    Texts.Pt("Como a ferramenta Remover do jogo: esteira e baú saem na hora e largam o que custaram; máquina fica marcada e precisa de trabalho para ser demolida.",
                        "Like the game's Remove tool: belts and chests go at once and drop what they cost; a machine is marked and takes work to bring down."));
                y += 30f;
            }
            RectTransform show = BoardUi.Row(orderList, "Show", y + 2f, 26f, null, false, null);
            BoardUi.Button(show, "Go", 4f, 2f, LeftW - 16f, 22f, Texts.Pt("Mostrar na fábrica", "Show in the factory"), false, () =>
            {
                Close();
                FactoryMap.Tint(new[] { (p.Id, BoardUi.Gold) }, 20f);
            });
            return y + 32f;
        }

        /// <summary>How each side of a conveyor chest is set.</summary>
        private float SidesOf(FactoryMap.Piece p, float y)
        {
            var data = MainGame.WorldData?.GetWgoData(p.Id) as ConveyorWgoData;
            List<ConveyorChestSlotData> slots = data?.ConveyorComponent is ConveyorChestComponent c ? c.SlotsData
                : data?.ConveyorComponent is ConveyorChestOutComponent o ? o.SlotsData : null;
            List<ConveyorChestSlotData> linked = slots?.Where(s => s != null && !SGuid.IsNullOrEmpty(s.conveyorWgoDataUniqueId)).ToList();
            if (linked == null || linked.Count == 0)
            {
                return y;
            }
            y = Group(orderList, y, Texts.Pt("Lados", "Sides"));
            foreach (ConveyorChestSlotData slot in linked)
            {
                bool input = data.ConveyorComponent.ParentsData.Keys.Any(k => k == slot.conveyorWgoDataUniqueId);
                SideMode mode = Sides.ModeOf(slot, out List<string> items);
                string what = input ? Texts.Pt("entrada", "input")
                    : mode == SideMode.Items ? Names(items)
                    : mode == SideMode.Demand ? Texts.Pt("sob demanda", "on demand")
                    : mode == SideMode.Rest ? Texts.Pt("o resto", "the rest")
                    : mode == SideMode.Blocked ? Texts.Pt("bloqueado", "blocked") : Texts.Pt("qualquer item", "any item");
                y = Line(y, $"{SideName(slot.slotPosDirection.ToString())}: {what}", input ? BoardUi.Muted : BoardUi.Ink);
            }
            return y;
        }

        private static string SideName(string direction) =>
            direction == "Left" ? Texts.Pt("Esquerda", "Left") : direction == "Right" ? Texts.Pt("Direita", "Right")
                : direction == "Up" ? Texts.Pt("Cima", "Up") : direction == "Down" ? Texts.Pt("Baixo", "Down") : direction;

        private float Legend(float y)
        {
            y = Group(orderList, y + 4f, Texts.Pt("Legenda", "Legend"));
            (Color color, string text)[] rows =
            {
                (MapWorking, Texts.Pt("máquina produzindo", "machine working")),
                (MapWaiting, Texts.Pt("máquina esperando insumo", "machine waiting for ingredients")),
                (MapBlocked, Texts.Pt("máquina com a saída travada", "machine with its exit blocked")),
                (MapIdle, Texts.Pt("máquina sem receita", "machine with no recipe")),
                (MapStock, Texts.Pt("baú ou reserva", "chest or reserve")),
                (MapLoaded, Texts.Pt("esteira com item", "belt carrying an item")),
                (MapBelt, Texts.Pt("esteira vazia", "empty belt")),
                (MapBoard, Texts.Pt("o Quadro (você)", "the Board (you)")),
            };
            foreach ((Color color, string text) in rows)
            {
                RectTransform row = BoardUi.Row(orderList, "Legend", y, 18f, null, false, null);
                row.GetComponent<UImage>().raycastTarget = false;
                RectTransform dot = BoardUi.Rect(row, "Dot", 10f, 4f, 10f, 10f);
                dot.gameObject.AddComponent<UImage>().color = color;
                BoardUi.Text(row, "Text", BoardUi.StyleSmall, 28f, 0f, LeftW - 36f, 18f, BoardUi.Muted).text = text;
                y += 18f;
            }
            return y;
        }
    }
}
