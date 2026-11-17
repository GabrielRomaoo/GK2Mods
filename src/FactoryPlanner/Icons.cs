using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// Build icons shipped as embedded PNGs (made by tools/icons/make_planner_icons.py). They are served through
    /// EasySpritesCollection.GetSprite, the lookup every icon in the game goes through.
    /// </summary>
    internal static class Icons
    {
        internal const string DraftingTable = "gk2fp_icon_drafting_table";
        internal const string ArchitectTable = "gk2fp_icon_architect_table";
        internal const string Blueprint = "gk2fp_icon_blueprint";
        internal const string BlueprintCopy = "gk2fp_icon_blueprint_copy";

        // The game's i_b_* sprites are 48x48 at 50 pixels per unit with an 8px transparent margin: the art is
        // the centre 32x32 (Unity reports the margin as padding and UI images draw it at 2/3 of their box).
        // Our 32x32 art goes in the same 48x48 frame, or it would fill the whole box and look 1.5x bigger.
        private const float PixelsPerUnit = 50f;
        private const int Frame = 48;

        // Unity 6 also has Span overloads of LoadImage, which net472 reference assemblies can't resolve.
        private static readonly MethodInfo LoadImage = AccessTools.Method(typeof(ImageConversion), nameof(ImageConversion.LoadImage),
            new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });

        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private static bool loaded;

        internal static bool TryGet(string name, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(name) || !name.StartsWith("gk2fp_", System.StringComparison.Ordinal))
            {
                return false;
            }
            EnsureLoaded();
            return sprites.TryGetValue(name, out sprite) && sprite != null;
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }
            loaded = true;
            Assembly asm = typeof(Icons).Assembly;
            foreach (string resource in asm.GetManifestResourceNames())
            {
                if (!resource.EndsWith(".png") || !resource.Contains(".Icons."))
                {
                    continue;
                }
                string name = Path.GetFileNameWithoutExtension(resource);
                name = name.Substring(name.LastIndexOf('.') + 1);
                using (Stream stream = asm.GetManifestResourceStream(resource))
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        name = name,
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                    if (!(bool)LoadImage.Invoke(null, new object[] { texture, memory.ToArray(), false }))
                    {
                        Plugin.Log.LogError($"Icon {name}: could not decode PNG.");
                        continue;
                    }
                    texture = Framed(texture);
                    Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
                    sprite.name = name;
                    Object.DontDestroyOnLoad(texture);
                    sprites[name] = sprite;
                }
            }
            Plugin.Log.LogInfo($"Loaded {sprites.Count} icon(s).");
        }

        /// <summary>Centres the art in a transparent 48x48 frame, like the game's own icons.</summary>
        private static Texture2D Framed(Texture2D art)
        {
            if (art.width >= Frame && art.height >= Frame)
            {
                return art;
            }
            var framed = new Texture2D(Frame, Frame, TextureFormat.RGBA32, false)
            {
                name = art.name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var clear = new Color32[Frame * Frame];
            framed.SetPixels32(clear);
            framed.SetPixels32((Frame - art.width) / 2, (Frame - art.height) / 2, art.width, art.height, art.GetPixels32());
            framed.Apply();
            Object.Destroy(art);
            return framed;
        }
    }

    [HarmonyPatch(typeof(EasySpritesCollection), nameof(EasySpritesCollection.GetSprite))]
    internal static class EasySpritesCollection_GetSprite
    {
        private static bool Prefix(string spriteName, ref Sprite __result)
        {
            if (!Icons.TryGet(spriteName, out Sprite sprite))
            {
                return true;
            }
            __result = sprite;
            return false;
        }
    }

    [HarmonyPatch(typeof(EasySpritesCollection), nameof(EasySpritesCollection.HasSprite))]
    internal static class EasySpritesCollection_HasSprite
    {
        private static bool Prefix(string spriteName, ref bool __result)
        {
            if (!Icons.TryGet(spriteName, out _))
            {
                return true;
            }
            __result = true;
            return false;
        }
    }
}
