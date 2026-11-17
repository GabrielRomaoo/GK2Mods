using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using LazyBearTechnology;
using Newtonsoft.Json;

namespace GK2Mods.FactoryPlanner.Blueprints
{
    /// <summary>One piece of a blueprint: the build-menu def that places it and where, relative to the origin.</summary>
    internal sealed class BlueprintPiece
    {
        /// <summary>Build-menu def (a "_place_p" construction site for machines, "_cp" for belts and the like).</summary>
        public string Def;
        /// <summary>The finished object's id (what stands in the factory once built).</summary>
        public string Wgo;
        /// <summary>Offset of the object's position from the blueprint origin, in world units.</summary>
        public float X, Y, Z;
        public string Variation;
        public int Rotation = -1;
        /// <summary>What the machine was making when the blueprint was copied (item id), if anything.</summary>
        public string Product;
    }

    internal sealed class Blueprint
    {
        public int Version = 1;
        public string Name;
        public DateTime Created = DateTime.Now;
        /// <summary>Largest grid step among the pieces: the blueprint moves in steps of this many build cells.</summary>
        public int GridStep = 1;
        /// <summary>Drawn from scratch on the factory's cell lattice: the origin snaps to it (0.64 x 0.6).</summary>
        public bool OnFactoryGrid;
        public List<BlueprintPiece> Pieces = new List<BlueprintPiece>();
        /// <summary>Machine add-ons (hammer, press...) that were on the copied machines: placed by hand later.</summary>
        public Dictionary<string, int> AddOns = new Dictionary<string, int>();
        /// <summary>Objects in the copied area that can't be blueprinted (pallets, ...).</summary>
        public Dictionary<string, int> Skipped = new Dictionary<string, int>();

        [JsonIgnore]
        public string File;

        [JsonIgnore]
        public IEnumerable<string> Products => Pieces.Select(p => p.Product).Where(p => !string.IsNullOrEmpty(p)).Distinct();
    }

    /// <summary>Blueprints live in BepInEx/config/GK2FactoryPlanner/blueprints as JSON, shared by every save.</summary>
    internal static class BlueprintStore
    {
        private static List<Blueprint> cache;

        internal static string Folder => Path.Combine(Paths.ConfigPath, "GK2FactoryPlanner", "blueprints");

        internal static List<Blueprint> All
        {
            get
            {
                if (cache == null)
                {
                    cache = Load();
                }
                return cache;
            }
        }

        /// <summary>A blueprint in the tool that isn't saved (a recommended line of the factory), until another is chosen.</summary>
        private static Blueprint temporary;

        internal static Blueprint Selected
        {
            get
            {
                if (temporary != null)
                {
                    return temporary;
                }
                string name = Plugin.SelectedBlueprint.Value;
                return All.FirstOrDefault(b => b.Name == name) ?? All.FirstOrDefault();
            }
        }

        internal static void Select(Blueprint blueprint)
        {
            temporary = null;
            Plugin.SelectedBlueprint.Value = blueprint?.Name ?? "";
            BlueprintTexts.Apply(LLBase.currentLang);
        }

        internal static void UseTemporary(Blueprint blueprint)
        {
            temporary = blueprint;
            BlueprintTexts.Apply(LLBase.currentLang);
        }

        internal static void Save(Blueprint blueprint)
        {
            Directory.CreateDirectory(Folder);
            if (string.IsNullOrEmpty(blueprint.File))
            {
                blueprint.File = Path.Combine(Folder, FileName(blueprint.Name) + ".json");
            }
            System.IO.File.WriteAllText(blueprint.File, JsonConvert.SerializeObject(blueprint, Formatting.Indented), Encoding.UTF8);
            if (!All.Contains(blueprint))
            {
                All.Add(blueprint);
            }
            Plugin.Log.LogInfo($"Blueprint '{blueprint.Name}' saved: {blueprint.Pieces.Count} pieces -> {blueprint.File}");
        }

        /// <summary>Blueprints whose machines were making this item when they were traced.</summary>
        internal static List<Blueprint> Making(string itemId) =>
            All.Where(b => b.Products.Contains(itemId)).ToList();

        /// <summary>Moves a blueprint's file to blueprints/lixeira (kept, so a mistaken delete can be undone by hand).</summary>
        internal static void Delete(Blueprint blueprint)
        {
            if (blueprint == null)
            {
                return;
            }
            All.Remove(blueprint);
            if (!string.IsNullOrEmpty(blueprint.File) && System.IO.File.Exists(blueprint.File))
            {
                string bin = Path.Combine(Folder, "lixeira");
                Directory.CreateDirectory(bin);
                string target = Path.Combine(bin, Path.GetFileName(blueprint.File));
                for (int i = 2; System.IO.File.Exists(target); i++)
                {
                    target = Path.Combine(bin, $"{Path.GetFileNameWithoutExtension(blueprint.File)} ({i}).json");
                }
                System.IO.File.Move(blueprint.File, target);
                Plugin.Log.LogInfo($"Blueprint '{blueprint.Name}' moved to {target}");
            }
            if (temporary == null && Plugin.SelectedBlueprint.Value == blueprint.Name)
            {
                Select(All.FirstOrDefault());
            }
        }

        internal static string NewName(string product)
        {
            string stem = string.IsNullOrEmpty(product) ? Texts.Pt("Planta", "Blueprint") : Texts.Name(product);
            for (int i = 1; ; i++)
            {
                string name = $"{stem} {i}";
                if (All.All(b => b.Name != name))
                {
                    return name;
                }
            }
        }

        private static List<Blueprint> Load()
        {
            var list = new List<Blueprint>();
            if (!Directory.Exists(Folder))
            {
                return list;
            }
            foreach (string file in Directory.GetFiles(Folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var blueprint = JsonConvert.DeserializeObject<Blueprint>(System.IO.File.ReadAllText(file, Encoding.UTF8));
                    if (blueprint?.Pieces == null || blueprint.Pieces.Count == 0)
                    {
                        continue;
                    }
                    blueprint.File = file;
                    if (string.IsNullOrEmpty(blueprint.Name))
                    {
                        blueprint.Name = Path.GetFileNameWithoutExtension(file);
                    }
                    list.Add(blueprint);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Blueprint {file} could not be read: {e.Message}");
                }
            }
            Plugin.Log.LogInfo($"Loaded {list.Count} blueprint(s) from {Folder}");
            return list;
        }

        private static string FileName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
            }
            string stem = sb.ToString();
            string candidate = stem;
            for (int i = 2; System.IO.File.Exists(Path.Combine(Folder, candidate + ".json")); i++)
            {
                candidate = $"{stem} ({i})";
            }
            return candidate;
        }
    }

    internal static class BlueprintTexts
    {
        internal static void Apply(LLBase lang)
        {
            if (lang == null)
            {
                return;
            }
            bool pt = Texts.IsPortuguese(lang);
            Blueprint selected = null;
            try
            {
                selected = BlueprintStore.Selected;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Blueprints could not be listed: {e.Message}");
            }
            string placeName = selected == null
                ? (pt ? "Planta da fábrica" : "Factory blueprint")
                : (pt ? $"Planta: {selected.Name}" : $"Blueprint: {selected.Name}");
            Texts.Set(lang, Content.BlueprintPlaceId, placeName);
            Texts.Set(lang, Content.BlueprintPlaceId + "_d", pt
                ? "Põe no chão a planta escolhida na Mesa de Arquitetura. Clique para construir: cada peça vira a obra normal do jogo, paga com o que você e os baús da fábrica têm. O que não couber ou faltar material fica desenhado para depois. R prende o desenho na última obra."
                : "Lays the blueprint chosen on the Architect's Desk on the floor. Click to build: every piece becomes the game's normal construction, paid from you and the factory's chests. Whatever doesn't fit or lacks materials stays drawn for later. R pins the drawing to the last construction.");
            Texts.Set(lang, Content.BlueprintCopyId, pt ? "Decalque" : "Trace");
            Texts.Set(lang, Content.BlueprintCopyId + "_d", pt
                ? "Copia um trecho da fábrica para uma planta. Clique num canto e depois no canto oposto: as máquinas, esteiras e baús dentro do retângulo viram uma planta nova na Mesa de Arquitetura."
                : "Copies part of the factory into a blueprint. Click one corner, then the opposite corner: the machines, belts and chests inside become a new blueprint on the Architect's Desk.");
        }
    }
}
