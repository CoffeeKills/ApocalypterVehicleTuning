using UnityEngine;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// Every size the docked panel uses, as pure functions of its width (0.6.0). The panel
    /// applies these to stored RectTransforms (SettingsPanel.Relayout), so width changes apply
    /// live; the harness checks them at 300 / 400 / 460 / 800 / 1000 px (no layout engine in the stubs —
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
        // 0.7.0: = Limits.PanelWidthMin. 0.6.x used 320 here, so a PanelWidth of 300..319
        // (allowed by the slider and the config range) silently rendered 320 px wide.
        public const float MinWindowWidth = 300f;

        // Window chrome (pixels from the window's top / bottom edge).
        public const float TitleTop = 16f, TitleHeight = 34f;
        public const float SubtitleTop = 52f, SubtitleHeight = 34f;
        public const float TabsTop = 90f, TabRowHeight = 26f, TabRowGap = 4f, TabGap = 4f;
        public const int TabsPerRowDefault = 5;
        public const float NarrowTabWindow = 360f;                     // below: 4 tabs per row (3 rows)
        public const float PageGap = 10f;                              // tabs -> page
        // 0.7.0 (FEATURES §5, tighter UI): footer 124 -> 110.
        public const float FooterHeight = 110f;
        public const float PageBottom = FooterHeight + 8f;                          // 118
        public const float CloseSize = 40f;

        // Footer bands (from the footer's bottom edge).
        public const float FooterStatusBottom = 86f, FooterStatusHeight = 20f;
        public const float FooterRow1Bottom = 48f, FooterRow1Height = 32f;   // Copy | Paste | All off
        public const float FooterRow2Bottom = 8f, FooterRow2Height = 34f;    // Reset tab | Done

        // Block heights (wide window). 0.7.0 §5: ~15 % tighter than 0.6.x (58/44/76/38).
        public const float RowHeight = 50f;             // slider / option rows
        public const float PresetButtonHeight = 40f;
        public const float PresetGap = 8f;
        public const float MasterRowHeight = 66f;
        public const float SectionTitleHeight = 32f;

        // Fonts. 0.7.0 §5: +1..2 px ("bigger text, smaller boxes").
        public const int TitleFontWide = 19, TitleFontNarrow = 18;      // 0.6.x 17 / 16
        public const int HintFont = 14;                                  // 0.6.x 13
        public const int OptionHintFontWide = 15;                        // 0.6.x 14
        public const int MasterTitleFontWide = 23, MasterTitleFontNarrow = 21;   // 0.6.x 21 / 19
        public const int MasterHintFontWide = 17, MasterHintFontNarrow = 15;     // 0.6.x 15 / 14
        public const int SectionFont = 16;                               // 0.6.x 14
        public const int ValueFont = 19, ValueFactorFont = 17, ReadoutFont = 13; // 0.6.x 17 / 16 / 12
        public const int ResetFont = 14;                                 // 0.6.x 13
        public const int FooterFont = 15, FooterResetFont = 16, DoneFont = 19, StatusFont = 14;

        /// <summary>Notes keep their call-site size + 1 (0.7.0 §5).</summary>
        public static int NoteFont(int size)
        {
            return size + 1;
        }

        /// <summary>Tabs per row: 5 (two rows), 4 on a window under 360 px so the names fit (three rows).</summary>
        public static int TabsPerRow(float windowWidth)
        {
            return windowWidth < NarrowTabWindow ? 4 : TabsPerRowDefault;
        }

        public static int TabRows(float windowWidth, int tabCount)
        {
            int per = TabsPerRow(windowWidth);
            return (tabCount + per - 1) / per;
        }

        public static float TabsHeight(float windowWidth, int tabCount)
        {
            int rows = TabRows(windowWidth, tabCount);
            return rows * TabRowHeight + (rows - 1) * TabRowGap;
        }

        /// <summary>Top of the page area (below the tab strip).</summary>
        public static float PageTop(float windowWidth, int tabCount)
        {
            return TabsTop + TabsHeight(windowWidth, tabCount) + PageGap;
        }

        /// <summary>Tab button width for a window.</summary>
        public static float TabWidth(float windowWidth)
        {
            int per = TabsPerRow(windowWidth);
            return (windowWidth - 2f * Pad(windowWidth) - (per - 1) * TabGap) / per;
        }

        // Arial advance widths (1/1000 em) for ASCII 32..126, regular and bold (the panel's font:
        // Unity's built-in Arial). Anything else counts 600 (×, ·, °, accented letters: close enough).
        private static readonly short[] ArialRegular = { 278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556, 1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, 333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584 };
        private static readonly short[] ArialBold = { 278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611, 975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556, 333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611, 611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584 };

        /// <summary>
        /// Rendered width of a one-line label in Arial at <paramref name="font"/> px. uGUI text here
        /// overflows (never clips), so a label wider than its band draws over its neighbour: the
        /// panel fits fonts with this (FitFont) and the harness checks real labels against bands.
        /// </summary>
        public static float TextWidth(string text, int font, bool bold)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0f;
            }
            short[] table = bold ? ArialBold : ArialRegular;
            int units = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int c = text[i];
                units += c >= 32 && c <= 126 ? table[c - 32] : 600;
            }
            return units * font / 1000f;
        }

        /// <summary>The largest font from <paramref name="max"/> down to <paramref name="min"/> at which the label fits <paramref name="width"/>.</summary>
        public static int FitFont(string text, int max, float width, bool bold, int min = 11)
        {
            int f = max;
            while (f > min && TextWidth(text, f, bold) > width)
            {
                f--;
            }
            return f;
        }

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

        /// <summary>
        /// A slider row. 0.7.0 §5: 50 px wide / 86 px stacked (0.6.x 58 / 100), +1..2 px fonts.
        /// Wide: label 16..316 (title top, hint bottom) | slider from 328 | value | Reset.
        /// Narrow (stacked): title + Reset / hint / slider + value.
        /// </summary>
        public static SliderGeom SliderRow(float c, bool hasReadout)
        {
            var g = new SliderGeom();
            if (Wide(c))
            {
                g.RowHeight = RowHeight;
                g.Title = new Band(16f, 4f, 300f, 23f);
                g.Hint = new Band(16f, 27f, 300f, 20f);
                const float resetW = 62f, resetH = 28f;
                g.Reset = new Band(c - 12f - resetW, (RowHeight - resetH) / 2f, resetW, resetH);
                float right = g.Reset.X - 4f;
                float vw = hasReadout ? 150f : 86f;
                g.Value = new Band(right - vw, 0f, vw, RowHeight);
                g.Slider = new Band(328f, 3f, g.Value.X - 4f - 328f, RowHeight - 6f);
                g.TitleFont = TitleFontWide;
                g.HintFont = HintFont;
                return g;
            }
            g.Stacked = true;
            g.RowHeight = 86f;
            const float side = 12f, rw = 60f, rh = 26f;
            float valW = hasReadout ? 130f : 80f;
            g.Reset = new Band(c - side - rw, 6f, rw, rh);
            float titleRight = g.Reset.X - 8f;
            g.Title = new Band(side, 6f, titleRight - side, 24f);
            g.Hint = new Band(side, 32f, c - 2f * side, 16f);    // below Reset (6..32), above the slider (50)
            g.Value = new Band(c - side - valW, 48f, valW, 34f);
            g.Slider = new Band(side, 50f, c - 2f * side - valW - 10f, 32f);
            g.TitleFont = TitleFontNarrow;
            g.HintFont = HintFont;
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

        /// <summary>Secondary on/off row (0.7.0: 50 px wide, 64 px narrow).</summary>
        public static SwitchRowGeom OptionRow(float c)
        {
            var g = new SwitchRowGeom { SwitchW = 86f, SwitchH = 30f, SwitchRight = 14f, TitleFont = TitleFontWide, HintFont = OptionHintFontWide };
            float textW = c - 16f - 120f;
            if (Wide(c))
            {
                g.RowHeight = RowHeight;
                g.Title = new Band(16f, 4f, textW, 23f);
                g.Hint = new Band(16f, 27f, textW, 20f);
                return g;
            }
            g.RowHeight = 64f;
            g.SwitchW = 76f;
            g.SwitchH = 30f;
            g.SwitchRight = 10f;
            textW = c - 12f - (g.SwitchW + g.SwitchRight + 10f);
            g.Title = new Band(12f, 5f, textW, 22f);
            g.Hint = new Band(12f, 28f, textW, 32f);   // two wrapped lines at 14 px
            g.TitleFont = TitleFontNarrow;
            g.HintFont = HintFont;
            g.WrapHint = true;
            return g;
        }

        /// <summary>The big ON/OFF row at the top of each tab (0.7.0: 66 px wide; narrow 80, or 96 under 400 px of content).</summary>
        public static SwitchRowGeom MasterRow(float c)
        {
            var g = new SwitchRowGeom { SwitchW = 120f, SwitchH = 40f, SwitchRight = 14f, TitleFont = MasterTitleFontWide, HintFont = MasterHintFontWide };
            if (Wide(c))
            {
                g.RowHeight = MasterRowHeight;
                g.Title = new Band(18f, 6f, c - 18f - 160f, 28f);
                g.Hint = new Band(18f, 34f, c - 18f - 160f, 26f);
                return g;
            }
            bool veryNarrow = c < 400f;
            g.RowHeight = veryNarrow ? 96f : 80f;
            g.SwitchW = veryNarrow ? 80f : 96f;
            g.SwitchH = 38f;
            g.SwitchRight = 12f;
            float textW = c - 14f - (g.SwitchW + g.SwitchRight + 10f);
            g.Title = new Band(14f, 6f, textW, 26f);
            g.Hint = new Band(14f, 34f, textW, g.RowHeight - 40f);   // up to 2 (80) / 3 (96) wrapped lines
            g.TitleFont = MasterTitleFontNarrow;
            g.HintFont = MasterHintFontNarrow;
            g.WrapHint = true;
            return g;
        }

        // ---------------------------------------------------------------- misc

        /// <summary>Preset buttons per row: 3 on the wide window, 2 when narrow ("Custom (Off-road)" must fit).</summary>
        public static int PresetsPerRow(float c)
        {
            return Wide(c) ? 3 : 2;
        }

        /// <summary>Preset button font: 19 wide, 17 narrow, 14 under 360 px of content (so "Custom (Off-road)" still fits half the row).</summary>
        public static int PresetFont(float c)
        {
            return Wide(c) ? 19 : c >= 360f ? 17 : 14;
        }

        /// <summary>Wrapped note height: the wide height, or more lines when narrow.</summary>
        public static float NoteHeight(float c, float wideHeight)
        {
            return Wide(c) ? wideHeight : Mathf.Round(wideHeight * 1.5f);
        }

        /// <summary>
        /// Tab label font: up to 15 px (0.6.x 14), but every (bold) name must fit its tab with a 2 px
        /// margin. A fixed 15 overflowed "Suspension" at the default 460 px window.
        /// </summary>
        public static int TabFont(float windowWidth, string[] names)
        {
            float w = TabWidth(windowWidth) - 2f;
            int font = 15;
            for (int i = 0; i < names.Length; i++)
            {
                int f = FitFont(names[i], 15, w, true, 9);
                if (f < font)
                {
                    font = f;
                }
            }
            return font;
        }

        /// <summary>Digit hotkeys while the panel is open: 1..9 -> tabs 0..8, 0 -> tab 10; else -1.</summary>
        public static int TabForDigit(int digit)
        {
            if (digit == 0)
            {
                return 10;
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
