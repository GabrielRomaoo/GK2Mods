using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;

namespace GK2Mods.FactoryPlanner
{
    // ---------------------------------------------------------------- balance data

    [HarmonyPatch(typeof(GameBalance), nameof(GameBalance.InitCache))]
    internal static class GameBalance_InitCache
    {
        private static void Prefix(GameBalance __instance) => Content.InjectDefs(__instance);
    }

    [HarmonyPatch(typeof(GameBalance), nameof(GameBalance.CreateBuildCache))]
    internal static class GameBalance_CreateBuildCache
    {
        private static void Prefix() => Content.HideFromBuildCache();

        private static Exception Finalizer(GameBalance __instance, Exception __exception)
        {
            Content.RestoreAfterBuildCache(__instance);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(LLBase), nameof(LLBase.LoadLanguageResource))]
    internal static class LLBase_LoadLanguageResource
    {
        private static void Postfix() => Content.AddTranslations(LLBase.currentLang);
    }

    // ---------------------------------------------------------------- tagging built tables

    /// <summary>Tag handed to the next WgoData added to a scene (set around a build or a replace).</summary>
    internal static class PendingTag
    {
        internal static string Value;
    }

    [HarmonyPatch(typeof(WgoBuildPointer), nameof(WgoBuildPointer.TryDoBuildAction))]
    internal static class WgoBuildPointer_TryDoBuildAction
    {
        private static void Prefix(BuildingDef ___buildingDef)
        {
            string tag = Content.TagForDef(___buildingDef?.id);
            if (tag != null)
            {
                PendingTag.Value = tag;
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            PendingTag.Value = null;
            return __exception;
        }
    }

    /// <summary>A finished construction site is replaced by the final chest: carry the tag over.</summary>
    [HarmonyPatch(typeof(WorldData), nameof(WorldData.ReplaceWgoData), typeof(WgoData), typeof(string))]
    internal static class WorldData_ReplaceWgoData
    {
        private static void Prefix(WgoData wgoData)
        {
            if (Content.IsOurTag(wgoData?.CustomTag))
            {
                PendingTag.Value = wgoData.CustomTag;
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            PendingTag.Value = null;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(GameSceneData), nameof(GameSceneData.AddWgoData), typeof(WgoData), typeof(bool))]
    internal static class GameSceneData_AddWgoData
    {
        private static void Prefix(WgoData wgoData)
        {
            if (PendingTag.Value != null && wgoData != null && string.IsNullOrEmpty(wgoData.CustomTag))
            {
                wgoData.CustomTag = PendingTag.Value;
                PendingTag.Value = null;
                Plugin.Debug($"Tagged {wgoData.id} as {wgoData.CustomTag}");
            }
        }
    }

    // ---------------------------------------------------------------- names

    /// <summary>
    /// The info panel shown when pointing at an object (finished or under construction) names it after its
    /// WGO id, i.e. the template chest. The tagged table gets its own name, description and icon instead.
    /// </summary>
    [HarmonyPatch]
    internal static class UIInfoWidgetData_Ctors
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Constructor(typeof(UIInfoWidgetData), new[] { typeof(WgoData), typeof(string), typeof(bool) });
            yield return AccessTools.Constructor(typeof(UIInfoWidgetData), new[] { typeof(WgoData), typeof(CraftDef), typeof(bool) });
        }

        private static void Postfix(UIInfoWidgetData __instance, WgoData wgoData, MethodBase __originalMethod)
        {
            string tag = wgoData?.CustomTag;
            if (!Content.IsOurTag(tag))
            {
                return;
            }
            __instance.Header = LLBase.L(tag);
            // The craft variant describes the craft in progress (the construction); keep that text.
            if (__originalMethod.GetParameters()[1].ParameterType == typeof(string))
            {
                __instance.Description = LLBase.L(tag + "_d");
            }
            __instance.Icon = LazySingletonSO<EasySpritesCollection>.Instance.GetSprite(Content.IconForTag(tag));
        }
    }
}
