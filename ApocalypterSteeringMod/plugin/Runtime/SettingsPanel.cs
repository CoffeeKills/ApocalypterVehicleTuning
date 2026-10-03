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
    /// The tuning panel: one centred window with seven tabs (Steering, Suspension,
    /// Aero, Brakes, Grip, Drivetrain, Assists). Every category follows the same
    /// recipe: master ON/OFF switch, preset buttons, labelled sliders with hints,
    /// live values, "changed" highlight and per-slider Reset. Moving any slider on
    /// a built-in preset copies it into "Custom (Base)" so presets stay intact.
    /// Everything applies live and is saved when the panel closes.
    /// </summary>
    public sealed class SettingsPanel
    {
        private const float WindowW = 800f;
        private const float WindowH = 880f;
        private const float RowH = 58f;
        private const float Pad = 24f;
        private const float Dimmed = 0.35f;

        private const int TabCount = 7;
        private static readonly string[] TabNames = { "Steering", "Suspension", "Aero", "Brakes", "Grip", "Drivetrain", "Assists" };
        private static readonly string[] DiffModeLabels = { "Stock", "Open", "Locked", "LSD" };

        public GameObject Root;

        private readonly VehicleTuner _tuner;
        private readonly Action _requestClose;
        private readonly List<Action> _refreshers = new List<Action>();
        private bool _suppress;

        private int _tab;
        private readonly Image[] _tabBg = new Image[TabCount];
        private readonly Image[] _tabUnderline = new Image[TabCount];
        private readonly GameObject[] _pages = new GameObject[TabCount];
        private readonly ScrollRect[] _scrolls = new ScrollRect[TabCount];

        private Button _resetButton;
        private Text _resetLabel;
        private float _resetArmedUntil = -1f;
        private float _nextStatusRefresh;
        private Action[] _resetActions;
        private string[] _resetLabels;

        public static SettingsPanel Create(VehicleTuner tuner, Action requestClose)
        {
            return new SettingsPanel(tuner, requestClose);
        }

        private SettingsPanel(VehicleTuner tuner, Action requestClose)
        {
            _tuner = tuner;
            _requestClose = requestClose;
            _resetActions = new Action[]
            {
                () => SteeringSettings.ResetAll(),
                () => { SuspensionSettings.ResetAll(); _tuner.ReapplyNow(); },
                () => { AeroSettings.ResetAll(); _tuner.ReapplyNow(); },
                () => { BrakesSettings.ResetAll(); _tuner.ReapplyNow(); },
                () => { GripSettings.ResetAll(); _tuner.ReapplyNow(); },
                () => { DrivetrainSettings.ResetAll(); _tuner.ReapplyNow(); },
                () => { AssistsSettings.ResetAll(); _tuner.ReapplyNow(); }
            };
            _resetLabels = new[]
            {
                "Reset all steering", "Reset all suspension", "Reset all aero", "Reset all brakes",
                "Reset all grip", "Reset all drivetrain", "Reset all assists"
            };
            Build();
            ShowTab(0);
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
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;   // scale by height, so the window always fits vertically
            canvasGo.AddComponent<GraphicRaycaster>();

            RectTransform rootRt = (RectTransform)canvasGo.transform;

            // Dim layer: blocks clicks to the game, and clicking it closes the panel like any modal.
            RectTransform dim = UiKit.Make("Dim", rootRt);
            UiKit.Fill(dim);
            Image dimImg = UiKit.Paint(dim, new Color(0f, 0f, 0f, 0.55f), true);
            Button dimBtn = dim.gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.targetGraphic = dimImg;
            dimBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            dimBtn.onClick.AddListener(() => _requestClose());

            RectTransform win = UiKit.Make("Window", rootRt);
            win.anchorMin = win.anchorMax = new Vector2(0.5f, 0.5f);
            win.pivot = new Vector2(0.5f, 0.5f);
            win.sizeDelta = new Vector2(WindowW, WindowH);
            win.anchoredPosition = Vector2.zero;
            UiKit.Paint(win, UiKit.WindowBg, true);   // eats clicks so they never reach the dim layer

            BuildHeader(win);
            BuildTabs(win);
            BuildFooter(win);

            _pages[0] = BuildPage(win, "SteeringPage", 0, BuildSteering);
            _pages[1] = BuildPage(win, "SuspensionPage", 1, BuildSuspension);
            _pages[2] = BuildPage(win, "AeroPage", 2, BuildAero);
            _pages[3] = BuildPage(win, "BrakesPage", 3, BuildBrakes);
            _pages[4] = BuildPage(win, "GripPage", 4, BuildGrip);
            _pages[5] = BuildPage(win, "DrivetrainPage", 5, BuildDrivetrain);
            _pages[6] = BuildPage(win, "AssistsPage", 6, BuildAssists);
        }

        private void BuildHeader(RectTransform win)
        {
            RectTransform title = UiKit.Top(UiKit.Make("Title", win), 20f, 34f, Pad, 80f);
            UiKit.Label(title, "Vehicle Tuning", 28, UiKit.TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);

            RectTransform sub = UiKit.Top(UiKit.Make("Subtitle", win), 56f, 22f, Pad, 80f);
            UiKit.Label(sub, "Changes apply instantly and are saved when you close.  "
                + ModConfig.ToggleKeyString + " or Esc closes.", 15, UiKit.TextMuted, TextAnchor.MiddleLeft);

            Button close = UiKit.MakeButton(win, "Close", "X", UiKit.RowBase, 20, () => _requestClose(), out Text _);
            RectTransform crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(46f, 46f);
            crt.anchoredPosition = new Vector2(-16f, -16f);
        }

        private void BuildTabs(RectTransform win)
        {
            RectTransform bar = UiKit.Top(UiKit.Make("Tabs", win), 94f, 50f, Pad, Pad);
            RectTransform line = UiKit.Bottom(UiKit.Make("Line", bar), 0f, 1f);
            UiKit.Paint(line, UiKit.Divider, false);

            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                Button b = UiKit.MakeButton(bar, "Tab_" + TabNames[i], TabNames[i], UiKit.RowBase, 16, () => ShowTab(index), out Text _);
                UiKit.Place((RectTransform)b.transform, i / (float)TabCount, 0f, (i + 1) / (float)TabCount, 1f,
                    i == 0 ? 0f : 2f, 0f, i == TabCount - 1 ? 0f : 2f, 0f);
                _tabBg[i] = (Image)b.targetGraphic;
                RectTransform under = UiKit.Bottom(UiKit.Make("Underline", (RectTransform)b.transform), 0f, 3f);
                _tabUnderline[i] = UiKit.Paint(under, UiKit.Accent, false);
            }
        }

        private GameObject BuildPage(RectTransform win, string name, int index, Action<RectTransform> fill)
        {
            RectTransform host = UiKit.Make(name, win);
            UiKit.Place(host, 0f, 0f, 1f, 1f, Pad, 88f, Pad - 10f, 158f);
            RectTransform content = UiKit.MakeScroll(host, "Scroll", out ScrollRect scroll);
            _scrolls[index] = scroll;
            fill(content);
            return host.gameObject;
        }

        private void BuildFooter(RectTransform win)
        {
            RectTransform bar = UiKit.Bottom(UiKit.Make("Footer", win), 0f, 80f);
            RectTransform line = UiKit.Top(UiKit.Make("Line", bar), 0f, 1f, Pad, Pad);
            UiKit.Paint(line, UiKit.Divider, false);

            _resetButton = UiKit.MakeButton(bar, "ResetTab", "", UiKit.RowBase, 16, OnResetClicked, out _resetLabel);
            UiKit.LeftBox((RectTransform)_resetButton.transform, 320f, 46f, Pad);

            Button done = UiKit.MakeButton(bar, "Done", "Done", UiKit.Accent, 19, () => _requestClose(), out Text _);
            UiKit.RightBox((RectTransform)done.transform, 180f, 46f, Pad);
        }

        private void ShowTab(int index)
        {
            _tab = index;
            for (int i = 0; i < _pages.Length; i++)
            {
                bool on = i == index;
                _pages[i].SetActive(on);
                _tabUnderline[i].enabled = on;
                _tabBg[i].color = on ? UiKit.ChipBase : UiKit.RowNormal;
            }
            _scrolls[index].verticalNormalizedPosition = 1f;
            DisarmReset();
        }

        // ================================================================ row builders

        private static RectTransform AddBlock(RectTransform content, string name, float height, bool background)
        {
            RectTransform rt = UiKit.Make(name, content);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            if (background)
            {
                UiKit.Paint(rt, UiKit.RowNormal, false);
            }
            return rt;
        }

        private static void AddSectionTitle(RectTransform content, string text)
        {
            RectTransform rt = AddBlock(content, "Section_" + text, 38f, false);
            RectTransform t = UiKit.Place(UiKit.Make("T", rt), 0f, 0f, 1f, 1f, 2f, 4f, 0f, 0f);
            UiKit.Label(t, text.ToUpperInvariant(), 14, UiKit.TextMuted, TextAnchor.LowerLeft, FontStyle.Bold);
        }

        private static Text AddNote(RectTransform content, string name, float height, int size = 16)
        {
            RectTransform block = AddBlock(content, name, height, false);
            RectTransform t = UiKit.Place(UiKit.Make("T", block), 0f, 0f, 1f, 1f, 4f, 0f, 4f, 0f);
            return UiKit.Label(t, "", size, UiKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Normal, true);
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

        /// <summary>A pill switch showing ON (green) / OFF (grey).</summary>
        private void AddSwitch(RectTransform row, float width, float height, int fontSize, Func<bool> get, Action<bool> set)
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
            UiKit.RightBox((RectTransform)pill.transform, width, height, 14f);
            Image img = (Image)pill.targetGraphic;
            _refreshers.Add(() =>
            {
                bool on = get();
                img.color = on ? UiKit.Good : UiKit.SwitchOff;
                label.text = on ? "ON" : "OFF";
            });
        }

        /// <summary>The big ON/OFF row at the top of each tab.</summary>
        private void AddMasterSwitch(RectTransform content, string title, string hint, Func<bool> get, Action<bool> set)
        {
            RectTransform row = AddBlock(content, "Master", 76f, true);
            RectTransform t = UiKit.Place(UiKit.Make("Title", row), 0f, 0.5f, 1f, 1f, 18f, 0f, 160f, 8f);
            UiKit.Label(t, title, 21, UiKit.TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform h = UiKit.Place(UiKit.Make("Hint", row), 0f, 0f, 1f, 0.5f, 18f, 8f, 160f, 0f);
            UiKit.Label(h, hint, 15, UiKit.TextMuted, TextAnchor.MiddleLeft);
            AddSwitch(row, 120f, 44f, 18, get, set);
        }

        /// <summary>Secondary on/off option with a one-line explanation.</summary>
        private Text AddOption(RectTransform content, string title, string hint, Func<bool> get, Action<bool> set,
            Func<bool> enabled = null)
        {
            RectTransform row = AddBlock(content, "Opt_" + title, RowH, true);
            CanvasGroup group = row.gameObject.AddComponent<CanvasGroup>();
            RectTransform t = UiKit.Place(UiKit.Make("Title", row), 0f, 0.5f, 1f, 1f, 16f, 0f, 120f, 6f);
            UiKit.Label(t, title, 17, UiKit.TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform h = UiKit.Place(UiKit.Make("Hint", row), 0f, 0f, 1f, 0.5f, 16f, 6f, 120f, 0f);
            Text hintText = UiKit.Label(h, hint, 14, UiKit.TextMuted, TextAnchor.MiddleLeft);
            AddSwitch(row, 86f, 34f, 15, get, set);
            if (enabled != null)
            {
                BindGroup(group, enabled);
            }
            return hintText;
        }

        /// <summary>Grid of preset buttons; the active one is filled with the accent colour.</summary>
        private void AddPresetButtons(RectTransform content, string name, int count, int perRow,
            Func<int, string> label, Func<int> active, Action<int> select)
        {
            const float h = 48f, gap = 8f;
            int rows = (count + perRow - 1) / perRow;
            RectTransform block = AddBlock(content, name, rows * h + (rows - 1) * gap, false);

            var images = new Image[count];
            var texts = new Text[count];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                int r = i / perRow, c = i % perRow;
                float top = r * (h + gap);
                Button b = UiKit.MakeButton(block, "Preset" + i, "", UiKit.ChipBase, 17, () =>
                {
                    if (_suppress)
                    {
                        return;
                    }
                    select(index);
                    Refresh();
                }, out texts[i]);
                RectTransform rt = (RectTransform)b.transform;
                rt.anchorMin = new Vector2((float)c / perRow, 1f);
                rt.anchorMax = new Vector2((float)(c + 1) / perRow, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(c == 0 ? 0f : gap / 2f, -(top + h));
                rt.offsetMax = new Vector2(c == perRow - 1 ? 0f : -gap / 2f, -top);
                images[i] = (Image)b.targetGraphic;
            }

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
        /// Name and plain-language hint on the left, slider in the middle, value (and an
        /// optional absolute readout below it) and Reset on the right. The value turns
        /// blue when it differs from the reference; Reset puts it back.
        /// </summary>
        private GameObject AddSlider(RectTransform content, string title, string hint, float min, float max,
            Func<float> get, Action<float> set, Func<float> reference, Func<float, string> format,
            Func<bool> enabled = null, Func<float, string> readout = null)
        {
            RectTransform row = AddBlock(content, "Row_" + title, RowH, true);
            CanvasGroup group = row.gameObject.AddComponent<CanvasGroup>();

            // Columns (content is ~748 px wide): label 16-316 | slider | value | reset.
            RectTransform t = UiKit.Place(UiKit.Make("Title", row), 0f, 0.5f, 0f, 1f, 16f, 0f, -316f, 6f);
            UiKit.Label(t, title, 17, UiKit.TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform h = UiKit.Place(UiKit.Make("Hint", row), 0f, 0f, 0f, 0.5f, 16f, 6f, -316f, 0f);
            UiKit.Label(h, hint, 13, UiKit.TextMuted, TextAnchor.MiddleLeft);

            float sliderRight = readout != null ? 236f : 172f;
            Slider slider = UiKit.MakeSlider(row, "Slider");
            UiKit.Place((RectTransform)slider.transform, 0f, 0f, 1f, 1f, 328f, 4f, sliderRight, 4f);
            slider.minValue = min;
            slider.maxValue = max;
            slider.onValueChanged.AddListener(v =>
            {
                if (_suppress)
                {
                    return;
                }
                set(v);
                Refresh();
            });

            float valWidth = readout != null ? 150f : 86f;
            RectTransform valRt = UiKit.RightBox(UiKit.Make("Value", row), valWidth, RowH, 82f);
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
            UiKit.RightBox((RectTransform)reset.transform, 66f, 32f, 12f);

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
                if (readout != null)
                {
                    valueReadout.text = readout(v);
                }
                reset.interactable = changed;
                resetLabel.color = changed ? UiKit.TextMain : UiKit.TextMuted;
            });
            return row.gameObject;
        }

        private static string Times(float v) { return "×" + v.ToString("0.00"); }
        private static string Force(float n) { return n.ToString("N0") + " N"; }
        private static string Rate(float r) { return r.ToString("N0") + " N·s/m"; }
        private static string Length(float m) { return Mathf.RoundToInt(m * 100f) + " cm"; }
        private static string Percent(float v) { return Mathf.RoundToInt(v * 100f) + "%"; }

        // ================================================================ shared semantics

        private static string PresetButtonLabel<T>(PresetBook<T> book, T p) where T : class, ITunablePreset
        {
            if (p == book.Custom)
            {
                T b = book.FindBuiltIn(p.BasedOn);
                return b != null ? "Custom (" + b.Label + ")" : "Custom";
            }
            return p.Label;
        }

        private static string CustomDescription<T>(PresetBook<T> book) where T : class, ITunablePreset
        {
            T b = book.FindBuiltIn(book.Custom.BasedOn);
            return b != null
                ? "Your tuning, based on " + b.Label + ". Reset on a slider returns it to the " + b.Label + " value."
                : "Your own tuning. Moving a slider on any preset copies it here, so presets stay intact.";
        }

        private void EditSuspension(Action<SuspensionPreset> edit)
        {
            SuspensionPreset p = SuspensionSettings.BeginEdit();
            if (p == null)
            {
                return;
            }
            edit(p);
            _tuner.ApplyLive();
        }

        private void EditAero(Action<AeroPreset> edit)
        {
            AeroPreset p = AeroSettings.BeginEdit();
            if (p == null)
            {
                return;
            }
            edit(p);
            _tuner.ApplyLive();
        }

        private void EditBrakes(Action<BrakesPreset> edit)
        {
            BrakesPreset p = BrakesSettings.BeginEdit();
            if (p == null)
            {
                return;
            }
            edit(p);
            _tuner.ApplyLive();
        }

        private void EditGrip(Action<GripPreset> edit)
        {
            GripPreset p = GripSettings.BeginEdit();
            if (p == null)
            {
                return;
            }
            edit(p);
            _tuner.ApplyLive();
        }

        private void EditDrivetrain(Action<DrivetrainPreset> edit)
        {
            DrivetrainPreset p = DrivetrainSettings.BeginEdit();
            if (p == null)
            {
                return;
            }
            edit(p);
            _tuner.ApplyLive();
        }

        private void EditAssists(Action<AssistsPreset> edit)
        {
            AssistsPreset p = AssistsSettings.BeginEdit();
            if (p == null)
            {
                return;
            }
            edit(p);
            _tuner.ApplyLive();
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

        private void BuildSteering(RectTransform content)
        {
            AddMasterSwitch(content, "Steering tuning", "OFF = the game's original steering, exactly as shipped.",
                () => SteeringSettings.Enabled, v => SteeringSettings.Enabled = v);

            CanvasGroup body = AddGroup(content, "Body", out RectTransform c);
            BindGroup(body, () => SteeringSettings.Enabled);

            AddSectionTitle(c, "Preset");
            SteeringPreset[] presets = SteeringPreset.Presets;
            AddPresetButtons(c, "Presets", presets.Length, 3,
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
                    desc.text = a.IsVanilla ? a.Description : a.Description + " Move any slider to customise it.";
                }
            });

            CanvasGroup tuning = AddGroup(c, "Tuning", out RectTransform t);
            BindGroup(tuning, () => !IsVanilla);

            AddSectionTitle(t, "Feel");
            AddSlider(t, "Steering speed", "How fast the wheels turn toward your input",
                Limits.RateMin, Limits.RateMax,
                () => Shown.RateMultiplier, v => EditSteering(p => p.RateMultiplier = v),
                () => SteeringSettings.Reference().RateMultiplier, Times);
            AddSlider(t, "Steering at speed", "How much lock you still get when going fast",
                Limits.CurveScaleMin, Limits.CurveScaleMax,
                () => Shown.SpeedCurveScale, v => EditSteering(p => p.SpeedCurveScale = v),
                () => SteeringSettings.Reference().SpeedCurveScale, Times);
            AddSlider(t, "Smoothing", "Higher = softer, lazier response",
                Limits.SmoothMin, Limits.SmoothMax,
                () => Shown.SmoothingScale, v => EditSteering(p => p.SmoothingScale = v),
                () => SteeringSettings.Reference().SmoothingScale, Times);

            AddSectionTitle(t, "Grip and slides");
            AddOption(t, "Grip assist", "Stops the front tyres turning past their grip limit",
                () => Shown.TractionClampEnabled, v => EditSteering(p => p.TractionClampEnabled = v));
            AddSlider(t, "Grip window", "Higher = more steering before tyres slide",
                Limits.SlipMin, Limits.SlipMax,
                () => Shown.SlipAngleDeg, v => EditSteering(p => p.SlipAngleDeg = v),
                () => SteeringSettings.Reference().SlipAngleDeg, v => v.ToString("0.0") + " deg",
                () => Shown.TractionClampEnabled);
            AddSlider(t, "Counter-steer speed", "Extra steering speed while catching a slide",
                Limits.OppLockMin, Limits.OppLockMax,
                () => Shown.OppositeLockBoost, v => EditSteering(p => p.OppositeLockBoost = v),
                () => SteeringSettings.Reference().OppositeLockBoost, Times);

            AddSectionTitle(t, "Input");
            AddOption(t, "Custom input curve", "OFF = use each car's own response curve",
                () => Shown.LinearityOverride, v => EditSteering(p => p.LinearityOverride = v));
            AddSlider(t, "Centre sensitivity", "Low = twitchy at centre, high = gentle",
                Limits.LinExpMin, Limits.LinExpMax,
                () => Shown.LinearityExponent, v => EditSteering(p => p.LinearityExponent = v),
                () => SteeringSettings.Reference().LinearityExponent, v => v.ToString("0.00"),
                () => Shown.LinearityOverride);

            AddSectionTitle(c, "Game setting");
            Text gameHint = AddOption(c, "Follow game's steering speed", "",
                () => SteeringSettings.MatchGameSteeringSpeed, v => SteeringSettings.MatchGameSteeringSpeed = v,
                () => !IsVanilla);
            _refreshers.Add(() =>
            {
                gameHint.text = GameSettingsReader.Loaded
                    ? "Also scale by the game's own steering speed option (currently " + GameSettingsReader.Steeringspeed.ToString("0") + ", 50 = normal)"
                    : "Also scale by the game's own steering speed option (not found, using 50)";
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
            AddPresetButtons(c, "Presets", presets.Length, 3,
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
                VehicleTuner.Readout.SpringForce, Force);
            AddAxleFactor(c, "Ride height", "Suspension travel; higher sits taller",
                p => p.RideHeightFront, (p, v) => p.RideHeightFront = v,
                p => p.RideHeightRear, (p, v) => p.RideHeightRear = v,
                VehicleTuner.Readout.RideHeight, Length);
            AddAxleFactor(c, "Bump damping", "Resists compression over bumps",
                p => p.BumpFront, (p, v) => p.BumpFront = v,
                p => p.BumpRear, (p, v) => p.BumpRear = v,
                VehicleTuner.Readout.BumpRate, Rate);
            AddAxleFactor(c, "Rebound damping", "Stops the body bouncing back up",
                p => p.ReboundFront, (p, v) => p.ReboundFront = v,
                p => p.ReboundRear, (p, v) => p.ReboundRear = v,
                VehicleTuner.Readout.ReboundRate, Rate);
            AddAxleFactor(c, "Anti-roll bar", "Higher = flatter in corners",
                p => p.ArbFront, (p, v) => p.ArbFront = v,
                p => p.ArbRear, (p, v) => p.ArbRear = v,
                VehicleTuner.Readout.ArbForce, Force);

            Text status = AddNote(content, "Status", 44f, 14);
            _refreshers.Add(() =>
            {
                int n = _tuner.TrackedVehicles;
                status.text = !SuspensionSettings.Enabled
                    ? "Suspension tuning is off. Vehicles use their original setup."
                    : n == 0
                        ? "No vehicles found yet. Settings apply as soon as one spawns."
                        : "Applied to " + n + (n == 1 ? " vehicle." : " vehicles.") + " A vehicle without an anti-roll bar is never given one.";
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
            Func<float, string> readoutF = v => unitFormat(v * _tuner.MeanBaseline(readout, true));
            Func<float, string> readoutR = v => unitFormat(v * _tuner.MeanBaseline(readout, false));

            GameObject both = AddSlider(content, title, hint, Limits.FactorMin, Limits.FactorMax,
                () => getF(SuspensionSettings.Shown),
                v => EditSuspension(p =>
                {
                    setF(p, v);
                    setR(p, v);
                }),
                () => getF(SuspensionSettings.Reference()), Times, null, readoutF);
            GameObject front = AddSlider(content, title + " (front)", hint, Limits.FactorMin, Limits.FactorMax,
                () => getF(SuspensionSettings.Shown), v => EditSuspension(p => setF(p, v)),
                () => getF(SuspensionSettings.Reference()), Times, null, readoutF);
            GameObject rear = AddSlider(content, title + " (rear)", hint, Limits.FactorMin, Limits.FactorMax,
                () => getR(SuspensionSettings.Shown), v => EditSuspension(p => setR(p, v)),
                () => getR(SuspensionSettings.Reference()), Times, null, readoutR);

            _refreshers.Add(() =>
            {
                bool split = SuspensionSettings.SplitFrontRear;
                if (both.activeSelf == split) both.SetActive(!split);
                if (front.activeSelf != split) front.SetActive(split);
                if (rear.activeSelf != split) rear.SetActive(split);
            });
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
            AddPresetButtons(c, "Presets", presets.Length, 3,
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
                () => AeroSettings.Reference().DownforceScale, Times);
            AddSlider(c, "Drag", "Air resistance (higher = slower top speed)",
                Limits.AeroScaleMin, Limits.AeroScaleMax,
                () => AeroSettings.Shown.DragScale, v => EditAero(p => p.DragScale = v),
                () => AeroSettings.Reference().DragScale, Times);
            AddSlider(c, "Downforce speed range", "How fast the downforce keeps growing",
                Limits.AeroSpeedScaleMin, Limits.AeroSpeedScaleMax,
                () => AeroSettings.Shown.MaxDownforceSpeedScale, v => EditAero(p => p.MaxDownforceSpeedScale = v),
                () => AeroSettings.Reference().MaxDownforceSpeedScale, Times);

            Text status = AddNote(content, "Status", 44f, 14);
            _refreshers.Add(() =>
            {
                int n = _tuner.TrackedVehicles;
                status.text = !AeroSettings.Enabled
                    ? "Aero tuning is off. Vehicles use their original aerodynamics."
                    : n == 0
                        ? "No vehicles found yet. Settings apply as soon as one spawns."
                        : "Applied to " + n + (n == 1 ? " vehicle." : " vehicles.") + " Vehicles without a downforce setup get drag tuning only.";
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
            AddPresetButtons(c, "Presets", presets.Length, 3,
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
                () => BrakesSettings.Reference().TorqueScale, Times);
            AddSlider(c, "Front brakes", "Front axle bite (brake balance)",
                Limits.BrakeAxleMin, Limits.BrakeAxleMax,
                () => BrakesSettings.Shown.FrontBrakeScale, v => EditBrakes(p => p.FrontBrakeScale = v),
                () => BrakesSettings.Reference().FrontBrakeScale, Times);
            AddSlider(c, "Rear brakes", "Rear axle bite (brake balance)",
                Limits.BrakeAxleMin, Limits.BrakeAxleMax,
                () => BrakesSettings.Shown.RearBrakeScale, v => EditBrakes(p => p.RearBrakeScale = v),
                () => BrakesSettings.Reference().RearBrakeScale, Times);
            AddSlider(c, "Handbrake", "Handbrake strength",
                Limits.BrakeAxleMin, Limits.BrakeAxleMax,
                () => BrakesSettings.Shown.HandbrakeScale, v => EditBrakes(p => p.HandbrakeScale = v),
                () => BrakesSettings.Reference().HandbrakeScale, Times);
            AddSlider(c, "Pedal response", "Higher = slower brake application",
                Limits.BrakeTorqueMin, Limits.BrakeTorqueMax,
                () => BrakesSettings.Shown.ActuationScale, v => EditBrakes(p => p.ActuationScale = v),
                () => BrakesSettings.Reference().ActuationScale, Times);

            Text status = AddNote(content, "Status", 44f, 14);
            _refreshers.Add(() =>
            {
                int n = _tuner.TrackedVehicles;
                status.text = !BrakesSettings.Enabled
                    ? "Brake tuning is off. Vehicles use their original brakes."
                    : n == 0
                        ? "No vehicles found yet. Settings apply as soon as one spawns."
                        : "Applied to " + n + (n == 1 ? " vehicle." : " vehicles.");
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
            AddPresetButtons(c, "Presets", presets.Length, 3,
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
                () => GripSettings.Reference().LongitudinalScale, Times);
            AddSlider(c, "Lateral grip", "Grip in corners",
                Limits.GripMin, Limits.GripMax,
                () => GripSettings.Shown.LateralScale, v => EditGrip(p => p.LateralScale = v),
                () => GripSettings.Reference().LateralScale, Times);
            AddSlider(c, "Tire stiffness", "How quickly the tires reach peak grip",
                Limits.GripMin, Limits.GripMax,
                () => GripSettings.Shown.StiffnessScale, v => EditGrip(p => p.StiffnessScale = v),
                () => GripSettings.Reference().StiffnessScale, Times);

            Text status = AddNote(content, "Status", 44f, 14);
            _refreshers.Add(() =>
            {
                int n = _tuner.TrackedVehicles;
                status.text = !GripSettings.Enabled
                    ? "Grip tuning is off. Vehicles use their original tires."
                    : n == 0
                        ? "No vehicles found yet. Settings apply as soon as one spawns."
                        : "Applied to " + n + (n == 1 ? " vehicle." : " vehicles.") + " Surface changes (mud, asphalt) still apply on top."
                          + (_tuner.AnyTyreWear ? " Warning: a vehicle has a tire-wear component that rewrites grip." : "");
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
            AddPresetButtons(c, "Presets", presets.Length, 3,
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
                () => DrivetrainSettings.Reference().PowerScale, Times);
            AddSlider(c, "Rev limit", "The engine's redline",
                Limits.RevLimitMin, Limits.RevLimitMax,
                () => DrivetrainSettings.Shown.RevLimiterScale, v => EditDrivetrain(p => p.RevLimiterScale = v),
                () => DrivetrainSettings.Reference().RevLimiterScale, Times);
            AddSlider(c, "Engine braking", "Off-throttle engine drag",
                Limits.FactorMin, Limits.FactorMax,
                () => DrivetrainSettings.Shown.LossScale, v => EditDrivetrain(p => p.LossScale = v),
                () => DrivetrainSettings.Reference().LossScale, Times);
            AddSlider(c, "Turbo / boost", "Forced induction gain (inert on electrics)",
                Limits.FactorMin, Limits.FactorMax,
                () => DrivetrainSettings.Shown.BoostScale, v => EditDrivetrain(p => p.BoostScale = v),
                () => DrivetrainSettings.Reference().BoostScale, Times);
            AddSlider(c, "Final drive", "Overall gearing (higher = shorter)",
                Limits.FinalDriveMin, Limits.FinalDriveMax,
                () => DrivetrainSettings.Shown.FinalDriveScale, v => EditDrivetrain(p => p.FinalDriveScale = v),
                () => DrivetrainSettings.Reference().FinalDriveScale, Times);
            AddSlider(c, "Upshift RPM", "Where the auto box shifts up (kept below redline)",
                Limits.ShiftRpmMin, Limits.ShiftRpmMax,
                () => DrivetrainSettings.Shown.UpshiftScale, v => EditDrivetrain(p => p.UpshiftScale = v),
                () => DrivetrainSettings.Reference().UpshiftScale, Times);
            AddSlider(c, "Downshift RPM", "Where the auto box shifts down",
                Limits.ShiftRpmMin, Limits.ShiftRpmMax,
                () => DrivetrainSettings.Shown.DownshiftScale, v => EditDrivetrain(p => p.DownshiftScale = v),
                () => DrivetrainSettings.Reference().DownshiftScale, Times);
            AddSlider(c, "Shift time", "How long a gear change takes",
                Limits.FactorMin, Limits.FactorMax,
                () => DrivetrainSettings.Shown.ShiftDurationScale, v => EditDrivetrain(p => p.ShiftDurationScale = v),
                () => DrivetrainSettings.Reference().ShiftDurationScale, Times);

            AddSectionTitle(c, "Differentials");
            AddPresetButtons(c, "DiffFront", DiffModeLabels.Length, DiffModeLabels.Length,
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
                diffFrontNote.text = "Front axle: " + DiffModeLabels[DiffModeIndex(DrivetrainSettings.Shown.DiffFrontMode)]
                    + ". Axles are found from the wheels each diff drives; an axle without a diff keeps its stock setup.";
            });
            AddPresetButtons(c, "DiffRear", DiffModeLabels.Length, DiffModeLabels.Length,
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
                diffRearNote.text = "Rear axle: " + DiffModeLabels[DiffModeIndex(DrivetrainSettings.Shown.DiffRearMode)]
                    + ". Centre (AWD) diffs always keep their stock type.";
            });
            AddSlider(c, "Diff stiffness", "How hard the diff locks",
                Limits.DiffScaleMin, Limits.DiffScaleMax,
                () => DrivetrainSettings.Shown.DiffStiffnessScale, v => EditDrivetrain(p => p.DiffStiffnessScale = v),
                () => DrivetrainSettings.Reference().DiffStiffnessScale, Times);
            AddSlider(c, "Diff bias (AWD)", "Front/rear split of a centre diff only",
                Limits.DiffScaleMin, Limits.DiffScaleMax,
                () => DrivetrainSettings.Shown.DiffBiasScale, v => EditDrivetrain(p => p.DiffBiasScale = v),
                () => DrivetrainSettings.Reference().DiffBiasScale, Times);

            Text status = AddNote(content, "Status", 44f, 14);
            _refreshers.Add(() =>
            {
                int n = _tuner.TrackedVehicles;
                status.text = !DrivetrainSettings.Enabled
                    ? "Drivetrain tuning is off. Vehicles use their original engine and gearing."
                    : n == 0
                        ? "No vehicles found yet. Settings apply as soon as one spawns."
                        : "Applied to " + n + (n == 1 ? " vehicle." : " vehicles.") + " The engine sound's max RPM follows the stock value.";
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
            AddPresetButtons(c, "Presets", presets.Length, 3,
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
                () => AssistsSettings.Reference().AbsCutoffSpeed, v => v.ToString("0.0") + " m/s",
                () => AssistsSettings.Shown.AbsEnabled);
            AddSlider(c, "ABS release force", "Brake strength while releasing",
                Limits.CutMultMin, Limits.CutMultMax,
                () => AssistsSettings.Shown.AbsCutMultiplier, v => EditAssists(p => p.AbsCutMultiplier = v),
                () => AssistsSettings.Reference().AbsCutMultiplier, Percent,
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
                () => AssistsSettings.Reference().TcsCutoffSpeed, v => v.ToString("0.0") + " m/s",
                () => AssistsSettings.Shown.TcsEnabled);
            AddSlider(c, "TCS cut strength", "Power allowed while spinning",
                Limits.CutMultMin, Limits.CutMultMax,
                () => AssistsSettings.Shown.TcsCutMultiplier, v => EditAssists(p => p.TcsCutMultiplier = v),
                () => AssistsSettings.Reference().TcsCutMultiplier, Percent,
                () => AssistsSettings.Shown.TcsEnabled);

            Text status = AddNote(content, "Status", 44f, 14);
            _refreshers.Add(() =>
            {
                int n = _tuner.TrackedVehicles;
                status.text = !AssistsSettings.Enabled
                    ? "Assists are off. The game's own ABS/TCS still work if a vehicle has them."
                    : n == 0
                        ? "No vehicles found yet. Assists attach as soon as one spawns."
                        : "Active on " + n + (n == 1 ? " vehicle." : " vehicles.");
            });
        }

        // ================================================================ footer reset

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
            _resetLabel.text = armed
                ? "Click again to confirm"
                : _resetLabels[_tab];
        }

        // ================================================================ lifecycle

        /// <summary>Called every frame while visible.</summary>
        public void Tick()
        {
            if (_resetArmedUntil > 0f && Time.unscaledTime > _resetArmedUntil)
            {
                DisarmReset();
            }
            // The vehicle count changes as vehicles spawn; refresh it now and then.
            if (Time.unscaledTime > _nextStatusRefresh)
            {
                _nextStatusRefresh = Time.unscaledTime + 1f;
                Refresh();
            }
        }

        public void OnOpened()
        {
            DisarmReset();
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
