using System;
using UnityEngine;
using UnityEngine.UI;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// Small uGUI toolkit for the tuning panel. Design rules (each one fixes a problem the
    /// old panel had):
    ///  - Rows are positioned with anchors, never by nesting layout groups inside rows, so
    ///    nothing can collapse to height 0. Only the scrolling list uses a VerticalLayoutGroup,
    ///    and every child there declares a preferred height.
    ///  - Text uses uGUI Text with Unity's built-in Arial: always available, and it renders
    ///    any glyph without needing a TMP atlas that contains it.
    ///  - Widgets have no keyboard navigation, so held driving keys (W/A/S/D/arrows) can never
    ///    move a slider or flip a switch while the panel is open.
    /// </summary>
    internal static class UiKit
    {
        // ------------------------------------------------------------------ palette
        public static readonly Color WindowBg = new Color(0.07f, 0.08f, 0.10f, 0.985f);
        public static readonly Color Divider = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color RowBase = new Color(0.17f, 0.19f, 0.23f, 1f);
        public static readonly Color RowNormal = new Color(0.12f, 0.135f, 0.165f, 1f);
        public static readonly Color ChipBase = new Color(0.25f, 0.28f, 0.34f, 1f);
        public static readonly Color Accent = new Color(0.27f, 0.62f, 1f, 1f);
        public static readonly Color Good = new Color(0.22f, 0.74f, 0.47f, 1f);
        public static readonly Color Danger = new Color(0.80f, 0.27f, 0.29f, 1f);
        public static readonly Color SwitchOff = new Color(0.30f, 0.33f, 0.39f, 1f);
        public static readonly Color TextMain = new Color(0.94f, 0.95f, 0.97f, 1f);
        public static readonly Color TextMuted = new Color(0.62f, 0.67f, 0.75f, 1f);
        public static readonly Color TrackBg = new Color(0.04f, 0.05f, 0.07f, 1f);
        public static readonly Color HandleColor = new Color(0.93f, 0.95f, 0.98f, 1f);
        // Practically invisible but still hit by raycasts.
        public static readonly Color HitArea = new Color(0f, 0f, 0f, 0.01f);

        // --------------------------------------------------------------------- font
        private static Font _font;
        private static bool _fontResolved;

        public static Font Font
        {
            get
            {
                if (!_fontResolved)
                {
                    _fontResolved = true;
                    _font = ResolveFont();
                    if (_font == null)
                    {
                        Plugin.Log.LogError("No usable UI font found; the tuning panel text will not render.");
                    }
                }
                return _font;
            }
        }

        private static Font ResolveFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (Exception) { }
            if (f == null)
            {
                try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (Exception) { }
            }
            if (f == null)
            {
                try { f = Font.CreateDynamicFontFromOSFont("Arial", 16); } catch (Exception) { }
            }
            return f;
        }

        // ------------------------------------------------------------------- layout
        public static RectTransform Make(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;   // UI
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Anchors (0..1) plus pixel insets from each edge of that anchor box.</summary>
        public static RectTransform Place(RectTransform rt, float xMin, float yMin, float xMax, float yMax,
            float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform Fill(RectTransform rt)
        {
            return Place(rt, 0f, 0f, 1f, 1f);
        }

        /// <summary>Fixed-size box anchored to the right edge, vertically centred.</summary>
        public static RectTransform RightBox(RectTransform rt, float width, float height, float fromRight)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(-fromRight, 0f);
            return rt;
        }

        /// <summary>Fixed-size box anchored to the left edge, vertically centred.</summary>
        public static RectTransform LeftBox(RectTransform rt, float width, float height, float fromLeft)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(fromLeft, 0f);
            return rt;
        }

        /// <summary>Full-width strip hanging from the top edge.</summary>
        public static RectTransform Top(RectTransform rt, float fromTop, float height, float left = 0f, float right = 0f)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -(fromTop + height));
            rt.offsetMax = new Vector2(-right, -fromTop);
            return rt;
        }

        /// <summary>Full-width strip standing on the bottom edge.</summary>
        public static RectTransform Bottom(RectTransform rt, float fromBottom, float height, float left = 0f, float right = 0f)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(left, fromBottom);
            rt.offsetMax = new Vector2(-right, fromBottom + height);
            return rt;
        }

        // ------------------------------------------------------------------ widgets
        public static Image Paint(RectTransform rt, Color color, bool raycast)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Text Label(RectTransform parent, string text, int size, Color color, TextAnchor anchor,
            FontStyle style = FontStyle.Normal, bool wrap = false)
        {
            RectTransform rt = Make("Text", parent);
            Fill(rt);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            // Rich text on: ApocaLanguage translations may carry rich-text tags.
            // Our own strings contain no angle brackets, so this costs nothing.
            t.supportRichText = true;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        /// <summary>Hover/press tint, and no keyboard navigation.</summary>
        public static void StyleSelectable(Selectable s)
        {
            s.transition = Selectable.Transition.ColorTint;
            ColorBlock cb = s.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            cb.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            cb.selectedColor = Color.white;
            cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.06f;
            s.colors = cb;

            Navigation nav = new Navigation();
            nav.mode = Navigation.Mode.None;
            s.navigation = nav;
        }

        public static Button MakeButton(RectTransform parent, string name, string text, Color color, int fontSize,
            Action onClick, out Text label)
        {
            RectTransform rt = Make(name, parent);
            Image img = Paint(rt, color, true);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            StyleSelectable(button);
            label = Label(rt, text, fontSize, TextMain, TextAnchor.MiddleCenter, FontStyle.Bold);
            button.onClick.AddListener(() => onClick());
            return button;
        }

        public static Slider MakeSlider(RectTransform parent, string name)
        {
            RectTransform root = Make(name, parent);
            Paint(root, HitArea, true);   // makes the whole row height clickable, not just the thin track

            RectTransform track = Make("Track", root);
            Place(track, 0f, 0.5f, 1f, 0.5f, 0f, -3f, 0f, -3f);
            Paint(track, TrackBg, false);

            RectTransform fillArea = Make("Fill Area", root);
            Place(fillArea, 0f, 0.5f, 1f, 0.5f, 0f, -3f, 0f, -3f);
            RectTransform fill = Make("Fill", fillArea);
            Fill(fill);
            Paint(fill, Accent, false);

            RectTransform handleArea = Make("Handle Slide Area", root);
            Place(handleArea, 0f, 0f, 1f, 1f, 7f, 8f, 7f, 8f);
            RectTransform handle = Make("Handle", handleArea);
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(14f, 0f);
            Image handleImg = Paint(handle, HandleColor, true);

            var slider = root.gameObject.AddComponent<Slider>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            StyleSelectable(slider);
            return slider;
        }

        /// <summary>
        /// Scroll view with mouse wheel, drag-to-scroll and a thin auto-hiding scrollbar.
        /// Returns the content rectangle (a VerticalLayoutGroup: add rows that each have a
        /// LayoutElement with a preferred height).
        /// </summary>
        public static RectTransform MakeScroll(RectTransform parent, string name, out ScrollRect scroll)
        {
            RectTransform root = Make(name, parent);
            Fill(root);
            Paint(root, HitArea, true);   // lets the wheel work over gaps between rows
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.inertia = false;

            RectTransform viewport = Make("Viewport", root);
            Place(viewport, 0f, 0f, 1f, 1f, 0f, 0f, 14f, 0f);   // leaves room for the scrollbar
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = Make("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.padding = new RectOffset(0, 0, 4, 14);
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RectTransform bar = Make("Scrollbar", root);
            Place(bar, 1f, 0f, 1f, 1f, -8f, 0f, 0f, 0f);
            Paint(bar, new Color(1f, 1f, 1f, 0.05f), true);
            RectTransform slidingArea = Make("Sliding Area", bar);
            Fill(slidingArea);
            RectTransform barHandle = Make("Handle", slidingArea);
            Fill(barHandle);
            Image barHandleImg = Paint(barHandle, new Color(1f, 1f, 1f, 0.30f), true);
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = barHandle;
            scrollbar.targetGraphic = barHandleImg;
            StyleSelectable(scrollbar);

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return content;
        }
    }
}
