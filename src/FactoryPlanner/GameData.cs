using System;
using System.Collections.Generic;
using System.Linq;
using GK2Mods.FactoryPlanner.Core;
using LazyBearTechnology;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>Reads the planner's model (recipes, orders, the factory as built) from the running game.</summary>
    internal static class GameData
    {
        private const string FactoryPrefix = "conveyor_";
        private const int DaysInWeek = 6;  // EnvironmentData.DAYS_IN_WEEK

        private static RecipeBook cachedBook;
        private static int cachedUnlockCount = -1;

        internal static KnowledgeSystem Knowledge => MainGame.Instance?.GameSave?.knowledgeSystem;

        /// <summary>The factory recipes, with "learned" flags for the current save.</summary>
        internal static RecipeBook Recipes()
        {
            KnowledgeSystem knowledge = Knowledge;
            int unlockCount = knowledge?.unlockedCrafts?.Count ?? 0;
            if (cachedBook != null && unlockCount == cachedUnlockCount)
            {
                return cachedBook;
            }
            GameBalance gb = GameBalance.Me;
            var techByCraft = new Dictionary<string, string>();
            foreach (TechDef tech in gb.techDefs)
            {
                foreach (string craft in tech.craftsAfterUnlock)
                {
                    if (!techByCraft.ContainsKey(craft))
                    {
                        techByCraft[craft] = tech.id;
                    }
                }
            }
            var recipes = new List<Recipe>();
            foreach (CraftDef def in gb.craftDefs)
            {
                if (def == null || !def.craftsIn.Any(s => s.StartsWith(FactoryPrefix, StringComparison.Ordinal)))
                {
                    continue;
                }
                ChanceOutputItem output = def.outputItems?.chanceOutputItems?.FirstOrDefault(o => !string.IsNullOrEmpty(o.id));
                if (output == null)
                {
                    continue;  // repairs and other crafts that make nothing
                }
                var recipe = new Recipe
                {
                    Id = def.id,
                    Stations = def.craftsIn.Where(s => s.StartsWith(FactoryPrefix, StringComparison.Ordinal)).ToList(),
                    Extension = string.IsNullOrEmpty(def.extensionNeedId) ? null : def.extensionNeedId,
                    Duration = Math.Max(1, Number(def.duration, 8)),
                    OutItem = output.id,
                    OutCount = Math.Max(1, Number(output.count, 1)),
                    Unlocked = !def.isNeedsUnlock || (knowledge?.unlockedCrafts.Contains(def.id) ?? false),
                    TechId = techByCraft.TryGetValue(def.id, out string techId) ? techId : null,
                };
                foreach (NeedItemData need in def.needItems)
                {
                    if (need == null || need.IsGroup || string.IsNullOrEmpty(need.id))
                    {
                        continue;
                    }
                    recipe.Needs.Add(new Ingredient(need.id, Math.Max(1, Number(need.count, 1))));
                }
                recipes.Add(recipe);
            }
            cachedBook = new RecipeBook(recipes);
            cachedUnlockCount = unlockCount;
            Plugin.Debug($"Planner: {recipes.Count} factory recipes, {recipes.Count(r => r.Unlocked)} learned");
            return cachedBook;
        }

        /// <summary>
        /// The leading number of an expression ("4", "1+WorkerPar(...)"), so the planner doesn't evaluate
        /// expressions that expect a worker or a station.
        /// </summary>
        internal static int Number(LazyExpression expression, int fallback)
        {
            if (expression == null)
            {
                return fallback;
            }
            string raw = (expression.ToUnparsedString() ?? "").Trim();
            int i = 0;
            while (i < raw.Length && char.IsDigit(raw[i]))
            {
                i++;
            }
            if (i > 0 && int.TryParse(raw.Substring(0, i), out int value))
            {
                return value;
            }
            try
            {
                return expression.HasExpression ? expression.EvaluateInt() : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        /// <summary>Every order of every town vendor, with its state in this save.</summary>
        internal static List<Order> Orders()
        {
            var result = new List<Order>();
            GameSave save = MainGame.Instance?.GameSave;
            VendorSystem vendors = save?.vendorSystem;
            if (vendors?.vendors == null)
            {
                return result;
            }
            var accepted = new HashSet<Guid>((vendors.currentOrders ?? new List<SGuid>()).Where(g => !g.IsEmpty).Select(g => g.Guid));
            KnowledgeSystem knowledge = Knowledge;
            foreach (Vendor vendor in vendors.vendors)
            {
                if (vendor?.Orders == null || vendor.Orders.Count == 0)
                {
                    continue;
                }
                bool vendorOpen = knowledge == null || knowledge.IsVendorForOrdersUnlocked(vendor.id);
                foreach (VendorOrderData data in vendor.Orders)
                {
                    VendorOrderDef def = data?.Definition;
                    if (def == null || string.IsNullOrEmpty(def.itemId))
                    {
                        continue;
                    }
                    float happiness = 0f;
                    try
                    {
                        happiness = def.happinessReward?.EvaluateFloat() ?? 0f;
                    }
                    catch (Exception)
                    {
                        // keep 0
                    }
                    result.Add(new Order
                    {
                        Id = data.id,
                        VendorId = vendor.id,
                        BoxItem = def.itemId,
                        Count = def.count,
                        Urgent = def.isUrgent,
                        Tier = data.Tier,
                        Happiness = happiness,
                        Available = vendorOpen && vendor.CurTier >= data.Tier,
                        DoneThisWeek = data.IsFinishedThisWeek,
                        DoneOnce = data.IsFinishedOnce,
                        Accepted = accepted.Contains(data.Guid.Guid),
                    });
                }
            }
            return result;
        }

        internal static int VendorTier(string vendorId) =>
            MainGame.Instance?.GameSave?.vendorSystem?.GetVendor(vendorId)?.CurTier ?? 0;

        /// <summary>What the factory runs today: each machine's recipe and the extensions attached.</summary>
        internal static FactoryState Factory()
        {
            var state = new FactoryState();
            ConveyorSystemData data = MainGame.Instance?.GameSave?.conveyorSystemData;
            if (data?.conveyorComponents == null)
            {
                return state;
            }
            foreach (ConveyorComponent component in data.conveyorComponents)
            {
                WgoData wgo = component?.WgoData;
                if (wgo?.CraftComponent == null || wgo.Definition == null || wgo.Definition.conveyorType != ConveyorElementType.Workbench)
                {
                    continue;
                }
                state.Stations[wgo.id] = state.Stations.TryGetValue(wgo.id, out int n) ? n + 1 : 1;
                string craft = wgo.CraftComponent.CraftElementsQueue.FirstOrDefault()?.Def?.id
                    ?? wgo.CraftComponent.LastStartedCraftWithRequirements?.Def?.id;
                if (!string.IsNullOrEmpty(craft))
                {
                    state.RunningRecipes.Add(craft);
                }
                foreach (SGuid guid in wgo.AttachedWorkbenchExtensions)
                {
                    string ext = MainGame.WorldData.GetWgoData(guid)?.id;
                    if (!string.IsNullOrEmpty(ext))
                    {
                        state.Extensions[ext] = state.Extensions.TryGetValue(ext, out int e) ? e + 1 : 1;
                    }
                }
            }
            return state;
        }

        /// <summary>Factory power right now: (used, produced), as the game compares them.</summary>
        internal static (float used, float produced) Power()
        {
            try
            {
                WorldZoneData zone = MainGame.Instance.conveyorSystem.ConveyorWorldZone;
                if (zone == null)
                {
                    return (0f, 0f);
                }
                return (zone.GetTotalQuality(WorldZoneWgoQualityType.ConveyorCells),
                        zone.GetTotalQuality(WorldZoneWgoQualityType.ConveyorPowerSource));
            }
            catch (Exception e)
            {
                Plugin.Debug($"Power read failed: {e.Message}");
                return (0f, 0f);
            }
        }

        /// <summary>
        /// Seconds of a game week, and seconds per craft tick on a station (the station's own expression,
        /// evaluated on a built machine of that kind when there is one).
        /// </summary>
        internal static float WeekSeconds()
        {
            float minutes = MainGame.Instance?.GameSave?.environmentData?.gameplayDayInMinutes ?? 0f;
            if (minutes <= 0f)
            {
                minutes = 5f;
            }
            return minutes * 60f * DaysInWeek;
        }

        internal static float TickSeconds(string stationId)
        {
            WGODef def = GameBalance.Me.GetDataOrNull<WGODef>(stationId);
            if (def?.autocraftTickDuration == null || !def.autocraftTickDuration.HasExpression)
            {
                return 5f;
            }
            try
            {
                ConveyorSystemData data = MainGame.Instance?.GameSave?.conveyorSystemData;
                WgoData built = data?.conveyorComponents?.Select(c => c?.WgoData).FirstOrDefault(w => w?.id == stationId);
                float value = built != null ? def.autocraftTickDuration.EvaluateFloat(built) : def.autocraftTickDuration.EvaluateFloat();
                return value > 0f ? value : 5f;
            }
            catch (Exception)
            {
                return 5f;
            }
        }

        // ---------------------------------------------------------------- plan saved in the save file

        private const string PlanRes = "gk2fp_plan_";
        private const string ExternalRes = "gk2fp_ext_";

        internal static HashSet<string> LoadPlan(IEnumerable<Order> orders) =>
            new HashSet<string>(orders.Where(o => MainGame.PlayerData.GetRes(PlanRes + o.Id) > 0f).Select(o => o.Id));

        internal static void SetInPlan(string orderId, bool inPlan) =>
            MainGame.PlayerData.SetRes(PlanRes + orderId, inPlan ? 1f : 0f);

        internal static HashSet<string> LoadExternal(RecipeBook book)
        {
            var items = new HashSet<string>(book.ById.Values.SelectMany(r => r.Needs.Select(n => n.Item)));
            return new HashSet<string>(items.Where(i => MainGame.PlayerData.GetRes(ExternalRes + i) > 0f));
        }

        internal static void SetExternal(string itemId, bool external) =>
            MainGame.PlayerData.SetRes(ExternalRes + itemId, external ? 1f : 0f);

        // Quantities set by hand on the board, in crafts a week; stored as crafts + 1 so 0 means "as the week asks".
        private const string QuantityRes = "gk2fp_qty_";

        internal static Dictionary<string, int> LoadQuantities(RecipeBook book)
        {
            var result = new Dictionary<string, int>();
            foreach (string id in book.ById.Keys)
            {
                float v = MainGame.PlayerData.GetRes(QuantityRes + id);
                if (v > 0f)
                {
                    result[id] = (int)Math.Round(v) - 1;
                }
            }
            return result;
        }

        /// <summary>Sets a recipe's crafts a week, or (null) goes back to what the orders ask for.</summary>
        internal static void SetQuantity(string recipeId, int? crafts) =>
            MainGame.PlayerData.SetRes(QuantityRes + recipeId, crafts.HasValue ? Math.Max(0, crafts.Value) + 1 : 0f);

        // The player's own orders (any factory product, apart from the shop orders): item -> amount a week.
        private const string OwnRes = "gk2fp_own_";

        internal static Dictionary<string, int> LoadOwn(RecipeBook book)
        {
            var result = new Dictionary<string, int>();
            foreach (string item in book.ById.Values.Select(r => r.OutItem).Distinct())
            {
                float v = MainGame.PlayerData.GetRes(OwnRes + item);
                if (v >= 1f)
                {
                    result[item] = (int)Math.Round(v);
                }
            }
            return result;
        }

        /// <summary>Sets an own order's amount a week; 0 removes it.</summary>
        internal static void SetOwn(string itemId, int amount) =>
            MainGame.PlayerData.SetRes(OwnRes + itemId, Math.Max(0, amount));
    }
}
