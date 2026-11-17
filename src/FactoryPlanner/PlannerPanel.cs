using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using GK2Mods.FactoryPlanner.Blueprints;
using GK2Mods.FactoryPlanner.Core;
using HarmonyLib;
using LazyBearTechnology;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// The Drafting Table's window: the game's chest window with the town's orders on the left and the factory
    /// plan on the right. Every section is a display-only ("virtual") inventory whose cells stand for orders,
    /// machines or inputs; clicking a cell changes the plan, hovering it explains it.
    /// </summary>
    internal static class PlannerPanel
    {
        private enum CellKind
        {
            Order,
            Machine,
            Input,
        }

        private sealed class Cell
        {
            public CellKind Kind;
            public string OrderId;
            public string RecipeId;
            public string ItemId;
            public bool Active = true;
            public string Header;
            public string Body;
        }

        private sealed class ByReference : IEqualityComparer<Item>
        {
            public bool Equals(Item a, Item b) => ReferenceEquals(a, b);
            public int GetHashCode(Item item) => RuntimeHelpers.GetHashCode(item);
        }

        private static readonly Dictionary<Item, Cell> cells = new Dictionary<Item, Cell>(new ByReference());
        private static readonly HashSet<Inventory> virtuals = new HashSet<Inventory>();

        private static PlayerController player;
        private static WgoData table;
        private static UIBaseChestWindowData data;

        private static RecipeBook book;
        private static List<Order> orders;
        private static FactoryState factory;
        private static HashSet<string> plan;
        private static HashSet<string> external;
        private static Dictionary<string, LineResult> lineByOrder;

        private static int sectionCounter;

        internal static bool IsVirtual(Inventory inventory) => inventory != null && virtuals.Contains(inventory);

        internal static bool TryOpen(PlayerController interactor, WgoData tableWgo)
        {
            player = interactor;
            table = tableWgo;
            book = GameData.Recipes();
            orders = GameData.Orders();
            if (orders.Count == 0)
            {
                Plugin.Log.LogWarning("Drafting Table: no town orders found in this save; opening as a chest.");
                return false;
            }
            factory = GameData.Factory();
            plan = GameData.LoadPlan(orders);
            external = GameData.LoadExternal(book);

            data = Build();
            UIChestWindow window = LazyUI.GetWindow<UIChestWindow>();
            window.Open(data);
            SetWindowHeaders(window);
            Plugin.Debug($"Drafting Table: {orders.Count} orders, {plan.Count} in the plan, {factory.RunningRecipes.Count} recipes running");
            return true;
        }

        /// <summary>
        /// Redraws the open window with a freshly built plan. Each column must be hidden first: drawing new data
        /// on a shown column adds widgets for it without releasing the old ones, which breaks the chest window
        /// until the game restarts. The scroll positions are kept, so a click doesn't jump back to the top.
        /// </summary>
        private static void Refresh()
        {
            UIChestWindow window = LazyUI.GetWindow<UIChestWindow>();
            if (!window.IsShown)
            {
                return;
            }
            float leftScroll = window.leftMultiInventoryWidget.scrollRect.verticalNormalizedPosition;
            float rightScroll = window.rightMultiInventoryWidget.scrollRect.verticalNormalizedPosition;
            try
            {
                data = Build();
                window.leftMultiInventoryWidget.Hide();
                window.rightMultiInventoryWidget.Hide();
                window.Draw(data);
                SetWindowHeaders(window);
                UnityEngine.Canvas.ForceUpdateCanvases();
                window.leftMultiInventoryWidget.scrollRect.verticalNormalizedPosition = leftScroll;
                window.rightMultiInventoryWidget.scrollRect.verticalNormalizedPosition = rightScroll;
            }
            catch
            {
                // Never leave a half-drawn chest window behind: close it, the board can be opened again.
                try
                {
                    window.Close();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Planning Board: closing the window failed too: {e.Message}");
                }
                throw;
            }
        }

        private static void SetWindowHeaders(UIBaseChestWindow window)
        {
            try
            {
                window.headerLeft.text = Texts.Pt("Pedidos das lojas", "Shop orders");
                window.headerRight.text = Texts.Pt("Planta da fábrica", "Factory plan");
            }
            catch (Exception e)
            {
                Plugin.Debug($"Window headers not set: {e.Message}");
            }
        }

        // ---------------------------------------------------------------- building the window

        private static UIBaseChestWindowData Build()
        {
            cells.Clear();
            virtuals.Clear();
            sectionCounter = 0;

            var options = new PlanOptions { Factory = factory, External = external };
            lineByOrder = orders.ToDictionary(o => o.Id, o => Planner.ForOrders(new[] { o }, book, options));
            List<Order> planned = orders.Where(o => plan.Contains(o.Id)).ToList();
            LineResult line = Planner.ForOrders(planned, book, options);
            List<Expansion> ranked = Planner.RankExpansions(
                orders.Where(o => !plan.Contains(o.Id) && o.Available && !o.DoneThisWeek), line, book, options);

            var d = new UIBaseChestWindowData(player.PlayerData.inventory, null, table);
            MultiInventoryWidgetData left = d.FirstMultiInventoryData;
            MultiInventoryWidgetData right = d.SecondMultiInventoryData;
            left.inventoriesData.Clear();
            right.inventoriesData.Clear();
            // No selected section: in the selection modes every section but the first is drawn greyed out.
            left.WidgetSelectionMode = MultiInventoryWidgetMode.Default;
            right.WidgetSelectionMode = MultiInventoryWidgetMode.Default;
            left.OnMoveAllSimilarPressed = null;
            left.GetMoveAllSimilarTargetInventory = () => null;

            // Left: accepted orders first, then every vendor's orders.
            List<Order> accepted = orders.Where(o => o.Accepted && !plan.Contains(o.Id)).ToList();
            if (accepted.Count > 0)
            {
                AddSection(left, Texts.Pt("Aceitos no quadro de pedidos", "Accepted on the order board"),
                    accepted.Select(OrderCell));
            }
            foreach (var vendor in orders.Where(o => !o.Accepted || plan.Contains(o.Id)).GroupBy(o => o.VendorId)
                         .OrderByDescending(g => g.Any(o => o.Available))
                         .ThenBy(g => Texts.Name(g.Key), StringComparer.CurrentCulture))
            {
                List<Order> open = vendor.Where(o => !plan.Contains(o.Id)).OrderBy(o => o.Tier).ThenBy(o => o.Urgent).ThenBy(o => o.Id).ToList();
                if (open.Count == 0)
                {
                    continue;
                }
                AddSection(left, $"{Texts.Name(vendor.Key)} · {Texts.Pt("nível", "level")} {GameData.VendorTier(vendor.Key)}",
                    open.Select(OrderCell));
            }

            // Right: the plan, its machines, what comes in, and what to add next.
            float happiness = planned.Sum(o => o.Happiness);
            AddSection(right,
                planned.Count == 0
                    ? Texts.Pt("Planta vazia: clique num pedido à esquerda", "Empty plan: click an order on the left")
                    : Texts.Pt($"Na planta: {planned.Count} pedido(s) · +{happiness:0} felicidade por semana",
                        $"In the plan: {planned.Count} order(s) · +{happiness:0} happiness a week"),
                planned.Select(OrderCell));

            if (planned.Count > 0)
            {
                var (used, produced) = GameData.Power();
                int newMachines = line.RecipeOrder.Count(id => !factory.RunningRecipes.Contains(id));
                AddSection(right,
                    Texts.Pt($"Máquinas: {line.RecipeOrder.Count} ({newMachines} nova(s)) · energia da fábrica {used:0}/{produced:0}",
                        $"Machines: {line.RecipeOrder.Count} ({newMachines} new) · factory power {used:0}/{produced:0}"),
                    line.RecipeOrder.Select(id => MachineCell(id, line, planned)));
                AddSection(right, Texts.Pt("Entra na fábrica por semana", "Brought into the factory each week"),
                    line.Inputs.OrderByDescending(kv => kv.Value).Select(kv => InputCell(kv.Key, kv.Value, line)));
            }

            List<Expansion> next = ranked.Where(e => !e.Locked).Take(Plugin.Suggestions.Value).ToList();
            if (next.Count > 0)
            {
                AddSection(right,
                    Texts.Pt("Próximo pedido sugerido (menos máquinas novas)", "Suggested next order (fewest new machines)"),
                    next.Select(e => OrderCell(e.Order)));
            }
            return d;
        }

        private static void AddSection(MultiInventoryWidgetData side, string title, IEnumerable<(Item item, Cell cell)> content)
        {
            List<(Item item, Cell cell)> list = content.ToList();
            string key = Texts.Dynamic($"gk2fp_section_{sectionCounter++}", title);
            var inventory = new Inventory("inventory", Math.Max(1, list.Count), key);
            foreach (var (item, cell) in list)
            {
                inventory.Data.Inventory.Add(item);
                cells[item] = cell;
            }
            inventory.Data.CalculateInventoryFillSize();
            virtuals.Add(inventory);
            // Cells call the planner directly: the game's move handler would open the "how many?" window for stacks.
            List<InventoryWidgetDataBase> widgets = InventoryWidgetDataHelper.GetWidgetsDataForInventory(
                inventory, _ => LazyAudio.PlayAndForget("gui_hover_light"), null, OnPress, OnPress,
                null, item => item == null || !cells.TryGetValue(item, out Cell c) || c.Active, addBags: false);
            // The default header icon is the player's sack (main inventory); these sections are not inventories.
            foreach (InventoryWidgetDataBase widget in widgets)
            {
                if (widget is InventoryWidgetData section && section.InventoryHeaderWidgetData != null)
                {
                    section.InventoryHeaderWidgetData.HeaderIconId = null;
                }
            }
            side.inventoriesData.AddRange(widgets);
        }

        // ---------------------------------------------------------------- cells

        private static (Item, Cell) OrderCell(Order order)
        {
            LineResult line = lineByOrder[order.Id];
            bool inPlan = plan.Contains(order.Id);
            var body = new StringBuilder();
            body.Append($"{Texts.Name(order.VendorId)} · {Texts.Pt("nível", "level")} {order.Tier}\n");
            body.Append(Texts.Pt($"{order.Count} caixas · +{order.Happiness:0} felicidade", $"{order.Count} boxes · +{order.Happiness:0} happiness"));
            if (order.Urgent)
            {
                body.Append(Texts.Pt("\nEntrega única: as caixas precisam estar juntas nos paletes.", "\nSingle delivery: all boxes must be on the pallets together."));
            }
            if (order.Accepted)
            {
                body.Append(Texts.Pt("\nAceito no quadro de pedidos.", "\nAccepted on the order board."));
            }
            if (order.DoneThisWeek)
            {
                body.Append(Texts.Pt("\nJá entregue nesta semana.", "\nAlready delivered this week."));
            }
            if (!order.Available)
            {
                body.Append(GameData.VendorTier(order.VendorId) < order.Tier
                    ? Texts.Pt($"\nA loja precisa chegar ao nível {order.Tier}.", $"\nThe shop must reach level {order.Tier}.")
                    : Texts.Pt("\nA loja ainda não aceita pedidos.", "\nThe shop doesn't take orders yet."));
            }

            var have = new HashSet<string>(factory.RunningRecipes);
            if (!inPlan)
            {
                have.UnionWith(Planner.ForOrders(orders.Where(o => plan.Contains(o.Id)), book,
                    new PlanOptions { Factory = factory, External = external }).Crafts.Keys);
            }
            List<string> fresh = line.RecipeOrder.Where(id => !have.Contains(id)).ToList();
            body.Append("\n\n");
            body.Append(fresh.Count == 0
                ? Texts.Pt("Usa só máquinas que a fábrica e a planta já têm.", "Uses only machines the factory and the plan already have.")
                : Texts.Pt("Máquinas novas: ", "New machines: ") + string.Join(", ", fresh.Select(MachineName)));
            if (line.Locked.Count > 0)
            {
                body.Append(Texts.Pt("\nFalta pesquisar: ", "\nStill to research: ") + string.Join(", ", LockedTechNames(line.Locked)));
            }
            body.Append("\n\n");
            bool active = order.Available && line.Locked.Count == 0;
            body.Append(inPlan
                ? Texts.Pt("Clique para tirar da planta.", "Click to remove it from the plan.")
                : active
                    ? Texts.Pt("Clique para pôr na planta.", "Click to add it to the plan.")
                    : Texts.Pt("Ainda não dá para pôr na planta.", "Can't be planned yet."));

            var cell = new Cell
            {
                Kind = CellKind.Order,
                OrderId = order.Id,
                ItemId = order.BoxItem,
                Active = active || inPlan,
                Header = Texts.Name(order.BoxItem),
                Body = body.ToString(),
            };
            return (new Item(order.BoxItem, Math.Max(1, order.Count)), cell);
        }

        private static (Item, Cell) MachineCell(string recipeId, LineResult line, List<Order> planned)
        {
            Recipe recipe = book.ById[recipeId];
            int crafts = line.Crafts[recipeId];
            int made = line.Made.TryGetValue(recipe.OutItem, out int m) ? m : crafts * recipe.OutCount;
            bool isBox = planned.Any(o => o.BoxItem == recipe.OutItem);

            var body = new StringBuilder();
            body.Append(Texts.Pt($"{crafts} craft(s) por semana: {made} × {Texts.Name(recipe.OutItem)}",
                $"{crafts} craft(s) a week: {made} × {Texts.Name(recipe.OutItem)}"));
            body.Append(Texts.Pt("\nCada craft pede: ", "\nEach craft takes: ") +
                string.Join(", ", recipe.Needs.Select(n => $"{n.Count} × {Texts.Name(n.Item)}")));
            float load = crafts * recipe.Duration * GameData.TickSeconds(recipe.Station) / Math.Max(1f, GameData.WeekSeconds());
            body.Append(Texts.Pt($"\nUso estimado: ~{Math.Max(1, (int)Math.Round(load * 100))}% de uma máquina",
                $"\nEstimated use: ~{Math.Max(1, (int)Math.Round(load * 100))}% of one machine"));
            if (load > 1f)
            {
                body.Append(Texts.Pt($" (pede {(int)Math.Ceiling(load)} máquinas)", $" (needs {(int)Math.Ceiling(load)} machines)"));
            }
            if (recipe.Stations.Count > 1)
            {
                body.Append(Texts.Pt("\nTambém roda em: ", "\nAlso runs on: ") + string.Join(", ", recipe.Stations.Skip(1).Select(Texts.Name)));
            }
            body.Append("\n\n");
            if (!recipe.Unlocked)
            {
                body.Append(Texts.Pt("Receita não pesquisada", "Recipe not researched") +
                    (recipe.TechId != null ? $": {Texts.Name(recipe.TechId)}" : "") + ".");
            }
            else if (factory.RunningRecipes.Contains(recipeId))
            {
                body.Append(Texts.Pt("A fábrica já tem uma máquina fazendo isso.", "The factory already has a machine making this."));
            }
            else
            {
                body.Append(Texts.Pt("Máquina nova.", "New machine."));
                if (recipe.Extension != null && !factory.Extensions.ContainsKey(recipe.Extension))
                {
                    body.Append(Texts.Pt($" A fábrica ainda não tem {Texts.Name(recipe.Extension)}.",
                        $" The factory has no {Texts.Name(recipe.Extension)} yet."));
                }
                List<Blueprint> making = BlueprintStore.Making(recipe.OutItem);
                body.Append(making.Count > 0
                    ? Texts.Pt("\nNa Mesa de Arquitetura: ", "\nOn the Architect's Desk: ") + string.Join(", ", making.Select(b => b.Name))
                    : Texts.Pt("\nNenhuma planta faz isto ainda.", "\nNo blueprint makes this yet."));
            }
            if (!isBox)
            {
                body.Append(Texts.Pt($"\nClique para trazer {Texts.Name(recipe.OutItem)} pronto de fora em vez de fazer na fábrica.",
                    $"\nClick to bring {Texts.Name(recipe.OutItem)} in ready-made instead of making it in the factory."));
            }

            var cell = new Cell
            {
                Kind = CellKind.Machine,
                RecipeId = recipeId,
                ItemId = recipe.OutItem,
                Active = recipe.Unlocked,
                Header = MachineName(recipeId),
                Body = body.ToString(),
            };
            return (new Item(recipe.OutItem, Math.Max(1, made)), cell);
        }

        private static (Item, Cell) InputCell(string itemId, int amount, LineResult line)
        {
            var users = line.RecipeOrder.Select(id => book.ById[id]).Where(r => r.Needs.Any(n => n.Item == itemId)).ToList();
            var body = new StringBuilder();
            body.Append(Texts.Pt($"{amount} por semana", $"{amount} a week"));
            if (users.Count > 0)
            {
                body.Append(Texts.Pt("\nVai para: ", "\nGoes to: ") + string.Join(", ", users.Select(r => MachineName(r.Id))));
            }
            if (external.Contains(itemId))
            {
                body.Append(Texts.Pt("\n\nVocê marcou para trazer pronto. Clique para voltar a fazer na fábrica.",
                    "\n\nMarked as brought in ready-made. Click to make it in the factory again."));
            }
            var cell = new Cell
            {
                Kind = CellKind.Input,
                ItemId = itemId,
                Header = Texts.Name(itemId),
                Body = body.ToString(),
            };
            return (new Item(itemId, Math.Max(1, amount)), cell);
        }

        private static string MachineName(string recipeId)
        {
            Recipe recipe = book.ById[recipeId];
            string station = Texts.Name(recipe.Station);
            if (recipe.Extension != null)
            {
                station += " + " + Texts.Name(recipe.Extension);
            }
            return $"{station}: {Texts.Name(recipe.OutItem)}";
        }

        private static IEnumerable<string> LockedTechNames(IEnumerable<string> recipeIds) =>
            recipeIds.Select(id => book.ById[id].TechId).Where(t => t != null).Distinct().Select(Texts.Name);

        // ---------------------------------------------------------------- clicks and tooltips

        private static void OnPress(UIItemCell itemCell)
        {
            try
            {
                OnClick(itemCell);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Drafting Table click failed: {e}");
            }
        }

        internal static bool OnClick(UIItemCell itemCell)
        {
            Item item = itemCell?.DisplayingItem;
            if (item == null || !cells.TryGetValue(item, out Cell cell))
            {
                return false;
            }
            switch (cell.Kind)
            {
                case CellKind.Order:
                    bool add = !plan.Contains(cell.OrderId);
                    if (add)
                    {
                        plan.Add(cell.OrderId);
                    }
                    else
                    {
                        plan.Remove(cell.OrderId);
                    }
                    GameData.SetInPlan(cell.OrderId, add);
                    break;
                case CellKind.Machine:
                    if (orders.Any(o => plan.Contains(o.Id) && o.BoxItem == cell.ItemId))
                    {
                        return false;  // the boxes themselves are what the plan is for
                    }
                    external.Add(cell.ItemId);
                    GameData.SetExternal(cell.ItemId, true);
                    break;
                case CellKind.Input:
                    if (!external.Remove(cell.ItemId))
                    {
                        return false;
                    }
                    GameData.SetExternal(cell.ItemId, false);
                    break;
            }
            LazyAudio.PlayAndForget("gui_hover_light");
            UITooltip.Hide();
            Refresh();
            return true;
        }

        internal static bool TryShowTooltip(UIItemCell itemCell)
        {
            Item item = itemCell?.DisplayingItem;
            if (item == null || !cells.TryGetValue(item, out Cell cell))
            {
                return false;
            }
            UITooltip.ShowSimpleInfo(itemCell.transform, cell.Body, default, cell.Header);
            return true;
        }
    }

    /// <summary>Guard: every item move in an inventory window goes through TryMoveItem; none may touch the table's sections.</summary>
    [HarmonyPatch(typeof(InventoryUIItemMoveOpHandler), nameof(InventoryUIItemMoveOpHandler.TryMoveItem))]
    internal static class InventoryUIItemMoveOpHandler_TryMoveItem
    {
        private static bool Prefix(UIItemCell itemCell, Inventory from, Inventory to, ref bool __result)
        {
            if (!PlannerPanel.IsVirtual(from) && !PlannerPanel.IsVirtual(to) && !ArchitectPanel.IsVirtual(from) && !ArchitectPanel.IsVirtual(to))
            {
                return true;
            }
            __result = false;  // nothing moves in or out of the table's sections
            return false;
        }
    }

    [HarmonyPatch(typeof(UITooltip), nameof(UITooltip.ShowItemCell))]
    internal static class UITooltip_ShowItemCell
    {
        private static bool Prefix(UIItemCell uiItemCell)
        {
            try
            {
                return !PlannerPanel.TryShowTooltip(uiItemCell) && !ArchitectPanel.TryShowTooltip(uiItemCell);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Drafting Table tooltip failed: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(ChestInteractionHandler), nameof(ChestInteractionHandler.Interact))]
    internal static class ChestInteractionHandler_Interact_DraftingTable
    {
        private static bool Prefix(ChestInteractionHandler __instance, PlayerController interactor, ref bool __result)
        {
            WgoData data = __instance.assignedWgo?.Data;
            if (!Content.IsOurTag(data?.CustomTag) || data.id.EndsWith("_place", StringComparison.Ordinal))
            {
                return true;
            }
            try
            {
                // The board opens its own window; the chest-window version stays as the fallback (and by config).
                bool opened = data.CustomTag == Content.ArchitectTag
                    ? ArchitectPanel.TryOpen(interactor, data)
                    : (!Plugin.ClassicBoard.Value && BoardWindow.TryOpen(interactor, data)) || PlannerPanel.TryOpen(interactor, data);
                if (opened)
                {
                    __result = true;
                    return false;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"{data.CustomTag} failed, opening as a chest: {e}");
            }
            return true;
        }
    }
}
