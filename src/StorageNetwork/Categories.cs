using System;
using System.Collections.Generic;
using System.Linq;

namespace GK2Mods.StorageNetwork
{
    /// <summary>
    /// Groups items for the Central Storage panel by what they are and what they are for, using the game's own
    /// item groups (the bags that may carry them: buil_bag, food_bag, alch_bag, bodypart, tool...) and, for
    /// building materials, the raw material named in the id.
    /// </summary>
    internal static class Categories
    {
        internal class Category
        {
            public string Key, Pt, En;
        }

        // Display order in the panel.
        internal static readonly Category[] All =
        {
            C("wood", "Madeira", "Wood"),
            C("stone", "Pedra e Cerâmica", "Stone and Ceramics"),
            C("metal", "Metais e Peças", "Metals and Parts"),
            C("building", "Outros Materiais", "Other Materials"),
            C("food", "Comida e Bebida", "Food and Drink"),
            C("garden", "Jardim: Sementes e Adubos", "Garden: Seeds and Fertilizer"),
            C("fish", "Pesca", "Fishing"),
            C("alchemy", "Alquimia", "Alchemy"),
            C("potions", "Poções", "Potions"),
            C("body", "Corpos e Órgãos", "Bodies and Organs"),
            C("church", "Igreja", "Church"),
            C("tools", "Ferramentas e Armas", "Tools and Weapons"),
            C("supplies", "Suprimentos", "Supplies"),
            C("graves", "Túmulos", "Graves"),
            C("quest", "Itens de Missão", "Quest Items"),
            C("other", "Outros", "Other"),
        };

        private static readonly Dictionary<string, Category> byName = All.ToDictionary(c => c.Key.Substring("gk2sn_cat_".Length));

        private static readonly HashSet<ItemType> BodyTypes = new HashSet<ItemType>
            { ItemType.Brain, ItemType.Heart, ItemType.Flesh, ItemType.Bones, ItemType.Skull, ItemType.Guts, ItemType.Skin };

        private static readonly HashSet<ItemType> ToolTypes = new HashSet<ItemType>
        {
            ItemType.Axe, ItemType.Shovel, ItemType.Pickaxe, ItemType.Hammer, ItemType.FishingRod, ItemType.Sword,
            ItemType.BodyArmor, ItemType.Bow, ItemType.SurgicalKit, ItemType.SmallTools, ItemType.Pike,
        };

        private static readonly string[] WoodWords = { "wood", "plank", "beam", "flitch", "log", "joint", "carved", "stick", "lacquer" };
        private static readonly string[] StoneWords = { "stone", "marble", "brick", "clay", "sand", "ceramic", "sculpture", "coal" };
        private static readonly string[] MetalWords = { "ingot", "iron", "bronze", "steel", "copper", "gold", "lead", "nails", "detail", "gear", "screw", "ore", "glass" };

        private static Category C(string key, string pt, string en) => new Category { Key = "gk2sn_cat_" + key, Pt = pt, En = en };

        internal static Category Classify(string itemId, ItemDef def)
        {
            string id = itemId.ToLowerInvariant();
            var groups = new HashSet<string>(def?.itemGroupIds ?? new List<string>());
            ItemType type = def?.type ?? ItemType.None;

            if (groups.Contains("quest_bag"))
                return byName["quest"];
            if (groups.Contains("bodypart") || groups.Contains("corpse") || groups.Contains("body") || BodyTypes.Contains(type))
                return byName["body"];
            if (groups.Contains("tool") || groups.Contains("weapon") || groups.Contains("melee") || groups.Contains("range") || ToolTypes.Contains(type))
                return byName["tools"];
            if (groups.Contains("battle_potion") || groups.Contains("pot_bag") || id.Contains("potion"))
                return byName["potions"];
            if (groups.Contains("gravetop") || groups.Contains("gravebot"))
                return byName["graves"];
            if (groups.Contains("town_box"))
                return byName["supplies"];
            if (type == ItemType.Preach || id.StartsWith("prayer") || id.StartsWith("candle") || id.StartsWith("incense"))
                return byName["church"];
            if (groups.Contains("seed") || groups.Contains("vineyard_seed") || groups.Contains("fertilizer") || groups.Contains("farm_bag")
                || (id.Contains("seed") && !id.Contains("oil")))
                return byName["garden"];
            if (groups.Contains("fishes") || groups.Contains("fish_bag") || type == ItemType.Bait)
                return byName["fish"];
            // Raw and worked materials: building items, overhead loads (logs, blocks) and alchemy ingredients
            // that are really stone, clay, ore... go by material before food/alchemy claim them.
            bool building = groups.Contains("buil_bag");
            if (building || groups.Contains("overhead") || groups.Contains("alch_bag"))
            {
                Category material = Material(id, building);
                if (material != null)
                    return material;
                if (building || groups.Contains("overhead"))
                    return byName["building"];
            }
            if (groups.Contains("food_bag") || groups.Contains("seedable") || groups.Contains("crop") || id.StartsWith("cook_"))
                return byName["food"];
            if (groups.Contains("alch_bag") || type == ItemType.Embalm || type == ItemType.Reagents)
                return byName["alchemy"];
            return Material(id, false) ?? byName["other"];
        }

        private static Category Material(string id, bool building)
        {
            if (WoodWords.Any(id.Contains))
                return byName["wood"];
            if (StoneWords.Any(id.Contains))
                return byName["stone"];
            // "glass" only for building parts; alchemy glassware stays in alchemy
            if (MetalWords.Any(w => id.Contains(w) && (building || w != "glass")))
                return byName["metal"];
            return null;
        }
    }
}
