using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using GK2Mods.FactoryPlanner.Blueprints;
using GK2Mods.FactoryPlanner.Core;
using LazyBearTechnology;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// The Architect's Desk window (the game's chest window, display-only sections like the Planning Board's).
    /// Left: the Trace tool and the saved blueprints. Right: the week's schedule from the Planning Board, its new
    /// machines and which blueprints make them. A click on a tool, a blueprint or a covered machine closes the
    /// window and opens the factory's build mode on that tool.
    /// </summary>
    internal static class ArchitectPanel
    {
        private enum CellKind
        {
            Info,
            Trace,
            StagesToggle,
            Module,
            Blueprint,
            Cluster,
            Machine,
        }

        private sealed class Cell
        {
            public CellKind Kind;
            public string BlueprintName;
            public string RecipeId;
            public int ClusterIndex = -1;
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

        private const string PaperItem = "clean_paper";

        private static readonly Dictionary<Item, Cell> cells = new Dictionary<Item, Cell>(new ByReference());
        private static readonly HashSet<Inventory> virtuals = new HashSet<Inventory>();

        private static PlayerController player;
        private static WgoData desk;
        private static int sectionCounter;

        private static RecipeBook book;
        private static FactoryState factory;
        private static LineResult week;
        private static HashSet<string> weekItems = new HashSet<string>();
        private static MultiInventory stock;
        private static List<FactoryCluster> clusters = new List<FactoryCluster>();
        private static List<Order> planned = new List<Order>();

        /// <summary>The right column shows the module catalog by stage (default) instead of the week's schedule.</summary>
        private static bool stagesView = true;
        private static StagePlan stages;
        private static readonly Dictionary<string, Blueprint> modules = new Dictionary<string, Blueprint>();

        /// <summary>Blueprint waiting for a second right click to be deleted.</summary>
        private static string deleteArmed;

        internal static bool IsVirtual(Inventory inventory) => inventory != null && virtuals.Contains(inventory);

        internal static bool TryOpen(PlayerController interactor, WgoData deskWgo)
        {
            player = interactor;
            desk = deskWgo;
            deleteArmed = null;
            UIChestWindow window = LazyUI.GetWindow<UIChestWindow>();
            window.Open(Build());
            SetHeaders(window);
            Plugin.Debug($"Architect's Desk: {BlueprintStore.All.Count} blueprint(s), {week?.RecipeOrder.Count ?? 0} machine(s) in the week's plan");
            return true;
        }

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
                UIBaseChestWindowData data = Build();
                // Hide first: drawing on a shown column adds widgets without releasing the old ones.
                window.leftMultiInventoryWidget.Hide();
                window.rightMultiInventoryWidget.Hide();
                window.Draw(data);
                SetHeaders(window);
                UnityEngine.Canvas.ForceUpdateCanvases();
                window.leftMultiInventoryWidget.scrollRect.verticalNormalizedPosition = leftScroll;
                window.rightMultiInventoryWidget.scrollRect.verticalNormalizedPosition = rightScroll;
            }
            catch
            {
                try
                {
                    window.Close();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Architect's Desk: closing the window failed too: {e.Message}");
                }
                throw;
            }
        }

        private static void SetHeaders(UIBaseChestWindow window)
        {
            try
            {
                window.headerLeft.text = Texts.Pt("Plantas", "Blueprints");
                window.headerRight.text = stagesView
                    ? Texts.Pt("Módulos por etapa", "Modules by stage")
                    : Texts.Pt("Cronograma da semana", "This week's schedule");
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

            book = GameData.Recipes();
            factory = GameData.Factory();
            List<Order> orders = GameData.Orders();
            HashSet<string> plan = GameData.LoadPlan(orders);
            planned = orders.Where(o => plan.Contains(o.Id)).ToList();
            var options = new PlanOptions { Factory = factory, External = GameData.LoadExternal(book) };
            week = Planner.ForOrders(planned, book, options);
            weekItems = new HashSet<string>(week.RecipeOrder.Select(id => book.ById[id].OutItem));
            stock = new MultiInventory(player.PlayerData);
            try
            {
                clusters = Clusters.Find(book, factory);
            }
            catch (Exception e)
            {
                clusters = new List<FactoryCluster>();
                Plugin.Log.LogError($"Architect's Desk: factory lines could not be read: {e}");
            }

            var d = new UIBaseChestWindowData(player.PlayerData.inventory, null, desk);
            MultiInventoryWidgetData left = d.FirstMultiInventoryData;
            MultiInventoryWidgetData right = d.SecondMultiInventoryData;
            left.inventoriesData.Clear();
            right.inventoriesData.Clear();
            left.WidgetSelectionMode = MultiInventoryWidgetMode.Default;
            right.WidgetSelectionMode = MultiInventoryWidgetMode.Default;
            left.OnMoveAllSimilarPressed = null;
            left.GetMoveAllSimilarTargetInventory = () => null;

            // Left: the tool, the factory's own lines by family, then the player's blueprints (those the week needs first).
            AddSection(left, Texts.Pt("Ferramentas", "Tools"), new[] { TraceCell(), StagesToggleCell() });
            foreach (var family in clusters.Select((c, i) => (c, i)).GroupBy(x => x.c.RawItem)
                         .OrderByDescending(g => g.Count()).ThenBy(g => FamilyName(g.Key), StringComparer.CurrentCulture))
            {
                AddSection(left, Texts.Pt($"Recomendadas · {FamilyName(family.Key)} ({family.Count()})",
                        $"Recommended · {FamilyName(family.Key)} ({family.Count()})"),
                    family.OrderByDescending(x => x.c.Products.Any(weekItems.Contains)).ThenByDescending(x => x.c.Machines.Count)
                        .Select(x => ClusterCell(x.c, x.i)));
            }
            List<Blueprint> blueprints = BlueprintStore.All
                .OrderByDescending(b => b.Products.Any(weekItems.Contains))
                .ThenBy(b => b.Name, StringComparer.CurrentCulture).ToList();
            if (blueprints.Count == 0)
            {
                AddSection(left, Texts.Pt("Nenhuma planta ainda", "No blueprints yet"), new[]
                {
                    InfoCell(Texts.Pt("Nenhuma planta ainda", "No blueprints yet"),
                        Texts.Pt("Use o Decalque acima num trecho da fábrica que já funciona: as máquinas, esteiras e baús dele viram uma planta aqui.",
                            "Use Trace above on a part of the factory that already works: its machines, belts and chests become a blueprint here.")),
                });
            }
            else
            {
                AddSection(left, Texts.Pt($"Minhas plantas ({blueprints.Count}) · clique para pôr no chão", $"My blueprints ({blueprints.Count}) · click to lay one"),
                    blueprints.Select(BlueprintCell));
            }

            // Right: the module catalog, or the week's schedule read from the Planning Board.
            if (stagesView)
            {
                BuildModules(right);
                return d;
            }
            if (planned.Count == 0)
            {
                AddSection(right, Texts.Pt("Cronograma vazio", "Empty schedule"), new[]
                {
                    InfoCell(Texts.Pt("Cronograma vazio", "Empty schedule"),
                        Texts.Pt("Escolha os pedidos da semana no Quadro de Planejamento. A mesa mostra aqui as máquinas novas que eles pedem e as plantas que as fazem.",
                            "Pick the week's orders on the Planning Board. The desk then shows the new machines they need and the blueprints that make them.")),
                });
                return d;
            }
            List<string> fresh = week.RecipeOrder.Where(id => !factory.RunningRecipes.Contains(id)).ToList();
            int covered = fresh.Count(id => BlueprintStore.Making(book.ById[id].OutItem).Count > 0);
            AddSection(right,
                Texts.Pt($"{planned.Count} pedido(s) no Quadro · {week.RecipeOrder.Count} máquina(s)",
                    $"{planned.Count} order(s) on the board · {week.RecipeOrder.Count} machine(s)"),
                planned.Select(OrderCell));
            if (fresh.Count == 0)
            {
                AddSection(right, Texts.Pt("Máquinas novas: nenhuma", "New machines: none"), new[]
                {
                    InfoCell(Texts.Pt("Nada novo para construir", "Nothing new to build"),
                        Texts.Pt("A fábrica já tem uma máquina para cada receita do cronograma.", "The factory already has a machine for every recipe on the schedule.")),
                });
            }
            else
            {
                AddSection(right,
                    Texts.Pt($"Máquinas novas da semana · {covered} de {fresh.Count} com planta",
                        $"This week's new machines · {covered} of {fresh.Count} with a blueprint"),
                    fresh.Select(MachineCell));
            }
            return d;
        }

        private static void AddSection(MultiInventoryWidgetData side, string title, IEnumerable<(Item item, Cell cell)> content)
        {
            List<(Item item, Cell cell)> list = content.ToList();
            string key = Texts.Dynamic($"gk2fp_desk_section_{sectionCounter++}", title);
            var inventory = new Inventory("inventory", Math.Max(1, list.Count), key);
            foreach (var (item, cell) in list)
            {
                inventory.Data.Inventory.Add(item);
                cells[item] = cell;
            }
            inventory.Data.CalculateInventoryFillSize();
            virtuals.Add(inventory);
            List<InventoryWidgetDataBase> widgets = InventoryWidgetDataHelper.GetWidgetsDataForInventory(
                inventory, _ => LazyAudio.PlayAndForget("gui_hover_light"), null, OnPress, OnPress2,
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

        private static (Item, Cell) InfoCell(string header, string body)
        {
            var cell = new Cell { Kind = CellKind.Info, ItemId = PaperItem, Header = header, Body = body, Active = false };
            return (new Item(PaperItem, 1), cell);
        }

        private static (Item, Cell) TraceCell()
        {
            var cell = new Cell
            {
                Kind = CellKind.Trace,
                ItemId = PaperItem,
                Header = Texts.Pt("Decalque", "Trace"),
                Body = Texts.Pt("Copia um trecho da fábrica para uma planta nova.\nClique num canto e depois no canto oposto: o que ficar amarelo entra na planta. As extensões das máquinas ficam anotadas para pôr à mão.\n\nClique para começar.",
                    "Copies part of the factory into a new blueprint.\nClick one corner, then the opposite one: whatever turns yellow goes in. Machine add-ons are noted, to be placed by hand.\n\nClick to start."),
            };
            return (new Item(PaperItem, 1), cell);
        }

        private static (Item, Cell) StagesToggleCell()
        {
            var cell = new Cell
            {
                Kind = CellKind.StagesToggle,
                ItemId = "box_empty",
                Header = stagesView ? Texts.Pt("Ver o cronograma da semana", "See the week's schedule") : Texts.Pt("Ver os módulos por etapa", "See the modules by stage"),
                Body = stagesView
                    ? Texts.Pt("Clique para ver à direita o cronograma escolhido no Quadro e as máquinas novas que ele pede.", "Click to see on the right the schedule picked on the Board and the new machines it needs.")
                    : Texts.Pt("Mostra à direita todos os produtos da fábrica por etapa e tipo de saída, cada um com um módulo pronto para pôr no chão: a etapa 1 refina a matéria-prima, cada etapa seguinte pega do estoque da anterior, e o produto final sai numa estação.",
                        "Shows on the right every factory product by stage and output type, each with a ready module to lay: stage 1 refines raw materials, each next stage takes from the stock before it, and final products leave at a station."),
            };
            return (new Item(cell.ItemId, 1), cell);
        }

        private static string StageName(Stage stage) =>
            stage.Final ? Texts.Pt("Produto final", "Final products")
            : stage.Index == 1 ? Texts.Pt("Refino", "Refining")
            : Texts.Pt("Montagem", "Assembly");

        private static string ChestName => Texts.Name("conveyor_chest_t1");

        private static string SourceOf(string item) =>
            stages.MadeAt.TryGetValue(item, out int at) && at > 0
                ? Texts.Pt($"estoque da etapa {at}", $"stage {at} stock")
                : Texts.Pt("matéria-prima", "raw material");

        /// <summary>The catalog: every factory product by stage and output family, one standard module each.</summary>
        private static void BuildModules(MultiInventoryWidgetData right)
        {
            stages = Stager.Catalog(book, factory, item => Clusters.MainRaw(new[] { item }, book, factory));
            modules.Clear();
            AddSection(right, Texts.Pt($"Módulos por etapa · {stages.Stages.Sum(st => st.Steps.Count)} produtos", $"Modules by stage · {stages.Stages.Sum(st => st.Steps.Count)} products"), new[]
            {
                InfoCell(Texts.Pt("Como encadear", "How to chain them"),
                    Texts.Pt($"Cada módulo é uma máquina com um {ChestName} atrás de cada entrada e outro na saída (o estoque daquela etapa). Um baú guarda um item só, sem filtro.\n" +
                             "A etapa 1 refina a matéria-prima; cada etapa seguinte pega do estoque da anterior; o produto final sai numa estação.\n" +
                             "Para encadear, ponha o módulo da etapa seguinte com o baú de entrada em cima do baú de saída do anterior: a peça que já está lá é aproveitada.\n" +
                             "Os baús de matéria-prima você enche à mão. O que produzir a cada semana se decide no Quadro.",
                        $"Each module is one machine with a {ChestName} behind each input and one at its output (that stage's stock). A chest holds one item, no filter.\n" +
                             "Stage 1 refines raw materials; each next stage takes from the stock before it; final products leave at a station.\n" +
                             "To chain them, lay the next stage's module with its input chest on the previous one's output chest: the piece already there is reused.\n" +
                             "Raw material chests are filled by hand. What to make each week is decided on the Planning Board.")),
            });
            foreach (Stage stage in stages.Stages)
            {
                foreach (var family in stage.Steps.GroupBy(st => st.Family).OrderBy(g => FamilyName(g.Key), StringComparer.CurrentCulture))
                {
                    AddSection(right, Texts.Pt($"Etapa {stage.Index} · {StageName(stage)} · {FamilyName(family.Key)} ({family.Count()})",
                            $"Stage {stage.Index} · {StageName(stage)} · {FamilyName(family.Key)} ({family.Count()})"),
                        family.OrderBy(st => st.Recipe.Unlocked ? 0 : 1).ThenBy(st => Texts.Name(st.Recipe.OutItem), StringComparer.CurrentCulture)
                            .Select(st => ModuleCell(stage, st)));
                }
            }
        }

        private static (Item, Cell) ModuleCell(Stage stage, StageStep step)
        {
            Recipe recipe = step.Recipe;
            LayoutResult module = LayoutPlanner.Module(recipe, FromScratch.ShapeFor(recipe.Station), stage.Final);
            Blueprint blueprint = FromScratch.ToBlueprint(module, Texts.Pt($"Módulo: {Texts.Name(recipe.OutItem)}", $"Module: {Texts.Name(recipe.OutItem)}"));
            modules[recipe.Id] = blueprint;
            var body = new StringBuilder();
            body.Append(Texts.Pt($"Faz {recipe.OutCount} × {Texts.Name(recipe.OutItem)} por craft, em {Texts.Name(recipe.Station)}.",
                $"Makes {recipe.OutCount} × {Texts.Name(recipe.OutItem)} per craft, on {Texts.Name(recipe.Station)}."));
            body.Append(Texts.Pt("\nPega: ", "\nTakes: ") + string.Join(", ", recipe.Needs.Select(n => $"{n.Count} × {Texts.Name(n.Item)} ({SourceOf(n.Item)})")));
            if (stage.Final)
            {
                body.Append(Texts.Pt("\nSai numa estação (produto final).", "\nLeaves at a station (final product)."));
            }
            else
            {
                var users = stages.Stages.SelectMany(st => st.Steps.Where(x => x.Recipe.Needs.Any(n => n.Item == recipe.OutItem))
                    .Select(x => Texts.Pt($"{Texts.Name(x.Recipe.OutItem)} (etapa {st.Index})", $"{Texts.Name(x.Recipe.OutItem)} (stage {st.Index})"))).ToList();
                body.Append(Texts.Pt($"\nVai para o estoque da etapa {stage.Index}, que alimenta: ", $"\nGoes to the stage {stage.Index} stock, which feeds: ") +
                    (users.Count > 0 ? string.Join(", ", users.Take(8)) + (users.Count > 8 ? Texts.Pt($" e mais {users.Count - 8}", $" and {users.Count - 8} more") : "") : "-"));
            }
            if (recipe.Extension != null)
            {
                body.Append(Texts.Pt($"\nPede a extensão {Texts.Name(recipe.Extension)} (à mão)", $"\nNeeds the add-on {Texts.Name(recipe.Extension)} (by hand)") +
                    (factory.Extensions.ContainsKey(recipe.Extension) ? "." : Texts.Pt("; a fábrica ainda não tem.", "; the factory has none yet.")));
            }
            if (!recipe.Unlocked)
            {
                body.Append(Texts.Pt("\nReceita ainda não aprendida.", "\nRecipe not learned yet."));
            }
            if (factory.RunningRecipes.Contains(recipe.Id))
            {
                body.Append(Texts.Pt("\nSua fábrica já tem uma máquina fazendo isto.", "\nYour factory already has a machine doing this."));
            }
            if (module.Presumed.Count > 0)
            {
                body.Append(Texts.Pt("\nForma da máquina presumida (ainda não vi uma na sua fábrica): confira o fantasma.",
                    "\nMachine shape assumed (none seen in your factory yet): check the ghost."));
            }
            if (module.Unrouted.Count > 0)
            {
                body.Append(Texts.Pt("\nA máquina tem menos entradas que ingredientes: ligue à mão ", "\nThe machine has fewer inputs than ingredients: connect by hand ") +
                    string.Join(", ", module.Unrouted.Select(u => Texts.Name(u.Item))));
            }
            body.Append(Texts.Pt($"\nPeças: 1 máquina, {module.Chests} baú(s), {module.Belts} esteira(s) ({module.Belts} de energia).",
                $"\nPieces: 1 machine, {module.Chests} chest(s), {module.Belts} belt(s) ({module.Belts} power)."));
            AppendCost(body, blueprint);
            body.Append(Texts.Pt("\n\nClique para pôr o módulo no chão. Clique direito salva em Minhas plantas.",
                "\n\nClick to lay the module. Right click saves it to My blueprints."));
            var cell = new Cell
            {
                Kind = CellKind.Module,
                RecipeId = recipe.Id,
                ItemId = recipe.OutItem,
                Header = $"{Texts.Name(recipe.Station)}: {Texts.Name(recipe.OutItem)}" + (recipe.Unlocked ? "" : Texts.Pt(" (não aprendida)", " (not learned)")),
                Body = body.ToString(),
            };
            return (new Item(recipe.OutItem, 1), cell);
        }

        private static (Item, Cell) BlueprintCell(Blueprint blueprint)
        {
            List<string> products = blueprint.Products.ToList();
            List<string> forWeek = products.Where(weekItems.Contains).ToList();
            var body = new StringBuilder();
            body.Append(Texts.Pt($"{blueprint.Pieces.Count} peças", $"{blueprint.Pieces.Count} pieces"));
            var kinds = blueprint.Pieces.GroupBy(p => p.Wgo).OrderByDescending(g => g.Count()).ToList();
            body.Append(": " + string.Join(", ", kinds.Take(6).Select(g => g.Count() > 1 ? $"{Texts.Name(g.Key)} ×{g.Count()}" : Texts.Name(g.Key))));
            if (kinds.Count > 6)
            {
                body.Append(Texts.Pt($" e mais {kinds.Count - 6} tipo(s)", $" and {kinds.Count - 6} more kind(s)"));
            }
            if (products.Count > 0)
            {
                body.Append(Texts.Pt("\nFaz: ", "\nMakes: ") + string.Join(", ", products.Select(Texts.Name)));
            }
            if (forWeek.Count > 0)
            {
                body.Append(Texts.Pt("\nServe ao cronograma da semana: ", "\nServes this week's schedule: ") + string.Join(", ", forWeek.Select(Texts.Name)));
            }
            AppendCost(body, blueprint);
            if (blueprint.AddOns.Count > 0)
            {
                body.Append(Texts.Pt("\nExtensões (à mão): ", "\nAdd-ons (by hand): ") +
                    string.Join(", ", blueprint.AddOns.Select(kv => $"{Texts.Name(kv.Key)} ×{kv.Value}")));
            }
            body.Append("\n\n");
            body.Append(deleteArmed == blueprint.Name
                ? Texts.Pt("Clique direito de novo para apagar (vai para a pasta lixeira).", "Right click again to delete (it goes to the lixeira folder).")
                : Texts.Pt("Clique para pôr no chão. Clique direito para apagar.", "Click to lay it. Right click to delete."));
            var cell = new Cell
            {
                Kind = CellKind.Blueprint,
                BlueprintName = blueprint.Name,
                ItemId = products.FirstOrDefault() ?? PaperItem,
                Header = (deleteArmed == blueprint.Name ? Texts.Pt("APAGAR? ", "DELETE? ") : "") + blueprint.Name +
                    (forWeek.Count > 0 ? Texts.Pt(" (semana)", " (this week)") : ""),
                Body = body.ToString(),
            };
            return (new Item(cell.ItemId, Math.Max(1, blueprint.Pieces.Count)), cell);
        }

        private static string FamilyName(string rawItem) =>
            rawItem == null ? Texts.Pt("Sem família", "No family") : Texts.Name(rawItem);

        private static (Item, Cell) ClusterCell(FactoryCluster cluster, int index)
        {
            Blueprint bp = cluster.Blueprint;
            List<string> forWeek = cluster.Products.Where(weekItems.Contains).ToList();
            var body = new StringBuilder();
            body.Append(Texts.Pt($"Linha da sua fábrica que já funciona. Família: {FamilyName(cluster.RawItem)}.",
                $"A line of your factory that already works. Family: {FamilyName(cluster.RawItem)}."));
            var machines = cluster.Machines.GroupBy(m => m).Select(g => g.Count() > 1 ? $"{Texts.Name(g.Key)} ×{g.Count()}" : Texts.Name(g.Key));
            body.Append(Texts.Pt("\nMáquinas: ", "\nMachines: ") + string.Join(", ", machines));
            body.Append(Texts.Pt($"\nEsteiras e divisores: {cluster.Belts} · baús: {cluster.Chests}", $"\nBelts and splitters: {cluster.Belts} · chests: {cluster.Chests}"));
            if (cluster.Products.Count > 0)
            {
                body.Append(Texts.Pt("\nFaz: ", "\nMakes: ") + string.Join(", ", cluster.Products.Select(Texts.Name)));
            }
            if (forWeek.Count > 0)
            {
                body.Append(Texts.Pt("\nServe ao cronograma da semana: ", "\nServes this week's schedule: ") + string.Join(", ", forWeek.Select(Texts.Name)));
            }
            AppendCost(body, bp);
            if (bp.AddOns.Count > 0)
            {
                body.Append(Texts.Pt("\nExtensões (à mão): ", "\nAdd-ons (by hand): ") +
                    string.Join(", ", bp.AddOns.Select(kv => $"{Texts.Name(kv.Key)} ×{kv.Value}")));
            }
            body.Append(Texts.Pt("\n\nClique para pôr uma cópia no chão. Clique direito salva em Minhas plantas.",
                "\n\nClick to lay a copy. Right click saves it to My blueprints."));
            string item = cluster.MainProduct ?? PaperItem;
            var cell = new Cell
            {
                Kind = CellKind.Cluster,
                ClusterIndex = index,
                ItemId = item,
                Header = bp.Name + (forWeek.Count > 0 ? Texts.Pt(" (semana)", " (this week)") : ""),
                Body = body.ToString(),
            };
            return (new Item(item, Math.Max(1, cluster.Machines.Count)), cell);
        }

        /// <summary>Total cost of a blueprint against what the player and the zone's chests hold right now.</summary>
        private static void AppendCost(StringBuilder body, Blueprint blueprint)
        {
            var cost = new Dictionary<string, int>();
            foreach (BlueprintPiece piece in blueprint.Pieces)
            {
                BuildingDef def = FactoryPieces.Def(piece.Def);
                foreach (NeedItemData need in def?.needItems ?? new List<NeedItemData>())
                {
                    int n = GameData.Number(need.count, 1);
                    cost[need.id] = cost.TryGetValue(need.id, out int c) ? c + n : n;
                }
            }
            if (cost.Count == 0)
            {
                return;
            }
            body.Append(Texts.Pt("\nCusto total (tem/precisa): ", "\nTotal cost (have/need): "));
            body.Append(string.Join(", ", cost.OrderByDescending(kv => kv.Value).Select(kv =>
            {
                int have = Have(kv.Key);
                return $"{Texts.Name(kv.Key)} {have}/{kv.Value}" + (have < kv.Value ? Texts.Pt(" (falta)", " (short)") : "");
            })));
        }

        private static int Have(string itemId)
        {
            try
            {
                return stock?.GetTotalCount(itemId) ?? 0;
            }
            catch (Exception)
            {
                return 0;  // group ids ("any plank") have no plain count
            }
        }

        private static (Item, Cell) OrderCell(Order order)
        {
            var cell = new Cell
            {
                Kind = CellKind.Info,
                ItemId = order.BoxItem,
                Active = true,
                Header = Texts.Name(order.BoxItem),
                Body = $"{Texts.Name(order.VendorId)} · " + Texts.Pt($"{order.Count} caixas por semana", $"{order.Count} boxes a week") +
                    Texts.Pt("\n\nPedido escolhido no Quadro de Planejamento.", "\n\nOrder picked on the Planning Board."),
            };
            return (new Item(order.BoxItem, Math.Max(1, order.Count)), cell);
        }

        private static (Item, Cell) MachineCell(string recipeId)
        {
            Recipe recipe = book.ById[recipeId];
            List<Blueprint> making = BlueprintStore.Making(recipe.OutItem);
            int crafts = week.Crafts.TryGetValue(recipeId, out int c) ? c : 0;
            var body = new StringBuilder();
            body.Append(Texts.Pt($"Máquina nova do cronograma: {crafts} craft(s) por semana.", $"New machine on the schedule: {crafts} craft(s) a week."));
            if (recipe.Extension != null && !factory.Extensions.ContainsKey(recipe.Extension))
            {
                body.Append(Texts.Pt($"\nPede {Texts.Name(recipe.Extension)}, que a fábrica ainda não tem.",
                    $"\nNeeds {Texts.Name(recipe.Extension)}, which the factory doesn't have yet."));
            }
            body.Append("\n\n");
            if (making.Count > 0)
            {
                Blueprint first = Preferred(making);
                body.Append(Texts.Pt("Plantas que fazem isto: ", "Blueprints that make this: ") + string.Join(", ", making.Select(b => b.Name)));
                body.Append(Texts.Pt($"\nClique para pôr \"{first.Name}\" no chão.", $"\nClick to lay \"{first.Name}\"."));
            }
            else
            {
                body.Append(Texts.Pt("Nenhuma planta faz isto ainda. Monte a linha uma vez à mão e faça um Decalque dela.",
                    "No blueprint makes this yet. Build the line once by hand and Trace it."));
            }
            string raw = Clusters.MainRaw(new[] { recipe.OutItem }, book, factory);
            List<FactoryCluster> family = clusters.Where(cl => cl.RawItem != null && cl.RawItem == raw).ToList();
            if (family.Count > 0)
            {
                body.Append(Texts.Pt($"\nLinhas da mesma família ({FamilyName(raw)}) na fábrica: ", $"\nLines of the same family ({FamilyName(raw)}) in the factory: ") +
                    string.Join(", ", family.Select(cl => cl.Blueprint.Name)));
            }
            var cell = new Cell
            {
                Kind = CellKind.Machine,
                ItemId = recipe.OutItem,
                Active = making.Count > 0,
                Header = $"{Texts.Name(recipe.Station)}: {Texts.Name(recipe.OutItem)}",
                Body = body.ToString(),
            };
            return (new Item(recipe.OutItem, Math.Max(1, crafts)), cell);
        }

        /// <summary>The blueprint already in the tool if it is one of them, else the first.</summary>
        private static Blueprint Preferred(List<Blueprint> making)
        {
            Blueprint selected = BlueprintStore.Selected;
            return making.Contains(selected) ? selected : making[0];
        }

        // ---------------------------------------------------------------- clicks and tooltips

        private static void OnPress(UIItemCell itemCell) => Guarded(() => OnClick(itemCell, secondary: false));

        private static void OnPress2(UIItemCell itemCell) => Guarded(() => OnClick(itemCell, secondary: true));

        private static void Guarded(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Architect's Desk click failed: {e}");
            }
        }

        private static void OnClick(UIItemCell itemCell, bool secondary)
        {
            Item item = itemCell?.DisplayingItem;
            if (item == null || !cells.TryGetValue(item, out Cell cell))
            {
                return;
            }
            Blueprint blueprint = cell.BlueprintName == null ? null : BlueprintStore.All.FirstOrDefault(b => b.Name == cell.BlueprintName);
            FactoryCluster cluster = cell.ClusterIndex >= 0 && cell.ClusterIndex < clusters.Count ? clusters[cell.ClusterIndex] : null;
            Blueprint module = cell.RecipeId != null && modules.TryGetValue(cell.RecipeId, out Blueprint m) ? m : null;
            if (secondary && cell.Kind == CellKind.Module && module != null)
            {
                module.Name = BlueprintStore.NewName(cell.ItemId);
                module.File = null;
                BlueprintStore.Save(module);
                LazyAudio.PlayAndForget("gui_hover_light");
                UITooltip.Hide();
                Refresh();
                return;
            }
            if (secondary && cluster != null)
            {
                Blueprint copy = cluster.Blueprint;
                copy.Name = BlueprintStore.NewName(cluster.MainProduct);
                copy.File = null;
                BlueprintStore.Save(copy);
                LazyAudio.PlayAndForget("gui_hover_light");
                UITooltip.Hide();
                Refresh();
                return;
            }
            if (secondary)
            {
                if (cell.Kind != CellKind.Blueprint || blueprint == null)
                {
                    return;
                }
                if (deleteArmed == blueprint.Name)
                {
                    deleteArmed = null;
                    BlueprintStore.Delete(blueprint);
                }
                else
                {
                    deleteArmed = blueprint.Name;
                }
                LazyAudio.PlayAndForget("gui_hover_light");
                UITooltip.Hide();
                Refresh();
                return;
            }
            switch (cell.Kind)
            {
                case CellKind.Trace:
                    StartTool(copy: true);
                    break;
                case CellKind.Module:
                    if (module != null && module.Pieces.Count > 0)
                    {
                        BlueprintStore.UseTemporary(module);
                        StartTool(copy: false);
                    }
                    break;
                case CellKind.StagesToggle:
                    stagesView = !stagesView;
                    LazyAudio.PlayAndForget("gui_hover_light");
                    UITooltip.Hide();
                    Refresh();
                    break;
                case CellKind.Blueprint:
                    if (blueprint != null)
                    {
                        BlueprintStore.Select(blueprint);
                        StartTool(copy: false);
                    }
                    break;
                case CellKind.Cluster:
                    if (cluster != null)
                    {
                        BlueprintStore.UseTemporary(cluster.Blueprint);
                        StartTool(copy: false);
                    }
                    break;
                case CellKind.Machine:
                    List<Blueprint> making = BlueprintStore.Making(cell.ItemId);
                    if (making.Count > 0)
                    {
                        BlueprintStore.Select(Preferred(making));
                        StartTool(copy: false);
                    }
                    break;
            }
        }

        private static void StartTool(bool copy)
        {
            LazyAudio.PlayAndForget("gui_hover_light");
            UITooltip.Hide();
            LazyUI.GetWindow<UIChestWindow>().Close();
            Plugin.Instance.StartCoroutine(BlueprintMode.StartFromDesk(copy));
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
}
