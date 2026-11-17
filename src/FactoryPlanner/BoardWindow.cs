using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GK2Mods.FactoryPlanner.Blueprints;
using GK2Mods.FactoryPlanner.Core;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// The Planning Board's own window. Left: the town's orders, click to put one in the plan. Right: the week by
    /// stage, each recipe with its machines and a quantity to adjust; "Dispatch" puts the factory's machines on it,
    /// one stage or the whole week. The frame is a copy of the game's settings window, the rows use the game's
    /// sprites and text styles.
    /// </summary>
    internal sealed partial class BoardWindow : LazyWindow<LazyWidgetDataBase>
    {
        private const string WindowName = "GK2FP_BoardWindow";
        private const float FootH = 44f;
        private const float TitleH = 24f;

        private static BoardWindow instance;
        private static Vector2 builtFor;
        private static float Width = 760f;
        private static float Height = 440f;

        private static float LeftW => Width < 680f ? 230f : 260f;
        private static float RightX => LeftW + 14f;
        private static float RightW => Width - RightX;
        private static float ListTop => TitleH + 6f;
        private static float ListH => Height - ListTop - FootH - 6f;

        private RectTransform content;
        private TMP_Text title;
        private TMP_Text leftTitle;
        private TMP_Text rightTitle;
        private RectTransform orderList;
        private RectTransform weekList;
        private RectTransform foot;

        private WeekDispatch.Snapshot snap;
        private Dictionary<string, LineResult> lineByOrder;
        /// <summary>"all" or "stage:n" after a first click on a dispatch button; the second click does it.</summary>
        private string armed;
        private string message = "";

        // ---------------------------------------------------------------- opening

        internal static bool TryOpen(PlayerController player, WgoData board)
        {
            try
            {
                Vector2 size = UiSize();
                if (instance != null && instance.gameObject != null && size != builtFor)
                {
                    Destroy(instance.gameObject);
                    instance = null;
                }
                if (instance == null || instance.gameObject == null)
                {
                    instance = Build();
                }
                instance.armed = null;
                instance.message = "";
                instance.picking = false;
                instance.focus = null;
                instance.mapMode = false;
                instance.picked = default;
                instance.moving = false;
                instance.removeArmed = null;
                instance.trail.Clear();
                instance.boardId = board?.UniqueId ?? default;
                instance.Fill();
                instance.gameObject.SetActive(true);
                instance.Open(null);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Planning Board window failed, opening the classic one: {e}");
                try
                {
                    if (instance != null)
                    {
                        instance.CloseWithoutCallback();
                        Destroy(instance.gameObject);
                    }
                }
                catch (Exception)
                {
                    // already gone
                }
                instance = null;
                return false;
            }
        }

        /// <summary>Lab runs: the board with its map up, on a piece if one is given.</summary>
        internal static bool LabOpen(WgoData board, bool map, SGuid pick)
        {
            if (!TryOpen(MainGame.PlayerController, board))
            {
                return false;
            }
            instance.mapMode = map;
            instance.picked = pick;
            instance.Refill();
            return true;
        }

        /// <summary>Lab runs: the map's Move, as a click on the spot dc, dr cells away would do it.</summary>
        internal static string LabMove(SGuid id, int dc, int dr)
        {
            FactoryMap floor = FactoryMap.Read(GameData.Recipes(), default);
            return FloorActions.Move(floor, id, dc, dr);
        }

        internal static void LabClose()
        {
            if (instance != null && instance.gameObject != null)
            {
                instance.Close();
            }
        }

        private static Vector2 UiSize()
        {
            GUIElements gui = GUIElements.Instance;
            if (gui != null && BoardUi.UiRoot(gui) is RectTransform root)
            {
                return root.rect.size;
            }
            return new Vector2(1280f, 800f);
        }

        private static BoardWindow Build()
        {
            GUIElements gui = GUIElements.Instance ?? throw new InvalidOperationException("GUIElements not found");
            Transform uiRoot = BoardUi.UiRoot(gui);
            builtFor = UiSize();
            Width = Mathf.Clamp(Mathf.Floor(builtFor.x - 80f), 560f, 820f);
            Height = Mathf.Clamp(Mathf.Floor(builtFor.y - 96f), 320f, 470f);

            UIGameSettingsWindow settings = LazyUI.GetWindow<UIGameSettingsWindow>() ?? throw new InvalidOperationException("settings window not available");
            GenericWindowLayout frameSource = PickFrame(settings) ?? throw new InvalidOperationException("no window frame to copy");
            Transform shadow = settings.transform.Find("BgShadow");

            var root = new GameObject(WindowName, typeof(RectTransform));
            root.SetActive(false);
            root.layer = settings.gameObject.layer;
            root.transform.SetParent(uiRoot, false);
            BoardUi.Stretch((RectTransform)root.transform);
            root.AddComponent<Canvas>();
            root.AddComponent<GraphicRaycaster>();
            GamepadNavigationController nav = root.AddComponent<GamepadNavigationController>();
            nav.navigationGroupSources = new List<GamepadNavigationController.NavigationGroupSource>();
            nav.useGridSkippedIfListEmpty = true;
            try
            {
                if (shadow != null)
                {
                    Instantiate(shadow.gameObject, root.transform, false).name = "BgShadow";
                }
                GameObject frame = Instantiate(frameSource.gameObject, root.transform, false);
                frame.name = "GenericWindowLayout";
                BoardUi.UniqueIds(frame, "gk2fp_board");
                frame.SetActive(true);
                var frameContent = (RectTransform)(frame.transform.Find("Content") ?? throw new InvalidOperationException("frame has no Content"));
                for (int i = frameContent.childCount - 1; i >= 0; i--)
                {
                    DestroyImmediate(frameContent.GetChild(i).gameObject);
                }
                BoardUi.DestroyAll<ContentSizeFitter>(frameContent.gameObject, true);
                BoardUi.DestroyAll<VerticalLayoutGroup>(frameContent.gameObject, true);
                frameContent.sizeDelta = new Vector2(Width, Height);
                BoardUi.DestroyAll<LazyPlatformDependentElement>(root, false);
                BoardUi.DestroyAll<LocalizedSize>(root, false);
                Transform header = frame.transform.Find("Frame/HeaderGroup") ?? throw new InvalidOperationException("frame has no HeaderGroup");
                LazyButton close = header.Find("CloseButton")?.GetComponent<LazyButton>() ?? throw new InvalidOperationException("frame has no CloseButton");
                TMP_Text titleLabel = header.Find("Header")?.GetComponent<TMP_Text>();

                root.SetActive(true);
                BoardWindow window = root.AddComponent<BoardWindow>();
                window.closeButton = close;
                window.title = titleLabel;
                BoardUi.Sample = titleLabel;
                window.content = BoardUi.Rect(frameContent, "GK2FP_Board", 0f, 0f, Width, Height);
                window.Layout();
                BoardUi.RemoveNestedCanvases(root);
                foreach (LocalizedLabel label in root.GetComponentsInChildren<LocalizedLabel>(true))
                {
                    label.IgnoreLocalize = true;
                }
                window.Init();
                Plugin.Debug($"Planning Board window built: {Width}x{Height} for a {builtFor.x}x{builtFor.y} screen");
                return window;
            }
            catch
            {
                root.SetActive(false);
                Destroy(root);
                throw;
            }
        }

        /// <summary>The settings window's frame: the one with a Content area and a close button.</summary>
        private static GenericWindowLayout PickFrame(UIGameSettingsWindow settings)
        {
            GenericWindowLayout own = settings.GetComponentInChildren<GenericWindowLayout>(true);
            if (own != null && FrameFits(own.transform))
            {
                return own;
            }
            foreach (GenericWindowLayout other in Resources.FindObjectsOfTypeAll<GenericWindowLayout>())
            {
                if (other != null && other != own && FrameFits(other.transform))
                {
                    return other;
                }
            }
            return own;
        }

        private static bool FrameFits(Transform frame) =>
            frame.Find("Content") is RectTransform && frame.Find("Frame/HeaderGroup/CloseButton")?.GetComponent<LazyButton>() != null;

        private void Layout()
        {
            leftTitle = BoardUi.Text(content, "LeftTitle", BoardUi.StyleBold, 4f, 0f, LeftW - 8f, TitleH, BoardUi.Gold);
            rightTitle = BoardUi.Text(content, "RightTitle", BoardUi.StyleBold, RightX + 4f, 0f, RightW - 8f, TitleH, BoardUi.Gold);
            orderList = BoardUi.ScrollList(content, "Orders", 0f, ListTop, LeftW, ListH, out _);
            weekList = BoardUi.ScrollList(content, "Week", RightX, ListTop, RightW, ListH, out _);
            foot = BoardUi.Rect(content, "Foot", 0f, Height - FootH, Width, FootH);
            modeHolder = BoardUi.Rect(content, "Mode", Width - 112f, 0f, 108f, 22f);
        }

        public override void Close()
        {
            base.Close();
            UITooltip.Hide();
        }

        public override void CloseWithoutCallback()
        {
            base.CloseWithoutCallback();
            UITooltip.Hide();
        }

        public override void TestDraw()
        {
        }

        // ---------------------------------------------------------------- filling

        private void Fill()
        {
            snap = WeekDispatch.Read();
            map = null;
            if (focus != null && !snap.Book.ById.ContainsKey(focus))
            {
                focus = null;
                trail.Clear();
            }
            var plain = new PlanOptions { Factory = snap.Factory, External = snap.Options.External };
            lineByOrder = snap.Orders.ToDictionary(o => o.Id, o => Planner.ForOrders(new[] { o }, snap.Book, plain));
            if (title != null)
            {
                title.text = Texts.Pt("Quadro de Planejamento", "Planning Board");
            }
            BoardUi.Clear(modeHolder);
            BoardUi.Button(modeHolder, "Toggle", 0f, 0f, 108f, 22f, mapMode ? Texts.Pt("< Semana", "< Week") : Texts.Pt("Mapa da fábrica", "Factory map"), false, () =>
            {
                mapMode = !mapMode;
                picked = default;
                moving = false;
                removeArmed = null;
                message = "";
                UITooltip.Hide();
                Refill();
                orderList.anchoredPosition = Vector2.zero;
                weekList.anchoredPosition = Vector2.zero;
            });
            if (mapMode)
            {
                FillFloor();
            }
            else
            {
                FillOrders();
                FillWeek();
            }
            FillFoot();
        }

        private void Refill()
        {
            try
            {
                Fill();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Planning Board window: refresh failed, closing it: {e}");
                Close();
            }
        }

        // ---- left: orders

        private void FillOrders()
        {
            BoardUi.Clear(orderList);
            float y = 0f;
            HashSet<string> planIds = new HashSet<string>(snap.Planned.Select(o => o.Id));
            float happiness = snap.Planned.Sum(o => o.Happiness);
            leftTitle.text = snap.Planned.Count == 0
                ? Texts.Pt("Pedidos das lojas", "Shop orders")
                : Texts.Pt($"Na planta: {snap.Planned.Count} · +{happiness:0} felicidade", $"In the plan: {snap.Planned.Count} · +{happiness:0} happiness");

            if (snap.Planned.Count > 0)
            {
                y = Group(orderList, y, Texts.Pt("Na planta (clique para tirar)", "In the plan (click to remove)"));
                foreach (Order o in snap.Planned)
                {
                    y = OrderRow(o, y, true);
                }
            }
            y = OwnSection(y);
            List<Expansion> ranked = Planner.RankExpansions(
                snap.Orders.Where(o => !planIds.Contains(o.Id) && o.Available && !o.DoneThisWeek), snap.Week, snap.Book,
                new PlanOptions { Factory = snap.Factory, External = snap.Options.External });
            List<Expansion> next = ranked.Where(e => !e.Locked).Take(Plugin.Suggestions.Value).ToList();
            if (next.Count > 0)
            {
                y = Group(orderList, y, Texts.Pt("Sugeridos (menos máquinas novas)", "Suggested (fewest new machines)"));
                foreach (Expansion e in next)
                {
                    y = OrderRow(e.Order, y, false);
                }
            }
            List<Order> accepted = snap.Orders.Where(o => o.Accepted && !planIds.Contains(o.Id)).ToList();
            if (accepted.Count > 0)
            {
                y = Group(orderList, y, Texts.Pt("Aceitos no quadro de pedidos", "Accepted on the order board"));
                foreach (Order o in accepted)
                {
                    y = OrderRow(o, y, false);
                }
            }
            foreach (var vendor in snap.Orders.Where(o => !planIds.Contains(o.Id) && !o.Accepted).GroupBy(o => o.VendorId)
                         .OrderByDescending(g => g.Any(o => o.Available))
                         .ThenBy(g => Texts.Name(g.Key), StringComparer.CurrentCulture))
            {
                y = Group(orderList, y, $"{Texts.Name(vendor.Key)} · {Texts.Pt("nível", "level")} {GameData.VendorTier(vendor.Key)}");
                foreach (Order o in vendor.OrderByDescending(o => o.Available).ThenBy(o => o.Tier).ThenBy(o => o.Id))
                {
                    y = OrderRow(o, y, false);
                }
            }
            orderList.sizeDelta = new Vector2(0f, y + 4f);
        }

        private static float Group(RectTransform list, float y, string text)
        {
            RectTransform row = BoardUi.Row(list, "Group", y + 4f, 24f, BoardUi.HeaderSprite, false, null);
            BoardUi.Text(row, "Name", BoardUi.StyleBold, 10f, 0f, 600f, 24f, BoardUi.Hex("f6e6c4")).text = text;
            return y + 30f;
        }

        private float OrderRow(Order order, float y, bool inPlan)
        {
            LineResult line = lineByOrder[order.Id];
            bool canAdd = order.Available && line.Locked.Count == 0 && !order.DoneThisWeek;
            bool clickable = inPlan || canAdd;
            RectTransform row = BoardUi.Row(orderList, "Order_" + order.Id, y + 2f, 40f, BoardUi.PlateSprite, inPlan,
                clickable ? (Action)(() => ToggleOrder(order.Id, !inPlan)) : null);
            BoardUi.ItemIcon(row, order.BoxItem, 6f, 6f, 28f);
            float w = LeftW - 52f;
            BoardUi.Text(row, "Name", inPlan ? BoardUi.StyleBold : BoardUi.StyleText, 40f, 3f, w, 18f,
                clickable ? (inPlan ? BoardUi.Gold : BoardUi.Ink) : BoardUi.Muted).text = $"{Texts.Name(order.BoxItem)} ×{order.Count}";

            var have = new HashSet<string>(snap.Factory.RunningRecipes);
            if (!inPlan)
            {
                have.UnionWith(snap.Week.Crafts.Keys);
            }
            int fresh = line.RecipeOrder.Count(id => !have.Contains(id));
            string sub = !order.Available
                ? (GameData.VendorTier(order.VendorId) < order.Tier
                    ? Texts.Pt($"loja precisa do nível {order.Tier}", $"shop needs level {order.Tier}")
                    : Texts.Pt("loja ainda não aceita pedidos", "shop doesn't take orders yet"))
                : order.DoneThisWeek
                    ? Texts.Pt("já entregue nesta semana", "delivered this week")
                    : line.Locked.Count > 0
                        ? Texts.Pt("falta pesquisar receitas", "recipes still to research")
                        : $"+{order.Happiness:0} · " + (fresh == 0
                            ? Texts.Pt("só máquinas que já tem", "only machines you have")
                            : Texts.Pt($"{fresh} máquina(s) nova(s)", $"{fresh} new machine(s)"));
            if (order.Urgent)
            {
                sub += Texts.Pt(" · entrega única", " · single delivery");
            }
            BoardUi.Text(row, "Sub", BoardUi.StyleSmall, 40f, 21f, w, 16f, BoardUi.Muted).text = sub;

            var body = new StringBuilder();
            body.Append($"{Texts.Name(order.VendorId)} · {Texts.Pt("nível", "level")} {order.Tier}\n");
            body.Append(Texts.Pt($"{order.Count} caixas · +{order.Happiness:0} felicidade", $"{order.Count} boxes · +{order.Happiness:0} happiness"));
            if (order.Urgent)
            {
                body.Append(Texts.Pt("\nEntrega única: as caixas precisam estar juntas nos paletes.", "\nSingle delivery: all boxes must be on the pallets together."));
            }
            List<string> freshNames = line.RecipeOrder.Where(id => !have.Contains(id)).Select(id => Texts.Name(snap.Book.ById[id].OutItem)).ToList();
            body.Append("\n\n" + (freshNames.Count == 0
                ? Texts.Pt("Usa só máquinas que a fábrica e a planta já têm.", "Uses only machines the factory and the plan already have.")
                : Texts.Pt("Máquinas novas: ", "New machines: ") + string.Join(", ", freshNames)));
            if (line.Locked.Count > 0)
            {
                body.Append(Texts.Pt("\nFalta pesquisar: ", "\nStill to research: ") +
                    string.Join(", ", line.Locked.Select(id => snap.Book.ById[id].TechId).Where(t => t != null).Distinct().Select(Texts.Name)));
            }
            body.Append("\n\n" + (inPlan
                ? Texts.Pt("Clique para tirar da planta.", "Click to remove it from the plan.")
                : canAdd ? Texts.Pt("Clique para pôr na planta.", "Click to add it to the plan.") : Texts.Pt("Ainda não dá para pôr na planta.", "Can't be planned yet.")));
            BoardUi.Tip(row.gameObject, Texts.Name(order.BoxItem), body.ToString());
            return y + 44f;
        }

        private void ToggleOrder(string orderId, bool add)
        {
            armed = null;
            GameData.SetInPlan(orderId, add);
            Refill();
        }

        // ---- left: the player's own orders

        /// <summary>The product list is open under "New own order".</summary>
        private bool picking;

        private float OwnSection(float y)
        {
            y = Group(orderList, y, Texts.Pt("Pedidos avulsos (máquina própria)", "Own orders (a machine of their own)"));
            foreach (var kv in snap.Options.Own.OrderBy(kv => Texts.Name(kv.Key), StringComparer.CurrentCulture))
            {
                y = OwnRow(kv.Key, kv.Value, y);
            }
            RectTransform add = BoardUi.Row(orderList, "OwnAdd", y + 2f, 30f, BoardUi.PlateSprite, picking, () => { picking = !picking; Refill(); });
            BoardUi.Text(add, "Name", BoardUi.StyleText, 12f, 0f, LeftW - 24f, 30f, BoardUi.Gold).text =
                picking ? Texts.Pt("- Fechar a lista", "- Close the list") : Texts.Pt("+ Novo pedido avulso", "+ New own order");
            BoardUi.Tip(add.gameObject, Texts.Pt("Pedido avulso", "Own order"),
                Texts.Pt("Qualquer peça que a fábrica faz (Kit de Madeira II, Kit de Montagem…), na quantidade que você quiser por semana. " +
                         "Ela roda numa máquina só dela e fica no baú da saída: não vira insumo de outra etapa.",
                    "Any product the factory makes (wood kit II, assembly kit…), as many a week as you like. " +
                         "It runs on a machine of its own and stays in the exit chest: it doesn't become another stage's ingredient."));
            y += 34f;
            if (!picking)
            {
                return y;
            }
            StagePlan catalog = Stager.Catalog(snap.Book, snap.Factory);
            foreach (Stage stage in catalog.Stages)
            {
                List<StageStep> steps = stage.Steps.Where(s => !snap.Options.Own.ContainsKey(s.Recipe.OutItem)).ToList();
                if (steps.Count == 0)
                {
                    continue;
                }
                string kind = stage.Final ? Texts.Pt("Produto final", "Final product")
                    : stage.Index == 1 ? Texts.Pt("Refino", "Refining") : Texts.Pt("Montagem", "Assembly");
                TMP_Text head = BoardUi.Text(orderList, "PickStage", BoardUi.StyleSmall, 10f, y + 2f, LeftW - 20f, 18f, BoardUi.Muted);
                head.text = $"{Texts.Pt("Etapa", "Stage")} {stage.Index} · {kind}";
                y += 22f;
                foreach (StageStep s in steps.OrderBy(s => Texts.Name(s.Recipe.OutItem), StringComparer.CurrentCulture))
                {
                    Recipe r = s.Recipe;
                    RectTransform row = BoardUi.Row(orderList, "Pick_" + r.OutItem, y + 1f, 30f, BoardUi.PlateSprite, false,
                        r.Unlocked ? (Action)(() => AddOwn(r)) : null);
                    BoardUi.ItemIcon(row, r.OutItem, 6f, 3f, 24f);
                    BoardUi.Text(row, "Name", BoardUi.StyleSmall, 36f, 0f, LeftW - 44f, 30f, r.Unlocked ? BoardUi.Ink : BoardUi.Muted).text =
                        Texts.Name(r.OutItem) + (r.Unlocked ? "" : Texts.Pt(" (não pesquisada)", " (not researched)"));
                    BoardUi.Tip(row.gameObject, Texts.Name(r.OutItem),
                        $"{Texts.Name(r.Station)}{(r.Extension != null ? " + " + Texts.Name(r.Extension) : "")}: " +
                        string.Join(", ", r.Needs.Select(i => $"{i.Count} × {Texts.Name(i.Item)}")) + $" → {r.OutCount}" +
                        (r.Unlocked ? Texts.Pt("\n\nClique para pedir.", "\n\nClick to order it.") : ""));
                    y += 32f;
                }
            }
            return y + 4f;
        }

        private float OwnRow(string item, int amount, float y)
        {
            Recipe recipe = snap.Week.Own.Keys.Select(id => snap.Book.ById[id]).FirstOrDefault(r => r.OutItem == item);
            RectTransform row = BoardUi.Row(orderList, "Own_" + item, y + 2f, 40f, BoardUi.PlateSprite, true,
                recipe != null ? (Action)(() => OpenChain(recipe.Id)) : null);
            BoardUi.ItemIcon(row, item, 6f, 6f, 28f);
            float w = LeftW - 52f - 58f;
            BoardUi.Text(row, "Name", BoardUi.StyleBold, 40f, 3f, w, 18f, BoardUi.Gold).text = $"{Texts.Name(item)} ×{amount}";
            string sub;
            Color color = BoardUi.Muted;
            if (recipe == null)
            {
                sub = Texts.Pt("a fábrica não faz isto", "the factory doesn't make it");
                color = BoardUi.Short;
            }
            else
            {
                List<Assignment> mine = snap.For(recipe.Id, true).ToList();
                Unplaced unplaced = snap.MissingFor(recipe.Id, true);
                if (unplaced != null)
                {
                    sub = unplaced.NeedsExtension ? Texts.Pt($"falta a extensão {Texts.Name(recipe.Extension)}", $"needs the {Texts.Name(recipe.Extension)} extension")
                        : unplaced.NoMachine ? Texts.Pt($"sem {Texts.Name(recipe.Station)} livre", $"no free {Texts.Name(recipe.Station)}")
                        : Texts.Pt($"nenhuma livre recebe {Names(unplaced.Lacking)}", $"none free gets {Names(unplaced.Lacking)}");
                    color = BoardUi.Short;
                }
                else if (mine.Any(a => a.Lacking.Count > 0))
                {
                    sub = Texts.Pt($"não chega {Names(mine.SelectMany(a => a.Lacking).Distinct())}", $"{Names(mine.SelectMany(a => a.Lacking).Distinct())} doesn't arrive");
                    color = BoardUi.Short;
                }
                else if (mine.Count == 0)
                {
                    sub = Texts.Pt("fora desta semana", "not this week");
                }
                else
                {
                    sub = mine.Any(a => !a.Change) ? Texts.Pt("máquina dedicada pronta", "its machine is ready")
                        : Texts.Pt($"no disparo: {Texts.Name(mine[0].Machine.Station)} ({From(mine[0])})", $"on dispatch: {Texts.Name(mine[0].Machine.Station)} ({From(mine[0])})");
                    color = mine.Any(a => !a.Change) ? BoardUi.Ok : BoardUi.Ink;
                }
            }
            BoardUi.Text(row, "Sub", BoardUi.StyleSmall, 40f, 21f, w, 16f, color).text = sub;
            float right = LeftW - 8f;
            BoardUi.Button(row, "Plus", right - 24f, 9f, 24f, 22f, "+", false, () => StepOwn(item, +1));
            BoardUi.Button(row, "Minus", right - 52f, 9f, 24f, 22f, "-", false, () => StepOwn(item, -1));
            BoardUi.Tip(row.gameObject, Texts.Name(item),
                Texts.Pt($"{amount} por semana, numa máquina só deles; ficam no baú da saída.\n+ e - (Shift: 5 crafts); em 0 o pedido sai.\nClique: de onde vêm os insumos e onde pôr a máquina.",
                    $"{amount} a week, on a machine of their own; they stay in the exit chest.\n+ and - (Shift: 5 crafts); at 0 the order goes.\nClick: where the ingredients come from and where to put the machine."));
            return y + 44f;
        }

        private void AddOwn(Recipe recipe)
        {
            armed = null;
            message = "";
            int batch = Math.Max(1, recipe.OutCount);
            GameData.SetOwn(recipe.OutItem, (10 + batch - 1) / batch * batch);
            picking = false;
            Refill();
        }

        private void StepOwn(string item, int direction)
        {
            armed = null;
            message = "";
            Recipe recipe = snap.Book.Choose(item, snap.Factory);
            int batch = Math.Max(1, recipe?.OutCount ?? 1) * (BoardUi.Shift() ? 5 : 1);
            int now = snap.Options.Own.TryGetValue(item, out int n) ? n : 0;
            GameData.SetOwn(item, Math.Max(0, now + direction * batch));
            Refill();
        }

        // ---- right: the week by stage

        private void FillWeek()
        {
            BoardUi.Clear(weekList);
            if (focus != null)
            {
                FillChain();
                return;
            }
            var (used, produced) = GameData.Power();
            int machines = snap.Plan.Assignments.Select(a => a.Machine.Key).Distinct().Count();
            rightTitle.text = snap.Week.RecipeOrder.Count == 0
                ? Texts.Pt("Produção da semana", "The week's production")
                : Texts.Pt($"Produção da semana · {machines} máquina(s) · energia {used:0}/{produced:0}",
                    $"The week's production · {machines} machine(s) · power {used:0}/{produced:0}");
            float y = 0f;
            if (snap.Planned.Count == 0 && snap.Options.Own.Count == 0)
            {
                RectTransform row = BoardUi.Row(weekList, "Empty", 4f, 60f, BoardUi.PlateSprite, false, null);
                BoardUi.Text(row, "Hint", BoardUi.StyleText, 12f, 0f, RightW - 24f, 60f, BoardUi.Muted, TextAlignmentOptions.MidlineLeft, true).text =
                    Texts.Pt("Escolha à esquerda os pedidos da semana. Aqui aparece o que cada etapa da fábrica precisa fazer, com a quantidade, e o botão para pôr as máquinas para produzir.",
                        "Pick the week's orders on the left. Here you'll see what each stage of the factory has to make, with the quantity, and the button that puts the machines to work.");
                weekList.sizeDelta = new Vector2(0f, 70f);
                return;
            }
            foreach (Stage stage in snap.Stages.Stages)
            {
                y = StageHeader(stage, y);
                foreach (StageStep step in stage.Steps)
                {
                    y = RecipeRow(stage, step, y);
                }
            }
            if (snap.Week.Inputs.Count > 0)
            {
                RectTransform head = BoardUi.Row(weekList, "RawHead", y + 6f, 26f, BoardUi.HeaderSprite, false, null);
                BoardUi.Text(head, "Name", BoardUi.StyleBold, 10f, 0f, RightW - 20f, 26f, BoardUi.Hex("f6e6c4")).text =
                    Texts.Pt("Matéria-prima da semana · vem das reservas da fábrica", "The week's raw materials · from the factory's reserves");
                y += 34f;
                foreach (var kv in snap.Week.Inputs.OrderByDescending(kv => kv.Value))
                {
                    y = InputRow(kv.Key, kv.Value, y);
                }
            }
            weekList.sizeDelta = new Vector2(0f, y + 4f);
        }

        private float StageHeader(Stage stage, float y)
        {
            string kind = stage.Final ? Texts.Pt("Produto final", "Final product")
                : stage.Index == 1 ? Texts.Pt("Refino", "Refining") : Texts.Pt("Montagem", "Assembly");
            RectTransform row = BoardUi.Row(weekList, "Stage" + stage.Index, y + 6f, 28f, BoardUi.HeaderSprite, false, null);
            BoardUi.Text(row, "Name", BoardUi.StyleBold, 10f, 0f, RightW - 150f, 28f, BoardUi.Hex("f6e6c4")).text =
                $"{Texts.Pt("Etapa", "Stage")} {stage.Index} · {kind}";
            string key = "stage:" + stage.Index;
            bool any = stage.Steps.Any(s => s.Crafts > 0 && snap.For(s.Recipe.Id, s.Own).Any());
            BoardUi.ButtonParts button = BoardUi.Button(row, "Dispatch", RightW - 136f, 3f, 128f, 22f,
                armed == key ? Texts.Pt("Confirmar?", "Confirm?") : Texts.Pt("Disparar etapa", "Dispatch stage"), armed == key,
                () => DispatchClick(key, stage.Steps.Select(s => s.Recipe.Id).ToList()), any);
            BoardUi.Tip(button.Root.gameObject, Texts.Pt($"Disparar a etapa {stage.Index}", $"Dispatch stage {stage.Index}"),
                Texts.Pt("Põe só as máquinas desta etapa nas receitas e quantidades abaixo. Clique duas vezes: a primeira mostra o que muda no rodapé.",
                    "Puts only this stage's machines on the recipes and quantities below. Click twice: the first shows what changes at the bottom."));
            y += 34f;

            // What the stage takes, and from where: the stages before, or the reserves.
            if (stage.Input.Count > 0)
            {
                string takes = Texts.Pt("Recebe: ", "Takes: ") + string.Join(" · ", stage.Input.OrderByDescending(kv => kv.Value).Select(kv =>
                {
                    int at = snap.Stages.MadeAt.TryGetValue(kv.Key, out int s) ? s : 0;
                    string from = at > 0 ? Texts.Pt($"etapa {at}", $"stage {at}")
                        : snap.Options.External.Contains(kv.Key) ? Texts.Pt("de fora", "brought in") : Texts.Pt("reserva", "reserve");
                    return $"{kv.Value} {Texts.Name(kv.Key)} <color=#9aa0ae>({from})</color>";
                }));
                TMP_Text label = BoardUi.Text(weekList, "Takes" + stage.Index, BoardUi.StyleSmall, 10f, y, RightW - 20f, 40f, BoardUi.Ink, TextAlignmentOptions.TopLeft, true);
                label.text = takes;
                float h = Mathf.Clamp(label.GetPreferredValues(takes, RightW - 20f, 0f).y + 2f, 16f, 48f);
                BoardUi.Place(label.rectTransform, 10f, y, RightW - 20f, h);
                y += h + 2f;
            }
            return y;
        }

        private float RecipeRow(Stage stage, StageStep step, float y)
        {
            Recipe recipe = step.Recipe;
            int crafts = step.Crafts;
            bool own = step.Own;
            int needed = own ? crafts : snap.Week.Needed.TryGetValue(recipe.Id, out int n) ? n : crafts;
            bool overridden = !own && snap.Options.Quantities.ContainsKey(recipe.Id);
            List<Assignment> mine = snap.For(recipe.Id, own).ToList();
            Unplaced unplaced = snap.MissingFor(recipe.Id, own);
            bool missing = unplaced != null;

            RectTransform row = BoardUi.Row(weekList, "Recipe_" + recipe.Id + (own ? "_own" : ""), y + 2f, 44f, BoardUi.PlateSprite, false, () => OpenChain(recipe.Id));
            BoardUi.ItemIcon(row, recipe.OutItem, 8f, 8f, 28f);
            float textW = RightW - 48f - 150f;
            string needs = string.Join(", ", recipe.Needs.Select(i => Texts.Name(i.Item)));
            BoardUi.Text(row, "Name", BoardUi.StyleText, 44f, 4f, textW, 18f, crafts > 0 ? BoardUi.Ink : BoardUi.Muted).text = Texts.Name(recipe.OutItem) +
                (own ? Texts.Pt(" <color=#f0c35a>(avulso)</color>", " <color=#f0c35a>(own order)</color>") : "") +
                (needs.Length > 0 ? $"  <size=80%><color=#9aa0ae>< {needs}</color></size>" : "");

            string status;
            Color color;
            if (!recipe.Unlocked)
            {
                status = Texts.Pt("Não pesquisada", "Not researched") + (recipe.TechId != null ? $": {Texts.Name(recipe.TechId)}" : "");
                color = BoardUi.Short;
            }
            else if (crafts == 0)
            {
                status = Texts.Pt("Fora desta semana (quantidade 0)", "Left out this week (quantity 0)");
                color = BoardUi.Muted;
            }
            else if (missing)
            {
                status = unplaced.NeedsExtension
                    ? Texts.Pt($"Nenhuma máquina tem a extensão {Texts.Name(recipe.Extension)}", $"No machine has the {Texts.Name(recipe.Extension)} extension")
                    : unplaced.NoMachine
                    ? (own
                        ? Texts.Pt($"Sem {Texts.Name(recipe.Station)} livre para o avulso (monte o módulo)", $"No free {Texts.Name(recipe.Station)} for it (lay its module)")
                        : Texts.Pt($"Sem máquina livre: {Texts.Name(recipe.Station)} (monte o módulo na Mesa)", $"No free machine: {Texts.Name(recipe.Station)} (lay its module on the Desk)"))
                    : Texts.Pt($"Nenhuma {Texts.Name(recipe.Station)} livre recebe {Names(unplaced.Lacking)}", $"No free {Texts.Name(recipe.Station)} gets {Names(unplaced.Lacking)}");
                color = BoardUi.Short;
            }
            else
            {
                int kept = mine.Count(a => !a.Change);
                List<Assignment> changes = mine.Where(a => a.Change).ToList();
                List<string> lacking = mine.SelectMany(a => a.Lacking).Distinct().ToList();
                List<string> extra = mine.SelectMany(a => a.Extra).Distinct().ToList();
                var parts = new List<string>();
                if (kept > 0)
                {
                    parts.Add(own ? Texts.Pt($"{kept} máquina dedicada", $"{kept} machine of its own") : Texts.Pt($"{kept} máquina(s) já faz(em)", $"{kept} machine(s) on it"));
                }
                if (changes.Count > 0)
                {
                    Assignment a = changes[0];
                    parts.Add((a.After != null
                            ? Texts.Pt($"pôr na fila da {Texts.Name(a.Machine.Station)}, depois de {Texts.Name(a.After.Recipe.OutItem)}",
                                $"queue on the {Texts.Name(a.Machine.Station)}, after {Texts.Name(a.After.Recipe.OutItem)}")
                            : Texts.Pt($"trocar {Texts.Name(a.Machine.Station)} ({From(a)})", $"switch {Texts.Name(a.Machine.Station)} ({From(a)})")) +
                        (changes.Count > 1 ? $" +{changes.Count - 1}" : ""));
                }
                foreach (Assignment a in mine.Where(a => a.After != null && !a.Change).Take(1))
                {
                    parts.Add(Texts.Pt($"na fila, depois de {Texts.Name(a.After.Recipe.OutItem)}", $"queued after {Texts.Name(a.After.Recipe.OutItem)}"));
                }
                if (lacking.Count > 0)
                {
                    parts.Add(Texts.Pt($"não chega {Names(lacking)}", $"{Names(lacking)} doesn't arrive"));
                }
                else if (extra.Count > 0)
                {
                    parts.Add(Texts.Pt($"chega também {Names(extra)}", $"{Names(extra)} arrives too"));
                }
                status = string.Join(" · ", parts);
                color = lacking.Count > 0 || extra.Count > 0 ? BoardUi.Short : changes.Count > 0 ? BoardUi.Gold : BoardUi.Ok;
            }
            BoardUi.Text(row, "Status", BoardUi.StyleSmall, 44f, 23f, textW, 16f, color).text = status;

            // Quantity: [auto] [-] N [+], in items (one step is one craft; Shift: five).
            float right = RightW - 8f;
            if (own)
            {
                BoardUi.Button(row, "Plus", right - 24f, 11f, 24f, 22f, "+", false, () => StepOwn(recipe.OutItem, +1), recipe.Unlocked);
                BoardUi.Text(row, "Qty", BoardUi.StyleBold, right - 74f, 4f, 48f, 22f, BoardUi.Gold, TextAlignmentOptions.Center).text =
                    (snap.Options.Own.TryGetValue(recipe.OutItem, out int asked) ? asked : crafts * recipe.OutCount).ToString();
                BoardUi.Text(row, "Week", BoardUi.StyleSmall, right - 84f, 24f, 68f, 16f, BoardUi.Muted, TextAlignmentOptions.Center).text = Texts.Pt("avulso", "own");
                BoardUi.Button(row, "Minus", right - 98f, 11f, 24f, 22f, "-", false, () => StepOwn(recipe.OutItem, -1), true);
                BoardUi.Tip(row.gameObject, $"{Texts.Name(recipe.Station)}{(recipe.Extension != null ? " + " + Texts.Name(recipe.Extension) : "")}: {Texts.Name(recipe.OutItem)}",
                    RecipeTip(recipe, step, crafts, needed, mine, unplaced));
                return y + 48f;
            }
            BoardUi.Button(row, "Plus", right - 24f, 11f, 24f, 22f, "+", false, () => Step(recipe, crafts, +1), recipe.Unlocked);
            TMP_Text value = BoardUi.Text(row, "Qty", BoardUi.StyleBold, right - 74f, 4f, 48f, 22f, overridden ? BoardUi.Plan : BoardUi.Ink, TextAlignmentOptions.Center);
            value.text = (crafts * recipe.OutCount).ToString();
            BoardUi.Text(row, "Week", BoardUi.StyleSmall, right - 84f, 24f, 68f, 16f, BoardUi.Muted, TextAlignmentOptions.Center).text =
                overridden ? Texts.Pt($"semana: {needed * recipe.OutCount}", $"week: {needed * recipe.OutCount}") : Texts.Pt("da semana", "the week's");
            BoardUi.Button(row, "Minus", right - 98f, 11f, 24f, 22f, "-", false, () => Step(recipe, crafts, -1), recipe.Unlocked && crafts > 0);
            if (overridden)
            {
                BoardUi.ButtonParts auto = BoardUi.Button(row, "Auto", right - 146f, 11f, 44f, 22f, Texts.Pt("auto", "auto"), false, () => SetQuantity(recipe.Id, null));
                BoardUi.Tip(auto.Root.gameObject, null, Texts.Pt("Volta para a quantidade que os pedidos da semana pedem.", "Back to what the week's orders ask for."));
            }
            else if (!stage.Final)
            {
                BoardUi.ButtonParts outside = BoardUi.Button(row, "Outside", right - 146f, 11f, 44f, 22f, Texts.Pt("fora", "buy"), false, () => SetExternal(recipe.OutItem, true));
                BoardUi.Tip(outside.Root.gameObject, null, Texts.Pt($"Trazer {Texts.Name(recipe.OutItem)} pronto de fora em vez de fazer na fábrica.",
                    $"Bring {Texts.Name(recipe.OutItem)} in ready-made instead of making it in the factory."));
            }

            BoardUi.Tip(row.gameObject, $"{Texts.Name(recipe.Station)}{(recipe.Extension != null ? " + " + Texts.Name(recipe.Extension) : "")}: {Texts.Name(recipe.OutItem)}",
                RecipeTip(recipe, step, crafts, needed, mine, unplaced));
            return y + 48f;
        }

        private static string Names(IEnumerable<string> items) => string.Join(", ", items.Select(Texts.Name));

        private string From(Assignment a) =>
            a.Machine.Current != null && snap.Book.ById.TryGetValue(a.Machine.Current, out Recipe old)
                ? Texts.Pt($"fazia {Texts.Name(old.OutItem)}", $"was on {Texts.Name(old.OutItem)}")
                : Texts.Pt("parada", "idle");

        private string RecipeTip(Recipe recipe, StageStep step, int crafts, int needed, List<Assignment> mine, Unplaced unplaced)
        {
            var body = new StringBuilder();
            body.Append(Texts.Pt($"{crafts} craft(s) na semana: {crafts * recipe.OutCount} × {Texts.Name(recipe.OutItem)}",
                $"{crafts} craft(s) this week: {crafts * recipe.OutCount} × {Texts.Name(recipe.OutItem)}"));
            if (step.Own)
            {
                body.Append(Texts.Pt("\nPedido avulso: roda numa máquina só dele, e o que ela faz fica no baú da saída (não segue para a próxima etapa). " +
                        "Os insumos vêm das mesmas reservas e baús da linha: puxe uma esteira de um lado livre do baú que alimenta a máquina da linha, com o filtro do lado no item.",
                    "\nOwn order: it runs on a machine of its own, and what it makes stays in the exit chest (it doesn't go on to the next stage). " +
                        "The ingredients come from the line's reserves and chests: run a belt from a free side of the chest feeding the line's machine, that side filtered to the item."));
            }
            else if (crafts != needed)
            {
                body.Append(Texts.Pt($" (os pedidos pedem {needed * recipe.OutCount})", $" (the orders ask for {needed * recipe.OutCount})"));
            }
            body.Append(Texts.Pt("\nCada craft pede: ", "\nEach craft takes: ") +
                string.Join(", ", recipe.Needs.Select(i => $"{i.Count} × {Texts.Name(i.Item)}")));
            body.Append(Texts.Pt($"\nUso estimado: ~{Math.Max(1, (int)Math.Round(step.Load * 100))}% de uma máquina",
                $"\nEstimated use: ~{Math.Max(1, (int)Math.Round(step.Load * 100))}% of one machine"));
            if (mine.Count > 0)
            {
                body.Append(Texts.Pt("\n\nNo disparo:", "\n\nOn dispatch:"));
                foreach (Assignment a in mine)
                {
                    body.Append(a.After != null
                        ? Texts.Pt($"\n · {Texts.Name(a.Machine.Station)} faz isto depois de {Texts.Name(a.After.Recipe.OutItem)}, {a.Crafts} craft(s). " +
                                "A bancada só puxa o insumo da receita da vez: se os insumos das duas vêm pela mesma esteira, use o lado Sob demanda do baú.",
                            $"\n · {Texts.Name(a.Machine.Station)} makes this after {Texts.Name(a.After.Recipe.OutItem)}, {a.Crafts} craft(s). " +
                                "A bench only pulls what its current recipe takes: if both recipes are fed by the same belt, set the chest side to On demand.")
                        : a.Change
                        ? Texts.Pt($"\n · {Texts.Name(a.Machine.Station)} ({From(a)}) passa a fazer isto, {a.Crafts} craft(s)",
                            $"\n · {Texts.Name(a.Machine.Station)} ({From(a)}) switches to this, {a.Crafts} craft(s)")
                        : Texts.Pt($"\n · {Texts.Name(a.Machine.Station)} continua, agora com {a.Crafts} craft(s)",
                            $"\n · {Texts.Name(a.Machine.Station)} keeps it, now {a.Crafts} craft(s)"));
                }
                if (mine.Any(a => a.Change))
                {
                    body.Append(Texts.Pt("\nUma máquina no meio de um craft termina esse antes de trocar.", "\nA machine in the middle of a craft finishes it before switching."));
                }
                foreach (Assignment a in mine.Where(a => a.Lacking.Count > 0 || a.Extra.Count > 0))
                {
                    if (a.Lacking.Count > 0)
                    {
                        body.Append(Texts.Pt($"\nAs esteiras dessa {Texts.Name(a.Machine.Station)} não trazem {Names(a.Lacking)}: ela vai parar sem insumo.",
                            $"\nThat {Texts.Name(a.Machine.Station)}'s belts don't bring {Names(a.Lacking)}: it will stall."));
                    }
                    if (a.Extra.Count > 0)
                    {
                        body.Append(Texts.Pt($"\nChega também {Names(a.Extra)}, que a receita não usa: o item para na entrada e trava a esteira. Ponha filtro no lado do baú.",
                            $"\n{Names(a.Extra)} arrives too, which the recipe doesn't take: it stops at the input and blocks the belt. Filter that chest side."));
                    }
                }
            }
            else if (unplaced != null)
            {
                List<Blueprint> making = BlueprintStore.Making(recipe.OutItem);
                body.Append(unplaced.NeedsExtension
                    ? Texts.Pt($"\n\nA receita exige a extensão {Texts.Name(recipe.Extension)}, e nenhuma máquina da fábrica tem essa extensão. Construa a extensão ao lado de uma {Texts.Name(recipe.Station)}.",
                        $"\n\nThe recipe needs the {Texts.Name(recipe.Extension)} extension and no machine in the factory has it. Build it next to a {Texts.Name(recipe.Station)}.")
                    : unplaced.NoMachine
                    ? Texts.Pt($"\n\nNenhuma {Texts.Name(recipe.Station)} livre para isto.", $"\n\nNo free {Texts.Name(recipe.Station)} for this.")
                    : Texts.Pt($"\n\nHá {Texts.Name(recipe.Station)} livre, mas as esteiras de nenhuma trazem {Names(unplaced.Lacking)}. Ligue a entrada dela a quem tem isso, ou monte o módulo.",
                        $"\n\nThere is a free {Texts.Name(recipe.Station)}, but no belts bring it {Names(unplaced.Lacking)}. Link its input to where that comes out, or lay the module."));
                body.Append(making.Count > 0 ? Texts.Pt(" Na Mesa de Arquitetura: ", " On the Architect's Desk: ") + string.Join(", ", making.Select(b => b.Name)) : "");
            }
            body.Append(step.Own
                ? Texts.Pt("\n\n+ e - mudam a quantidade do avulso (Shift: 5 crafts); em 0 ele sai.", "\n\n+ and - change the own order (Shift: 5 crafts); at 0 it goes.")
                : Texts.Pt("\n\n+ e - mudam a quantidade (Shift: 5 crafts); os insumos acompanham.", "\n\n+ and - change the quantity (Shift: 5 crafts); the ingredients follow."));
            body.Append(Texts.Pt("\nClique na linha: de onde vem cada insumo e onde pôr a máquina.",
                "\nClick the row: where each ingredient comes from and where to put the machine."));
            return body.ToString();
        }

        private float InputRow(string item, int amount, float y)
        {
            int have = snap.Reserves.TryGetValue(item, out int h) ? h : 0;
            bool external = snap.Options.External.Contains(item);
            RectTransform row = BoardUi.Row(weekList, "Input_" + item, y + 2f, 32f, BoardUi.PlateSprite, false, null);
            BoardUi.ItemIcon(row, item, 8f, 4f, 24f);
            BoardUi.Text(row, "Name", BoardUi.StyleText, 40f, 0f, RightW - 260f, 32f, BoardUi.Ink).text = Texts.Name(item) +
                (external ? Texts.Pt("  (trazido pronto)", "  (brought in)") : "");
            float right = RightW - 8f;
            if (external)
            {
                BoardUi.ButtonParts make = BoardUi.Button(row, "Make", right - 56f, 5f, 56f, 22f, Texts.Pt("fazer", "make"), false, () => SetExternal(item, false));
                BoardUi.Tip(make.Root.gameObject, null, Texts.Pt("Voltar a fazer na fábrica.", "Make it in the factory again."));
                right -= 64f;
            }
            BoardUi.Text(row, "Amount", BoardUi.StyleSmall, right - 170f, 0f, 170f, 32f, have >= amount ? BoardUi.Ink : BoardUi.Short, TextAlignmentOptions.MidlineRight).text =
                Texts.Pt($"{amount} na semana · reserva {have}", $"{amount} a week · reserve {have}");
            BoardUi.Tip(row.gameObject, Texts.Name(item),
                Texts.Pt($"A semana usa {amount}. Agora há {have} nas reservas e baús da fábrica; os zumbis repõem as reservas e as máquinas puxam pela esteira.",
                    $"The week uses {amount}. The factory's reserves and chests hold {have} now; the zombies refill the reserves and the machines pull down the belts."));
            return y + 36f;
        }

        // ---- foot

        private void FillFoot()
        {
            BoardUi.Clear(foot);
            bool any = snap.Plan.Assignments.Count > 0;
            float right = Width;
            BoardUi.ButtonParts go = BoardUi.Button(foot, "DispatchAll", right - 170f, 8f, 170f, 30f,
                armed == "all" ? Texts.Pt("Confirmar disparo?", "Confirm dispatch?") : Texts.Pt("Disparar a semana", "Dispatch the week"),
                true, () => DispatchClick("all", null), any);
            BoardUi.Tip(go.Root.gameObject, Texts.Pt("Disparar a semana", "Dispatch the week"),
                Texts.Pt("Põe as máquinas que já existem nas receitas e quantidades da semana. Clique duas vezes: a primeira mostra o que muda.",
                    "Puts the machines you already have on the week's recipes and quantities. Click twice: the first shows what changes."));
            right -= 178f;
            if (snap.Options.Quantities.Count > 0)
            {
                BoardUi.ButtonParts reset = BoardUi.Button(foot, "Reset", right - 150f, 10f, 150f, 26f, Texts.Pt("Quantidades da semana", "The week's quantities"), false, ResetQuantities);
                BoardUi.Tip(reset.Root.gameObject, null, Texts.Pt("Apaga os ajustes de quantidade e volta ao que os pedidos pedem.", "Drops the quantity changes and goes back to what the orders ask for."));
                right -= 158f;
            }
            BoardUi.Text(foot, "Note", BoardUi.StyleSmall, 4f, 4f, right - 12f, FootH - 6f, armed != null ? BoardUi.Gold : BoardUi.Muted,
                TextAlignmentOptions.MidlineLeft, true).text = Note();
        }

        private string Note()
        {
            if (armed != null)
            {
                ICollection<string> only = armed == "all" ? null : StageRecipes(armed);
                List<Assignment> list = snap.Plan.Assignments.Where(a => only == null || only.Contains(a.Recipe.Id)).ToList();
                int changes = list.Count(a => a.Change);
                int kept = list.Count - changes;
                int missing = snap.Plan.Missing.Count(m => only == null || only.Contains(m.Recipe.Id));
                int unfed = list.Count(a => a.Lacking.Count > 0);
                int blocked = list.Count(a => a.Lacking.Count == 0 && a.Extra.Count > 0);
                int stalled = snap.Plan.Stalled.Count;
                if (stalled > 0)
                {
                    string names = string.Join(", ", snap.Plan.Stalled.Take(3).Select(s => $"{Texts.Name(s.Machine.Station)} ({Names(s.Lacking)})"));
                    return Texts.Pt($"Vai trocar {changes} e ajustar {kept}. Atenção: {stalled} máquina(s) ficam numa receita que as esteiras não alimentam: {names}. Clique de novo para confirmar.",
                        $"Switches {changes}, sets {kept}. Note: {stalled} machine(s) stay on a recipe their belts can't feed: {names}. Click again to confirm.");
                }
                return Texts.Pt($"Vai trocar a receita de {changes} máquina(s) e ajustar a quantidade de {kept}" +
                        (missing > 0 ? $"; {missing} receita(s) ficam sem máquina (nenhuma livre recebe os insumos)" : "") +
                        (unfed > 0 ? $"; {unfed} já na receita sem algum insumo chegando" : "") +
                        (blocked > 0 ? $"; {blocked} recebem item que não usam" : "") + ". Clique de novo para confirmar.",
                    $"Switches {changes} machine(s) and sets the quantity of {kept}" +
                        (missing > 0 ? $"; {missing} recipe(s) have no machine (no free one gets the ingredients)" : "") +
                        (unfed > 0 ? $"; {unfed} already on it lack an ingredient" : "") +
                        (blocked > 0 ? $"; {blocked} get an item they don't use" : "") + ". Click again to confirm.");
            }
            if (!string.IsNullOrEmpty(message))
            {
                return message;
            }
            if (WeekDispatch.PendingCount > 0)
            {
                return Texts.Pt($"{WeekDispatch.PendingCount} máquina(s) terminando o craft antes de trocar.", $"{WeekDispatch.PendingCount} machine(s) finishing a craft before switching.");
            }
            return snap.Planned.Count == 0 && snap.Options.Own.Count == 0
                ? Texts.Pt("As máquinas puxam a matéria-prima das reservas pela esteira; o disparo só diz a cada uma o que fazer e quanto.",
                    "Machines pull raw materials from the reserves down the belts; dispatching only tells each one what to make and how much.")
                : Texts.Pt("Ajuste as quantidades com + e -, depois dispare a semana inteira ou uma etapa por vez.",
                    "Adjust the quantities with + and -, then dispatch the whole week or one stage at a time.");
        }

        private List<string> StageRecipes(string key)
        {
            int index = int.Parse(key.Substring("stage:".Length));
            return snap.Stages.Stages.Where(s => s.Index == index).SelectMany(s => s.Steps).Select(s => s.Recipe.Id).ToList();
        }

        // ---------------------------------------------------------------- actions

        private void DispatchClick(string key, ICollection<string> only)
        {
            if (armed != key)
            {
                armed = key;
                Refill();
                return;
            }
            armed = null;
            WeekDispatch.Result r = WeekDispatch.Dispatch(only);
            string what = key == "all" ? Texts.Pt("Semana disparada", "Week dispatched")
                : Texts.Pt($"Etapa {key.Substring(6)} disparada", $"Stage {key.Substring(6)} dispatched");
            message = $"{what}: {WeekDispatch.Describe(r)}.";
            if (r.Supply != null && !r.Supply.Off && (r.Supply.Moved.Count > 0 || r.Supply.Short.Count > 0 || r.Supply.NoChest.Count > 0))
            {
                message += Texts.Pt(" Suprimentos: ", " Supplies: ") + Supply.Describe(r.Supply) + ".";
            }
            WeekDispatch.Notify("gk2fp_dispatch_done", message, Icons.DraftingTable);
            Refill();
        }

        private void Step(Recipe recipe, int crafts, int direction)
        {
            int step = BoardUi.Shift() ? 5 : 1;
            SetQuantity(recipe.Id, Math.Max(0, crafts + direction * step));
        }

        private void SetQuantity(string recipeId, int? crafts)
        {
            armed = null;
            message = "";
            int needed = snap.Week.Needed.TryGetValue(recipeId, out int n) ? n : -1;
            GameData.SetQuantity(recipeId, crafts.HasValue && crafts.Value == needed ? (int?)null : crafts);
            Refill();
        }

        private void ResetQuantities()
        {
            armed = null;
            message = "";
            foreach (string id in snap.Options.Quantities.Keys.ToList())
            {
                GameData.SetQuantity(id, null);
            }
            Refill();
        }

        private void SetExternal(string item, bool external)
        {
            armed = null;
            message = "";
            GameData.SetExternal(item, external);
            Refill();
        }
    }
}
