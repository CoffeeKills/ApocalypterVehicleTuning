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
    /// double-click a point to remove it. A drag that does not start on a point
    /// is forwarded to the surrounding ScrollRect, so the list still scrolls.
    /// The graph is a single MaskableGraphic (one Graphic per GameObject rule);
    /// all display strings come from the panel (translatable statics).
    /// </summary>
    public sealed class CurveEditor
    {
        private const float PickRadius = 14f;
        private const float Pad = 10f;
        private const int SegmentsPerCircle = 12;

        // Row layout, in pixels from the row's top edge. The header (title, readout,
        // Reset, hint) is a band ABOVE the graph and never overlaps it: the graph is a
        // later sibling and an opaque raycast target, so anything under it is invisible
        // and unclickable. 0.4.0 placed the header in the row's vertical halves while the
        // graph filled the row, which hid the title/hint/readout and made the per-graph
        // Reset button unreachable (a click there added a curve point instead).
        // Public so the harness can check the bands (no layout engine in the stubs).
        public const float RowHeight = 300f;
        public const float Side = 16f;
        public const float TitleTop = 8f, TitleHeight = 24f;
        public const float ResetWidth = 72f, ResetHeight = 28f, ResetRight = 14f;
        public const float ReadoutWidth = 150f, ReadoutRight = ResetRight + ResetWidth + 10f;
        public const float HintTop = 38f, HintHeight = 56f;   // 3 wrapped lines at 13 px (the longest English hint)
        public const float GraphTop = 100f, GraphBottom = 10f;

        // 0.6.0 width-adaptive bands (the constants above are the 0.5.0 values and the maxima).
        public const float TitleMinWidth = 150f, ReadoutMinWidth = 80f, GraphHeight = 190f, HintGap = 6f;

        public struct Bands
        {
            public float RowHeight, ReadoutWidth, HintHeight, GraphTop;
        }

        /// <summary>
        /// Header bands for a given row width and hint length. At the 0.5.0 width (748 px content,
        /// ~330-char hint) this gives back the 0.5.0 constants' geometry or taller; narrower rows
        /// shrink the readout (never below 80 px) and grow the hint band so the wrapped hint
        /// never runs under the graph.
        /// </summary>
        public static Bands ComputeBands(float contentWidth, int hintChars)
        {
            var b = new Bands();
            float free = contentWidth - Side - ReadoutRight - TitleMinWidth;
            b.ReadoutWidth = Mathf.Clamp(free, ReadoutMinWidth, ReadoutWidth);
            b.HintHeight = Mathf.Max(HintHeight, PanelLayout.WrappedHintHeight(hintChars, contentWidth - 2f * Side));
            b.GraphTop = Mathf.Max(GraphTop, HintTop + b.HintHeight + HintGap);
            b.RowHeight = b.GraphTop + GraphHeight + GraphBottom;
            return b;
        }

        private CurveGraphic _graphic;
        private Button _reset;
        private Text _readout;
        private LayoutElement _rowLayout;
        private RectTransform _titleRt, _readoutRt, _hintRt, _graphRt;
        private int _hintChars;
        private Func<EditableCurve> _get;
        private Func<EditableCurve> _reference;
        private Func<bool> _canEdit;

        public GameObject Row { get; private set; }

        /// <param name="canEdit">
        /// False while the surrounding group is disabled (category OFF, Vanilla). A
        /// CanvasGroup's interactable flag only gates Selectables, never a custom Graphic's
        /// event handlers, so the graph has to ask; 0.4.0's graph stayed editable on a dimmed
        /// tab and silently forked the active preset into Custom.
        /// </param>
        public static CurveEditor Create(RectTransform content, string title, string hint,
            Func<EditableCurve> get, Func<EditableCurve> reference, Func<Action<EditableCurve>, EditableCurve> edit,
            Func<bool> canEdit)
        {
            var editor = new CurveEditor();
            editor._get = get;
            editor._reference = reference;
            editor._canEdit = canEdit;

            RectTransform row = UiKit.Make("Curve_" + title, content);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = RowHeight;
            le.minHeight = RowHeight;
            UiKit.Paint(row, UiKit.RowNormal, false);
            editor.Row = row.gameObject;
            editor._rowLayout = le;
            editor._hintChars = hint != null ? hint.Length : 0;

            RectTransform t = UiKit.Top(UiKit.Make("Title", row), TitleTop, TitleHeight, Side, ReadoutRight + ReadoutWidth);
            UiKit.Label(t, title, 17, UiKit.TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);
            editor._titleRt = t;

            RectTransform ro = TopRightBox(UiKit.Make("Readout", row), ReadoutWidth, TitleHeight, ReadoutRight, TitleTop);
            editor._readout = UiKit.Label(ro, "", 13, UiKit.TextMuted, TextAnchor.MiddleRight);
            editor._readoutRt = ro;

            Button reset = UiKit.MakeButton(row, "Reset", "Reset", UiKit.ChipBase, 14, () =>
            {
                EditableCurve r = editor._reference();
                EditableCurve live = edit(c => c.CopyFrom(r));
                if (live != null)
                {
                    editor.Refresh();
                }
            }, out Text resetLabel);
            TopRightBox((RectTransform)reset.transform, ResetWidth, ResetHeight, ResetRight, TitleTop);
            editor._reset = reset;

            RectTransform h = UiKit.Top(UiKit.Make("Hint", row), HintTop, HintHeight, Side, Side);
            UiKit.Label(h, hint, 13, UiKit.TextMuted, TextAnchor.UpperLeft, FontStyle.Normal, true);
            editor._hintRt = h;

            RectTransform graph = UiKit.Make("Graph", row);
            editor._graphRt = graph;
            // Full-area anchors with insets (NOT a zero-height band): UiKit.Place keeps a
            // centred pivot, which inverts the rect on degenerate bands — the graph would
            // spill over neighbouring rows and eat their clicks. The top inset leaves the
            // header band free.
            UiKit.Place(graph, 0f, 0f, 1f, 1f, Side, GraphBottom, Side, GraphTop);
            var graphic = graph.gameObject.AddComponent<CurveGraphic>();
            graphic.raycastTarget = true;
            graphic.Owner = editor;
            graphic.Edit = edit;
            editor._graphic = graphic;

            editor.Refresh();
            return editor;
        }

        /// <summary>Re-apply the header bands for a new row width (live panel-width changes).</summary>
        public void Relayout(float contentWidth)
        {
            Bands b = ComputeBands(contentWidth, _hintChars);
            if (_rowLayout != null)
            {
                _rowLayout.preferredHeight = b.RowHeight;
                _rowLayout.minHeight = b.RowHeight;
            }
            if (_titleRt != null)
            {
                UiKit.Top(_titleRt, TitleTop, TitleHeight, Side, ReadoutRight + b.ReadoutWidth);
            }
            if (_readoutRt != null)
            {
                TopRightBox(_readoutRt, b.ReadoutWidth, TitleHeight, ReadoutRight, TitleTop);
            }
            if (_hintRt != null)
            {
                UiKit.Top(_hintRt, HintTop, b.HintHeight, Side, Side);
            }
            if (_graphRt != null)
            {
                UiKit.Place(_graphRt, 0f, 0f, 1f, 1f, Side, GraphBottom, Side, b.GraphTop);
            }
            if (_graphic != null)
            {
                _graphic.SetVerticesDirty();
            }
        }

        /// <summary>Fixed-size box hanging from the top-right corner.</summary>
        private static RectTransform TopRightBox(RectTransform rt, float width, float height, float fromRight, float fromTop)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(-fromRight, -fromTop);
            return rt;
        }

        internal bool CanEdit()
        {
            return _canEdit == null || _canEdit();
        }

        internal bool IsChanged(EditableCurve cur)
        {
            EditableCurve r = _reference();
            return cur != null && r != null && !cur.SameAs(r);
        }

        // ------------------------------------------------------------- input routing
        // Pure decisions, public so the harness can check them without a UI runtime.

        public enum DragRoute
        {
            None,          // nothing to do (no handle, no scroll view)
            MovePoint,     // the drag started on a handle of an editable curve
            ScrollList     // anything else scrolls the surrounding list
        }

        /// <summary>
        /// A drag over the graph moves a point only when it starts on a handle of an
        /// editable curve. Everything else is handed to the parent ScrollRect: uGUI sends
        /// the drag to the first IDragHandler up the hierarchy, which is the graph itself,
        /// so 0.4.0's "don't Use() the event" never reached the list and the list could not
        /// be scrolled by dragging over a graph.
        /// </summary>
        /// <summary>
        /// Index of the handle within PickRadius of a point in the graph's local space, or -1.
        /// 0.6.2: OnBeginDrag must pick at the PRESS position. uGUI only calls OnBeginDrag once
        /// the pointer has moved past the EventSystem drag threshold (10 px by default), so the
        /// current position is already 10+ px from where the user grabbed — often outside the
        /// 14 px pick radius, and the drag fell through to scrolling the list: a point the user
        /// clearly grabbed would not move.
        /// </summary>
        public static int PickHandleAt(EditableCurve curve, Vector2 local, float width, float height, float pivotX, float pivotY)
        {
            if (curve == null)
            {
                return -1;
            }
            float gw = width - 2f * Pad;
            float gh = height - 2f * Pad;
            float px = width * pivotX;
            float py = height * pivotY;
            int best = -1;
            float bestD = PickRadius * PickRadius;
            for (int i = 0; i < curve.Count; i++)
            {
                float hx = Pad + curve.X(i) * gw - px;
                float hy = Pad + curve.Y(i) * gh - py;
                float dx = local.x - hx;
                float dy = local.y - hy;
                float d = dx * dx + dy * dy;
                if (d <= bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        public static DragRoute RouteDrag(bool canEdit, int handle, bool hasScroll)
        {
            if (canEdit && handle >= 0)
            {
                return DragRoute.MovePoint;
            }
            return hasScroll ? DragRoute.ScrollList : DragRoute.None;
        }

        public enum ClickAction
        {
            None,
            AddPoint,
            RemovePoint
        }

        /// <summary>
        /// uGUI still delivers OnPointerClick after a drag when the press and drag handler
        /// are the same object (eventData.dragging is still true at that point), so a drag
        /// must never turn into a click: in 0.4.0, scrolling the list by dragging over the
        /// graph added a point where the mouse was released.
        /// </summary>
        public static ClickAction RouteClick(bool canEdit, bool dragging, bool leftButton, int clickCount, int handle)
        {
            if (!canEdit || dragging || !leftButton)
            {
                return ClickAction.None;
            }
            if (clickCount >= 2)
            {
                return handle >= 0 ? ClickAction.RemovePoint : ClickAction.None;
            }
            return handle < 0 ? ClickAction.AddPoint : ClickAction.None;
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
            bool changed = !cur.SameAs(r);
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
            private bool _scrolling;     // this drag is being forwarded to the list
            private ScrollRect _scroll;  // resolved lazily (the row is parented after creation)

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

                Color curveColor = Owner.IsChanged(curve) ? UiKit.Accent : UiKit.HandleColor;
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

            private int PickHandle(Vector2 screenPoint, Camera cam)
            {
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, cam, out local))
                {
                    return -1;
                }
                Rect r = rectTransform.rect;
                return PickHandleAt(Owner.GetCurve(), local, r.width, r.height, rectTransform.pivot.x, rectTransform.pivot.y);
            }

            private ScrollRect ParentScroll()
            {
                if (_scroll == null)
                {
                    _scroll = GetComponentInParent<ScrollRect>();
                }
                return _scroll;
            }

            public void OnBeginDrag(PointerEventData e)
            {
                bool canEdit = Owner.CanEdit();
                // Where the drag STARTED (see PickHandleAt): e.position is past the drag threshold.
                int handle = canEdit ? PickHandle(e.pressPosition, e.pressEventCamera) : -1;
                _dragged = -1;
                _scrolling = false;
                switch (RouteDrag(canEdit, handle, ParentScroll() != null))
                {
                    case DragRoute.MovePoint:
                        _dragged = handle;
                        e.Use();
                        break;
                    case DragRoute.ScrollList:
                        _scrolling = true;
                        _scroll.OnBeginDrag(e);
                        break;
                }
            }

            public void OnDrag(PointerEventData e)
            {
                if (_scrolling)
                {
                    if (_scroll != null)
                    {
                        _scroll.OnDrag(e);
                    }
                    return;
                }
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
                if (_scrolling)
                {
                    _scrolling = false;
                    if (_scroll != null)
                    {
                        _scroll.OnEndDrag(e);
                    }
                    return;
                }
                if (_dragged < 0)
                {
                    return;
                }
                _dragged = -1;
                Owner.Refresh();
            }

            public void OnPointerClick(PointerEventData e)
            {
                bool canEdit = Owner.CanEdit();
                int handle = canEdit ? PickHandle(e.position, e.pressEventCamera) : -1;
                ClickAction action = RouteClick(canEdit, e.dragging,
                    e.button == PointerEventData.InputButton.Left, e.clickCount, handle);
                if (action == ClickAction.None)
                {
                    return;
                }
                float x, y;
                if (!MapPoint(e, out x, out y))
                {
                    return;
                }
                if (action == ClickAction.RemovePoint)
                {
                    Edit(c => c.TryRemovePoint(handle));
                }
                else
                {
                    Edit(c => c.TryAddPoint(x, y));
                }
                Owner.Refresh();
            }
        }
    }
}
