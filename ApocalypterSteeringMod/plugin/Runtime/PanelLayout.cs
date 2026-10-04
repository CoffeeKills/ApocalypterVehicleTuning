using UnityEngine;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// Every size the docked panel uses, as pure functions of its width (0.6.0). The panel
    /// applies these to stored RectTransforms (SettingsPanel.Relayout), so width changes apply
    /// live; the harness checks them at 400 / 460 / 800 px (no layout engine in the stubs —
    /// the CurveEditor-bands precedent).
    ///
    /// Units are reference pixels (the canvas runs ConstantPixelSize with
    /// scaleFactor = Screen.height / 1080 x PanelScale, which renders exactly like 0.5.0's
    /// height-matched 1920x1080 scaler at PanelScale 1).
    ///
    /// Rows have two forms: WIDE (content >= 600 px, i.e. the 800 px window) keeps 0.5.0's
    /// single-line geometry byte-for-byte; NARROW stacks the row (title + Reset, hint, then
    /// slider + value) so nothing overlaps down to a 400 px window.
    /// </summary>
    public static class PanelLayout
    {
        public const float WideContentMin = 600f;
        public const float ScrollGutter = 14f;      // scroll view's scrollbar strip (UiKit.MakeScroll)
        public const float MinWindowWidth = 320f;   // when the screen itself is narrower than PanelWidth

        // Window chrome (pixels from the window's top / bottom edge).
        public const float TitleTop = 16f, TitleHeight = 34f;
        public const float SubtitleTop = 52f, SubtitleHeight = 34f;
        public const float TabsTop = 90f, TabRowHeight = 26f, TabRowGap = 4f;
        public const int TabsPerRow = 5;
        public const float TabsBottom = TabsTop + 2f * TabRowHeight + TabRowGap;   // 146
        public const float PageTop = TabsBottom + 10f;                              // 156
        public const float FooterHeight = 124f;
        public const float PageBottom = FooterHeight + 8f;                          // 132
        public const float CloseSize = 40f;

        // Footer bands (from the footer's bottom edge).
        public const float FooterStatusBottom = 96f, FooterStatusHeight = 22f;
        public const float FooterRow1Bottom = 54f, FooterRow1Height = 36f;   // Copy | Paste | All off
        public const float FooterRow2Bottom = 10f, FooterRow2Height = 38f;   // Reset tab | Done

        /// <summary>Window side padding: 0.5.0's 24 px on the wide window, 16 px when docked narrow.</summary>
        public static float Pad(float windowWidth)
        {
            return windowWidth >= 760f ? 24f : 16f;
        }

        /// <summary>
        /// The width actually used: PanelWidth, but never wider than the canvas
        /// (screen width / scale factor) minus a 20 px margin, never below MinWindowWidth.
        /// </summary>
        public static float EffectiveWidth(float panelWidth, float screenWidth, float screenHeight, float panelScale)
        {
            float scale = ScaleFactor(screenHeight, panelScale);
            float canvasWidth = scale > 0f ? screenWidth / scale : panelWidth;
            float w = Mathf.Min(panelWidth, canvasWidth - 20f);
            return w < MinWindowWidth ? MinWindowWidth : w;
        }

        /// <summary>ConstantPixelSize factor: Screen.height / 1080 x PanelScale (PanelScale 1 = 0.5.0).</summary>
        public static float ScaleFactor(float screenHeight, float panelScale)
        {
            float h = screenHeight > 0f ? screenHeight : 1080f;
            return h / 1080f * (panelScale > 0f ? panelScale : 1f);
        }

        /// <summary>Width of a page's row content: window - left pad - (pad - 10) right - scrollbar.</summary>
        public static float ContentWidth(float windowWidth)
        {
            float pad = Pad(windowWidth);
            return windowWidth - pad - (pad - 10f) - ScrollGutter;
        }

        public static bool Wide(float contentWidth)
        {
            return contentWidth >= WideContentMin;
        }

        /// <summary>A rectangle in a row: X/Top from the row's top-left, fixed W/H.</summary>
        public struct Band
        {
            public float X, Top, W, H;

            public Band(float x, float top, float w, float h)
            {
                X = x;
                Top = top;
                W = w;
                H = h;
            }

            public float Right { get { return X + W; } }
            public float Bottom { get { return Top + H; } }

            public bool Overlaps(Band o)
            {
                return X < o.Right && o.X < Right && Top < o.Bottom && o.Top < Bottom;
            }

            public bool Inside(float width, float height)
            {
                return X >= 0f && Top >= 0f && Right <= width + 0.01f && Bottom <= height + 0.01f && W > 0f && H > 0f;
            }
        }

        /// <summary>Apply a Band to a RectTransform anchored to the row's top-left corner.</summary>
        public static void Apply(RectTransform rt, Band b)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(b.W, b.H);
            rt.anchoredPosition = new Vector2(b.X, -b.Top);
        }

        // ---------------------------------------------------------------- slider row

        public struct SliderGeom
        {
            public bool Stacked;
            public float RowHeight;
            public Band Title, Hint, Slider, Value, Reset;
            public int TitleFont, HintFont;
        }

        public static SliderGeom SliderRow(float c, bool hasReadout)
        {
            var g = new SliderGeom();
            if (Wide(c))
            {
                // 0.5.0 exactly: label 16..316 (title top half, hint bottom half) | slider from
                // 328 to (c - 236/172) | value 150/86 wide at 82 from the right | Reset 66x32 at 12.
                g.RowHeight = 58f;
                g.Title = new Band(16f, 6f, 300f, 23f);
                g.Hint = new Band(16f, 29f, 300f, 23f);
                float sliderRight = hasReadout ? 236f : 172f;
                g.Slider = new Band(328f, 4f, c - 328f - sliderRight, 50f);
                float vw = hasReadout ? 150f : 86f;
                g.Value = new Band(c - 82f - vw, 0f, vw, 58f);
                g.Reset = new Band(c - 12f - 66f, 13f, 66f, 32f);
                g.TitleFont = 17;
                g.HintFont = 13;
                return g;
            }
            g.Stacked = true;
            g.RowHeight = 100f;
            const float side = 12f, resetW = 60f;
            float valW = hasReadout ? 130f : 80f;
            g.Reset = new Band(c - side - resetW, 6f, resetW, 28f);
            g.Title = new Band(side, 6f, c - 2f * side - resetW - 8f, 24f);
            g.Hint = new Band(side, 34f, c - 2f * side, 16f);   // below Reset (6..34), above the slider (54)
            g.Value = new Band(c - side - valW, 52f, valW, 44f);
            g.Slider = new Band(side, 54f, c - 2f * side - valW - 10f, 40f);
            g.TitleFont = 16;
            g.HintFont = 13;
            return g;
        }

        // ---------------------------------------------------------------- option / master rows

        public struct SwitchRowGeom
        {
            public float RowHeight;
            public Band Title, Hint;
            public float SwitchW, SwitchH, SwitchRight;
            public int TitleFont, HintFont;
            public bool WrapHint;
        }

        /// <summary>Secondary on/off row (0.5.0 wide: 58 px, switch 86x34 at 14 from the right).</summary>
        public static SwitchRowGeom OptionRow(float c)
        {
            var g = new SwitchRowGeom { SwitchW = 86f, SwitchH = 34f, SwitchRight = 14f, TitleFont = 17, HintFont = 14 };
            float textW = c - 16f - 120f;
            if (Wide(c))
            {
                g.RowHeight = 58f;
                g.Title = new Band(16f, 6f, textW, 23f);
                g.Hint = new Band(16f, 29f, textW, 23f);
                return g;
            }
            g.RowHeight = 74f;
            g.SwitchW = 76f;
            g.SwitchH = 32f;
            g.SwitchRight = 10f;
            textW = c - 12f - (g.SwitchW + g.SwitchRight + 10f);
            g.Title = new Band(12f, 6f, textW, 22f);
            g.Hint = new Band(12f, 30f, textW, 38f);   // two wrapped lines at 13 px
            g.TitleFont = 16;
            g.HintFont = 13;
            g.WrapHint = true;
            return g;
        }

        /// <summary>The big ON/OFF row at the top of each tab (0.5.0 wide: 76 px, switch 120x44).</summary>
        public static SwitchRowGeom MasterRow(float c)
        {
            var g = new SwitchRowGeom { SwitchW = 120f, SwitchH = 44f, SwitchRight = 14f, TitleFont = 21, HintFont = 15 };
            if (Wide(c))
            {
                g.RowHeight = 76f;
                g.Title = new Band(18f, 8f, c - 18f - 160f, 30f);
                g.Hint = new Band(18f, 38f, c - 18f - 160f, 30f);
                return g;
            }
            g.RowHeight = 92f;
            g.SwitchW = 96f;
            g.SwitchH = 40f;
            g.SwitchRight = 12f;
            float textW = c - 14f - (g.SwitchW + g.SwitchRight + 10f);
            g.Title = new Band(14f, 8f, textW, 28f);
            g.Hint = new Band(14f, 38f, textW, 48f);   // up to three wrapped lines at 14 px
            g.TitleFont = 19;
            g.HintFont = 14;
            g.WrapHint = true;
            return g;
        }

        // ---------------------------------------------------------------- misc

        /// <summary>Preset buttons per row: 3 on the wide window, 2 when narrow ("Custom (Off-road)" must fit).</summary>
        public static int PresetsPerRow(float c)
        {
            return Wide(c) ? 3 : 2;
        }

        public static int PresetFont(float c)
        {
            return Wide(c) ? 17 : 15;
        }

        /// <summary>Wrapped note height: the wide height, or more lines when narrow.</summary>
        public static float NoteHeight(float c, float wideHeight)
        {
            return Wide(c) ? wideHeight : Mathf.Round(wideHeight * 1.5f);
        }

        public static int TabFont(float windowWidth)
        {
            return windowWidth < 440f ? 12 : 14;
        }

        /// <summary>Digit hotkeys while the panel is open: 1..9 -> tabs 0..8, 0 -> tab 9; else -1.</summary>
        public static int TabForDigit(int digit)
        {
            if (digit == 0)
            {
                return 9;
            }
            return digit >= 1 && digit <= 9 ? digit - 1 : -1;
        }

        /// <summary>
        /// Height of a wrapped hint at 13 px Arial for a header band (curve editor / gear graph):
        /// a conservative 6.6 px per character, 16 px per line, plus one spare line.
        /// </summary>
        public static float WrappedHintHeight(int chars, float width)
        {
            if (width < 40f)
            {
                width = 40f;
            }
            int lines = Mathf.CeilToInt(chars * 6.6f / width);
            if (lines < 1)
            {
                lines = 1;
            }
            return (lines + 1) * 16f;
        }
    }
}
