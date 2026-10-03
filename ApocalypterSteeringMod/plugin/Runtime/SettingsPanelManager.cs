using System;
using System.Collections.Generic;
using ApocalypterSteeringMod.Persistence;
using HutongGames.PlayMaker;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// Lives on the hidden runner GameObject. Polls the panel hotkey, injects a
    /// "TUNING" button into every visible game menu canvas (cloned from the game's own
    /// menu buttons, the Apocasetter recipe), and manages the settings panel:
    /// open/close, cursor and EventSystem handling, lazy build, auto-save.
    /// </summary>
    public sealed class SettingsPanelManager : MonoBehaviour
    {
        private const string MenuButtonText = "Vehicle Tuning";

        // ------------------------------------------------------------- hotkey
        private KeyCode _toggleKey = KeyCode.F7;
        private Key _toggleInputKey = Key.F7;

        // ------------------------------------------------------------- panel state
        private SettingsPanel _panel;
        private bool _buildFailed;
        private bool _visible;
        private bool _unblockNextFrame;
        private CursorLockMode _prevLockState;
        private bool _prevCursorVisible;
        private EventSystem _eventSystem;
        private bool _eventSystemWasEnabled;
        private bool _createdEventSystem;

        // ------------------------------------------------------------- menu button
        private sealed class Slot
        {
            public Canvas Canvas;
            public GameObject Button;
            public int InjectAttempts;
            // Refilled in place every scan (no per-scan array allocation).
            public readonly List<Button> GameButtons = new List<Button>();
        }

        // A canvas whose clone keeps failing (or keeps being destroyed) is retried this
        // many times, then left alone so the log is not spammed four times a second.
        private const int MaxInjectAttempts = 5;

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly List<Button> _scratchButtons = new List<Button>();
        private float _nextScan;
        private static readonly string[] TemplateNames =
        {
            "Settings", "Credits", "Tutorial", "Codex", "Quit", "Quit_To_Menu", "Exit", "Options"
        };

        private void Awake()
        {
            if (!Enum.TryParse(ModConfig.ToggleKeyString, true, out _toggleKey) || _toggleKey == KeyCode.None)
            {
                _toggleKey = KeyCode.F7;
                Plugin.Log.LogWarning("Unrecognized UI.ToggleKey '" + ModConfig.ToggleKeyString + "'; using F7.");
            }
            try
            {
                _toggleInputKey = (Key)Enum.Parse(typeof(Key), _toggleKey.ToString(), true);
            }
            catch
            {
                _toggleInputKey = Key.None;   // fall back to the legacy Input path
            }
        }

        private void OnEnable()
        {
            ModConfig.SettingsChanged += OnSettingsChanged;
        }

        private void OnDisable()
        {
            ModConfig.SettingsChanged -= OnSettingsChanged;
            if (_visible)
            {
                // Runner going away while open: never leave the game frozen or the cursor stuck.
                SetVisible(false);
                InputBlocker.Set(false);
            }
            else if (_unblockNextFrame)
            {
                // Closed this frame and disabled before the deferred unblock ran.
                _unblockNextFrame = false;
                InputBlocker.Set(false);
            }
        }

        private void OnDestroy()
        {
            // The clones live inside the GAME's canvases, not under the runner, so they
            // would outlive this manager (a replacement runner would then add a second
            // button whose twin calls into a dead manager).
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Button != null)
                {
                    Destroy(_slots[i].Button);
                }
            }
            _slots.Clear();
        }

        private void Update()
        {
            // Keep blocking for one extra frame after closing so the game never
            // sees the key press that closed the panel.
            if (_unblockNextFrame)
            {
                _unblockNextFrame = false;
                if (!_visible)
                {
                    InputBlocker.Set(false);
                }
            }

            if (IsKeyPressed(_toggleKey, _toggleInputKey))
            {
                SetVisible(!_visible);
            }
            else if (_visible && IsKeyPressed(KeyCode.Escape, Key.Escape))
            {
                SetVisible(false);
            }

            if (_visible && _panel != null)
            {
                _panel.Tick();
            }

            TickMenuButton();
        }

        private void LateUpdate()
        {
            // The game re-locks the cursor through its FSMs; keep re-freeing it
            // for as long as the panel is open.
            if (_visible)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void OnSettingsChanged()
        {
            // Config edited externally (e.g. a config manager) while the panel exists.
            if (_panel != null)
            {
                _panel.Refresh();
            }
        }

        /// <summary>
        /// Reads a key through the new Input System when a Keyboard device is
        /// present, falling back to the legacy Input class otherwise.
        /// </summary>
        private static bool IsKeyPressed(KeyCode legacy, Key newKey)
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && newKey != Key.None)
            {
                return kb[newKey].wasPressedThisFrame;
            }
            return Input.GetKeyDown(legacy);
        }

        // ------------------------------------------------------------- menu button

        private static bool IsTemplateName(string n)
        {
            for (int i = 0; i < TemplateNames.Length; i++)
            {
                if (string.Equals(TemplateNames[i], n, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsVisibleMenuButton(Button b, GameObject ours)
        {
            if (b == null || !b.isActiveAndEnabled)
            {
                return false;
            }
            if (ours != null && (b.gameObject == ours || b.transform.IsChildOf(ours.transform)))
            {
                return false;
            }
            return IsTemplateName(b.gameObject.name);
        }

        private Slot FindSlot(Canvas c)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Canvas == c)
                {
                    return _slots[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Every enabled menu canvas that shows game menu buttons gets one cloned
        /// "Vehicle Tuning" button, shown only while that menu is showing.
        /// </summary>
        private void TickMenuButton()
        {
            if (Time.unscaledTime < _nextScan)
            {
                return;
            }
            _nextScan = Time.unscaledTime + 0.25f;
            _slots.RemoveAll(x => x.Canvas == null);

            Canvas[] canvases;
            try
            {
                canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            }
            catch
            {
                return;
            }

            for (int ci = 0; ci < canvases.Length; ci++)
            {
                Canvas c = canvases[ci];
                if (c == null || !c.isActiveAndEnabled || c.transform.lossyScale.x <= 0.0001f)
                {
                    continue;
                }
                if (_panel != null && c.gameObject == _panel.Root)
                {
                    continue;
                }
                Slot slot = FindSlot(c);
                if (slot == null)
                {
                    c.GetComponentsInChildren(true, _scratchButtons);
                    bool hasTemplate = false;
                    for (int i = 0; i < _scratchButtons.Count; i++)
                    {
                        if (IsVisibleMenuButton(_scratchButtons[i], null))
                        {
                            hasTemplate = true;
                            break;
                        }
                    }
                    _scratchButtons.Clear();
                    if (!hasTemplate)
                    {
                        continue;
                    }
                    slot = new Slot { Canvas = c };
                    _slots.Add(slot);
                }
                if (slot.Button == null && slot.InjectAttempts < MaxInjectAttempts)
                {
                    // First injection, or the clone was destroyed (e.g. the menu rebuilt
                    // its children): inject again instead of leaving the menu without one.
                    slot.InjectAttempts++;
                    InjectButton(slot);
                }
                c.GetComponentsInChildren(true, slot.GameButtons);
            }

            for (int si = 0; si < _slots.Count; si++)
            {
                Slot slot = _slots[si];
                if (slot.Button == null)
                {
                    continue;
                }
                Canvas c = slot.Canvas;
                bool vis = !_visible && c.isActiveAndEnabled && c.transform.lossyScale.x > 0.0001f;
                if (vis)
                {
                    vis = false;
                    List<Button> buttons = slot.GameButtons;
                    for (int i = 0; i < buttons.Count; i++)
                    {
                        if (IsVisibleMenuButton(buttons[i], slot.Button))
                        {
                            vis = true;
                            break;
                        }
                    }
                }
                if (vis != slot.Button.activeSelf)
                {
                    slot.Button.SetActive(vis);
                }
                if (vis)
                {
                    slot.Button.transform.SetAsLastSibling();
                }
            }
        }

        private void InjectButton(Slot slot)
        {
            Canvas canvas = slot.Canvas;
            try
            {
                Button[] buttons = canvas.GetComponentsInChildren<Button>(true);
                Button template = null;
                int best = int.MaxValue;
                for (int i = 0; i < buttons.Length; i++)
                {
                    string n = buttons[i].gameObject.name;
                    for (int t = 0; t < TemplateNames.Length && t < best; t++)
                    {
                        if (string.Equals(TemplateNames[t], n, StringComparison.OrdinalIgnoreCase))
                        {
                            best = t;
                            template = buttons[i];
                        }
                    }
                }
                if (template == null)
                {
                    return;
                }

                GameObject go = UnityEngine.Object.Instantiate(template.gameObject, canvas.transform);
                go.name = "ApocalypterSteeringMod_Button";
                // Strip the game's logic (PlayMaker FSMs, nested confirm dialogs).
                foreach (PlayMakerFSM f in go.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    UnityEngine.Object.DestroyImmediate(f);
                }
                for (int i = go.transform.childCount - 1; i >= 0; i--)
                {
                    Transform child = go.transform.GetChild(i);
                    bool isText = child.GetComponentInChildren<Text>(true) != null || child.GetComponentInChildren<TMP_Text>(true) != null;
                    bool isGraphic = child.GetComponent<Graphic>() != null;
                    if (!isText && !isGraphic)
                    {
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                    }
                    else
                    {
                        child.gameObject.SetActive(true);
                    }
                }
                foreach (Text tx in go.GetComponentsInChildren<Text>(true))
                {
                    tx.text = IsAllCaps(tx.text) ? MenuButtonText.ToUpperInvariant() : MenuButtonText;
                }
                foreach (TMP_Text tx in go.GetComponentsInChildren<TMP_Text>(true))
                {
                    tx.text = IsAllCaps(tx.text) ? MenuButtonText.ToUpperInvariant() : MenuButtonText;
                }

                Button btn = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>(true);
                btn.onClick = new Button.ButtonClickedEvent();
                btn.onClick.AddListener(() => SetVisible(true));
                btn.interactable = true;
                CanvasGroup cg = go.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = 1f;
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                }

                // Escape any layout group and pin to the top-right corner.
                LayoutElement le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
                RectTransform rt = (RectTransform)go.transform;
                Vector2 size = ((RectTransform)template.transform).rect.size;
                if (size.x < 10f || size.y < 10f)
                {
                    size = new Vector2(240f, 50f);
                }
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.sizeDelta = new Vector2(Mathf.Max(size.x, 240f), size.y);
                rt.anchoredPosition = new Vector2(-30f, -30f);
                rt.localScale = Vector3.one;
                go.transform.SetAsLastSibling();
                go.SetActive(false);
                slot.Button = go;
                Plugin.Log.LogInfo("Menu button injected into canvas '" + canvas.name + "'.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Could not inject menu button into " + canvas.name + ": " + ex.Message);
            }
        }

        private static bool IsAllCaps(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            bool sawLetter = false;
            foreach (char ch in s)
            {
                if (char.IsLetter(ch))
                {
                    sawLetter = true;
                    if (char.IsLower(ch))
                    {
                        return false;
                    }
                }
            }
            return sawLetter;
        }

        // ------------------------------------------------------------- panel

        public void SetVisible(bool visible)
        {
            if (_visible == visible)
            {
                return;
            }

            if (visible && _panel == null)
            {
                if (_buildFailed)
                {
                    return;   // already logged; don't spam rebuild attempts on every key press
                }
                try
                {
                    _panel = SettingsPanel.Create(GetComponent<VehicleTuner>(), () => SetVisible(false));
                }
                catch (Exception ex)
                {
                    // A failed build must never brick the game: clean up and stay closed.
                    _buildFailed = true;
                    Plugin.Log.LogError("Tuning panel build failed: " + ex);
                    Transform stale = transform.Find("ApocalypterSettingsCanvas");
                    if (stale != null)
                    {
                        Destroy(stale.gameObject);
                    }
                    _panel = null;
                    return;
                }
            }

            _visible = visible;
            if (_panel != null)
            {
                _panel.Root.SetActive(visible);
            }

            if (visible)
            {
                OnPanelOpened();
            }
            else
            {
                OnPanelClosed();
            }
        }

        private void OnPanelOpened()
        {
            _prevLockState = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            InputBlocker.Set(true);
            _unblockNextFrame = false;

            _eventSystem = EventSystem.current != null ? EventSystem.current : UnityEngine.Object.FindObjectOfType<EventSystem>();
            if (_eventSystem != null)
            {
                _eventSystemWasEnabled = _eventSystem.enabled;
                _eventSystem.enabled = true;
                _createdEventSystem = false;
            }
            else
            {
                var go = new GameObject("ModEventSystem", typeof(EventSystem), typeof(StandaloneInputModule))
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                DontDestroyOnLoad(go);
                _eventSystem = go.GetComponent<EventSystem>();
                _eventSystemWasEnabled = true;
                _createdEventSystem = true;
            }
            // Clear any game menu selection so Enter/Space can't trigger a button behind the panel.
            _eventSystem.SetSelectedGameObject(null);

            // Pick up a steering-speed change made in the game's own options since startup
            // (read-only; replaces the old "Reload game settings" button).
            Game.GameSettingsReader.Read();
            Settings.SteeringSettings.UpdateGameSteeringSpeedFactor();

            VehicleTuner tuner = GetComponent<VehicleTuner>();
            if (tuner != null)
            {
                tuner.ReapplyNow();   // so the panel's vehicle count is current
            }
            _panel.OnOpened();
        }

        private void OnPanelClosed()
        {
            ModConfig.Save();

            Cursor.lockState = _prevLockState;
            Cursor.visible = _prevCursorVisible;
            _unblockNextFrame = true;

            if (_eventSystem != null)
            {
                _eventSystem.SetSelectedGameObject(null);
            }
            if (_createdEventSystem && _eventSystem != null)
            {
                Destroy(_eventSystem.gameObject);
            }
            else if (_eventSystem != null)
            {
                _eventSystem.enabled = _eventSystemWasEnabled;
            }
            _createdEventSystem = false;
            _eventSystem = null;
        }
    }
}
