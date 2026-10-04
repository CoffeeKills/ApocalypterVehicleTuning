using System;
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
    /// child per value. Hidden while the panel is open, when disabled, and when there is
    /// no active vehicle. Updates at 4 Hz (allocations are fine at that rate; the physics
    /// hot paths stay allocation-free).
    /// </summary>
    public sealed class TelemetryStrip : MonoBehaviour
    {
        public const float UpdateInterval = 0.25f;
        public const float Width = 440f, Height = 32f, Margin = 12f;
        public const int SortingOrder = 31000;   // below the panel (32000)

        private GameObject _canvasGo;
        private CanvasScaler _scaler;
        private RectTransform _strip;
        private readonly Text[] _cells = new Text[4];
        private VehicleTuner _tuner;
        private SettingsPanelManager _manager;
        private float _next;
        private bool _buildFailed;
        private TelemetryCorner _corner = (TelemetryCorner)(-1);

        private void Start()
        {
            _tuner = GetComponent<VehicleTuner>();
            _manager = GetComponent<SettingsPanelManager>();
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
            bool panelOpen = _manager != null && _manager.Visible;
            bool show = UiSettings.TelemetryEnabled && !panelOpen && _tuner != null && _tuner.TryGetTelemetry(out s);
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
                    UiKit.Place(cell, i / 4f, 0f, (i + 1) / 4f, 1f, 6f, 0f, 6f, 0f);
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
            _strip.sizeDelta = new Vector2(Width, Height);
            _strip.anchoredPosition = CornerOffset(_corner, Margin);
        }
    }
}
