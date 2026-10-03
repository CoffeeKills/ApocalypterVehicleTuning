using System;
using ApocalypterSteeringMod.Settings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// A visual, mouse-only curve editor row for the tuning panel: a graph of a
    /// piecewise-linear EditableCurve over x = speed (0..1 = 0..180 km/h) and
    /// y = amount. Click empty space to add a point, drag a point to move it,
    /// double-click a point to remove it. Dragging only consumes the event when
    /// it starts on a point, so the surrounding ScrollRect still scrolls.
    /// The graph is a single MaskableGraphic (one Graphic per GameObject rule);
    /// all display strings come from the panel (translatable statics).
    /// </summary>
    public sealed class CurveEditor
    {
        private const float PickRadius = 14f;
        private const float Pad = 10f;
        private const float RowHeight = 268f;
        private const int SegmentsPerCircle = 12;

        private CurveGraphic _graphic;
        private Button _reset;
        private Text _readout;
        private Func<EditableCurve> _get;
        private Func<EditableCurve> _reference;

        public GameObject Row { get; private set; }

        public static CurveEditor Create(RectTransform content, string title, string hint,
            Func<EditableCurve> get, Func<EditableCurve> reference, Func<Action<EditableCurve>, EditableCurve> edit)
        {
            var editor = new CurveEditor();
            editor._get = get;
            editor._reference = reference;

            RectTransform row = UiKit.Make("Curve_" + title, content);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = RowHeight;
            le.minHeight = RowHeight;
            UiKit.Paint(row, UiKit.RowNormal, false);
            editor.Row = row.gameObject;

            RectTransform t = UiKit.Place(UiKit.Make("Title", row), 0f, 0.5f, 1f, 1f, 16f, 0f, 180f, 6f);
            UiKit.Label(t, title, 17, UiKit.TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform h = UiKit.Place(UiKit.Make("Hint", row), 0f, 0f, 1f, 0.5f, 16f, 6f, 180f, 0f);
            UiKit.Label(h, hint, 13, UiKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Normal, true);
            RectTransform ro = UiKit.Place(UiKit.Make("Readout", row), 0f, 0f, 1f, 0.5f, 16f, 6f, 88f, 0f);
            editor._readout = UiKit.Label(ro, "", 13, UiKit.TextMuted, TextAnchor.MiddleRight);

            Button reset = UiKit.MakeButton(row, "Reset", "Reset", UiKit.ChipBase, 14, () =>
            {
                EditableCurve r = editor._reference();
                EditableCurve live = edit(c => c.CopyFrom(r));
                if (live != null)
                {
                    editor.Refresh();
                }
            }, out Text resetLabel);
            UiKit.RightBox((RectTransform)reset.transform, 72f, 32f, 14f);
            editor._reset = reset;

            RectTransform graph = UiKit.Make("Graph", row);
            // Full-area anchors (NOT a zero-height band): UiKit.Place keeps a centred
            // pivot, which inverts the rect on degenerate bands — the graph would spill
            // over neighbouring rows and eat their clicks.
            UiKit.Place(graph, 0f, 0f, 1f, 1f, 16f, 10f, 16f, 46f);
            var graphic = graph.gameObject.AddComponent<CurveGraphic>();
            graphic.raycastTarget = true;
            graphic.Owner = editor;
            graphic.Edit = edit;
            editor._graphic = graphic;

            editor.Refresh();
            return editor;
        }

        /// <summary>Called from the panel's refresher list on every control change.</summary>
        public void Refresh()
        {
            EditableCurve cur = _get();
            EditableCurve r = _reference();
            if (cur == null || r == null)
            {
                return;
            }
            bool changed = cur.Serialize() != r.Serialize();
            if (_reset != null)
            {
                _reset.interactable = changed;
            }
            if (_readout != null)
            {
                _readout.text = changed ? (cur.Count + " points") : "";
            }
            if (_graphic != null)
            {
                _graphic.SetVerticesDirty();
            }
        }

        /// <summary>Drag readout feedback, set by the graphic while dragging.</summary>
        internal void SetReadout(string text)
        {
            if (_readout != null)
            {
                _readout.text = text;
            }
        }

        internal EditableCurve GetCurve() { return _get(); }

        private sealed class CurveGraphic : MaskableGraphic, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
        {
            public CurveEditor Owner;
            public Func<Action<EditableCurve>, EditableCurve> Edit;
            private int _dragged = -1;

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                Rect r = rectTransform.rect;
                float w = r.width;
                float h = r.height;
                float px = rectTransform.pivot.x * w;
                float py = rectTransform.pivot.y * h;

                // Background.
                AddQuad(vh, new Vector2(-px, -py), new Vector2(w - px, -py), new Vector2(w - px, h - py), new Vector2(-px, h - py), UiKit.TrackBg);

                // 4x4 grid.
                for (int i = 1; i < 4; i++)
                {
                    float gx = -px + Pad + i * 0.25f * (w - 2f * Pad);
                    float gy = -py + Pad + i * 0.25f * (h - 2f * Pad);
                    AddQuad(vh, new Vector2(gx, -py + Pad), new Vector2(gx + 1f, -py + Pad), new Vector2(gx + 1f, h - py - Pad), new Vector2(gx, h - py - Pad), UiKit.Divider);
                    AddQuad(vh, new Vector2(-px + Pad, gy), new Vector2(w - px - Pad, gy), new Vector2(w - px - Pad, gy + 1f), new Vector2(-px + Pad, gy + 1f), UiKit.Divider);
                }

                // Border.
                AddQuad(vh, new Vector2(-px + Pad, -py + Pad), new Vector2(w - px - Pad, -py + Pad), new Vector2(w - px - Pad, -py + Pad + 1f), new Vector2(-px + Pad, -py + Pad + 1f), UiKit.Divider);
                AddQuad(vh, new Vector2(-px + Pad, h - py - Pad - 1f), new Vector2(w - px - Pad, h - py - Pad - 1f), new Vector2(w - px - Pad, h - py - Pad), new Vector2(-px + Pad, h - py - Pad), UiKit.Divider);
                AddQuad(vh, new Vector2(-px + Pad, -py + Pad), new Vector2(-px + Pad + 1f, -py + Pad), new Vector2(-px + Pad + 1f, h - py - Pad), new Vector2(-px + Pad, h - py - Pad), UiKit.Divider);
                AddQuad(vh, new Vector2(w - px - Pad - 1f, -py + Pad), new Vector2(w - px - Pad, -py + Pad), new Vector2(w - px - Pad, h - py - Pad), new Vector2(w - px - Pad - 1f, h - py - Pad), UiKit.Divider);

                EditableCurve curve = Owner.GetCurve();
                if (curve == null || curve.Count < 2)
                {
                    return;
                }

                bool changed = Owner._reference() != null && curve.Serialize() != Owner._reference().Serialize();
                Color curveColor = changed ? UiKit.Accent : UiKit.HandleColor;
                float gw = w - 2f * Pad;
                float gh = h - 2f * Pad;

                // Curve polyline, 2px thick.
                for (int i = 1; i < curve.Count; i++)
                {
                    Vector2 a = ToLocal(curve.X(i - 1), curve.Y(i - 1), gw, gh, px, py);
                    Vector2 b = ToLocal(curve.X(i), curve.Y(i), gw, gh, px, py);
                    float dx = b.x - a.x;
                    float dy = b.y - a.y;
                    float len = Mathf.Sqrt(dx * dx + dy * dy);
                    if (len < 1e-4f)
                    {
                        continue;
                    }
                    float nx = -dy / len;
                    float ny = dx / len;
                    AddQuad(vh,
                        new Vector2(a.x + nx, a.y + ny),
                        new Vector2(b.x + nx, b.y + ny),
                        new Vector2(b.x - nx, b.y - ny),
                        new Vector2(a.x - nx, a.y - ny), curveColor);
                }

                // Handles.
                for (int i = 0; i < curve.Count; i++)
                {
                    Vector2 p = ToLocal(curve.X(i), curve.Y(i), gw, gh, px, py);
                    AddCircle(vh, p, i == _dragged ? 6f : 5f, i == _dragged ? UiKit.Accent : UiKit.HandleColor);
                }
            }

            private static Vector2 ToLocal(float x, float y, float gw, float gh, float px, float py)
            {
                return new Vector2(Pad + x * gw - px, Pad + y * gh - py);
            }

            private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
            {
                int i = vh.currentVertCount;
                vh.AddVert(new Vector3(a.x, a.y, 0f), color, Vector2.zero);
                vh.AddVert(new Vector3(b.x, b.y, 0f), color, Vector2.zero);
                vh.AddVert(new Vector3(c.x, c.y, 0f), color, Vector2.zero);
                vh.AddVert(new Vector3(d.x, d.y, 0f), color, Vector2.zero);
                vh.AddTriangle(i, i + 1, i + 2);
                vh.AddTriangle(i, i + 2, i + 3);
            }

            private static void AddCircle(VertexHelper vh, Vector2 center, float radius, Color color)
            {
                int i = vh.currentVertCount;
                vh.AddVert(new Vector3(center.x, center.y, 0f), color, Vector2.zero);
                for (int s = 0; s <= SegmentsPerCircle; s++)
                {
                    float a = s * (float)Math.PI * 2f / SegmentsPerCircle;
                    vh.AddVert(new Vector3(center.x + Mathf.Cos(a) * radius, center.y + Mathf.Sin(a) * radius, 0f), color, Vector2.zero);
                }
                for (int s = 0; s < SegmentsPerCircle; s++)
                {
                    vh.AddTriangle(i, i + 1 + s, i + 2 + s);
                }
            }

            private bool MapPoint(PointerEventData e, out float x, out float y)
            {
                x = 0f;
                y = 0f;
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, e.position, e.pressEventCamera, out local))
                {
                    return false;
                }
                Rect r = rectTransform.rect;
                float gw = r.width - 2f * Pad;
                float gh = r.height - 2f * Pad;
                if (gw <= 1f || gh <= 1f)
                {
                    return false;
                }
                x = Mathf.Clamp01((local.x - r.xMin - Pad) / gw);
                y = Mathf.Clamp01((local.y - r.yMin - Pad) / gh);
                return true;
            }

            private int PickHandle(PointerEventData e)
            {
                EditableCurve curve = Owner.GetCurve();
                if (curve == null)
                {
                    return -1;
                }
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, e.position, e.pressEventCamera, out local))
                {
                    return -1;
                }
                Rect r = rectTransform.rect;
                float gw = r.width - 2f * Pad;
                float gh = r.height - 2f * Pad;
                int best = -1;
                float bestD = PickRadius * PickRadius;
                for (int i = 0; i < curve.Count; i++)
                {
                    Vector2 p = ToLocal(curve.X(i), curve.Y(i), gw, gh, r.width * rectTransform.pivot.x, r.height * rectTransform.pivot.y);
                    float dx = local.x - p.x;
                    float dy = local.y - p.y;
                    float d = dx * dx + dy * dy;
                    if (d <= bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
                return best;
            }

            public void OnBeginDrag(PointerEventData e)
            {
                _dragged = PickHandle(e);
                if (_dragged < 0)
                {
                    return;   // do not consume: let the ScrollRect scroll
                }
                e.Use();
            }

            public void OnDrag(PointerEventData e)
            {
                if (_dragged < 0)
                {
                    return;
                }
                e.Use();
                float x, y;
                if (!MapPoint(e, out x, out y))
                {
                    return;
                }
                int index = _dragged;
                EditableCurve live = Edit(c => c.TryMovePoint(index, x, y));
                if (live == null)
                {
                    _dragged = -1;
                    return;
                }
                Owner.SetReadout(Mathf.RoundToInt(x * 180f) + " km/h · " + Mathf.RoundToInt(y * 100f) + "%");
                SetVerticesDirty();
            }

            public void OnEndDrag(PointerEventData e)
            {
                if (_dragged < 0)
                {
                    return;
                }
                _dragged = -1;
                Owner.Refresh();
            }

            public void OnPointerClick(PointerEventData e)
            {
                if (e.button != PointerEventData.InputButton.Left)
                {
                    return;
                }
                float x, y;
                if (!MapPoint(e, out x, out y))
                {
                    return;
                }
                if (e.clickCount >= 2)
                {
                    int i = PickHandle(e);
                    if (i >= 0)
                    {
                        Edit(c => c.TryRemovePoint(i));
                        Owner.Refresh();
                    }
                }
                else if (PickHandle(e) < 0)
                {
                    Edit(c => c.TryAddPoint(x, y));
                    Owner.Refresh();
                }
            }
        }
    }
}
