using System;
using System.Collections.Generic;
using ApocalypterSteeringMod.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// A small click-through strip with the active vehicle's speed, RPM, gear and front slip
    /// (0.6.0). Lives on the hidden runner (survives scene loads with it), has its own
    /// canvas below the panel's, and every Graphic has raycastTarget = false, so it can never
    /// eat a click. One Graphic per GameObject: a background Image on the root, one Text
    /// child per value. Hidden when disabled and when there is no active vehicle (it stays
    /// visible while the panel is open). Updates at 4 Hz (allocations are fine at that rate;
    /// the physics hot paths stay allocation-free).
    ///
    /// 0.7.0 (FEATURES §4): pinned slider values (TelemetryPins) get their own cells stacked
    /// away from the fixed four, two per 26 px row; the strip grows from its corner (the fixed
    /// row hugs the corner). Geometry is the pure CellBand/StripHeight/CellAnchoredPosition —
    /// every cell is anchored to the strip's own corner, so the rows can never overlap at any
    /// corner (0.7.2 fix for the pin row overlapping the main strip).
    /// </summary>
    public sealed class TelemetryStrip : MonoBehaviour
    {
        public const float UpdateInterval = 0.25f;
        public const float Width = 440f, Height = 32f, Margin = 12f;
        public const int FixedCells = 4;
        public const int PinsPerRow = 2;
        public const float PinRowHeight = 26f, PinGap = 2f;
        public const int SortingOrder = 31000;   // below the panel (32000)

        private GameObject _canvasGo;
        private CanvasScaler _scaler;
        private RectTransform _strip;
        private readonly Text[] _cells = new Text[4];
        private VehicleTuner _tuner;
        private readonly List<Text> _pinLabels = new List<Text>();   // label (left) + value (right): two
        private readonly List<Text> _pinCells = new List<Text>();    // Text GOs so a translation pack matches the label
        private readonly List<GameObject> _pinCellObjects = new List<GameObject>();
        private int _pinsVersion = -1;
        private float _next;
        private bool _buildFailed;
        private TelemetryCorner _corner = (TelemetryCorner)(-1);

        private void Start()
        {
            _tuner = GetComponent<VehicleTuner>();
        }

        private void OnDestroy()
        {
            if (_canvasGo != null)
            {
                Destroy(_canvasGo);
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _next)
            {
                return;
            }
            _next = Time.unscaledTime + UpdateInterval;

            VehicleTuner.TelemetrySample s = default(VehicleTuner.TelemetrySample);
            // Visible while the panel is open too: the panel docks right and the strip's
            // corners are out of its way, and live feedback while tuning is the point.
            bool show = UiSettings.TelemetryEnabled && _tuner != null && _tuner.TryGetTelemetry(out s);
            if (!show)
            {
                if (_canvasGo != null && _canvasGo.activeSelf)
                {
                    _canvasGo.SetActive(false);
                }
                return;
            }
            if (_canvasGo == null && !Build())
            {
                return;
            }
            if (!_canvasGo.activeSelf)
            {
                _canvasGo.SetActive(true);
            }
            ApplyPlacement();
            _cells[0].text = UiStrings.TelemetrySpeed(s.SpeedKmh);
            _cells[1].text = UiStrings.TelemetryRpm(s.Rpm);
            _cells[2].text = UiStrings.TelemetryGear(s.Gear);
            _cells[3].text = UiStrings.TelemetrySlip(s.FrontSlip);
            UpdatePins();
        }

        /// <summary>Strip height for this many pins: the fixed row plus one 26 px row per two pins.</summary>
        public static float StripHeight(int pins)
        {
            if (pins <= 0)
            {
                return Height;
            }
            int rows = (pins + PinsPerRow - 1) / PinsPerRow;
            return Height + PinGap + rows * PinRowHeight;
        }

        /// <summary>Cell rectangle from the strip's top-left: 0..3 = the fixed cells, 4.. = pins.</summary>
        public static PanelLayout.Band CellBand(int index)
        {
            float fixedW = Width / FixedCells;
            if (index < FixedCells)
            {
                return new PanelLayout.Band(index * fixedW, 0f, fixedW, Height);
            }
            int j = index - FixedCells;
            float pinW = Width / PinsPerRow;
            return new PanelLayout.Band((j % PinsPerRow) * pinW, Height + PinGap + (j / PinsPerRow) * PinRowHeight, pinW, PinRowHeight);
        }

        private void UpdatePins()
        {
            IList<string> pins = TelemetryPins.Pins;
            if (_pinsVersion != TelemetryPins.Version)
            {
                _pinsVersion = TelemetryPins.Version;
                for (int i = 0; i < _pinCellObjects.Count; i++)
                {
                    Destroy(_pinCellObjects[i]);
                }
                _pinCellObjects.Clear();
                _pinCells.Clear();
                _pinLabels.Clear();
                for (int i = 0; i < pins.Count; i++)
                {
                    RectTransform cell = UiKit.Make("Pin" + i, _strip);
                    PlaceCell(cell, FixedCells + i, 8f, 16f);
                    _pinCellObjects.Add(cell.gameObject);
                    _pinLabels.Add(UiKit.Label(cell, TelemetryPins.Label(pins[i]), 13, UiKit.TextMuted, TextAnchor.MiddleLeft));
                    _pinCells.Add(UiKit.Label(cell, "", 13, UiKit.TextMain, TextAnchor.MiddleRight, FontStyle.Bold));
                }
                _corner = (TelemetryCorner)(-1);   // re-apply the size
            }
            for (int i = 0; i < pins.Count && i < _pinCells.Count; i++)
            {
                float v;
                string pin = pins[i];
                _pinLabels[i].text = TelemetryPins.Label(pin);   // the panel may register the slider title later
                _pinCells[i].text = TelemetryPins.TryGetValue(pin, out v) ? UiStrings.PinValue(TelemetryPins.UnitOf(pin), v) : "";
            }
        }

        /// <summary>
        /// A cell's anchored position in the strip's corner space, for any corner: Band.Top is
        /// the distance from the strip's TOP edge, so a top-corner strip places the cell at
        /// y = -Top below that edge, and a bottom-corner strip (which grows upward) places the
        /// cell's BOTTOM edge at (StripHeight - Top - H) from the strip's bottom. Pure
        /// (harness-tested): for every corner the pin rows sit strictly further from the
        /// corner than the fixed row, so they can never overlap it.
        /// </summary>
        public static Vector2 CellAnchoredPosition(TelemetryCorner corner, int index, float stripHeight)
        {
            PanelLayout.Band b = CellBand(index);
            bool top = CornerAnchor(corner).y > 0.5f;
            return top
                ? new Vector2(b.X, -b.Top)
                : new Vector2(b.X, stripHeight - b.Top - b.H);
        }

        /// <summary>Place one cell at its corner-mapped position (anchored to the strip's own corner).</summary>
        private void PlaceCell(RectTransform cell, int index, float insetX, float insetW)
        {
            PanelLayout.Band b = CellBand(index);
            b = new PanelLayout.Band(b.X + insetX, b.Top, b.W - insetW, b.H);
            float topY = CornerAnchor(UiSettings.TelemetryPosition).y;
            cell.anchorMin = new Vector2(0f, topY);
            cell.anchorMax = new Vector2(0f, topY);
            cell.pivot = new Vector2(0f, topY);
            cell.sizeDelta = new Vector2(b.W, b.H);
            Vector2 p = CellAnchoredPosition(UiSettings.TelemetryPosition, index, StripHeight(TelemetryPins.Count));
            cell.anchoredPosition = new Vector2(p.x + insetX, p.y);
        }

        private bool Build()
        {
            if (_buildFailed)
            {
                return false;
            }
            try
            {
                _canvasGo = new GameObject("ApocalypterTelemetryCanvas", typeof(RectTransform));
                _canvasGo.layer = 5;
                _canvasGo.transform.SetParent(transform, false);
                var canvas = _canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;
                _scaler = _canvasGo.AddComponent<CanvasScaler>();
                _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                // No GraphicRaycaster: nothing on this canvas can ever receive a click.

                _strip = UiKit.Make("Strip", (RectTransform)_canvasGo.transform);
                UiKit.Paint(_strip, new Color(0.07f, 0.08f, 0.10f, 0.72f), false);
                for (int i = 0; i < _cells.Length; i++)
                {
                    RectTransform cell = UiKit.Make("Cell" + i, _strip);
                    PlaceCell(cell, i, 6f, 12f);
                    _cells[i] = UiKit.Label(cell, "", 15, UiKit.TextMain, TextAnchor.MiddleCenter, FontStyle.Bold);
                }
                return true;
            }
            catch (Exception ex)
            {
                _buildFailed = true;
                Plugin.Log.LogError("Telemetry strip build failed: " + ex);
                if (_canvasGo != null)
                {
                    Destroy(_canvasGo);
                    _canvasGo = null;
                }
                return false;
            }
        }

        /// <summary>Anchor (and pivot) of a corner: (0|1, 0|1).</summary>
        public static Vector2 CornerAnchor(TelemetryCorner c)
        {
            switch (c)
            {
                case TelemetryCorner.TopLeft: return new Vector2(0f, 1f);
                case TelemetryCorner.TopRight: return new Vector2(1f, 1f);
                case TelemetryCorner.BottomRight: return new Vector2(1f, 0f);
                default: return new Vector2(0f, 0f);
            }
        }

        /// <summary>Offset from that corner, pointing into the screen.</summary>
        public static Vector2 CornerOffset(TelemetryCorner c, float margin)
        {
            Vector2 a = CornerAnchor(c);
            return new Vector2(a.x > 0.5f ? -margin : margin, a.y > 0.5f ? -margin : margin);
        }

        private void ApplyPlacement()
        {
            _scaler.scaleFactor = PanelLayout.ScaleFactor(Screen.height, UiSettings.TelemetryScale);
            if (_corner == UiSettings.TelemetryPosition)
            {
                return;
            }
            _corner = UiSettings.TelemetryPosition;
            Vector2 a = CornerAnchor(_corner);
            _strip.anchorMin = a;
            _strip.anchorMax = a;
            _strip.pivot = a;
            _strip.sizeDelta = new Vector2(Width, StripHeight(TelemetryPins.Count));
            _strip.anchoredPosition = CornerOffset(_corner, Margin);
        }
    }
}
