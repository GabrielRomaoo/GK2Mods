using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GK2Mods.FactoryPlanner.Blueprints;
using GK2Mods.FactoryPlanner.Core;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UImage = UnityEngine.UI.Image;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// The board's chain view: one product, what it takes and where each ingredient comes out in the factory
    /// today, where the product goes, and a map of the factory with the best free spot for one more machine.
    /// Clicking an ingredient walks down the chain; "Back" walks up.
    /// </summary>
    internal sealed partial class BoardWindow
    {
        private static readonly Color[] IngredientColors =
        {
            BoardUi.Hex("f0a050"), BoardUi.Hex("5ec8d8"), BoardUi.Hex("c58cf0"), BoardUi.Hex("e8e070"), BoardUi.Hex("f07890"),
        };
        private static readonly Color MapMachine = BoardUi.Hex("596072");
        private static readonly Color MapBelt = BoardUi.Hex("3a3f4c");
        private static readonly Color MapStock = BoardUi.Hex("7a6a50");
        private static readonly Color MapOther = BoardUi.Hex("2a2e38");
        private static readonly Color MapBoard = BoardUi.Hex("ffffff");

        /// <summary>Recipe the chain view shows, or null for the week.</summary>
        private string focus;
        private readonly Stack<string> trail = new Stack<string>();
        private SGuid boardId;
        private FactoryMap map;

        private FactoryMap Map => map ?? (map = FactoryMap.Read(snap.Book, boardId));

        private void OpenChain(string recipeId)
        {
            if (recipeId == null || !snap.Book.ById.ContainsKey(recipeId))
            {
                return;
            }
            if (focus != null && focus != recipeId)
            {
                trail.Push(focus);
            }
            focus = recipeId;
            armed = null;
            UITooltip.Hide();
            Refill();
            weekList.anchoredPosition = Vector2.zero;
        }

        private void ChainBack()
        {
            focus = trail.Count > 0 ? trail.Pop() : null;
            UITooltip.Hide();
            Refill();
            weekList.anchoredPosition = Vector2.zero;
        }

        private int CraftsOf(string recipeId) =>
            (snap.Week.Crafts.TryGetValue(recipeId, out int c) ? c : 0) + (snap.Week.Own.TryGetValue(recipeId, out int o) ? o : 0);

        /// <summary>The recipe is here for an own order only (no stage of the week uses its output).</summary>
        private bool OwnOnly(string recipeId) => snap.Week.Own.ContainsKey(recipeId) && !snap.Week.Crafts.ContainsKey(recipeId);

        private Stage StageOf(string recipeId) => snap.Stages.Stages.FirstOrDefault(s => s.Steps.Any(st => st.Recipe.Id == recipeId));

        /// <summary>The recipe the plan uses for an item made in the factory, or null for raw / brought-in items.</summary>
        private Recipe MakerRecipe(string item) =>
            snap.Options.External.Contains(item) || !snap.Book.IsMade(item) ? null
                : snap.Week.RecipeOrder.Select(id => snap.Book.ById[id]).FirstOrDefault(r => r.OutItem == item)
                  ?? snap.Book.Choose(item, snap.Factory);

        private void FillChain()
        {
            Recipe recipe = snap.Book.ById[focus];
            int crafts = CraftsOf(recipe.Id);
            Stage stage = StageOf(recipe.Id);
            FactoryMap floor = Map;
            float w = RightW;
            rightTitle.text = Texts.Pt($"Cadeia · {Texts.Name(recipe.OutItem)}", $"Chain · {Texts.Name(recipe.OutItem)}");

            // Header: back, then the product.
            float y = 0f;
            RectTransform head = BoardUi.Row(weekList, "ChainHead", y + 2f, 30f, BoardUi.HeaderSprite, false, null);
            BoardUi.Button(head, "Back", 6f, 4f, 76f, 22f, trail.Count > 0 ? Texts.Pt("< Voltar", "< Back") : Texts.Pt("< Semana", "< Week"), false, ChainBack);
            string path = string.Join(" < ", new[] { recipe.Id }.Concat(trail).Take(3).Select(id => Texts.Name(snap.Book.ById[id].OutItem)));
            BoardUi.Text(head, "Path", BoardUi.StyleSmall, 90f, 0f, w - 100f, 30f, BoardUi.Hex("f6e6c4")).text = path;
            y += 36f;

            List<FactoryMap.Piece> running = floor.Running(recipe.Id).ToList();
            RectTransform card = BoardUi.Row(weekList, "Product", y + 2f, 58f, BoardUi.PlateSprite, false, null);
            BoardUi.ItemIcon(card, recipe.OutItem, 8f, 8f, 40f);
            BoardUi.Text(card, "Name", BoardUi.StyleBold, 56f, 3f, w - 64f, 20f, BoardUi.Gold).text = Texts.Name(recipe.OutItem);
            string machine = Texts.Name(recipe.Station) + (recipe.Extension != null ? " + " + Texts.Name(recipe.Extension) : "");
            string where = stage != null ? $"{Texts.Pt("etapa", "stage")} {stage.Index} · " : "";
            BoardUi.Text(card, "Machine", BoardUi.StyleSmall, 56f, 22f, w - 64f, 16f, BoardUi.Ink).text =
                where + machine + " · " + (crafts > 0
                    ? Texts.Pt($"{crafts * recipe.OutCount} na semana ({crafts} craft(s))", $"{crafts * recipe.OutCount} a week ({crafts} craft(s))")
                    : Texts.Pt("fora da semana", "not in the week"));
            BoardUi.Text(card, "Today", BoardUi.StyleSmall, 56f, 38f, w - 64f, 16f, running.Count > 0 ? BoardUi.Ok : BoardUi.Short).text = running.Count > 0
                ? Texts.Pt($"Hoje: {running.Count} máquina(s) fazem isto (verde no mapa)", $"Today: {running.Count} machine(s) make this (green on the map)")
                : !recipe.Unlocked
                    ? Texts.Pt("Receita ainda não pesquisada", "Recipe not researched yet") + (recipe.TechId != null ? $": {Texts.Name(recipe.TechId)}" : "")
                    : Texts.Pt("Hoje: nenhuma máquina faz isto", "Today: no machine makes this");
            y += 64f;

            // What it takes.
            y = Group(weekList, y, crafts > 0 ? Texts.Pt("Precisa, por semana (clique para ver de onde vem)", "Takes, a week (click to see where it comes from)")
                : Texts.Pt("Precisa, por craft (clique para ver de onde vem)", "Takes, per craft (click to see where it comes from)"));
            var colors = new Dictionary<string, Color>();
            for (int i = 0; i < recipe.Needs.Count; i++)
            {
                Ingredient need = recipe.Needs[i];
                colors[need.Item] = IngredientColors[i % IngredientColors.Length];
                y = IngredientRow(need, crafts, colors[need.Item], floor, y);
            }

            // Where it goes.
            y = Group(weekList, y, Texts.Pt("Vai para", "Goes to"));
            List<Recipe> users = snap.Week.RecipeOrder.Select(id => snap.Book.ById[id]).Where(r => r.Needs.Any(n => n.Item == recipe.OutItem)).ToList();
            foreach (Recipe user in users)
            {
                int amount = CraftsOf(user.Id) * user.Needs.Where(n => n.Item == recipe.OutItem).Sum(n => n.Count);
                y = LinkRow(user.OutItem, Texts.Name(user.OutItem) + (amount > 0 ? $" ×{amount}" : ""),
                    Texts.Pt($"{Texts.Name(user.Station)} · ", $"{Texts.Name(user.Station)} · ") +
                    (floor.Running(user.Id).Any() ? Texts.Pt($"{floor.Running(user.Id).Count()} máquina(s) hoje (azul no mapa)", $"{floor.Running(user.Id).Count()} machine(s) today (blue on the map)")
                        : Texts.Pt("nenhuma máquina hoje", "no machine today")),
                    BoardUi.Plan, () => OpenChain(user.Id), y);
            }
            List<Order> orders = snap.Planned.Where(o => o.BoxItem == recipe.OutItem).ToList();
            foreach (Order o in orders)
            {
                y = LinkRow(o.BoxItem, $"{Texts.Name(o.VendorId)} ×{o.Count}",
                    Texts.Pt("pedido da semana: caixas nos paletes do armazém", "the week's order: boxes on the warehouse pallets"), BoardUi.Ink, null, y);
            }
            bool ownOrder = snap.Options.Own.TryGetValue(recipe.OutItem, out int ownAmount);
            if (ownOrder)
            {
                y = LinkRow(recipe.OutItem, Texts.Pt($"Pedido avulso ×{ownAmount}", $"Own order ×{ownAmount}"),
                    Texts.Pt("numa máquina só dele; fica no baú da saída", "on a machine of its own; it stays in the exit chest"), BoardUi.Gold, null, y);
            }
            if (users.Count == 0 && orders.Count == 0 && !ownOrder)
            {
                y = LinkRow(null, Texts.Pt("Nada na semana usa isto", "Nothing in the week uses this"), "", BoardUi.Muted, null, y);
            }

            // Where to put one more machine.
            y = Group(weekList, y, running.Count > 0 ? Texts.Pt("Onde pôr mais uma máquina", "Where to put one more machine") : Texts.Pt("Onde pôr a máquina", "Where to put the machine"));
            Placement spot = floor.Suggest(recipe, snap.Book);
            float mapH = Mathf.Clamp(ListH - 120f, 170f, 260f);
            y = DrawMap(floor, recipe, colors, spot, y + 2f, mapH);
            string note = MapNote(floor, recipe, spot, colors.Keys.ToList());
            TMP_Text noteText = BoardUi.Text(weekList, "MapNote", BoardUi.StyleSmall, 6f, y, w - 12f, 60f, BoardUi.Ink, TextAlignmentOptions.TopLeft, true);
            noteText.text = note;
            float noteH = Mathf.Clamp(noteText.GetPreferredValues(note, w - 12f, 0f).y + 4f, 18f, 90f);
            BoardUi.Place(noteText.rectTransform, 6f, y, w - 12f, noteH);
            y += noteH + 4f;

            RectTransform buttons = BoardUi.Row(weekList, "Actions", y, 30f, null, false, null);
            BoardUi.ButtonParts module = BoardUi.Button(buttons, "Module", 6f, 2f, 170f, 26f, Texts.Pt("Montar o módulo", "Lay its module"), false,
                () => LayModule(recipe, stage, colors), recipe.Unlocked);
            BoardUi.Tip(module.Root.gameObject, Texts.Pt("Montar o módulo", "Lay its module"),
                Texts.Pt("Fecha o Quadro e abre a Planta com o módulo desta peça (baú atrás de cada entrada, máquina, baú na saída). As fontes dos insumos ficam pintadas na fábrica por um minuto, com as cores do mapa.",
                    "Closes the board and opens the Blueprint tool with this product's module (a chest behind each input, the machine, a chest at the exit). The ingredients' sources stay tinted in the factory for a minute, in the map's colours."));
            BoardUi.ButtonParts show = BoardUi.Button(buttons, "Show", 184f, 2f, 170f, 26f, Texts.Pt("Mostrar na fábrica", "Show in the factory"), false,
                () => ShowInFactory(recipe, colors, 45f));
            BoardUi.Tip(show.Root.gameObject, Texts.Pt("Mostrar na fábrica", "Show in the factory"),
                Texts.Pt("Fecha o Quadro e pinta, por 45 segundos, as máquinas e baús de onde saem os insumos (cores do mapa), as que já fazem isto (verde) e as que usam (azul).",
                    "Closes the board and tints, for 45 seconds, the machines and chests the ingredients come out of (the map's colours), those already making this (green) and those using it (blue)."));
            y += 36f;
            weekList.sizeDelta = new Vector2(0f, y + 4f);
        }

        private float IngredientRow(Ingredient need, int crafts, Color color, FactoryMap floor, float y)
        {
            int amount = need.Count * Math.Max(1, crafts);
            Recipe maker = MakerRecipe(need.Item);
            int makers = floor.Makers(need.Item).Count();
            int held = floor.Held(need.Item);
            string sub;
            Color subColor;
            if (maker != null)
            {
                int at = snap.Stages.MadeAt.TryGetValue(need.Item, out int s) ? s : 0;
                string stageText = at > 0 ? Texts.Pt($"etapa {at} · ", $"stage {at} · ") : "";
                sub = stageText + Texts.Name(maker.Station) + " · " + (makers > 0
                    ? Texts.Pt($"{makers} máquina(s) fazem hoje", $"{makers} machine(s) make it today")
                    : Texts.Pt("nenhuma máquina faz hoje", "no machine makes it today"));
                if (held > 0)
                {
                    sub += Texts.Pt($" · {held} em baús", $" · {held} in chests");
                }
                subColor = makers > 0 ? BoardUi.Ok : held >= amount ? BoardUi.Ink : BoardUi.Short;
            }
            else
            {
                bool external = snap.Options.External.Contains(need.Item);
                sub = (external ? Texts.Pt("trazido pronto", "brought in") : Texts.Pt("matéria-prima", "raw material")) +
                      Texts.Pt($" · {held} nas reservas e baús", $" · {held} in the reserves and chests");
                subColor = held >= amount ? BoardUi.Ink : BoardUi.Short;
            }
            string recipeId = maker?.Id;
            RectTransform row = BoardUi.Row(weekList, "Need_" + need.Item, y + 2f, 40f, BoardUi.PlateSprite, false,
                recipeId != null ? (Action)(() => OpenChain(recipeId)) : null);
            UImage swatch = BoardUi.Rect(row, "Colour", 4f, 6f, 4f, 28f).gameObject.AddComponent<UImage>();
            swatch.color = color;
            swatch.raycastTarget = false;
            BoardUi.ItemIcon(row, need.Item, 12f, 6f, 28f);
            BoardUi.Text(row, "Name", BoardUi.StyleText, 46f, 3f, RightW - 54f, 18f, BoardUi.Ink).text =
                $"{Texts.Name(need.Item)} ×{amount}" + (recipeId != null ? "  <color=#9aa0ae>></color>" : "");
            BoardUi.Text(row, "Sub", BoardUi.StyleSmall, 46f, 21f, RightW - 54f, 16f, subColor).text = sub;
            var body = new StringBuilder();
            body.Append(Texts.Pt($"Cada craft pede {need.Count}.", $"Each craft takes {need.Count}."));
            body.Append(maker != null
                ? Texts.Pt($"\nFeito por {Texts.Name(maker.Station)}: " + string.Join(", ", maker.Needs.Select(n => $"{n.Count} × {Texts.Name(n.Item)}")),
                    $"\nMade by {Texts.Name(maker.Station)}: " + string.Join(", ", maker.Needs.Select(n => $"{n.Count} × {Texts.Name(n.Item)}")))
                : Texts.Pt("\nNão é feito na fábrica: vem das reservas que os zumbis enchem.", "\nNot made in the factory: it comes from the reserves the zombies fill."));
            body.Append(Texts.Pt("\nNo mapa: as peças desta cor.", "\nOn the map: the pieces in this colour."));
            if (recipeId != null)
            {
                body.Append(Texts.Pt("\n\nClique para ver a cadeia dele.", "\n\nClick to see its own chain."));
            }
            BoardUi.Tip(row.gameObject, Texts.Name(need.Item), body.ToString());
            return y + 44f;
        }

        private float LinkRow(string icon, string name, string sub, Color color, Action click, float y)
        {
            RectTransform row = BoardUi.Row(weekList, "Link", y + 2f, 36f, BoardUi.PlateSprite, false, click);
            float x = 12f;
            if (icon != null && BoardUi.ItemIcon(row, icon, 10f, 5f, 26f) != null)
            {
                x = 44f;
            }
            BoardUi.Text(row, "Name", BoardUi.StyleText, x, string.IsNullOrEmpty(sub) ? 0f : 2f, RightW - x - 8f, string.IsNullOrEmpty(sub) ? 36f : 17f, color).text =
                name + (click != null ? "  <color=#9aa0ae>></color>" : "");
            if (!string.IsNullOrEmpty(sub))
            {
                BoardUi.Text(row, "Sub", BoardUi.StyleSmall, x, 19f, RightW - x - 8f, 15f, BoardUi.Muted).text = sub;
            }
            return y + 40f;
        }

        // ---- the map

        private float DrawMap(FactoryMap floor, Recipe recipe, Dictionary<string, Color> colors, Placement spot, float y, float h)
        {
            float w = RightW - 12f;
            RectTransform panel = BoardUi.Rect(weekList, "Map", 6f, y, w, h);
            UImage bg = panel.gameObject.AddComponent<UImage>();
            BoardUi.SetSprite(bg, "comm-value_frame_1", BoardUi.Field);
            bg.raycastTarget = true;
            if (floor.Empty)
            {
                BoardUi.Text(panel, "Empty", BoardUi.StyleSmall, 8f, 0f, w - 16f, h, BoardUi.Muted, TextAlignmentOptions.Center, true).text =
                    Texts.Pt("A fábrica não está carregada: abra o Quadro dentro dela.", "The factory isn't loaded: open the board inside it.");
                return y + h + 6f;
            }

            Vector2 up = floor.Up;
            var right = new Vector2(up.y, -up.x);
            Vector2 Uv(int c, int r) => new Vector2(c * right.x + r * right.y, c * up.x + r * up.y);
            Rect UvRect(CellRect cells)
            {
                Vector2 a = Uv(cells.MinC, cells.MinR), b = Uv(cells.MaxC, cells.MaxR);
                return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            }
            Rect area = UvRect(floor.Area);
            float pad = 8f;
            float scale = Mathf.Min((w - 2f * pad) / (area.width + 1f), (h - 2f * pad) / (area.height + 1f));
            scale = Mathf.Min(scale, 16f);
            float ox = (w - (area.width + 1f) * scale) / 2f, oy = (h - (area.height + 1f) * scale) / 2f;

            RectTransform Box(Transform parent, string name, CellRect cells, Color color, float inset)
            {
                Rect r = UvRect(cells);
                float px = ox + (r.xMin - area.xMin) * scale + inset, py = oy + (area.yMax - r.yMax) * scale + inset;
                RectTransform rect = BoardUi.Rect(parent, name, px, py, Mathf.Max(1f, (r.width + 1f) * scale - 2f * inset), Mathf.Max(1f, (r.height + 1f) * scale - 2f * inset));
                UImage img = rect.gameObject.AddComponent<UImage>();
                img.color = color;
                img.raycastTarget = false;
                return rect;
            }

            float inset = scale >= 4f ? 0.5f : 0f;
            var users = new HashSet<SGuid>(floor.Users(recipe.OutItem, snap.Book).Select(p => p.Id));
            var highlighted = new List<(FactoryMap.Piece piece, Color color, string what)>();
            foreach (FactoryMap.Piece piece in floor.Pieces)
            {
                Color color;
                string what = null;
                string item = colors.Keys.FirstOrDefault(i => piece.Product == i || piece.Holds.ContainsKey(i) ||
                    (piece.Kind == FactoryMap.Kind.Reserve && piece.WgoId.StartsWith(i + "_", StringComparison.Ordinal)));
                if (piece.Kind == FactoryMap.Kind.Machine && piece.Recipe == recipe.Id)
                {
                    color = BoardUi.Ok;
                    what = Texts.Pt("faz ", "makes ") + Texts.Name(recipe.OutItem);
                }
                else if (item != null && piece.Kind != FactoryMap.Kind.Belt && piece.Kind != FactoryMap.Kind.Other)
                {
                    color = colors[item];
                    what = piece.Kind == FactoryMap.Kind.Machine
                        ? Texts.Pt("faz ", "makes ") + Texts.Name(item)
                        : Texts.Pt($"tem {(piece.Holds.TryGetValue(item, out int n) ? n : 0)} × ", $"holds {(piece.Holds.TryGetValue(item, out int m) ? m : 0)} × ") + Texts.Name(item);
                }
                else if (users.Contains(piece.Id))
                {
                    color = BoardUi.Plan;
                    what = Texts.Pt("usa ", "uses ") + Texts.Name(recipe.OutItem);
                }
                else
                {
                    color = piece.Kind == FactoryMap.Kind.Machine ? MapMachine
                        : piece.Kind == FactoryMap.Kind.Belt ? MapBelt
                        : piece.Kind == FactoryMap.Kind.Other ? MapOther : MapStock;
                }
                if (what != null)
                {
                    highlighted.Add((piece, color, what));
                    continue;  // drawn on top
                }
                Box(panel, "P", piece.Cells, color, inset);
            }
            foreach (var (piece, color, what) in highlighted)
            {
                RectTransform rect = Box(panel, "H", piece.Cells, color, 0f);
                UImage img = rect.GetComponent<UImage>();
                img.raycastTarget = true;
                BoardUi.Tip(rect.gameObject, Texts.Name(piece.WgoId), what);
            }
            if (floor.Board != null)
            {
                RectTransform board = Box(panel, "Board", floor.Board.Cells, MapBoard, 0f);
                board.GetComponent<UImage>().raycastTarget = true;
                BoardUi.Tip(board.gameObject, Texts.Pt("Você está aqui", "You are here"), Texts.Pt("O Quadro de Planejamento.", "The Planning Board."));
            }
            if (spot != null)
            {
                RectTransform mark = Box(panel, "Spot", spot.Box, new Color(BoardUi.Gold.r, BoardUi.Gold.g, BoardUi.Gold.b, 0.35f), 0f);
                UImage frame = BoardUi.Sliced(mark, "Frame", 0f, 0f, 10f, 10f, BoardUi.FrameSprite, BoardUi.Gold);
                BoardUi.Stretch(frame.rectTransform);
                mark.GetComponent<UImage>().raycastTarget = true;
                BoardUi.Tip(mark.gameObject, Texts.Pt("Melhor lugar livre", "Best free spot"),
                    Texts.Pt($"Uma {Texts.Name(recipe.Station)} cabe aqui, perto do que ela usa.", $"A {Texts.Name(recipe.Station)} fits here, close to what it uses."));
            }
            return y + h + 6f;
        }

        private string MapNote(FactoryMap floor, Recipe recipe, Placement spot, List<string> items)
        {
            if (floor.Empty)
            {
                return "";
            }
            var parts = new List<string>();
            if (spot != null)
            {
                var belts = spot.Belts.Select(kv => Texts.Pt($"~{kv.Value} até {Texts.Name(kv.Key)}", $"~{kv.Value} to {Texts.Name(kv.Key)}")).ToList();
                if (spot.OutBelts >= 0)
                {
                    belts.Add(Texts.Pt($"~{spot.OutBelts} da saída até quem usa", $"~{spot.OutBelts} from the exit to its users"));
                }
                parts.Add(Texts.Pt($"Dourado: o melhor chão livre para uma {Texts.Name(recipe.Station)}", $"Gold: the best free ground for a {Texts.Name(recipe.Station)}") +
                    (belts.Count > 0 ? Texts.Pt(" (esteiras: ", " (belts: ") + string.Join(", ", belts) + ")" : "") + ".");
                if (!spot.Roomy)
                {
                    parts.Add(Texts.Pt("Cabe justo: não sobra chão em volta para as esteiras.", "A tight fit: no ground left around it for the belts."));
                }
            }
            else
            {
                parts.Add(Texts.Pt($"Não achei chão livre onde caiba uma {Texts.Name(recipe.Station)}.", $"No free ground fits a {Texts.Name(recipe.Station)}."));
            }
            List<string> orphan = items.Where(i => !floor.Makers(i).Any() && !floor.Stocks(i).Any()).ToList();
            if (orphan.Count > 0)
            {
                parts.Add(Texts.Pt($"{string.Join(", ", orphan.Select(Texts.Name))} ainda não sai de nenhuma máquina nem baú: comece pela cadeia dele (clique nele acima).",
                    $"{string.Join(", ", orphan.Select(Texts.Name))} doesn't come out of any machine or chest yet: start with its own chain (click it above)."));
            }
            parts.Add(Texts.Pt("O mapa está como a câmera vê a fábrica; o quadrado branco é este Quadro.", "The map is the way the camera sees the factory; the white square is this board."));
            return string.Join(" ", parts);
        }

        // ---- actions

        private List<(SGuid, Color)> TintList(Recipe recipe, Dictionary<string, Color> colors)
        {
            FactoryMap floor = Map;
            var list = new List<(SGuid, Color)>();
            var seen = new HashSet<SGuid>();
            void Add(IEnumerable<FactoryMap.Piece> pieces, Color color)
            {
                foreach (FactoryMap.Piece p in pieces)
                {
                    if (seen.Add(p.Id))
                    {
                        list.Add((p.Id, color));
                    }
                }
            }
            Add(floor.Running(recipe.Id), BoardUi.Ok);
            foreach (var kv in colors)
            {
                Add(floor.Makers(kv.Key).Concat(floor.Stocks(kv.Key)), kv.Value);
            }
            Add(floor.Users(recipe.OutItem, snap.Book), BoardUi.Plan);
            return list;
        }

        private static readonly string[] ColorNamesPt = { "laranja", "ciano", "roxo", "amarelo", "rosa" };
        private static readonly string[] ColorNamesEn = { "orange", "cyan", "purple", "yellow", "pink" };

        private static string TintLegend(Dictionary<string, Color> colors) =>
            string.Join(", ", colors.Keys.Select((item, i) =>
                $"{Texts.Name(item)} {Texts.Pt(ColorNamesPt[i % ColorNamesPt.Length], ColorNamesEn[i % ColorNamesEn.Length])}"));

        private void ShowInFactory(Recipe recipe, Dictionary<string, Color> colors, float seconds)
        {
            List<(SGuid, Color)> pieces = TintList(recipe, colors);
            Close();
            FactoryMap.Tint(pieces, seconds);
            WeekDispatch.Notify("gk2fp_chain_shown",
                Texts.Pt($"Fontes de {Texts.Name(recipe.OutItem)}: {TintLegend(colors)}; verde faz, azul usa.",
                    $"Sources for {Texts.Name(recipe.OutItem)}: {TintLegend(colors)}; green makes it, blue uses it."), Icons.DraftingTable);
        }

        private void LayModule(Recipe recipe, Stage stage, Dictionary<string, Color> colors)
        {
            bool own = snap.Week.Own.ContainsKey(recipe.Id);
            // An own order ends in a chest the player empties; a box of the line ends in a station for the pallets.
            bool final = !OwnOnly(recipe.Id) && (stage?.Final ?? false);
            LayoutResult module = LayoutPlanner.Module(recipe, FromScratch.ShapeFor(recipe.Station), final);
            Blueprint blueprint = FromScratch.ToBlueprint(module, Texts.Pt($"Módulo: {Texts.Name(recipe.OutItem)}", $"Module: {Texts.Name(recipe.OutItem)}"));
            if (blueprint == null || blueprint.Pieces.Count == 0)
            {
                message = Texts.Pt("Não consegui montar o módulo desta peça (veja o log).", "Couldn't lay out this product's module (see the log).");
                Refill();
                return;
            }
            List<(SGuid, Color)> pieces = TintList(recipe, colors);
            BlueprintStore.UseTemporary(blueprint);
            Close();
            FactoryMap.Tint(pieces, 60f);
            if (own)
            {
                WeekDispatch.Notify("gk2fp_own_module",
                    Texts.Pt($"Avulso de {Texts.Name(recipe.OutItem)}: ligue cada baú de entrada a um lado livre do baú (ou reserva) pintado, com o filtro do lado no item. O baú da saída fica para você.",
                        $"Own order of {Texts.Name(recipe.OutItem)}: link each input chest to a free side of the tinted chest (or reserve), that side filtered to the item. The exit chest is yours."),
                    Icons.DraftingTable);
            }
            Plugin.Instance.StartCoroutine(BlueprintMode.StartFromDesk(false));
        }
    }
}
