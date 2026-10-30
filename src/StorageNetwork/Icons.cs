using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Mods.StorageNetwork
{
    /// <summary>
    /// Build icons shipped as embedded PNGs (made by tools/icons/make_icons.py). They are served through
    /// EasySpritesCollection.GetSprite, the lookup every icon in the game goes through.
    /// </summary>
    internal static class Icons
    {
        internal const string CentralStorage = "gk2sn_icon_central_storage";
        internal const string ZoneRelay = "gk2sn_icon_zone_relay";
        internal const string Connector = "gk2sn_icon_connector";

        // The game's i_b_* sprites are 48px at 50 pixels per unit, stored ~32px in their atlas.
        // A 32px sprite at 32/48*50 PPU has the same size in every layout.
        private const float PixelsPerUnit = 50f * 32f / 48f;

        // Unity 6 also has Span overloads of LoadImage, which net472 reference assemblies can't resolve.
        private static readonly MethodInfo LoadImage = AccessTools.Method(typeof(ImageConversion), nameof(ImageConversion.LoadImage),
            new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });

        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private static bool loaded;

        internal static bool TryGet(string name, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(name) || !name.StartsWith("gk2sn_", System.StringComparison.Ordinal))
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
                if (!resource.EndsWith(".png"))
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
                    Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
                    sprite.name = name;
                    Object.DontDestroyOnLoad(texture);
                    sprites[name] = sprite;
                }
            }
            Plugin.Log.LogInfo($"Loaded {sprites.Count} icon(s).");
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
