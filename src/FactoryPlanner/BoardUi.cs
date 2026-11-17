using System;
using System.Collections.Generic;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UButton = UnityEngine.UI.Button;
using UImage = UnityEngine.UI.Image;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// Building blocks for the board's own window, drawn with the game's own sprites and text styles (the way
    /// the Build Tools mod draws its Plans window): positioned rects, plates, buttons, labels and item icons.
    /// Coordinates are from the parent's top-left corner, y going down.
    /// </summary>
    internal static class BoardUi
    {
        internal static readonly Color Ink = Hex("e9dfc8");
        internal static readonly Color Muted = Hex("9aa0ae");
        internal static readonly Color Gold = Hex("f0c35a");
        internal static readonly Color Ok = Hex("8fc46a");
        internal static readonly Color Short = Hex("e07a5f");
        internal static readonly Color Plan = Hex("6fb8ff");
        internal static readonly Color Deep = Hex("1d212c");
        internal static readonly Color Field = Hex("12141b");
        internal static readonly Color ButtonFill = Hex("333a4b");
        internal static readonly Color Red = Hex("b8402f");
        internal static readonly Color Selected = Hex("323a4c");

        internal const string StyleText = "small_font-regular_text_1";
        internal const string StyleBold = "small_font_bold-sub_header_2";
        internal const string StyleSmall = "small_font-regular_text_3";

        internal const string PlateSprite = "craft_window-craft_plate";
        internal const string HeaderSprite = "comm-header_2";
        internal const string FrameSprite = "tech_frame_selection";

        private static readonly string[] wanted =
        {
            "craft_window-craft_plate", "tech_frame_selection", "comm-header_2", "comm-header_2-inactive",
            "comm-btn-small_grey-active", "comm-btn-small_grey-over", "comm-btn-small_grey-press", "comm-btn-small_grey-inactive",
            "comm-btn-simple_red-active", "comm-btn-simple_red-over", "comm-btn-simple_red-press",
            "comm-value_frame_1", "craft_window-separator_hor-drk_2",
        };

        private static Dictionary<string, Sprite> sprites;
        private static readonly Dictionary<string, TextStyle> styles = new Dictionary<string, TextStyle>(StringComparer.Ordinal);
        private static float lastPress = -1f;

        /// <summary>A label to copy the font from when a text style is missing (the window's own title).</summary>
        internal static TMP_Text Sample;

        internal static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color c);
            return c;
        }

        internal static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(y));
            rect.sizeDelta = new Vector2(Mathf.Round(w), Mathf.Round(h));
        }

        internal static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            Place(rect, x, y, w, h);
            return rect;
        }

        internal static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        internal static Sprite Game(string name)
        {
            if (sprites == null || (sprites.TryGetValue(name, out Sprite cached) && cached == null))
            {
                sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
                var want = new HashSet<string>(wanted, StringComparer.Ordinal);
                foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>())
                {
                    if (s != null && want.Contains(s.name) && !sprites.ContainsKey(s.name))
                    {
                        sprites[s.name] = s;
                    }
                }
                foreach (string w in wanted)
                {
                    if (!sprites.ContainsKey(w))
                    {
                        Plugin.Debug($"Board window: sprite {w} not found, a plain colour is drawn instead");
                    }
                }
            }
            return sprites.TryGetValue(name, out Sprite sprite) ? sprite : null;
        }

        internal static void SetSprite(UImage img, string sprite, Color fallback)
        {
            Sprite s = Game(sprite);
            if (s != null)
            {
                img.sprite = s;
                img.type = s.border != Vector4.zero ? UImage.Type.Sliced : UImage.Type.Simple;
                img.color = Color.white;
            }
            else
            {
                img.sprite = null;
                img.color = fallback;
            }
        }

        internal static UImage Sliced(Transform parent, string name, float x, float y, float w, float h, string sprite, Color fallback)
        {
            UImage img = Rect(parent, name, x, y, w, h).gameObject.AddComponent<UImage>();
            img.raycastTarget = false;
            SetSprite(img, sprite, fallback);
            return img;
        }

        internal static TMP_Text Text(Transform parent, string name, string style, float x, float y, float w, float h, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool wrap = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = align;
            if (!SetStyle(label, style) && Sample != null)
            {
                label.font = Sample.font;
                label.fontSharedMaterial = Sample.fontSharedMaterial;
            }
            if (Mathf.Approximately(label.fontSize, 36f))
            {
                label.fontSize = 16f;
            }
            label.color = color;
            Place(label.rectTransform, x, y, w, h);
            return label;
        }

        private static bool SetStyle(TMP_Text label, string style)
        {
            if (!styles.TryGetValue(style, out TextStyle found) || found == null)
            {
                styles.Clear();
                foreach (TextStyle s in Resources.FindObjectsOfTypeAll<TextStyle>())
                {
                    if (s != null && !styles.ContainsKey(s.name))
                    {
                        styles[s.name] = s;
                    }
                }
                styles.TryGetValue(style, out found);
            }
            if (found == null)
            {
                return false;
            }
            (label.GetComponent<TextStyleComponent>() ?? label.gameObject.AddComponent<TextStyleComponent>()).SetTextStyle(found);
            return true;
        }

        /// <summary>An item's game icon, or nothing when the item has none.</summary>
        internal static UImage ItemIcon(Transform parent, string itemId, float x, float y, float size)
        {
            Sprite sprite = null;
            try
            {
                string icon = GameBalance.Me.GetDataOrNull<ItemDef>(itemId)?.iconId;
                if (!string.IsNullOrEmpty(icon))
                {
                    sprite = LazySingletonSO<EasySpritesCollection>.Instance.GetSprite(icon);
                }
            }
            catch (Exception e)
            {
                Plugin.Debug($"Board window: no icon for {itemId}: {e.Message}");
            }
            if (sprite == null)
            {
                return null;
            }
            UImage img = Rect(parent, "Icon", x, y, size, size).gameObject.AddComponent<UImage>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Clicks a little apart: one press of the mouse is one click even when two handlers see it.</summary>
        internal static void Pressed(Action action)
        {
            if (Time.unscaledTime - lastPress < 0.15f)
            {
                return;
            }
            lastPress = Time.unscaledTime;
            try
            {
                LazyAudio.PlayAndForget("gui_hover_light");
                action();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Board window click failed: {e}");
            }
        }

        internal sealed class ButtonParts
        {
            internal RectTransform Root;
            internal TMP_Text Label;
            internal UButton Button;
        }

        /// <summary>A game-style button: red for the main action, grey otherwise.</summary>
        internal static ButtonParts Button(Transform parent, string name, float x, float y, float w, float h, string text, bool primary,
            Action click, bool enabled = true)
        {
            RectTransform root = Rect(parent, name, x, y, w, h);
            UImage img = root.gameObject.AddComponent<UImage>();
            img.raycastTarget = true;
            string sprite = primary ? "comm-btn-simple_red" : "comm-btn-small_grey";
            SetSprite(img, sprite + (enabled || primary ? "-active" : "-inactive"), primary ? Red : ButtonFill);
            Color color = enabled ? (primary ? Hex("fff2df") : Ink) : Muted;
            TMP_Text label = Text(root, "Label", primary ? StyleBold : StyleText, 4f, 0f, w - 8f, h, color, TextAlignmentOptions.Center);
            label.text = text;
            label.enableAutoSizing = true;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = Mathf.Max(8f, label.fontSize - 6f);
            UButton button = root.gameObject.AddComponent<UButton>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.interactable = enabled;
            Sprite over = Game(sprite + "-over");
            Sprite press = Game(sprite + "-press");
            if (over != null && press != null)
            {
                button.transition = Selectable.Transition.SpriteSwap;
                button.targetGraphic = img;
                button.spriteState = new SpriteState { highlightedSprite = over, pressedSprite = press, selectedSprite = img.sprite };
            }
            else
            {
                button.transition = Selectable.Transition.None;
            }
            button.onClick.AddListener(() => Pressed(click));
            return new ButtonParts { Root = root, Label = label, Button = button };
        }

        /// <summary>A clickable plate across a list (anchored to the list's width), optionally framed as selected.</summary>
        internal static RectTransform Row(RectTransform list, string name, float top, float h, string plate, bool selected, Action click)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UImage));
            go.transform.SetParent(list, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(0f, h);
            UImage img = go.GetComponent<UImage>();
            if (plate != null)
            {
                SetSprite(img, plate, selected ? Selected : Deep);
            }
            else
            {
                img.color = new Color(1f, 1f, 1f, 0f);
            }
            img.raycastTarget = true;
            if (click != null)
            {
                UButton button = go.AddComponent<UButton>();
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.onClick.AddListener(() => Pressed(click));
            }
            if (selected)
            {
                UImage frame = Sliced(rect, "Selected", 0f, 0f, 10f, h, FrameSprite, Gold);
                Stretch(frame.rectTransform);
            }
            return rect;
        }

        /// <summary>A small framed tag ("NA PLANTA"), right-aligned at x.</summary>
        internal static void Pill(Transform parent, float right, float y, string text, Color color)
        {
            TMP_Text label = Text(parent, "Pill", StyleSmall, 0f, y, 300f, 18f, color, TextAlignmentOptions.Center);
            label.text = text;
            float w = Mathf.Ceil(label.GetPreferredValues(text).x) + 12f;
            Place(label.rectTransform, right - w, y, w, 18f);
            UImage frame = Sliced(parent, "PillFrame", right - w, y, w, 18f, "comm-value_frame_1", Field);
            frame.transform.SetSiblingIndex(label.transform.GetSiblingIndex());
        }

        internal static void Tip(GameObject go, string header, string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return;
            }
            HoverTip tip = go.GetComponent<HoverTip>() ?? go.AddComponent<HoverTip>();
            tip.Header = header;
            tip.Body = body;
        }

        /// <summary>A vertical list that scrolls with the wheel; returns its content (rows go in there).</summary>
        internal static RectTransform ScrollList(Transform parent, string name, float x, float y, float w, float h, out ScrollRect scroll)
        {
            RectTransform area = Rect(parent, name, x, y, w, h);
            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(area, false);
            var viewport = (RectTransform)viewportGo.transform;
            Stretch(viewport);
            UImage catcher = viewportGo.AddComponent<UImage>();
            catcher.color = new Color(0f, 0f, 0f, 0f);  // takes the wheel over the gaps between rows
            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);
            scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.inertia = false;
            return content;
        }

        internal static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                GameObject child = t.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }

        internal static void DestroyAll<T>(GameObject go, bool onlySelf) where T : Component
        {
            foreach (T c in onlySelf ? go.GetComponents<T>() : go.GetComponentsInChildren<T>(true))
            {
                UnityEngine.Object.DestroyImmediate(c);
            }
        }

        /// <summary>Copies of the game's buttons keep the original's UI id; give them their own.</summary>
        internal static void UniqueIds(GameObject go, string prefix)
        {
            foreach (LazyButton button in go.GetComponentsInChildren<LazyButton>(true))
            {
                if (string.IsNullOrEmpty(button.LazyUIElementId))
                {
                    continue;
                }
                bool registered = LazyUIElementManager.TryUnregisterElement(button);
                button.LazyUIElementId = prefix + "_" + button.name;
                if (registered)
                {
                    LazyUIElementManager.TryRegisterElement(button);
                }
            }
        }

        /// <summary>Only the window's root may be a canvas: nested ones draw in their own order and eat clicks.</summary>
        internal static void RemoveNestedCanvases(GameObject root)
        {
            foreach (GraphicRaycaster r in root.GetComponentsInChildren<GraphicRaycaster>(true))
            {
                if (r.gameObject != root)
                {
                    UnityEngine.Object.DestroyImmediate(r);
                }
            }
            foreach (CanvasScaler s in root.GetComponentsInChildren<CanvasScaler>(true))
            {
                UnityEngine.Object.DestroyImmediate(s);
            }
            foreach (Canvas c in root.GetComponentsInChildren<Canvas>(true))
            {
                if (c.gameObject != root)
                {
                    UnityEngine.Object.DestroyImmediate(c);
                }
            }
        }

        internal static Transform UiRoot(GUIElements gui)
        {
            UIFitter fitter = Traverse.Create(gui).Field("uiFitter").GetValue<UIFitter>();
            return fitter != null ? fitter.transform : gui.Root;
        }

        internal static bool Shift()
        {
            try
            {
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>The game's tooltip over a row or button of the board's window.</summary>
    internal sealed class HoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal string Header;
        internal string Body;

        public void OnPointerEnter(PointerEventData eventData)
        {
            try
            {
                UITooltip.ShowSimpleInfo(transform, Body, default, Header);
            }
            catch (Exception e)
            {
                Plugin.Debug($"Board tooltip failed: {e.Message}");
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            try
            {
                UITooltip.Hide();
            }
            catch (Exception)
            {
                // nothing to hide
            }
        }

        private void OnDisable() => OnPointerExit(null);
    }
}
