using System;
using System.Collections.Generic;
using ApocalypterSteeringMod.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// A small click-through strip with the chosen vehicle readouts (0.7.4; the 0.7.0 pin
    /// feature is gone — the strip now shows a configurable list of telemetry cells: speed,
    /// RPM, gear, per-axle slip, g-forces, steering angle, pedals). Lives on the hidden
    /// runner (survives scene loads with it), has its own canvas below the panel's, and
    /// every Graphic has raycastTarget = false, so it can never eat a click. One Graphic per
    /// GameObject. Hidden while disabled and when there is no active vehicle. Updates at 4 Hz.
    /// Up to six cells per row; more wrap to a second row. Every cell is anchored to the
    /// strip's own corner through the pure CellAnchoredPosition, so rows can never overlap at
    /// any corner (the 0.7.2 fix, generalized to the cell list).
    /// </summary>
    public sealed class TelemetryStrip : MonoBehaviour
    {
        public const float UpdateInterval = 0.25f;
        public const float Width = 440f, Height = 32f, Margin = 12f;
        public const int CellsPerRow = 6;
        public const int SortingOrder = 31000;   // below the panel (32000)

        private GameObject _canvasGo;
        private CanvasScaler _scaler;
        private RectTransform _strip;
        private readonly List<Text> _cells = new List<Text>();
        private VehicleTuner _tuner;
        private int _cellsVersion = -1;
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
            UpdateCells(s);
        }

        /// <summary>Strip height for this many cells: one 32 px row per up to six cells.</summary>
        public static float StripHeight(int count)
        {
            int rows = count <= 0 ? 1 : (count + CellsPerRow - 1) / CellsPerRow;
            return rows * Height;
        }

        /// <summary>Cell rectangle from the strip's top-left, for a strip with this many cells.</summary>
        public static PanelLayout.Band CellBand(int count, int index)
        {
            int perRow = Math.Min(count <= 0 ? 1 : count, CellsPerRow);
            float cellW = Width / perRow;
            return new PanelLayout.Band((index % perRow) * cellW, (index / perRow) * Height, cellW, Height);
        }

        /// <summary>
        /// A cell's anchored position in the strip's corner space, for any corner: the band's
        /// Top is the distance from the strip's TOP edge, so a top-corner strip places the cell
        /// at y = -Top below that edge, and a bottom-corner strip (which grows upward) places
        /// the cell's BOTTOM edge at (StripHeight - Top - H) from the strip's bottom. Pure
        /// (harness-tested): rows are strictly ordered, never overlapping.
        /// </summary>
        public static Vector2 CellAnchoredPosition(TelemetryCorner corner, int count, int index, float stripHeight)
        {
            PanelLayout.Band b = CellBand(count, index);
            bool top = CornerAnchor(corner).y > 0.5f;
            return top
                ? new Vector2(b.X, -b.Top)
                : new Vector2(b.X, stripHeight - b.Top - b.H);
        }

        /// <summary>The strip label for a cell's value.</summary>
        public static string CellText(TelemetryCell cell, VehicleTuner.TelemetrySample s)
        {
            switch (cell)
            {
                case TelemetryCell.Speed: return Mathf.RoundToInt(Math.Abs(s.SpeedKmh)) + " km/h";
                case TelemetryCell.Rpm: return Mathf.RoundToInt(s.Rpm < 0f ? 0f : s.Rpm) + " rpm";
                case TelemetryCell.Gear: return "Gear " + (string.IsNullOrEmpty(s.Gear) ? "-" : s.Gear);
                case TelemetryCell.SlipFront: return "SlipF " + s.FrontSlip.ToString("0.0") + "°";
                case TelemetryCell.SlipRear: return "SlipR " + s.RearSlip.ToString("0.0") + "°";
                case TelemetryCell.LatG: return "Lat " + s.LatG.ToString("0.00") + "g";
                case TelemetryCell.LongG: return "Long " + s.LongG.ToString("0.00") + "g";
                case TelemetryCell.Steering: return "Steer " + s.SteeringDeg.ToString("0") + "°";
                case TelemetryCell.Throttle: return "Thr " + Mathf.RoundToInt(s.Throttle * 100f) + "%";
                default: return "Brk " + Mathf.RoundToInt(s.Brakes * 100f) + "%";
            }
        }

        private void UpdateCells(VehicleTuner.TelemetrySample s)
        {
            IList<TelemetryCell> cells = TelemetryCells.Selected;
            if (_cellsVersion != TelemetryCells.Version)
            {
                _cellsVersion = TelemetryCells.Version;
                for (int i = 0; i < _cells.Count; i++)
                {
                    Destroy(_cells[i].gameObject);
                }
                _cells.Clear();
                for (int i = 0; i < cells.Count; i++)
                {
                    RectTransform cell = UiKit.Make("Cell" + i, _strip);
                    PlaceCell(cell, cells.Count, i);
                    _cells.Add(UiKit.Label(cell, "", 15, UiKit.TextMain, TextAnchor.MiddleCenter, FontStyle.Bold));
                }
                _corner = (TelemetryCorner)(-1);   // re-apply the size
            }
            for (int i = 0; i < cells.Count && i < _cells.Count; i++)
            {
                _cells[i].text = CellText(cells[i], s);
            }
        }

        /// <summary>Place one cell at its corner-mapped position (anchored to the strip's own corner).</summary>
        private void PlaceCell(RectTransform cell, int count, int index)
        {
            PanelLayout.Band b = CellBand(count, index);
            float topY = CornerAnchor(UiSettings.TelemetryPosition).y;
            cell.anchorMin = new Vector2(0f, topY);
            cell.anchorMax = new Vector2(0f, topY);
            cell.pivot = new Vector2(0f, topY);
            cell.sizeDelta = new Vector2(b.W - 12f, b.H);
            Vector2 p = CellAnchoredPosition(UiSettings.TelemetryPosition, count, index, StripHeight(count));
            cell.anchoredPosition = new Vector2(p.x + 6f, p.y);
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
            _strip.sizeDelta = new Vector2(Width, StripHeight(TelemetryCells.Count));
            _strip.anchoredPosition = CornerOffset(_corner, Margin);
        }
    }
}
