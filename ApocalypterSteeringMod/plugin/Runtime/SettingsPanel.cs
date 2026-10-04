using System;
using System.Collections.Generic;
using ApocalypterSteeringMod.Game;
using ApocalypterSteeringMod.Persistence;
using ApocalypterSteeringMod.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// The tuning panel (0.6.0): a window docked to the right edge, full height, so the game
    /// stays visible (and drivable) beside it. Ten tabs in a two-row strip: Steering,
    /// Suspension, Aero, Brakes, Grip, Drivetrain, Assists, Alignment, Gearbox, Panel. Every
    /// category follows the same recipe: master ON/OFF switch, preset buttons, labelled
    /// sliders with hints, live values, "changed" highlight and per-slider Reset. Moving any
    /// slider on a built-in preset copies it into "Custom (Base)" so presets stay intact.
    /// Everything applies live and is saved when the panel closes.
    ///
    /// Layout: every size comes from PanelLayout as a function of the window width and is
    /// re-applied by Relayout(width) to stored RectTransforms, so width/scale/alpha changes
    /// apply live without a rebuild (and without touching the blocker state).
    /// </summary>
    public sealed class SettingsPanel
    {
        private const float Dimmed = 0.35f;

        public const int TabCount = 10;
        public static readonly string[] TabNames =
        {
            "Steering", "Suspension", "Aero", "Brakes", "Grip", "Drivetrain", "Assists", "Alignment", "Gearbox", "Panel"
        };
        // Preset book behind each tab (the Panel tab has none).
        private static readonly PresetCategory?[] TabCategory =
        {
            PresetCategory.Steering, PresetCategory.Suspension, PresetCategory.Aero, PresetCategory.Brakes,
            PresetCategory.Grip, PresetCategory.Drivetrain, PresetCategory.Assists, PresetCategory.Alignment,
            PresetCategory.Gearbox, null
        };
        private static readonly string[] DiffModeLabels = { "Stock", "Open", "Locked", "LSD" };
        private static readonly string[] ModeLabels = { "Stock", "Manual", "Automatic" };
        private static readonly string[] CornerLabels = { "Top left", "Top right", "Bottom left", "Bottom right" };

        public GameObject Root;

        private readonly VehicleTuner _tuner;
        private readonly Action _requestClose;
        private readonly Action _onModeChanged;
        private readonly List<Action> _refreshers = new List<Action>();
        private int _targetNameIndex;   // selected-vehicle cycle position (targeting)
        private readonly List<Action<float>> _relayouts = new List<Action<float>>();   // arg: content width
        private bool _suppress;

        private int _tab;
        private readonly Image[] _tabBg = new Image[TabCount];
        private readonly Image[] _tabUnderline = new Image[TabCount];
        private readonly RectTransform[] _tabRt = new RectTransform[TabCount];
        private readonly Text[] _tabLabel = new Text[TabCount];
        private readonly GameObject[] _pages = new GameObject[TabCount];
        private readonly RectTransform[] _pageHosts = new RectTransform[TabCount];
        private readonly ScrollRect[] _scrolls = new ScrollRect[TabCount];

        // Window chrome (relayout targets).
        private CanvasScaler _scaler;
        private RectTransform _win;
        private CanvasGroup _winGroup;
        private GameObject _dim;
        private RectTransform _titleRt, _subtitleRt, _tabBar, _footer, _footerLine, _statusRt;
        private Text _subtitle, _status;
        private RectTransform _copyRt, _pasteRt, _allOffRt, _resetRt, _doneRt;
        private Button _copyButton, _pasteButton, _allOffButton;
        private Text _allOffLabel;
        private float _appliedWidth = -1f, _appliedScale = -1f;
        private int _appliedScreenW = -1, _appliedScreenH = -1;

        private Button _resetButton;
        private Text _resetLabel;
        private float _resetArmedUntil = -1f;
        private float _allOffArmedUntil = -1f;
        private float _nextStatusRefresh;
        private Action[] _resetActions;
        private string[] _resetLabels;

        private int _selectedGear = -1;   // Gearbox tab: bar/slider highlighted

        public static SettingsPanel Create(VehicleTuner tuner, Action requestClose, Action onModeChanged = null)
        {
            return new SettingsPanel(tuner, requestClose, onModeChanged);
        }

        private SettingsPanel(VehicleTuner tuner, Action requestClose, Action onModeChanged)
        {
            _tuner = tuner;
            _requestClose = requestClose;
            _onModeChanged = onModeChanged;
            _resetActions = new Action[]
            {
                () => SteeringSettings.ResetAll(),
                () => { SuspensionSettings.ResetAll(); _tuner.ReapplyNow(); },
                () => { AeroSettings.ResetAll(); _tuner.ReapplyNow(); },
                () => { BrakesSettings.ResetAll(); _tuner.ReapplyNow(); },
                () => { GripSettings.ResetAll(); _tuner.ReapplyNow(); },
                () => { DrivetrainSettings.ResetAll(); _tuner.ReapplyNow(); },
                () => { AssistsSettings.ResetAll(); _tuner.ReapplyNow(); },
                () => { AlignmentSettings.ResetAll(); _tuner.ReapplyNow(); },
                () => { GearboxSettings.ResetAll(); _tuner.ReapplyNow(); },
                () =>
                {
                    UiSettings.ResetPanel();
                    UiSettings.TelemetryEnabled = true;
                    UiSettings.TelemetryScale = 1f;
                    UiSettings.TelemetryPosition = TelemetryCorner.BottomLeft;
                    ModeChanged();
                }
            };
            _resetLabels = new[]
            {
                "Reset all steering", "Reset all suspension", "Reset all aero", "Reset all brakes",
                "Reset all grip", "Reset all drivetrain", "Reset all assists", "Reset all alignment",
                "Reset all gearbox", "Reset panel settings"
            };
            Build();
            ApplyDisplaySettings();
            ShowTab(UiSettings.ClampTab(UiSettings.LastTab));
            Refresh();
            Root.SetActive(false);
        }

        // ================================================================ skeleton

        private void Build()
        {
            var canvasGo = new GameObject("ApocalypterSettingsCanvas", typeof(RectTransform));
            canvasGo.layer = 5;
            canvasGo.transform.SetParent(_tuner.transform, false);
            Root = canvasGo;

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;   // above every game menu canvas
            _scaler = canvasGo.AddComponent<CanvasScaler>();
            // Mod-owned interface size (the game has no UI-scale setting): at PanelScale 1 this
            // renders exactly like 0.5.0's height-matched 1920x1080 ScaleWithScreenSize scaler.
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            canvasGo.AddComponent<GraphicRaycaster>();

            RectTransform rootRt = (RectTransform)canvasGo.transform;

            // Dim layer — Freeze ON only (the 0.5.0 modal: blocks clicks to the game, a click
            // closes). Live mode has NO click catcher: only the window raycasts, the rest of the
            // screen belongs to the game (accepted risk: opened from the pause menu, its buttons
            // stay clickable beside the panel).
            RectTransform dim = UiKit.Make("Dim", rootRt);
            UiKit.Fill(dim);
            Image dimImg = UiKit.Paint(dim, new Color(0f, 0f, 0f, 0.55f), true);
            Button dimBtn = dim.gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.targetGraphic = dimImg;
            dimBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            dimBtn.onClick.AddListener(() => _requestClose());
            _dim = dim.gameObject;

            _win = UiKit.Make("Window", rootRt);
            _win.anchorMin = new Vector2(1f, 0.02f);
            _win.anchorMax = new Vector2(1f, 0.98f);
            _win.pivot = new Vector2(1f, 0.5f);
            _win.anchoredPosition = new Vector2(-10f, 0f);
            _win.sizeDelta = new Vector2(Limits.PanelWidthDefault, 0f);
            UiKit.Paint(_win, UiKit.WindowBg, true);   // eats clicks inside the window only
            _winGroup = _win.gameObject.AddComponent<CanvasGroup>();

            BuildHeader(_win);
            BuildTabs(_win);
            BuildFooter(_win);

            _pages[0] = BuildPage(_win, "SteeringPage", 0, BuildSteering);
            _pages[1] = BuildPage(_win, "SuspensionPage", 1, BuildSuspension);
            _pages[2] = BuildPage(_win, "AeroPage", 2, BuildAero);
            _pages[3] = BuildPage(_win, "BrakesPage", 3, BuildBrakes);
            _pages[4] = BuildPage(_win, "GripPage", 4, BuildGrip);
            _pages[5] = BuildPage(_win, "DrivetrainPage", 5, BuildDrivetrain);
            _pages[6] = BuildPage(_win, "AssistsPage", 6, BuildAssists);
            _pages[7] = BuildPage(_win, "AlignmentPage", 7, BuildAlignment);
            _pages[8] = BuildPage(_win, "GearboxPage", 8, BuildGearbox);
            _pages[9] = BuildPage(_win, "PanelPage", 9, BuildPanelTab);
        }

        private void BuildHeader(RectTransform win)
        {
            _titleRt = UiKit.Make("Title", win);
            UiKit.Label(_titleRt, "Vehicle Tuning", 26, UiKit.TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);

            _subtitleRt = UiKit.Make("Subtitle", win);
            _subtitle = UiKit.Label(_subtitleRt, "", 13, UiKit.TextMuted, TextAnchor.UpperLeft, FontStyle.Normal, true);
            _refreshers.Add(() =>
            {
                _subtitle.text = UiSettings.FreezeWhileOpen
                    ? "Changes apply instantly and are saved when you close. " + ModConfig.ToggleKeyString
                      + ", Esc or a click outside closes. Keys 1-0 switch tabs."
                    : "Changes apply instantly; you can keep driving. Saved when you close. " + ModConfig.ToggleKeyString
                      + " or Esc closes. Keys 1-0 switch tabs.";
            });

            Button close = UiKit.MakeButton(win, "Close", "X", UiKit.RowBase, 18, () => _requestClose(), out Text _);
            RectTransform crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(PanelLayout.CloseSize, PanelLayout.CloseSize);
            crt.anchoredPosition = new Vector2(-12f, -12f);
        }

        private void BuildTabs(RectTransform win)
        {
            _tabBar = UiKit.Make("Tabs", win);
            for (int i = 0; i < TabCount; i++)
            {
                int index = i;
                Button b = UiKit.MakeButton(_tabBar, "Tab_" + TabNames[i], TabNames[i], UiKit.RowBase, 14, () => ShowTab(index), out _tabLabel[i]);
                _tabRt[i] = (RectTransform)b.transform;
                _tabBg[i] = (Image)b.targetGraphic;
                RectTransform under = UiKit.Bottom(UiKit.Make("Underline", _tabRt[i]), 0f, 3f);
                _tabUnderline[i] = UiKit.Paint(under, UiKit.Accent, false);
            }
        }

        private GameObject BuildPage(RectTransform win, string name, int index, Action<RectTransform> fill)
        {
            RectTransform host = UiKit.Make(name, win);
            _pageHosts[index] = host;
            RectTransform content = UiKit.MakeScroll(host, "Scroll", out ScrollRect scroll);
            _scrolls[index] = scroll;
            fill(content);
            return host.gameObject;
        }

        private void BuildFooter(RectTransform win)
        {
            _footer = UiKit.Bottom(UiKit.Make("Footer", win), 0f, PanelLayout.FooterHeight);
            _footerLine = UiKit.Make("Line", _footer);
            UiKit.Paint(_footerLine, UiKit.Divider, false);

            _statusRt = UiKit.Make("Status", _footer);
            _status = UiKit.Label(_statusRt, "", 13, UiKit.TextMuted, TextAnchor.MiddleLeft);

            _copyButton = UiKit.MakeButton(_footer, "CopyPreset", "Copy preset", UiKit.RowBase, 14, OnCopyClicked, out Text _);
            _copyRt = (RectTransform)_copyButton.transform;
            _pasteButton = UiKit.MakeButton(_footer, "PastePreset", "Paste preset", UiKit.RowBase, 14, OnPasteClicked, out Text _);
            _pasteRt = (RectTransform)_pasteButton.transform;
            _allOffButton = UiKit.MakeButton(_footer, "AllOff", "", UiKit.RowBase, 14, OnAllOffClicked, out _allOffLabel);
            _allOffRt = (RectTransform)_allOffButton.transform;

            _resetButton = UiKit.MakeButton(_footer, "ResetTab", "", UiKit.RowBase, 15, OnResetClicked, out _resetLabel);
            _resetRt = (RectTransform)_resetButton.transform;
            Button done = UiKit.MakeButton(_footer, "Done", "Done", UiKit.Accent, 18, () => _requestClose(), out Text _);
            _doneRt = (RectTransform)done.transform;
        }

        // ================================================================ display settings + relayout

        /// <summary>
        /// Apply scale, width, transparency and the freeze dim (live, no rebuild). Called on build,
        /// on every Panel-tab change, on external config changes and once a second while open
        /// (screen-resolution changes).
        /// </summary>
        public void ApplyDisplaySettings()
        {
            if (Root == null)
            {
                return;
            }
            int sw = Screen.width, sh = Screen.height;
            float scale = PanelLayout.ScaleFactor(sh, UiSettings.PanelScale);
            if (scale != _appliedScale)
            {
                _scaler.scaleFactor = scale;
                _appliedScale = scale;
            }
            float width = PanelLayout.EffectiveWidth(UiSettings.PanelWidth, sw, sh, UiSettings.PanelScale);
            if (width != _appliedWidth || sw != _appliedScreenW || sh != _appliedScreenH)
            {
                _appliedScreenW = sw;
                _appliedScreenH = sh;
                Relayout(width);
            }
            _winGroup.alpha = Mathf.Clamp(UiSettings.PanelAlpha, Limits.PanelAlphaMin, Limits.PanelAlphaMax);
            bool dimOn = UiSettings.FreezeWhileOpen;
            if (_dim.activeSelf != dimOn)
            {
                _dim.SetActive(dimOn);
            }
        }

        /// <summary>Re-apply every width-dependent rect for a window <paramref name="width"/> px wide.</summary>
        public void Relayout(float width)
        {
            _appliedWidth = width;
            float pad = PanelLayout.Pad(width);
            _win.sizeDelta = new Vector2(width, 0f);

            UiKit.Top(_titleRt, PanelLayout.TitleTop, PanelLayout.TitleHeight, pad, PanelLayout.CloseSize + 24f);
            UiKit.Top(_subtitleRt, PanelLayout.SubtitleTop, PanelLayout.SubtitleHeight, pad, PanelLayout.CloseSize + 24f);

            // Two-row tab strip (5 + 5).
            UiKit.Top(_tabBar, PanelLayout.TabsTop, 2f * PanelLayout.TabRowHeight + PanelLayout.TabRowGap, pad, pad);
            float barW = width - 2f * pad;
            const float gap = 4f;
            float tabW = (barW - (PanelLayout.TabsPerRow - 1) * gap) / PanelLayout.TabsPerRow;
            int font = PanelLayout.TabFont(width);
            for (int i = 0; i < TabCount; i++)
            {
                int row = i / PanelLayout.TabsPerRow, col = i % PanelLayout.TabsPerRow;
                PanelLayout.Apply(_tabRt[i], new PanelLayout.Band(col * (tabW + gap),
                    row * (PanelLayout.TabRowHeight + PanelLayout.TabRowGap), tabW, PanelLayout.TabRowHeight));
                _tabLabel[i].fontSize = font;
            }

            // Footer: status line, Copy | Paste | All off, Reset tab | Done.
            UiKit.Top(_footerLine, 0f, 1f, pad, pad);
            float fw = width - 2f * pad;
            float top = PanelLayout.FooterHeight;
            PanelLayout.Apply(_statusRt, new PanelLayout.Band(pad, top - PanelLayout.FooterStatusBottom - PanelLayout.FooterStatusHeight, fw, PanelLayout.FooterStatusHeight));
            float r1Top = top - PanelLayout.FooterRow1Bottom - PanelLayout.FooterRow1Height;
            float third = (fw - 2f * 8f) / 3f;
            PanelLayout.Apply(_copyRt, new PanelLayout.Band(pad, r1Top, third, PanelLayout.FooterRow1Height));
            PanelLayout.Apply(_pasteRt, new PanelLayout.Band(pad + third + 8f, r1Top, third, PanelLayout.FooterRow1Height));
            PanelLayout.Apply(_allOffRt, new PanelLayout.Band(pad + 2f * (third + 8f), r1Top, third, PanelLayout.FooterRow1Height));
            float r2Top = top - PanelLayout.FooterRow2Bottom - PanelLayout.FooterRow2Height;
            float doneW = Mathf.Min(140f, fw * 0.3f);
            PanelLayout.Apply(_resetRt, new PanelLayout.Band(pad, r2Top, fw - doneW - 8f, PanelLayout.FooterRow2Height));
            PanelLayout.Apply(_doneRt, new PanelLayout.Band(pad + fw - doneW, r2Top, doneW, PanelLayout.FooterRow2Height));

            for (int i = 0; i < TabCount; i++)
            {
                UiKit.Place(_pageHosts[i], 0f, 0f, 1f, 1f, pad, PanelLayout.PageBottom, pad - 10f, PanelLayout.PageTop);
            }

            float c = PanelLayout.ContentWidth(width);
            for (int i = 0; i < _relayouts.Count; i++)
            {
                _relayouts[i](c);
            }
        }

        public void ShowTab(int index)
        {
            index = UiSettings.ClampTab(index);
            _tab = index;
            UiSettings.LastTab = index;
            for (int i = 0; i < _pages.Length; i++)
            {
                bool on = i == index;
                _pages[i].SetActive(on);
                _tabUnderline[i].enabled = on;
                _tabBg[i].color = on ? UiKit.ChipBase : UiKit.RowNormal;
            }
            _scrolls[index].verticalNormalizedPosition = 1f;
            bool hasBook = TabCategory[index] != null;
            _copyButton.interactable = hasBook;
            _pasteButton.interactable = hasBook;
            _status.text = "";
            DisarmReset();
        }

        // ================================================================ row builders

        private static RectTransform AddBlock(RectTransform content, string name, float height, bool background, out LayoutElement le)
        {
            RectTransform rt = UiKit.Make(name, content);
            le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            if (background)
            {
                UiKit.Paint(rt, UiKit.RowNormal, false);
            }
            return rt;
        }

        private static void SetHeight(LayoutElement le, float h)
        {
            le.preferredHeight = h;
            le.minHeight = h;
        }

        private static void AddSectionTitle(RectTransform content, string text)
        {
            RectTransform rt = AddBlock(content, "Section_" + text, 38f, false, out LayoutElement _);
            RectTransform t = UiKit.Place(UiKit.Make("T", rt), 0f, 0f, 1f, 1f, 2f, 4f, 0f, 0f);
            UiKit.Label(t, text.ToUpperInvariant(), 14, UiKit.TextMuted, TextAnchor.LowerLeft, FontStyle.Bold);
        }

        private Text AddNote(RectTransform content, string name, float height, int size = 16)
        {
            RectTransform block = AddBlock(content, name, height, false, out LayoutElement le);
            RectTransform t = UiKit.Place(UiKit.Make("T", block), 0f, 0f, 1f, 1f, 4f, 0f, 4f, 0f);
            Text text = UiKit.Label(t, "", size, UiKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            _relayouts.Add(c =>
            {
                SetHeight(le, PanelLayout.NoteHeight(c, height));
                text.fontSize = PanelLayout.Wide(c) ? size : Mathf.Min(size, 14);
            });
            return text;
        }

        /// <summary>Sub-list whose rows can be dimmed/disabled together.</summary>
        private static CanvasGroup AddGroup(RectTransform content, string name, out RectTransform inner)
        {
            inner = UiKit.Make(name, content);
            var vlg = inner.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            return inner.gameObject.AddComponent<CanvasGroup>();
        }

        private void BindGroup(CanvasGroup group, Func<bool> enabled)
        {
            _refreshers.Add(() =>
            {
                bool on = enabled();
                group.interactable = on;
                group.alpha = on ? 1f : Dimmed;
            });
        }

        /// <summary>A pill switch showing ON (green) / OFF (grey); returns its RectTransform for relayout.</summary>
        private RectTransform AddSwitch(RectTransform row, int fontSize, Func<bool> get, Action<bool> set)
        {
            Text label;
            Button pill = UiKit.MakeButton(row, "Switch", "", UiKit.SwitchOff, fontSize, () =>
            {
                if (_suppress)
                {
                    return;
                }
                set(!get());
                Refresh();
            }, out label);
            Image img = (Image)pill.targetGraphic;
            _refreshers.Add(() =>
            {
                bool on = get();
                img.color = on ? UiKit.Good : UiKit.SwitchOff;
                label.text = on ? "ON" : "OFF";
            });
            return (RectTransform)pill.transform;
        }

        private void LayoutSwitchRow(LayoutElement le, RectTransform title, Text titleText, RectTransform hint, Text hintText,
            RectTransform pill, PanelLayout.SwitchRowGeom g)
        {
            SetHeight(le, g.RowHeight);
            PanelLayout.Apply(title, g.Title);
            PanelLayout.Apply(hint, g.Hint);
            titleText.fontSize = g.TitleFont;
            hintText.fontSize = g.HintFont;
            hintText.horizontalOverflow = g.WrapHint ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            hintText.alignment = g.WrapHint ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            UiKit.RightBox(pill, g.SwitchW, g.SwitchH, g.SwitchRight);
        }

        /// <summary>The big ON/OFF row at the top of each tab.</summary>
        private void AddMasterSwitch(RectTransform content, string title, string hint, Func<bool> get, Action<bool> set)
        {
            RectTransform row = AddBlock(content, "Master", 76f, true, out LayoutElement le);
            RectTransform t = UiKit.Make("Title", row);
            Text tt = UiKit.Label(t, title, 21, UiKit.TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform h = UiKit.Make("Hint", row);
            Text ht = UiKit.Label(h, hint, 15, UiKit.TextMuted, TextAnchor.MiddleLeft);
            RectTransform pill = AddSwitch(row, 18, get, set);
            _relayouts.Add(c => LayoutSwitchRow(le, t, tt, h, ht, pill, PanelLayout.MasterRow(c)));
        }

        /// <summary>Secondary on/off option with a one-line explanation.</summary>
        private Text AddOption(RectTransform content, string title, string hint, Func<bool> get, Action<bool> set,
            Func<bool> enabled = null)
        {
            RectTransform row = AddBlock(content, "Opt_" + title, 58f, true, out LayoutElement le);
            CanvasGroup group = row.gameObject.AddComponent<CanvasGroup>();
            RectTransform t = UiKit.Make("Title", row);
            Text tt = UiKit.Label(t, title, 17, UiKit.TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform h = UiKit.Make("Hint", row);
            Text hintText = UiKit.Label(h, hint, 14, UiKit.TextMuted, TextAnchor.MiddleLeft);
            RectTransform pill = AddSwitch(row, 15, get, set);
            _relayouts.Add(c => LayoutSwitchRow(le, t, tt, h, hintText, pill, PanelLayout.OptionRow(c)));
            if (enabled != null)
            {
                BindGroup(group, enabled);
            }
            return hintText;
        }

        /// <summary>Grid of preset buttons; the active one is filled with the accent colour.</summary>
        private void AddPresetButtons(RectTransform content, string name, int count, int perRowWide, int perRowNarrow,
            Func<int, string> label, Func<int> active, Action<int> select)
        {
            const float h = 44f, gap = 8f;
            RectTransform block = AddBlock(content, name, h, false, out LayoutElement le);

            var images = new Image[count];
            var texts = new Text[count];
            var rts = new RectTransform[count];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                Button b = UiKit.MakeButton(block, "Preset" + i, "", UiKit.ChipBase, 17, () =>
                {
                    if (_suppress)
                    {
                        return;
                    }
                    select(index);
                    Refresh();
                }, out texts[i]);
                rts[i] = (RectTransform)b.transform;
                images[i] = (Image)b.targetGraphic;
            }

            _relayouts.Add(c =>
            {
                int perRow = Mathf.Max(1, PanelLayout.Wide(c) ? perRowWide : Mathf.Min(perRowWide, perRowNarrow));
                int rows = (count + perRow - 1) / perRow;
                SetHeight(le, rows * h + (rows - 1) * gap);
                int font = PanelLayout.PresetFont(c);
                for (int i = 0; i < count; i++)
                {
                    int r = i / perRow, col = i % perRow;
                    float top = r * (h + gap);
                    RectTransform rt = rts[i];
                    rt.anchorMin = new Vector2((float)col / perRow, 1f);
                    rt.anchorMax = new Vector2((float)(col + 1) / perRow, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.offsetMin = new Vector2(col == 0 ? 0f : gap / 2f, -(top + h));
                    rt.offsetMax = new Vector2(col == perRow - 1 ? 0f : -gap / 2f, -top);
                    texts[i].fontSize = font;
                }
            });

            _refreshers.Add(() =>
            {
                int a = active();
                for (int i = 0; i < count; i++)
                {
                    images[i].color = i == a ? UiKit.Accent : UiKit.ChipBase;
                    texts[i].text = label(i);
                }
            });
        }

        /// <summary>
        /// Name and plain-language hint, slider, value (and an optional absolute readout below
        /// it) and Reset. Wide: one line (0.5.0 geometry); narrow: stacked. The value turns
        /// blue when it differs from the reference; Reset puts it back.
        /// </summary>
        private GameObject AddSlider(RectTransform content, string title, string hint, float min, float max,
            Func<float> get, Action<float> set, Func<float> reference, Func<float, string> format,
            Func<bool> enabled = null, Func<float, string> readout = null, bool wholeNumbers = false)
        {
            RectTransform row = AddBlock(content, "Row_" + title, 58f, true, out LayoutElement le);
            CanvasGroup group = row.gameObject.AddComponent<CanvasGroup>();

            RectTransform t = UiKit.Make("Title", row);
            Text titleText = UiKit.Label(t, title, 17, UiKit.TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform h = UiKit.Make("Hint", row);
            Text hintText = UiKit.Label(h, hint, 13, UiKit.TextMuted, TextAnchor.MiddleLeft);

            Slider slider = UiKit.MakeSlider(row, "Slider");
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            slider.onValueChanged.AddListener(v =>
            {
                if (_suppress)
                {
                    return;
                }
                set(v);
                Refresh();
            });

            RectTransform valRt = UiKit.Make("Value", row);
            Text value;
            Text valueReadout = null;
            if (readout != null)
            {
                RectTransform factorRt = UiKit.Place(UiKit.Make("Factor", valRt), 0f, 0.5f, 1f, 1f, 0f, 0f, 0f, 0f);
                value = UiKit.Label(factorRt, "", 16, UiKit.TextMain, TextAnchor.MiddleCenter, FontStyle.Bold);
                RectTransform readoutRt = UiKit.Place(UiKit.Make("Readout", valRt), 0f, 0f, 1f, 0.5f, 0f, 0f, 0f, 0f);
                valueReadout = UiKit.Label(readoutRt, "", 12, UiKit.TextMuted, TextAnchor.MiddleCenter);
            }
            else
            {
                value = UiKit.Label(valRt, "", 17, UiKit.TextMain, TextAnchor.MiddleCenter, FontStyle.Bold);
            }

            Text resetLabel;
            Button reset = UiKit.MakeButton(row, "Reset", "Reset", UiKit.ChipBase, 13, () =>
            {
                if (_suppress)
                {
                    return;
                }
                set(reference());
                Refresh();
            }, out resetLabel);
            RectTransform resetRt = (RectTransform)reset.transform;

            bool hasReadout = readout != null;
            _relayouts.Add(c =>
            {
                PanelLayout.SliderGeom g = PanelLayout.SliderRow(c, hasReadout);
                SetHeight(le, g.RowHeight);
                PanelLayout.Apply(t, g.Title);
                PanelLayout.Apply(h, g.Hint);
                PanelLayout.Apply((RectTransform)slider.transform, g.Slider);
                PanelLayout.Apply(valRt, g.Value);
                PanelLayout.Apply(resetRt, g.Reset);
                titleText.fontSize = g.TitleFont;
                hintText.fontSize = g.HintFont;
            });

            _refreshers.Add(() =>
            {
                bool on = enabled == null || enabled();
                group.interactable = on;
                group.alpha = on ? 1f : Dimmed;

                float v = get();
                float r = reference();
                slider.value = v;
                bool changed = Mathf.Abs(v - r) > 0.005f * Mathf.Max(1f, Mathf.Abs(r));
                value.text = format(v);
                value.color = changed ? UiKit.Accent : UiKit.TextMain;
                if (valueReadout != null)
                {
                    valueReadout.text = readout(v);
                }
                reset.interactable = changed;
                resetLabel.color = changed ? UiKit.TextMain : UiKit.TextMuted;
            });
            return row.gameObject;
        }

        /// <summary>Show/hide a row from a predicate on every refresh.</summary>
        private void BindVisible(GameObject row, Func<bool> visible)
        {
            _refreshers.Add(() =>
            {
                bool on = visible();
                if (row.activeSelf != on)
                {
                    row.SetActive(on);
                }
            });
        }

        // ================================================================ shared semantics

        // Picks the singular/plural template for the per-category vehicle status lines.
        private static string VehicleStatus(int n, string oneFmt, string manyFmt)
        {
            return string.Format(n == 1 ? oneFmt : manyFmt, n);
        }

        private static string PresetButtonLabel<T>(PresetBook<T> book, T p) where T : class, ITunablePreset
        {
            if (p == book.Custom)
            {
                T b = book.FindBuiltIn(p.BasedOn);
                return b != null ? string.Format(UiStrings.CustomPresetFmt, b.Label) : "Custom";
            }
            return p.Label;
        }

        private static string CustomDescription<T>(PresetBook<T> book) where T : class, ITunablePreset
        {
            T b = book.FindBuiltIn(book.Custom.BasedOn);
            return b != null
                ? string.Format(UiStrings.CustomBasedOnDescFmt, b.Label, b.Label)
                : UiStrings.CustomPlainDesc;
        }

        /// <summary>An absolute readout, or nothing while no vehicle supplies a baseline.</summary>
        private static string Absolute(float factor, float baseline, Func<float, string> unit)
        {
            return baseline > 0f ? unit(factor * baseline) : "";
        }

        private void Edit<T>(PresetBook<T> book, Action<T> edit) where T : class, ITunablePreset
        {
            T p = book.BeginEdit();
            if (p == null)
            {
                return;
            }
            edit(p);
            _tuner.ApplyLive();
        }

        private void EditSuspension(Action<SuspensionPreset> edit) { Edit(SuspensionSettings.Book, edit); }
        private void EditAero(Action<AeroPreset> edit) { Edit(AeroSettings.Book, edit); }
        private void EditBrakes(Action<BrakesPreset> edit) { Edit(BrakesSettings.Book, edit); }
        private void EditGrip(Action<GripPreset> edit) { Edit(GripSettings.Book, edit); }
        private void EditDrivetrain(Action<DrivetrainPreset> edit) { Edit(DrivetrainSettings.Book, edit); }
        private void EditAssists(Action<AssistsPreset> edit) { Edit(AssistsSettings.Book, edit); }
        private void EditAlignment(Action<AlignmentPreset> edit) { Edit(AlignmentSettings.Book, edit); }
        private void EditGearbox(Action<GearboxPreset> edit) { Edit(GearboxSettings.Book, edit); }

        private void ModeChanged()
        {
            ApplyDisplaySettings();
            if (_onModeChanged != null)
            {
                _onModeChanged();
            }
        }

        // ================================================================ steering tab

        /// <summary>Values the sliders display: the active preset, or the defaults while Vanilla is active.</summary>
        private static SteeringPreset Shown
        {
            get
            {
                SteeringPreset a = SteeringSettings.ActivePreset ?? SteeringPreset.Custom;
                return a.IsVanilla ? SteeringPreset.Defaults : a;
            }
        }

        private static bool IsVanilla
        {
            get { return SteeringSettings.ActivePreset != null && SteeringSettings.ActivePreset.IsVanilla; }
        }

        private static void EditSteering(Action<SteeringPreset> edit)
        {
            SteeringPreset p = SteeringSettings.BeginEdit();
            if (p != null)
            {
                edit(p);
            }
        }

        /// <summary>
        /// Curve-editor edit entry: forks the active preset into Custom (copying
        /// its curves), applies the mutation to the chosen curve and returns the
        /// live curve, or null when the active preset cannot be edited (Vanilla).
        /// </summary>
        private EditableCurve EditCurve(Func<SteeringPreset, EditableCurve> pick, Action<EditableCurve> mutate)
        {
            SteeringPreset p = SteeringSettings.BeginEdit();
            if (p == null)
            {
                return null;
            }
            mutate(pick(p));
            Refresh();
            return pick(p);
        }

        private void BuildSteering(RectTransform content)
        {
            AddMasterSwitch(content, "Steering tuning", "OFF = the game's original steering, exactly as shipped.",
                () => SteeringSettings.Enabled, v => SteeringSettings.Enabled = v);

            CanvasGroup body = AddGroup(content, "Body", out RectTransform c);
            BindGroup(body, () => SteeringSettings.Enabled);

            AddSectionTitle(c, "Preset");
            SteeringPreset[] presets = SteeringPreset.Presets;
            AddPresetButtons(c, "Presets", presets.Length, 3, 2,
                i => PresetButtonLabel(SteeringSettings.Book, presets[i]),
                () => Array.IndexOf(presets, SteeringSettings.ActivePreset),
                i => SteeringSettings.Select(presets[i]));

            Text desc = AddNote(c, "Description", 50f);
            _refreshers.Add(() =>
            {
                SteeringPreset a = SteeringSettings.ActivePreset ?? SteeringPreset.Custom;
                if (a == SteeringPreset.Custom)
                {
                    desc.text = CustomDescription(SteeringSettings.Book);
                }
                else
                {
                    desc.text = a.IsVanilla ? a.Description : string.Format(UiStrings.PresetEditNoteFmt, a.Description);
                }
            });

            CanvasGroup tuning = AddGroup(c, "Tuning", out RectTransform t);
            BindGroup(tuning, () => !IsVanilla);

            AddSectionTitle(t, "Response");
            AddSlider(t, "Steering speed", "How fast the wheels turn toward your input",
                Limits.RateMin, Limits.RateMax,
                () => Shown.RateMultiplier, v => EditSteering(p => p.RateMultiplier = v),
                () => SteeringSettings.Reference().RateMultiplier, UiStrings.Times);
            AddSlider(t, "Smoothing", "Higher = softer, lazier response",
                Limits.SmoothMin, Limits.SmoothMax,
                () => Shown.SmoothingScale, v => EditSteering(p => p.SmoothingScale = v),
                () => SteeringSettings.Reference().SmoothingScale, UiStrings.Times);
            AddOption(t, "Use the vehicle's input curve", "OFF = pow curve with the exponent below",
                () => !Shown.LinearityOverride, v => EditSteering(p => p.LinearityOverride = !v));
            AddSlider(t, "Centre sensitivity", "Low = twitchy at centre, high = gentle",
                Limits.LinExpMin, Limits.LinExpMax,
                () => Shown.LinearityExponent, v => EditSteering(p => p.LinearityExponent = v),
                () => SteeringSettings.Reference().LinearityExponent, v => v.ToString("0.00"),
                () => Shown.LinearityOverride);

            AddOption(t, "Use the vehicle's own curve", "OFF = use the custom lock curve below",
                () => Shown.UseVehicleCurve, v => EditSteering(p => p.UseVehicleCurve = v));
            CurveEditor lockEditor = CurveEditor.Create(t,
                "Lock at speed",
                "How much steering you keep at speed. Left edge = stopped, right edge = 180 km/h and above. Click = add a point, drag = move, double-click = remove.",
                () => Shown.LockCurve,
                () => SteeringSettings.Reference().LockCurve,
                mutate => EditCurve(pp => pp.LockCurve, mutate),
                () => SteeringSettings.Enabled && !IsVanilla);
            _refreshers.Add(lockEditor.Refresh);
            _relayouts.Add(lockEditor.Relayout);
            GameObject lockRow = lockEditor.Row;
            BindVisible(lockRow, () => !Shown.UseVehicleCurve);

            CurveEditor returnEditor = CurveEditor.Create(t,
                "Return to center",
                "How fast the wheel straightens after you let go. Flat at 1 = steers back as fast as it steers in. A lower line = lazier. Ramping up from 0 = holds the wheels while stopped, then straightens out as you drive. Flat at the left edge = vanilla low-speed handling.",
                () => Shown.ReturnCurve,
                () => SteeringSettings.Reference().ReturnCurve,
                mutate => EditCurve(pp => pp.ReturnCurve, mutate),
                () => SteeringSettings.Enabled && !IsVanilla);
            _refreshers.Add(returnEditor.Refresh);
            _relayouts.Add(returnEditor.Relayout);

            AddSectionTitle(t, "Grip and slides");
            AddOption(t, "Front slip clamp", "Stops the front tyres turning past their grip limit",
                () => Shown.TractionClampEnabled, v => EditSteering(p => p.TractionClampEnabled = v));
            AddSlider(t, "Slip window", "Higher = more steering before tyres slide",
                Limits.SlipMin, Limits.SlipMax,
                () => Shown.SlipAngleDeg, v => EditSteering(p => p.SlipAngleDeg = v),
                () => SteeringSettings.Reference().SlipAngleDeg, v => UiStrings.Deg(v),
                () => Shown.TractionClampEnabled);
            AddSlider(t, "Counter-steer speed", "Extra steering speed while catching a slide",
                Limits.OppLockMin, Limits.OppLockMax,
                () => Shown.OppositeLockBoost, v => EditSteering(p => p.OppositeLockBoost = v),
                () => SteeringSettings.Reference().OppositeLockBoost, UiStrings.Times);

            AddSectionTitle(c, "Game setting");
            Text gameHint = AddOption(c, "Follow game's steering speed", "",
                () => SteeringSettings.MatchGameSteeringSpeed, v => SteeringSettings.MatchGameSteeringSpeed = v,
                () => !IsVanilla);
            _refreshers.Add(() =>
            {
                gameHint.text = GameSettingsReader.Loaded
                    ? string.Format(UiStrings.GameSpeedHintFoundFmt, GameSettingsReader.Steeringspeed.ToString("0"))
                    : UiStrings.GameSpeedHintMissing;
            });
        }

        // ================================================================ suspension tab

        private void BuildSuspension(RectTransform content)
        {
            AddMasterSwitch(content, "Suspension tuning", "OFF = every vehicle keeps its original suspension.",
                () => SuspensionSettings.Enabled, v =>
                {
                    SuspensionSettings.Enabled = v;
                    _tuner.ReapplyNow();
                });

            CanvasGroup body = AddGroup(content, "Body", out RectTransform c);
            BindGroup(body, () => SuspensionSettings.Enabled);

            AddSectionTitle(c, "Preset");
            SuspensionPreset[] presets = SuspensionPreset.Presets;
            AddPresetButtons(c, "Presets", presets.Length, 3, 2,
                i => PresetButtonLabel(SuspensionSettings.Book, presets[i]),
                () => Array.IndexOf(presets, SuspensionSettings.ActivePreset),
                i =>
                {
                    SuspensionSettings.Book.Select(presets[i]);
                    _tuner.ApplyLive();
                });

            Text desc = AddNote(c, "Description", 50f);
            _refreshers.Add(() =>
            {
                SuspensionPreset p = SuspensionSettings.Shown;
                desc.text = p == SuspensionPreset.Custom
                    ? CustomDescription(SuspensionSettings.Book)
                    : p.Description + " Move any slider to customise it.";
            });

            AddSectionTitle(c, "Tuning");
            AddOption(c, "Separate front and rear", "Tune each axle on its own",
                () => SuspensionSettings.SplitFrontRear, v =>
                {
                    SuspensionSettings.SplitFrontRear = v;
                    // Linking only needs an edit (and a fork into Custom) when the
                    // axles actually differ; Stock/Comfort/Off-road stay selected.
                    if (!v && SuspensionSettings.AxlesDiffer())
                    {
                        SuspensionSettings.LinkRearToFront();
                        _tuner.ApplyLive();
                    }
                });

            AddAxleFactor(c, "Stiffness", "Higher = firmer ride, less body movement",
                p => p.SpringFront, (p, v) => p.SpringFront = v,
                p => p.SpringRear, (p, v) => p.SpringRear = v,
                VehicleTuner.Readout.SpringForce, UiStrings.Force);
            AddAxleFactor(c, "Ride height", "Suspension travel; higher sits taller",
                p => p.RideHeightFront, (p, v) => p.RideHeightFront = v,
                p => p.RideHeightRear, (p, v) => p.RideHeightRear = v,
                VehicleTuner.Readout.RideHeight, UiStrings.Length);
            AddAxleFactor(c, "Bump damping", "Resists compression over bumps",
                p => p.BumpFront, (p, v) => p.BumpFront = v,
                p => p.BumpRear, (p, v) => p.BumpRear = v,
                VehicleTuner.Readout.BumpRate, UiStrings.Rate);
            AddAxleFactor(c, "Rebound damping", "Stops the body bouncing back up",
                p => p.ReboundFront, (p, v) => p.ReboundFront = v,
                p => p.ReboundRear, (p, v) => p.ReboundRear = v,
                VehicleTuner.Readout.ReboundRate, UiStrings.Rate);
            AddAxleFactor(c, "Anti-roll bar", "Higher = flatter in corners",
                p => p.ArbFront, (p, v) => p.ArbFront = v,
                p => p.ArbRear, (p, v) => p.ArbRear = v,
                VehicleTuner.Readout.ArbForce, UiStrings.Force);

            Text status = AddNote(content, "Status", 44f, 14);
            _refreshers.Add(() =>
            {
                int n = _tuner.TargetedCount;
                status.text = !SuspensionSettings.Enabled
                    ? "Suspension tuning is off. Vehicles use their original setup."
                    : n == 0
                        ? "No vehicles found yet. Settings apply as soon as one spawns."
                        : VehicleStatus(n, UiStrings.AppliedOneArbFmt, UiStrings.AppliedManyArbFmt);
            });
        }

        /// <summary>
        /// One factor slider for both axles, or two ("front" / "rear") when split mode is on.
        /// Linked mode writes the same value to both. Readouts show the computed absolute
        /// value (mean stock baseline x factor).
        /// </summary>
        private void AddAxleFactor(RectTransform content, string title, string hint,
            Func<SuspensionPreset, float> getF, Action<SuspensionPreset, float> setF,
            Func<SuspensionPreset, float> getR, Action<SuspensionPreset, float> setR,
            VehicleTuner.Readout readout, Func<float, string> unitFormat)
        {
            Func<float, string> readoutF = v => Absolute(v, _tuner.MeanBaseline(readout, true), unitFormat);
            Func<float, string> readoutR = v => Absolute(v, _tuner.MeanBaseline(readout, false), unitFormat);

            GameObject both = AddSlider(content, title, hint, Limits.SuspFactorMin, Limits.SuspFactorMax,
                () => getF(SuspensionSettings.Shown),
                v => EditSuspension(p =>
                {
                    setF(p, v);
                    setR(p, v);
                }),
                () => getF(SuspensionSettings.Reference()), UiStrings.Times, null, readoutF);
            GameObject front = AddSlider(content, title + " (front)", hint, Limits.SuspFactorMin, Limits.SuspFactorMax,
                () => getF(SuspensionSettings.Shown), v => EditSuspension(p => setF(p, v)),
                () => getF(SuspensionSettings.Reference()), UiStrings.Times, null, readoutF);
            GameObject rear = AddSlider(content, title + " (rear)", hint, Limits.SuspFactorMin, Limits.SuspFactorMax,
                () => getR(SuspensionSettings.Shown), v => EditSuspension(p => setR(p, v)),
                () => getR(SuspensionSettings.Reference()), UiStrings.Times, null, readoutR);

            BindVisible(both, () => !SuspensionSettings.SplitFrontRear);
            BindVisible(front, () => SuspensionSettings.SplitFrontRear);
            BindVisible(rear, () => SuspensionSettings.SplitFrontRear);
        }

        // ================================================================ aero tab

        private void BuildAero(RectTransform content)
        {
            AddMasterSwitch(content, "Aero tuning", "OFF = every vehicle keeps its original aerodynamics.",
                () => AeroSettings.Enabled, v =>
                {
                    AeroSettings.Enabled = v;
                    _tuner.ReapplyNow();
                });

            CanvasGroup body = AddGroup(content, "Body", out RectTransform c);
            BindGroup(body, () => AeroSettings.Enabled);

            AddSectionTitle(c, "Preset");
            AeroPreset[] presets = AeroPreset.Presets;
            AddPresetButtons(c, "Presets", presets.Length, 3, 2,
                i => PresetButtonLabel(AeroSettings.Book, presets[i]),
                () => Array.IndexOf(presets, AeroSettings.ActivePreset),
                i =>
                {
                    AeroSettings.Book.Select(presets[i]);
                    _tuner.ApplyLive();
                });

            Text desc = AddNote(c, "Description", 50f);
            _refreshers.Add(() =>
            {
                AeroPreset p = AeroSettings.Shown;
                desc.text = p == AeroPreset.Custom ? CustomDescription(AeroSettings.Book) : p.Description;
            });

            AddSectionTitle(c, "Tuning");
            AddSlider(c, "Downforce", "How hard the car is pushed onto the road",
                Limits.AeroScaleMin, Limits.AeroScaleMax,
                () => AeroSettings.Shown.DownforceScale, v => EditAero(p => p.DownforceScale = v),
                () => AeroSettings.Reference().DownforceScale, UiStrings.Times);
            AddSlider(c, "Drag", "Air resistance (higher = slower top speed)",
                Limits.AeroScaleMin, Limits.AeroScaleMax,
                () => AeroSettings.Shown.DragScale, v => EditAero(p => p.DragScale = v),
                () => AeroSettings.Reference().DragScale, UiStrings.Times);
            AddSlider(c, "Downforce speed range", "How fast the downforce keeps growing",
                Limits.AeroSpeedScaleMin, Limits.AeroSpeedScaleMax,
                () => AeroSettings.Shown.MaxDownforceSpeedScale, v => EditAero(p => p.MaxDownforceSpeedScale = v),
                () => AeroSettings.Reference().MaxDownforceSpeedScale, UiStrings.Times);

            Text status = AddNote(content, "Status", 44f, 14);
            _refreshers.Add(() =>
            {
                int n = _tuner.TargetedCount;
                status.text = !AeroSettings.Enabled
                    ? "Aero tuning is off. Vehicles use their original aerodynamics."
                    : n == 0
                        ? "No vehicles found yet. Settings apply as soon as one spawns."
                        : VehicleStatus(n, UiStrings.AppliedOneAeroFmt, UiStrings.AppliedManyAeroFmt);
            });
        }

        // ================================================================ brakes tab

        private void BuildBrakes(RectTransform content)
        {
            AddMasterSwitch(content, "Brake tuning", "OFF = every vehicle keeps its original brakes.",
                () => BrakesSettings.Enabled, v =>
                {
                    BrakesSettings.Enabled = v;
                    _tuner.ReapplyNow();
                });

            CanvasGroup body = AddGroup(content, "Body", out RectTransform c);
            BindGroup(body, () => BrakesSettings.Enabled);

            AddSectionTitle(c, "Preset");
            BrakesPreset[] presets = BrakesPreset.Presets;
            AddPresetButtons(c, "Presets", presets.Length, 3, 2,
                i => PresetButtonLabel(BrakesSettings.Book, presets[i]),
                () => Array.IndexOf(presets, BrakesSettings.ActivePreset),
                i =>
                {
                    BrakesSettings.Book.Select(presets[i]);
                    _tuner.ApplyLive();
                });

            Text desc = AddNote(c, "Description", 50f);
            _refreshers.Add(() =>
            {
                BrakesPreset p = BrakesSettings.Shown;
                desc.text = p == BrakesPreset.Custom ? CustomDescription(BrakesSettings.Book) : p.Description;
            });

            AddSectionTitle(c, "Tuning");
            AddSlider(c, "Brake strength", "How hard the brakes bite",
                Limits.BrakeTorqueMin, Limits.BrakeTorqueMax,
                () => BrakesSettings.Shown.TorqueScale, v => EditBrakes(p => p.TorqueScale = v),
                () => BrakesSettings.Reference().TorqueScale, UiStrings.Times, null,
                v => Absolute(v, _tuner.MeanBaseline(VehicleTuner.Readout.BrakeTorque, true), UiStrings.Torque));
            AddSlider(c, "Front brakes", "Front axle bite (brake balance)",
                Limits.BrakeAxleMin, Limits.BrakeAxleMax,
                () => BrakesSettings.Shown.FrontBrakeScale, v => EditBrakes(p => p.FrontBrakeScale = v),
                () => BrakesSettings.Reference().FrontBrakeScale, UiStrings.Times);
            AddSlider(c, "Rear brakes", "Rear axle bite (brake balance)",
                Limits.BrakeAxleMin, Limits.BrakeAxleMax,
                () => BrakesSettings.Shown.RearBrakeScale, v => EditBrakes(p => p.RearBrakeScale = v),
                () => BrakesSettings.Reference().RearBrakeScale, UiStrings.Times);
            AddSlider(c, "Handbrake", "Handbrake strength",
                Limits.BrakeAxleMin, Limits.BrakeAxleMax,
                () => BrakesSettings.Shown.HandbrakeScale, v => EditBrakes(p => p.HandbrakeScale = v),
                () => BrakesSettings.Reference().HandbrakeScale, UiStrings.Times);
            AddSlider(c, "Pedal response", "Higher = slower brake application",
                Limits.ActuationMin, Limits.ActuationMax,
                () => BrakesSettings.Shown.ActuationScale, v => EditBrakes(p => p.ActuationScale = v),
                () => BrakesSettings.Reference().ActuationScale, UiStrings.Times);

            Text status = AddNote(content, "Status", 44f, 14);
            _refreshers.Add(() =>
            {
                int n = _tuner.TargetedCount;
                status.text = !BrakesSettings.Enabled
                    ? "Brake tuning is off. Vehicles use their original brakes."
                    : n == 0
                        ? "No vehicles found yet. Settings apply as soon as one spawns."
                        : VehicleStatus(n, UiStrings.AppliedOneFmt, UiStrings.AppliedManyFmt);
            });
        }

        // ================================================================ grip tab

        private void BuildGrip(RectTransform content)
        {
            AddMasterSwitch(content, "Grip tuning", "OFF = every vehicle keeps its original tires.",
                () => GripSettings.Enabled, v =>
                {
                    GripSettings.Enabled = v;
                    _tuner.ReapplyNow();
                });

            CanvasGroup body = AddGroup(content, "Body", out RectTransform c);
            BindGroup(body, () => GripSettings.Enabled);

            AddSectionTitle(c, "Preset");
            GripPreset[] presets = GripPreset.Presets;
            AddPresetButtons(c, "Presets", presets.Length, 3, 2,
                i => PresetButtonLabel(GripSettings.Book, presets[i]),
                () => Array.IndexOf(presets, GripSettings.ActivePreset),
                i =>
                {
                    GripSettings.Book.Select(presets[i]);
                    _tuner.ApplyLive();
                });

            Text desc = AddNote(c, "Description", 50f);
            _refreshers.Add(() =>
            {
                GripPreset p = GripSettings.Shown;
                desc.text = p == GripPreset.Custom ? CustomDescription(GripSettings.Book) : p.Description;
            });

            AddSectionTitle(c, "Tuning");
            AddSlider(c, "Longitudinal grip", "Grip under acceleration and braking",
                Limits.GripMin, Limits.GripMax,
                () => GripSettings.Shown.LongitudinalScale, v => EditGrip(p => p.LongitudinalScale = v),
                () => GripSettings.Reference().LongitudinalScale, UiStrings.Times);
            AddSlider(c, "Lateral grip", "Grip in corners",
                Limits.GripMin, Limits.GripMax,
                () => GripSettings.Shown.LateralScale, v => EditGrip(p => p.LateralScale = v),
                () => GripSettings.Reference().LateralScale, UiStrings.Times);
            AddSlider(c, "Tire stiffness", "How quickly the tires reach peak grip",
                Limits.GripMin, Limits.GripMax,
                () => GripSettings.Shown.StiffnessScale, v => EditGrip(p => p.StiffnessScale = v),
                () => GripSettings.Reference().StiffnessScale, UiStrings.Times);

            Text status = AddNote(content, "Status", 44f, 14);
            _refreshers.Add(() =>
            {
                int n = _tuner.TargetedCount;
                status.text = !GripSettings.Enabled
                    ? "Grip tuning is off. Vehicles use their original tires."
                    : n == 0
                        ? "No vehicles found yet. Settings apply as soon as one spawns."
                        : VehicleStatus(n,
                            _tuner.AnyTyreWear ? UiStrings.AppliedOneGripWarnFmt : UiStrings.AppliedOneGripFmt,
                            _tuner.AnyTyreWear ? UiStrings.AppliedManyGripWarnFmt : UiStrings.AppliedManyGripFmt);
            });
        }

        // ================================================================ drivetrain tab

        private void BuildDrivetrain(RectTransform content)
        {
            AddMasterSwitch(content, "Drivetrain tuning", "OFF = every vehicle keeps its original engine and gearing.",
                () => DrivetrainSettings.Enabled, v =>
                {
                    DrivetrainSettings.Enabled = v;
                    _tuner.ReapplyNow();
                });

            CanvasGroup body = AddGroup(content, "Body", out RectTransform c);
            BindGroup(body, () => DrivetrainSettings.Enabled);

            AddSectionTitle(c, "Preset");
            DrivetrainPreset[] presets = DrivetrainPreset.Presets;
            AddPresetButtons(c, "Presets", presets.Length, 3, 2,
                i => PresetButtonLabel(DrivetrainSettings.Book, presets[i]),
                () => Array.IndexOf(presets, DrivetrainSettings.ActivePreset),
                i =>
                {
                    DrivetrainSettings.Book.Select(presets[i]);
                    _tuner.ApplyLive();
                });

            Text desc = AddNote(c, "Description", 50f);
            _refreshers.Add(() =>
            {
                DrivetrainPreset p = DrivetrainSettings.Shown;
                desc.text = p == DrivetrainPreset.Custom ? CustomDescription(DrivetrainSettings.Book) : p.Description;
            });

            AddSectionTitle(c, "Engine and gearing");
            AddSlider(c, "Engine power", "How much power the engine makes",
                Limits.PowerMin, Limits.PowerMax,
                () => DrivetrainSettings.Shown.PowerScale, v => EditDrivetrain(p => p.PowerScale = v),
                () => DrivetrainSettings.Reference().PowerScale, UiStrings.Times);
            AddSlider(c, "Rev limit", "The engine's redline",
                Limits.RevLimitMin, Limits.RevLimitMax,
                () => DrivetrainSettings.Shown.RevLimiterScale, v => EditDrivetrain(p => p.RevLimiterScale = v),
                () => DrivetrainSettings.Reference().RevLimiterScale, UiStrings.Times);
            AddSlider(c, "Engine braking", "Off-throttle engine drag",
                Limits.LossMin, Limits.LossMax,
                () => DrivetrainSettings.Shown.LossScale, v => EditDrivetrain(p => p.LossScale = v),
                () => DrivetrainSettings.Reference().LossScale, UiStrings.Times);
            AddSlider(c, "Turbo / boost", "Forced induction gain (inert on electrics)",
                Limits.BoostMin, Limits.BoostMax,
                () => DrivetrainSettings.Shown.BoostScale, v => EditDrivetrain(p => p.BoostScale = v),
                () => DrivetrainSettings.Reference().BoostScale, UiStrings.Times);
            AddSlider(c, "Final drive", "Overall gearing (higher = shorter)",
                Limits.FinalDriveMin, Limits.FinalDriveMax,
                () => DrivetrainSettings.Shown.FinalDriveScale, v => EditDrivetrain(p => p.FinalDriveScale = v),
                () => DrivetrainSettings.Reference().FinalDriveScale, UiStrings.Times);
            AddSlider(c, "Upshift RPM", "Where the auto box shifts up (kept below redline)",
                Limits.ShiftRpmMin, Limits.ShiftRpmMax,
                () => DrivetrainSettings.Shown.UpshiftScale, v => EditDrivetrain(p => p.UpshiftScale = v),
                () => DrivetrainSettings.Reference().UpshiftScale, UiStrings.Times);
            AddSlider(c, "Downshift RPM", "Where the auto box shifts down",
                Limits.ShiftRpmMin, Limits.ShiftRpmMax,
                () => DrivetrainSettings.Shown.DownshiftScale, v => EditDrivetrain(p => p.DownshiftScale = v),
                () => DrivetrainSettings.Reference().DownshiftScale, UiStrings.Times);
            AddSlider(c, "Shift time", "How long a gear change takes",
                Limits.FactorMin, Limits.FactorMax,
                () => DrivetrainSettings.Shown.ShiftDurationScale, v => EditDrivetrain(p => p.ShiftDurationScale = v),
                () => DrivetrainSettings.Reference().ShiftDurationScale, UiStrings.Times);

            AddSectionTitle(c, "Differentials");
            AddPresetButtons(c, "DiffFront", DiffModeLabels.Length, DiffModeLabels.Length, DiffModeLabels.Length,
                i => DiffModeLabels[i],
                () => DiffModeIndex(DrivetrainSettings.Shown.DiffFrontMode),
                i =>
                {
                    // Re-clicking the active mode must not fork the preset into Custom.
                    if (DrivetrainSettings.Shown.DiffFrontMode != IndexToDiffMode(i))
                    {
                        EditDrivetrain(p => p.DiffFrontMode = IndexToDiffMode(i));
                    }
                });
            Text diffFrontNote = AddNote(c, "DiffFrontNote", 26f, 13);
            _refreshers.Add(() =>
            {
                diffFrontNote.text = string.Format(UiStrings.FrontAxleFmt,
                    DiffModeLabels[DiffModeIndex(DrivetrainSettings.Shown.DiffFrontMode)]);
            });
            AddPresetButtons(c, "DiffRear", DiffModeLabels.Length, DiffModeLabels.Length, DiffModeLabels.Length,
                i => DiffModeLabels[i],
                () => DiffModeIndex(DrivetrainSettings.Shown.DiffRearMode),
                i =>
                {
                    if (DrivetrainSettings.Shown.DiffRearMode != IndexToDiffMode(i))
                    {
                        EditDrivetrain(p => p.DiffRearMode = IndexToDiffMode(i));
                    }
                });
            Text diffRearNote = AddNote(c, "DiffRearNote", 26f, 13);
            _refreshers.Add(() =>
            {
                diffRearNote.text = string.Format(UiStrings.RearAxleFmt,
                    DiffModeLabels[DiffModeIndex(DrivetrainSettings.Shown.DiffRearMode)]);
            });
            AddSlider(c, "Diff stiffness", "How hard the diff locks",
                Limits.DiffScaleMin, Limits.DiffScaleMax,
                () => DrivetrainSettings.Shown.DiffStiffnessScale, v => EditDrivetrain(p => p.DiffStiffnessScale = v),
                () => DrivetrainSettings.Reference().DiffStiffnessScale, UiStrings.Times);
            AddSlider(c, "Diff bias (AWD)", "Front/rear split of a centre diff only",
                Limits.DiffScaleMin, Limits.DiffScaleMax,
                () => DrivetrainSettings.Shown.DiffBiasScale, v => EditDrivetrain(p => p.DiffBiasScale = v),
                () => DrivetrainSettings.Reference().DiffBiasScale, UiStrings.Times);

            Text status = AddNote(content, "Status", 44f, 14);
            _refreshers.Add(() =>
            {
                int n = _tuner.TargetedCount;
                status.text = !DrivetrainSettings.Enabled
                    ? "Drivetrain tuning is off. Vehicles use their original engine and gearing."
                    : n == 0
                        ? "No vehicles found yet. Settings apply as soon as one spawns."
                        : VehicleStatus(n, UiStrings.AppliedOneDtFmt, UiStrings.AppliedManyDtFmt);
            });
        }

        private static int DiffModeIndex(DiffMode m)
        {
            switch (m)
            {
                case DiffMode.Open: return 1;
                case DiffMode.Locked: return 2;
                case DiffMode.LimitedSlip: return 3;
                default: return 0;
            }
        }

        private static DiffMode IndexToDiffMode(int i)
        {
            switch (i)
            {
                case 1: return DiffMode.Open;
                case 2: return DiffMode.Locked;
                case 3: return DiffMode.LimitedSlip;
                default: return DiffMode.Stock;
            }
        }

        // ================================================================ assists tab

        private void BuildAssists(RectTransform content)
        {
            AddMasterSwitch(content, "Stability assists", "OFF = no assists from this mod (the game's own ABS/TCS still work).",
                () => AssistsSettings.Enabled, v =>
                {
                    AssistsSettings.Enabled = v;
                    _tuner.ReapplyNow();
                });

            CanvasGroup body = AddGroup(content, "Body", out RectTransform c);
            BindGroup(body, () => AssistsSettings.Enabled);

            AddSectionTitle(c, "Preset");
            AssistsPreset[] presets = AssistsPreset.Presets;
            AddPresetButtons(c, "Presets", presets.Length, 3, 2,
                i => PresetButtonLabel(AssistsSettings.Book, presets[i]),
                () => Array.IndexOf(presets, AssistsSettings.ActivePreset),
                i =>
                {
                    AssistsSettings.Book.Select(presets[i]);
                    _tuner.ApplyLive();
                });

            Text desc = AddNote(c, "Description", 50f);
            _refreshers.Add(() =>
            {
                AssistsPreset p = AssistsSettings.Shown;
                desc.text = p == AssistsPreset.Custom ? CustomDescription(AssistsSettings.Book) : p.Description;
            });

            AddSectionTitle(c, "ABS");
            AddOption(c, "ABS", "Prevents wheel lock-up under braking",
                () => AssistsSettings.Shown.AbsEnabled, v => EditAssists(p => p.AbsEnabled = v));
            AddSlider(c, "ABS slip threshold", "How much wheel slip before release",
                Limits.SlipThrMin, Limits.SlipThrMax,
                () => AssistsSettings.Shown.AbsSlipThreshold, v => EditAssists(p => p.AbsSlipThreshold = v),
                () => AssistsSettings.Reference().AbsSlipThreshold, v => v.ToString("0.00"),
                () => AssistsSettings.Shown.AbsEnabled);
            AddSlider(c, "ABS cutoff speed", "No ABS below this speed",
                Limits.CutoffSpeedMin, Limits.CutoffSpeedMax,
                () => AssistsSettings.Shown.AbsCutoffSpeed, v => EditAssists(p => p.AbsCutoffSpeed = v),
                () => AssistsSettings.Reference().AbsCutoffSpeed, v => UiStrings.SpeedMps(v),
                () => AssistsSettings.Shown.AbsEnabled);
            AddSlider(c, "ABS release force", "Brake strength while releasing",
                Limits.CutMultMin, Limits.CutMultMax,
                () => AssistsSettings.Shown.AbsCutMultiplier, v => EditAssists(p => p.AbsCutMultiplier = v),
                () => AssistsSettings.Reference().AbsCutMultiplier, UiStrings.Percent,
                () => AssistsSettings.Shown.AbsEnabled);

            AddSectionTitle(c, "TCS");
            AddOption(c, "TCS", "Cuts power while the wheels spin",
                () => AssistsSettings.Shown.TcsEnabled, v => EditAssists(p => p.TcsEnabled = v));
            AddSlider(c, "TCS slip threshold", "How much wheel spin before cutting",
                Limits.SlipThrMin, Limits.SlipThrMax,
                () => AssistsSettings.Shown.TcsSlipThreshold, v => EditAssists(p => p.TcsSlipThreshold = v),
                () => AssistsSettings.Reference().TcsSlipThreshold, v => v.ToString("0.00"),
                () => AssistsSettings.Shown.TcsEnabled);
            AddSlider(c, "TCS cutoff speed", "No TCS below this speed",
                Limits.CutoffSpeedMin, Limits.CutoffSpeedMax,
                () => AssistsSettings.Shown.TcsCutoffSpeed, v => EditAssists(p => p.TcsCutoffSpeed = v),
                () => AssistsSettings.Reference().TcsCutoffSpeed, v => UiStrings.SpeedMps(v),
                () => AssistsSettings.Shown.TcsEnabled);
            AddSlider(c, "TCS cut strength", "Power allowed while spinning",
                Limits.CutMultMin, Limits.CutMultMax,
                () => AssistsSettings.Shown.TcsCutMultiplier, v => EditAssists(p => p.TcsCutMultiplier = v),
                () => AssistsSettings.Reference().TcsCutMultiplier, UiStrings.Percent,
                () => AssistsSettings.Shown.TcsEnabled);

            Text status = AddNote(content, "Status", 44f, 14);
            _refreshers.Add(() =>
            {
                int n = _tuner.TargetedCount;
                status.text = !AssistsSettings.Enabled
                    ? "Assists are off. The game's own ABS/TCS still work if a vehicle has them."
                    : n == 0
                        ? "No vehicles found yet. Assists attach as soon as one spawns."
                        : VehicleStatus(n, UiStrings.ActiveOnOneFmt, UiStrings.ActiveOnManyFmt);
            });
        }

        // ================================================================ alignment tab

        private static readonly string[] RoleNames = { "front left", "front right", "rear left", "rear right" };
        private static readonly string[] AxisTitles = { "Track (outward)", "Height", "Fore/aft" };
        private static readonly string[] AxisHints =
        {
            "Moves the wheel away from the centreline",
            "Positive = the car sits higher, negative = lower",
            "Moves the wheel forward (+) or back (-)"
        };

        private bool HasVehicle()
        {
            return _tuner.HasReferenceVehicle;
        }

        private string CamberStock(WheelRole role)
        {
            VehicleTuner.WheelStock s = _tuner.ReferenceWheelStock(role);
            return s.Valid ? string.Format(UiStrings.AlignmentStockFmt, UiStrings.DegSigned(s.Camber)) : "";
        }

        private string PosStock(WheelRole role, int axis)
        {
            VehicleTuner.WheelStock s = _tuner.ReferenceWheelStock(role);
            if (!s.Valid)
            {
                return "";
            }
            float v = axis == 0 ? Mathf.Abs(s.LocalPos.x) : axis == 1 ? s.LocalPos.y : s.LocalPos.z;
            return string.Format(UiStrings.AlignmentStockFmt, UiStrings.CmSigned(v * 100f));
        }

        private string GroupStock(bool front, bool caster)
        {
            VehicleTuner.GroupStock s = _tuner.ReferenceGroupStock(front);
            return s.Valid ? string.Format(UiStrings.AlignmentStockFmt, UiStrings.DegSigned2(caster ? s.Caster : s.Toe)) : "";
        }

        private void BuildAlignment(RectTransform content)
        {
            AddMasterSwitch(content, "Wheel alignment", "OFF = every vehicle keeps its original wheel geometry.",
                () => AlignmentSettings.Enabled, v =>
                {
                    AlignmentSettings.Enabled = v;
                    _tuner.ReapplyNow();
                });

            CanvasGroup body = AddGroup(content, "Body", out RectTransform c);
            BindGroup(body, () => AlignmentSettings.Enabled);

            AddSectionTitle(c, "Preset");
            AlignmentPreset[] presets = AlignmentPreset.Presets;
            AddPresetButtons(c, "Presets", presets.Length, 3, 2,
                i => PresetButtonLabel(AlignmentSettings.Book, presets[i]),
                () => Array.IndexOf(presets, AlignmentSettings.ActivePreset),
                i =>
                {
                    AlignmentSettings.Book.Select(presets[i]);
                    _tuner.ApplyLive();
                });

            Text desc = AddNote(c, "Description", 50f);
            _refreshers.Add(() =>
            {
                AlignmentPreset p = AlignmentSettings.Shown;
                desc.text = p == AlignmentPreset.Custom
                    ? CustomDescription(AlignmentSettings.Book)
                    : p.Description + " Values add to each vehicle's own geometry.";
            });

            CanvasGroup rows = AddGroup(c, "Rows", out RectTransform r);
            BindGroup(rows, HasVehicle);

            AddOption(r, "Per-wheel (advanced)", "Separate left and right camber and position",
                () => AlignmentSettings.PerWheel, v =>
                {
                    AlignmentSettings.PerWheel = v;
                    // Linking only forks into Custom when the sides actually differ.
                    if (!v && AlignmentSettings.Shown.SidesDiffer())
                    {
                        AlignmentSettings.LinkSides();
                        _tuner.ApplyLive();
                    }
                });

            AddSectionTitle(r, "Camber");
            for (int a = 0; a < 2; a++)
            {
                bool front = a == 0;
                WheelRole left = front ? WheelRole.FL : WheelRole.RL;
                GameObject axle = AddSlider(r, front ? "Camber front" : "Camber rear", "Negative = top of the wheel leans in",
                    Limits.AlignmentCamberMin, Limits.AlignmentCamberMax,
                    () => AlignmentSettings.Shown.Camber(left),
                    v => EditAlignment(p => AlignmentSettings.SetAxleCamber(p, front, v)),
                    () => AlignmentSettings.Reference().Camber(left), UiStrings.DegSigned, null, v => CamberStock(left));
                BindVisible(axle, () => !AlignmentSettings.PerWheel);
            }
            for (int w = 0; w < 4; w++)
            {
                WheelRole role = (WheelRole)w;
                GameObject row = AddSlider(r, "Camber " + RoleNames[w], "Negative = top of the wheel leans in",
                    Limits.AlignmentCamberMin, Limits.AlignmentCamberMax,
                    () => AlignmentSettings.Shown.Camber(role),
                    v => EditAlignment(p => p.SetCamber(role, v)),
                    () => AlignmentSettings.Reference().Camber(role), UiStrings.DegSigned, null, v => CamberStock(role));
                BindVisible(row, () => AlignmentSettings.PerWheel);
            }

            AddSectionTitle(r, "Caster and toe");
            for (int a = 0; a < 2; a++)
            {
                bool front = a == 0;
                AddSlider(r, front ? "Caster front" : "Caster rear", "More = stronger self-centering, heavier steering",
                    Limits.AlignmentCasterMin, Limits.AlignmentCasterMax,
                    () => AlignmentSettings.Shown.Caster(front),
                    v => EditAlignment(p => { if (front) p.CasterF = v; else p.CasterR = v; }),
                    () => AlignmentSettings.Reference().Caster(front), UiStrings.DegSigned, null, v => GroupStock(front, true));
            }
            for (int a = 0; a < 2; a++)
            {
                bool front = a == 0;
                AddSlider(r, front ? "Toe front" : "Toe rear", "Positive = toe-in (stable), negative = toe-out",
                    Limits.AlignmentToeMin, Limits.AlignmentToeMax,
                    () => AlignmentSettings.Shown.Toe(front),
                    v => EditAlignment(p => { if (front) p.ToeF = v; else p.ToeR = v; }),
                    () => AlignmentSettings.Reference().Toe(front), UiStrings.DegSigned2, null, v => GroupStock(front, false));
            }

            AddSectionTitle(r, "Wheel position");
            for (int axis = 0; axis < 3; axis++)
            {
                for (int a = 0; a < 2; a++)
                {
                    int ax = axis;
                    bool front = a == 0;
                    WheelRole left = front ? WheelRole.FL : WheelRole.RL;
                    GameObject axle = AddSlider(r, AxisTitles[axis] + (front ? " front" : " rear"), AxisHints[axis],
                        Limits.AlignmentPosMin, Limits.AlignmentPosMax,
                        () => AlignmentSettings.Shown.Pos(left, ax),
                        v => EditAlignment(p => AlignmentSettings.SetAxlePos(p, front, ax, v)),
                        () => AlignmentSettings.Reference().Pos(left, ax), UiStrings.CmSigned, null, v => PosStock(left, ax));
                    BindVisible(axle, () => !AlignmentSettings.PerWheel);
                }
            }
            for (int w = 0; w < 4; w++)
            {
                for (int axis = 0; axis < 3; axis++)
                {
                    int ax = axis;
                    WheelRole role = (WheelRole)w;
                    GameObject row = AddSlider(r, AxisTitles[axis] + " " + RoleNames[w], AxisHints[axis],
                        Limits.AlignmentPosMin, Limits.AlignmentPosMax,
                        () => AlignmentSettings.Shown.Pos(role, ax),
                        v => EditAlignment(p => p.SetPos(role, ax, v)),
                        () => AlignmentSettings.Reference().Pos(role, ax), UiStrings.CmSigned, null, v => PosStock(role, ax));
                    BindVisible(row, () => AlignmentSettings.PerWheel);
                }
            }

            Text warn = AddNote(content, "Warnings", 64f, 13);
            _refreshers.Add(() =>
            {
                string s = "";
                if (_tuner.AnyCamberLocked)
                {
                    s += "Some wheels set their own camber (camber controller or solid axle); the mod leaves their camber alone. ";
                }
                if (AlignmentSettings.Enabled && _tuner.AnyWheelsMoved)
                {
                    s += "Moved wheels keep the vehicle's original wheelbase and track width for Ackermann steering. ";
                }
                if (AlignmentSettings.PerWheel)
                {
                    s += "Per-wheel values are re-applied every 2 s if the game resets the geometry.";
                }
                warn.text = s;
            });

            Text status = AddNote(content, "Status", 44f, 14);
            _refreshers.Add(() =>
            {
                int n = _tuner.TargetedCount;
                status.text = !AlignmentSettings.Enabled
                    ? "Alignment is off. Vehicles use their original wheel geometry."
                    : n == 0
                        ? "No vehicles found yet. The sliders unlock as soon as one spawns."
                        : VehicleStatus(n, UiStrings.AppliedOneAlignFmt, UiStrings.AppliedManyAlignFmt);
            });
        }

        // ================================================================ gearbox tab

        /// <summary>Number of gear rows/bars shown: the preset's count, else the reference vehicle's (6 when none).</summary>
        private int ShownGearCount()
        {
            int n = GearboxSettings.Shown.GearCount;
            if (n <= 0)
            {
                n = _tuner.ReferenceGearCount;
            }
            if (n <= 0)
            {
                n = 6;
            }
            return Mathf.Min(n, GearboxPreset.MaxGears);
        }

        private void BuildGearbox(RectTransform content)
        {
            AddMasterSwitch(content, "Gearbox", "OFF = every vehicle keeps its original gears and clutch.",
                () => GearboxSettings.Enabled, v =>
                {
                    GearboxSettings.Enabled = v;
                    _tuner.ReapplyNow();
                });

            CanvasGroup body = AddGroup(content, "Body", out RectTransform c);
            BindGroup(body, () => GearboxSettings.Enabled);

            AddSectionTitle(c, "Preset");
            GearboxPreset[] presets = GearboxPreset.Presets;
            AddPresetButtons(c, "Presets", presets.Length, 3, 2,
                i => PresetButtonLabel(GearboxSettings.Book, presets[i]),
                () => Array.IndexOf(presets, GearboxSettings.ActivePreset),
                i =>
                {
                    GearboxSettings.Book.Select(presets[i]);
                    _tuner.ApplyLive();
                });

            Text desc = AddNote(c, "Description", 50f);
            _refreshers.Add(() =>
            {
                GearboxPreset p = GearboxSettings.Shown;
                desc.text = p == GearboxPreset.Custom ? CustomDescription(GearboxSettings.Book) : p.Description;
            });

            AddSectionTitle(c, "Gears");
            AddOption(c, "Keep vehicle's gear count", "OFF = choose the number of forward gears",
                () => GearboxSettings.Shown.GearCount == 0,
                v => EditGearbox(p => p.GearCount = v ? 0 : Mathf.Max(1, Mathf.Min(GearboxPreset.MaxGears,
                    _tuner.ReferenceGearCount > 0 ? _tuner.ReferenceGearCount : 6))));
            AddSlider(c, "Gear count", "Extra gears continue the vehicle's own spacing",
                Limits.GearCountMin, Limits.GearCountMax,
                () => GearboxSettings.Shown.GearCount, v => EditGearbox(p => p.GearCount = Mathf.RoundToInt(v)),
                () => GearboxSettings.Reference().GearCount,
                v => v < 0.5f ? "Own" : string.Format(UiStrings.GearCountFmt, Mathf.RoundToInt(v)),
                () => GearboxSettings.Shown.GearCount != 0, null, true);

            GearGraph graph = GearGraph.Create(c, "Gear ratios",
                "Bars = each gear's ratio, outlines = the vehicle's own. Click a bar to pick its gear, drag it to change it.",
                ShownGearCount,
                i => _tuner.ReferenceGearStock(i),
                i => _tuner.ReferenceGearStock(i) * GearboxSettings.PerGearScale(i),
                () => GearboxSettings.Enabled,
                () => _selectedGear,
                i => _selectedGear = i,
                (i, scale) => EditGearbox(p => p.SetScale(i, scale)),
                () =>
                {
                    if (_selectedGear < 1)
                    {
                        return "";
                    }
                    float stock = _tuner.ReferenceGearStock(_selectedGear);
                    string title = string.Format(UiStrings.GearTitleFmt, _selectedGear);
                    return stock > 0f ? title + " · " + UiStrings.Ratio(stock * GearboxSettings.PerGearScale(_selectedGear)) : title;
                });
            _refreshers.Add(graph.Refresh);
            _relayouts.Add(graph.Relayout);

            for (int g = 1; g <= GearboxPreset.MaxGears; g++)
            {
                int gear = g;
                GameObject row = AddSlider(c, string.Format(UiStrings.GearTitleFmt, gear), "Factor on this gear's own ratio",
                    Limits.GearRatioMin, Limits.GearRatioMax,
                    () => GearboxSettings.PerGearScale(gear),
                    v => EditGearbox(p => p.SetScale(gear, v)),
                    () => GearboxSettings.Reference().Scale(gear), UiStrings.Times,
                    () => !_tuner.ReferenceIsCvt, v =>
                    {
                        float stock = _tuner.ReferenceGearStock(gear);
                        return stock > 0f ? UiStrings.Ratio(stock * v) : "";
                    });
                BindVisible(row, () => gear <= ShownGearCount());
                Image bg = row.GetComponent<Image>();
                if (bg != null)
                {
                    _refreshers.Add(() => bg.color = gear == _selectedGear ? UiKit.ChipBase : UiKit.RowNormal);
                }
            }

            AddSectionTitle(c, "Clutch");
            Text clutchNote = AddNote(c, "ClutchNote", 44f, 13);
            clutchNote.text = "NWH2 has no real clutch model — clutch type is emulated through clutch capacity and engagement speed.";
            ClutchType[] types = GearboxPreset.ClutchTypes;
            AddPresetButtons(c, "ClutchTypes", types.Length + 1, 5, 3,
                i => i < types.Length ? types[i].Name : "Custom",
                () =>
                {
                    int idx = GearboxSettings.ClutchTypeIndex(GearboxSettings.Shown);
                    return idx < 0 ? types.Length : idx;
                },
                i =>
                {
                    if (i < types.Length && GearboxSettings.ClutchTypeIndex(GearboxSettings.Shown) != i)
                    {
                        EditGearbox(p => p.ApplyClutchType(types[i]));
                    }
                });
            AddSlider(c, "Clutch capacity", "Torque the clutch holds before slipping",
                Limits.ClutchGripMin, Limits.ClutchGripMax,
                () => GearboxSettings.Shown.ClutchGripScale, v => EditGearbox(p => p.ClutchGripScale = v),
                () => GearboxSettings.Reference().ClutchGripScale, UiStrings.Times);
            AddSlider(c, "Engagement range", "Lower = the clutch bites faster",
                Limits.ClutchRangeMin, Limits.ClutchRangeMax,
                () => GearboxSettings.Shown.ClutchRangeScale, v => EditGearbox(p => p.ClutchRangeScale = v),
                () => GearboxSettings.Reference().ClutchRangeScale, UiStrings.Times);
            AddSlider(c, "Engagement point", "RPM where the clutch starts to bite",
                Limits.ClutchRpmMin, Limits.ClutchRpmMax,
                () => GearboxSettings.Shown.ClutchRpmOffset, v => EditGearbox(p => p.ClutchRpmOffset = Mathf.Round(v / 10f) * 10f),
                () => GearboxSettings.Reference().ClutchRpmOffset, v => v.ToString("+0;-0;0") + " rpm");

            AddSectionTitle(c, "Transmission mode");
            AddPresetButtons(c, "Mode", ModeLabels.Length, 3, 3,
                i => ModeLabels[i],
                () => (int)GearboxSettings.Shown.TransmissionMode,
                i =>
                {
                    if ((int)GearboxSettings.Shown.TransmissionMode != i)
                    {
                        EditGearbox(p => p.TransmissionMode = (GearboxMode)i);
                    }
                });

            Text status = AddNote(content, "Status", 60f, 14);
            _refreshers.Add(() =>
            {
                int n = _tuner.TargetedCount;
                string s = !GearboxSettings.Enabled
                    ? "Gearbox customisation is off. Vehicles use their original gears and clutch."
                    : n == 0
                        ? "No vehicles found yet. Settings apply as soon as one spawns."
                        : VehicleStatus(n, UiStrings.AppliedOneGearFmt, UiStrings.AppliedManyGearFmt);
                if (_tuner.AnyCvt)
                {
                    s += " CVT vehicles keep their gears and mode; only the clutch applies to them.";
                }
                if (GearboxSettings.Enabled)
                {
                    s += " Saving the game while tuned bakes the changed gears into that save (fixed automatically on load).";
                }
                if (_tuner.AnyResizeSkipped)
                {
                    s += " Gear count changes are skipped on automatic transmissions (the game's shift logic expects the stock gears).";
                }
                status.text = s;
            });
        }

        // ================================================================ panel tab

        private void BuildPanelTab(RectTransform content)
        {
            AddSectionTitle(content, "Apply to");
            AddPresetButtons(content, "ApplyTarget", 3, 3, 3,
                i => TargetSettings.ModeName((TargetMode)i),
                () => (int)TargetSettings.Mode,
                i =>
                {
                    TargetSettings.Mode = (TargetMode)i;
                    if (TargetSettings.Mode != TargetMode.Selected && _targetNameIndex > 0)
                    {
                        _targetNameIndex = 0;
                    }
                    _tuner.ApplyLive();
                    Refresh();
                });
            GameObject targetRow = AddBlock(content, "TargetVehicle", 58f, true, out LayoutElement _).gameObject;
            BindVisible(targetRow, () => TargetSettings.Mode == TargetMode.Selected);
            RectTransform tr = (RectTransform)targetRow.transform;
            RectTransform tl = UiKit.Place(UiKit.Make("T", tr), 0f, 0.5f, 1f, 1f, 16f, 0f, 100f, 6f);
            UiKit.Label(tl, "Vehicle", 17, UiKit.TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);
            Button targetBtn = UiKit.MakeButton(tr, "VehicleBtn", "", UiKit.ChipBase, 15, () =>
            {
                List<string> names = _tuner.TrackedNames();
                if (names.Count > 0)
                {
                    _targetNameIndex = (_targetNameIndex + 1) % names.Count;
                    TargetSettings.SelectedName = names[_targetNameIndex];
                    _tuner.ApplyLive();
                    Refresh();
                }
            }, out Text targetNameLabel);
            UiKit.RightBox((RectTransform)targetBtn.transform, 220f, 34f, 14f);
            Text targetHint = AddNote(content, "TargetHint", 26f, 13);
            _refreshers.Add(() =>
            {
                List<string> names = _tuner.TrackedNames();
                if (names.Count == 0)
                {
                    targetNameLabel.text = "-";
                    targetHint.text = "No vehicles found yet.";
                    return;
                }
                if (_targetNameIndex >= names.Count)
                {
                    _targetNameIndex = 0;
                }
                if (TargetSettings.SelectedName == "" || !names.Contains(TargetSettings.SelectedName))
                {
                    TargetSettings.SelectedName = names[_targetNameIndex];
                }
                targetNameLabel.text = TargetSettings.SelectedName;
                targetHint.text = TargetSettings.Mode == TargetMode.Selected
                    ? "Only this vehicle is tuned. Click the button to pick another."
                    : "";
            });

            AddSectionTitle(content, "Panel");
            AddOption(content, "Freeze game while open", "OFF = keep driving while the panel is open",
                () => UiSettings.FreezeWhileOpen, v =>
                {
                    UiSettings.FreezeWhileOpen = v;
                    ModeChanged();
                });
            AddSlider(content, "Transparency", "Lower = see more of the game through the panel",
                Limits.PanelAlphaMin, Limits.PanelAlphaMax,
                () => UiSettings.PanelAlpha, v => { UiSettings.PanelAlpha = v; ApplyDisplaySettings(); },
                () => UiSettings.DefaultAlpha, UiStrings.Percent);
            AddSlider(content, "Size", "Interface size of the panel",
                Limits.PanelScaleMin, Limits.PanelScaleMax,
                () => UiSettings.PanelScale, v => { UiSettings.PanelScale = Mathf.Round(v * 20f) / 20f; ApplyDisplaySettings(); },
                () => UiSettings.DefaultScale, UiStrings.Times);
            AddSlider(content, "Width", "How wide the docked panel is",
                Limits.PanelWidthMin, Limits.PanelWidthMax,
                () => UiSettings.PanelWidth, v => { UiSettings.PanelWidth = Mathf.Round(v / 10f) * 10f; ApplyDisplaySettings(); },
                () => Limits.PanelWidthDefault, UiStrings.Px);
            Text note = AddNote(content, "PanelNote", 30f, 13);
            note.text = "Changes apply immediately.";

            AddSectionTitle(content, "Telemetry");
            AddOption(content, "Show telemetry", "Speed, RPM, gear and front slip while driving",
                () => UiSettings.TelemetryEnabled, v => UiSettings.TelemetryEnabled = v);
            AddSlider(content, "Telemetry size", "Size of the telemetry strip",
                Limits.TelemetryScaleMin, Limits.TelemetryScaleMax,
                () => UiSettings.TelemetryScale, v => UiSettings.TelemetryScale = Mathf.Round(v * 20f) / 20f,
                () => 1f, UiStrings.Times, () => UiSettings.TelemetryEnabled);
            AddPresetButtons(content, "TelemetryCorner", CornerLabels.Length, 4, 2,
                i => CornerLabels[i],
                () => (int)UiSettings.TelemetryPosition,
                i => UiSettings.TelemetryPosition = (TelemetryCorner)i);
            Text telNote = AddNote(content, "TelemetryNote", 44f, 13);
            telNote.text = "The strip never takes clicks and hides while this panel is open. Slip is NWH's normalised slip shown in approximate degrees.";
        }

        // ================================================================ footer: reset, copy/paste, all off

        private void OnResetClicked()
        {
            if (Time.unscaledTime > _resetArmedUntil)
            {
                // First click arms; a second click within 3 s confirms. No accidental wipes.
                _resetArmedUntil = Time.unscaledTime + 3f;
                UpdateResetButton();
                return;
            }
            DisarmReset();
            _resetActions[_tab]();
            Refresh();
        }

        private void DisarmReset()
        {
            _resetArmedUntil = -1f;
            UpdateResetButton();
        }

        private void UpdateResetButton()
        {
            if (_resetButton == null)
            {
                return;
            }
            bool armed = _resetArmedUntil > 0f;
            ((Image)_resetButton.targetGraphic).color = armed ? UiKit.Danger : UiKit.RowBase;
            _resetLabel.text = armed ? "Click again to confirm" : _resetLabels[_tab];
            bool allArmed = _allOffArmedUntil > 0f;
            ((Image)_allOffButton.targetGraphic).color = allArmed ? UiKit.Danger : UiKit.RowBase;
            _allOffLabel.text = allArmed ? "Click again" : "Turn everything off";
        }

        /// <summary>Two-click arm state (pure, for the harness): armed until <paramref name="armedUntil"/>.</summary>
        public static bool ConfirmTwoClick(ref float armedUntil, float now, float window = 3f)
        {
            if (now > armedUntil)
            {
                armedUntil = now + window;
                return false;   // first click (or the arm expired): arm
            }
            armedUntil = -1f;
            return true;        // second click inside the window: confirm
        }

        /// <summary>Switch all nine tuning categories off (the panel's "Turn everything off").</summary>
        public static void TurnEverythingOff()
        {
            SteeringSettings.Enabled = false;
            SuspensionSettings.Enabled = false;
            AeroSettings.Enabled = false;
            BrakesSettings.Enabled = false;
            GripSettings.Enabled = false;
            DrivetrainSettings.Enabled = false;
            AssistsSettings.Enabled = false;
            AlignmentSettings.Enabled = false;
            GearboxSettings.Enabled = false;
        }

        private void OnAllOffClicked()
        {
            if (!ConfirmTwoClick(ref _allOffArmedUntil, Time.unscaledTime))
            {
                UpdateResetButton();
                return;
            }
            TurnEverythingOff();
            _tuner.ReapplyNow();
            UpdateResetButton();
            Refresh();
        }

        private ITunablePreset ActivePresetOf(PresetCategory c)
        {
            switch (c)
            {
                case PresetCategory.Steering: return SteeringSettings.ActivePreset ?? SteeringPreset.Custom;
                case PresetCategory.Suspension: return SuspensionSettings.Shown;
                case PresetCategory.Aero: return AeroSettings.Shown;
                case PresetCategory.Brakes: return BrakesSettings.Shown;
                case PresetCategory.Grip: return GripSettings.Shown;
                case PresetCategory.Drivetrain: return DrivetrainSettings.Shown;
                case PresetCategory.Assists: return AssistsSettings.Shown;
                case PresetCategory.Alignment: return AlignmentSettings.Shown;
                default: return GearboxSettings.Shown;
            }
        }

        private void OnCopyClicked()
        {
            PresetCategory? cat = TabCategory[_tab];
            if (cat == null)
            {
                return;
            }
            ITunablePreset p = ActivePresetOf(cat.Value);
            string text = PresetCodec.Serialize(cat.Value, p);
            try
            {
                GUIUtility.systemCopyBuffer = text;
                _status.text = string.Format(UiStrings.PresetCopiedFmt, p.Label);
            }
            catch (Exception)
            {
                _status.text = string.Format(UiStrings.PresetPasteFailedFmt, UiStrings.PasteReasonNoClipboard);
            }
        }

        private void OnPasteClicked()
        {
            PresetCategory? cat = TabCategory[_tab];
            if (cat == null)
            {
                return;
            }
            string text;
            try
            {
                text = GUIUtility.systemCopyBuffer;
            }
            catch (Exception)
            {
                _status.text = string.Format(UiStrings.PresetPasteFailedFmt, UiStrings.PasteReasonNoClipboard);
                return;
            }
            _status.text = PasteStatus(PresetCodec.Import(cat.Value, text));
            if (cat.Value != PresetCategory.Steering)
            {
                _tuner.ApplyLive();
            }
            Refresh();
        }

        /// <summary>Status line for a paste result (pure, for the harness).</summary>
        public static string PasteStatus(PresetCodec.Result r)
        {
            if (!r.Ok)
            {
                string why = r.Why == PresetCodec.Failure.Empty ? UiStrings.PasteReasonEmpty
                    : r.Why == PresetCodec.Failure.WrongCategory ? UiStrings.PasteReasonWrongCategory
                    : r.Why == PresetCodec.Failure.WrongTag ? UiStrings.PasteReasonWrongTag
                    : UiStrings.PasteReasonMalformed;
                return string.Format(UiStrings.PresetPasteFailedFmt, why);
            }
            string s = string.Format(UiStrings.PresetPastedFmt, r.Applied);
            if (r.Unknown > 0)
            {
                s += " " + string.Format(UiStrings.PresetPasteSkippedFmt, r.Unknown);
            }
            return s;
        }

        // ================================================================ lifecycle

        /// <summary>Called every frame while visible.</summary>
        public void Tick()
        {
            if (_resetArmedUntil > 0f && Time.unscaledTime > _resetArmedUntil)
            {
                DisarmReset();
            }
            if (_allOffArmedUntil > 0f && Time.unscaledTime > _allOffArmedUntil)
            {
                _allOffArmedUntil = -1f;
                UpdateResetButton();
            }
            // The vehicle count changes as vehicles spawn; refresh it now and then
            // (and follow screen-resolution changes).
            if (Time.unscaledTime > _nextStatusRefresh)
            {
                _nextStatusRefresh = Time.unscaledTime + 1f;
                ApplyDisplaySettings();
                Refresh();
            }
        }

        public void OnOpened()
        {
            DisarmReset();
            _allOffArmedUntil = -1f;
            ApplyDisplaySettings();
            ShowTab(UiSettings.ClampTab(UiSettings.LastTab));
            Refresh();
        }

        public void Refresh()
        {
            if (Root == null)
            {
                return;
            }
            _suppress = true;
            try
            {
                for (int i = 0; i < _refreshers.Count; i++)
                {
                    _refreshers[i]();
                }
            }
            finally
            {
                _suppress = false;
            }
        }
    }
}
