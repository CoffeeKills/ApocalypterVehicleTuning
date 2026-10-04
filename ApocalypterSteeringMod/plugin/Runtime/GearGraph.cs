using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ApocalypterSteeringMod.Runtime
{
    /// <summary>
    /// The gear-ratio bar graph row of the Gearbox tab (0.6.0), a sibling of CurveEditor:
    /// one bar per forward gear of the reference vehicle — the current ratio filled, the
    /// stock ratio as an outline. Click a bar to select that gear (its slider row lights
    /// up); drag a bar up/down to change that gear's factor. A drag that does not start on a
    /// bar is forwarded to the surrounding ScrollRect. One MaskableGraphic (one Graphic per
    /// GameObject), header band above the graph (the CurveEditor 0.5.0 lesson), inert while
    /// Gearbox is OFF. Routing decisions are pure static functions for the harness.
    /// </summary>
    public sealed class GearGraph
    {
        private const float Pad = 10f;

        // Same header conventions as CurveEditor (public for the harness).
        public const float Side = CurveEditor.Side;
        public const float TitleTop = CurveEditor.TitleTop, TitleHeight = CurveEditor.TitleHeight;
        public const float ReadoutRight = 14f, ReadoutWidth = 200f, ReadoutMinWidth = 90f, TitleMinWidth = 120f;
        public const float HintTop = 38f, HintHeight = 36f, HintGap = 6f;
        public const float GraphTop = 80f, GraphHeight = 150f, GraphBottom = 10f;
        public const float RowHeight = GraphTop + GraphHeight + GraphBottom;   // 240

        public struct Bands
        {
            public float RowHeight, ReadoutWidth, HintHeight, GraphTop;
        }

        public static Bands ComputeBands(float contentWidth, int hintChars)
        {
            var b = new Bands();
            b.ReadoutWidth = Mathf.Clamp(contentWidth - Side - ReadoutRight - TitleMinWidth, ReadoutMinWidth, ReadoutWidth);
            b.HintHeight = Mathf.Max(HintHeight, PanelLayout.WrappedHintHeight(hintChars, contentWidth - 2f * Side));
            b.GraphTop = Mathf.Max(GraphTop, HintTop + b.HintHeight + HintGap);
            b.RowHeight = b.GraphTop + GraphHeight + GraphBottom;
            return b;
        }

        public enum DragRoute
        {
            None,
            MoveBar,
            ScrollList
        }

        /// <summary>A drag edits a gear only when it starts on a bar of an editable graph; anything else scrolls.</summary>
        public static DragRoute RouteDrag(bool canEdit, int bar, bool hasScroll)
        {
            if (canEdit && bar >= 0)
            {
                return DragRoute.MoveBar;
            }
            return hasScroll ? DragRoute.ScrollList : DragRoute.None;
        }

        public enum ClickAction
        {
            None,
            Select
        }

        /// <summary>A left click on a bar selects that gear; a click that ends a drag never does.</summary>
        public static ClickAction RouteClick(bool canEdit, bool dragging, bool leftButton, int bar)
        {
            if (!canEdit || dragging || !leftButton || bar < 0)
            {
                return ClickAction.None;
            }
            return ClickAction.Select;
        }

        /// <summary>
        /// Factor for a bar dragged to <paramref name="yNorm"/> (0..1 of the graph height) when
        /// the graph's full height represents <paramref name="maxRatio"/>. Clamped to the gear limits.
        /// </summary>
        public static float ScaleForDrag(float yNorm, float maxRatio, float stockRatio, float min, float max)
        {
            if (stockRatio <= 0f)
            {
                return 1f;
            }
            float ratio = Mathf.Clamp01(yNorm) * maxRatio;
            return Mathf.Clamp(ratio / stockRatio, min, max);
        }

        private Func<int> _count;
        private Func<int, float> _stock, _current;
        private Func<bool> _canEdit;
        private Func<int> _selected;
        private Action<int> _select;
        private Action<int, float> _setScale;
        private Func<string> _readoutText;
        private BarGraphic _graphic;
        private Text _readout;
        private LayoutElement _rowLayout;
        private RectTransform _titleRt, _readoutRt, _hintRt, _graphRt;
        private int _hintChars;

        public GameObject Row { get; private set; }

        /// <param name="count">number of bars (forward gears shown)</param>
        /// <param name="stock">stock ratio of gear i (1-based)</param>
        /// <param name="current">current ratio of gear i (stock x factor)</param>
        /// <param name="setScale">new factor for gear i (forks the preset into Custom)</param>
        public static GearGraph Create(RectTransform content, string title, string hint,
            Func<int> count, Func<int, float> stock, Func<int, float> current,
            Func<bool> canEdit, Func<int> selected, Action<int> select, Action<int, float> setScale,
            Func<string> readoutText)
        {
            var g = new GearGraph
            {
                _count = count,
                _stock = stock,
                _current = current,
                _canEdit = canEdit,
                _selected = selected,
                _select = select,
                _setScale = setScale,
                _readoutText = readoutText,
                _hintChars = hint != null ? hint.Length : 0
            };

            RectTransform row = UiKit.Make("GearGraph", content);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = RowHeight;
            le.minHeight = RowHeight;
            UiKit.Paint(row, UiKit.RowNormal, false);
            g.Row = row.gameObject;
            g._rowLayout = le;

            g._titleRt = UiKit.Top(UiKit.Make("Title", row), TitleTop, TitleHeight, Side, ReadoutRight + ReadoutWidth);
            UiKit.Label(g._titleRt, title, 17, UiKit.TextMain, TextAnchor.MiddleLeft, FontStyle.Bold);

            g._readoutRt = TopRightBox(UiKit.Make("Readout", row), ReadoutWidth, TitleHeight, ReadoutRight, TitleTop);
            g._readout = UiKit.Label(g._readoutRt, "", 13, UiKit.TextMuted, TextAnchor.MiddleRight);

            g._hintRt = UiKit.Top(UiKit.Make("Hint", row), HintTop, HintHeight, Side, Side);
            UiKit.Label(g._hintRt, hint, 13, UiKit.TextMuted, TextAnchor.UpperLeft, FontStyle.Normal, true);

            g._graphRt = UiKit.Make("Bars", row);
            UiKit.Place(g._graphRt, 0f, 0f, 1f, 1f, Side, GraphBottom, Side, GraphTop);
            var graphic = g._graphRt.gameObject.AddComponent<BarGraphic>();
            graphic.raycastTarget = true;
            graphic.Owner = g;
            g._graphic = graphic;

            g.Refresh();
            return g;
        }

        private static RectTransform TopRightBox(RectTransform rt, float width, float height, float fromRight, float fromTop)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(-fromRight, -fromTop);
            return rt;
        }

        public void Relayout(float contentWidth)
        {
            Bands b = ComputeBands(contentWidth, _hintChars);
            _rowLayout.preferredHeight = b.RowHeight;
            _rowLayout.minHeight = b.RowHeight;
            UiKit.Top(_titleRt, TitleTop, TitleHeight, Side, ReadoutRight + b.ReadoutWidth);
            TopRightBox(_readoutRt, b.ReadoutWidth, TitleHeight, ReadoutRight, TitleTop);
            UiKit.Top(_hintRt, HintTop, b.HintHeight, Side, Side);
            UiKit.Place(_graphRt, 0f, 0f, 1f, 1f, Side, GraphBottom, Side, b.GraphTop);
            _graphic.SetVerticesDirty();
        }

        public void Refresh()
        {
            if (_readout != null)
            {
                _readout.text = _readoutText != null ? _readoutText() : "";
            }
            if (_graphic != null)
            {
                _graphic.SetVerticesDirty();
            }
        }

        internal bool CanEdit() { return _canEdit == null || _canEdit(); }

        /// <summary>Tallest ratio drawn (stock or current, +10% headroom), so every bar fits.</summary>
        internal float MaxRatio()
        {
            float m = 0f;
            int n = _count();
            for (int i = 1; i <= n; i++)
            {
                m = Mathf.Max(m, Mathf.Max(_stock(i), _current(i)));
            }
            // Headroom for dragging a bar above its stock ratio up to the factor limit.
            return m > 0f ? m * 1.1f : 1f;
        }

        private sealed class BarGraphic : MaskableGraphic, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
        {
            public GearGraph Owner;
            private int _dragged = -1;
            private bool _scrolling;
            private ScrollRect _scroll;
            private float _dragMax;   // frozen scale for the whole drag (bars would rescale under the mouse)

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                Rect r = rectTransform.rect;
                float w = r.width, h = r.height;
                float px = rectTransform.pivot.x * w, py = rectTransform.pivot.y * h;
                Quad(vh, -px, -py, w - px, h - py, UiKit.TrackBg);

                int n = Owner._count();
                if (n <= 0)
                {
                    return;
                }
                float max = _dragged >= 0 ? _dragMax : Owner.MaxRatio();
                float gw = w - 2f * Pad, gh = h - 2f * Pad;
                float slot = gw / n;
                float barW = Mathf.Max(2f, slot * 0.6f);
                int sel = Owner._selected != null ? Owner._selected() : -1;
                for (int i = 1; i <= n; i++)
                {
                    float cx = -px + Pad + (i - 0.5f) * slot;
                    float x0 = cx - barW * 0.5f, x1 = cx + barW * 0.5f;
                    float y0 = -py + Pad;
                    float cur = Owner._current(i), stock = Owner._stock(i);
                    float yCur = y0 + Mathf.Clamp01(cur / max) * gh;
                    float yStock = y0 + Mathf.Clamp01(stock / max) * gh;
                    bool changed = Mathf.Abs(cur - stock) > 1e-4f * Mathf.Max(1f, stock);
                    Color fill = i == sel ? UiKit.Accent : (changed ? UiKit.Good : UiKit.ChipBase);
                    Quad(vh, x0, y0, x1, yCur, fill);
                    // Stock outline: a 2 px frame at the stock height.
                    Quad(vh, x0 - 2f, yStock - 1f, x1 + 2f, yStock + 1f, UiKit.HandleColor);
                    Quad(vh, x0 - 2f, y0, x0 - 1f, yStock, UiKit.Divider);
                    Quad(vh, x1 + 1f, y0, x1 + 2f, yStock, UiKit.Divider);
                }
            }

            private static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color c)
            {
                int i = vh.currentVertCount;
                vh.AddVert(new Vector3(x0, y0, 0f), c, Vector2.zero);
                vh.AddVert(new Vector3(x1, y0, 0f), c, Vector2.zero);
                vh.AddVert(new Vector3(x1, y1, 0f), c, Vector2.zero);
                vh.AddVert(new Vector3(x0, y1, 0f), c, Vector2.zero);
                vh.AddTriangle(i, i + 1, i + 2);
                vh.AddTriangle(i, i + 2, i + 3);
            }

            private bool Local(PointerEventData e, out float xn, out float yn)
            {
                return Local(e.position, e.pressEventCamera, out xn, out yn);
            }

            private bool Local(Vector2 screenPoint, Camera cam, out float xn, out float yn)
            {
                xn = yn = 0f;
                Vector2 local;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, cam, out local))
                {
                    return false;
                }
                Rect r = rectTransform.rect;
                float gw = r.width - 2f * Pad, gh = r.height - 2f * Pad;
                if (gw <= 1f || gh <= 1f)
                {
                    return false;
                }
                xn = (local.x - r.xMin - Pad) / gw;
                yn = (local.y - r.yMin - Pad) / gh;
                return true;
            }

            /// <summary>Bar under the pointer (1-based), or -1. The whole slot column counts.</summary>
            private int PickBar(Vector2 screenPoint, Camera cam)
            {
                float xn, yn;
                int n = Owner._count();
                if (n <= 0 || !Local(screenPoint, cam, out xn, out yn) || xn < 0f || xn > 1f || yn < -0.05f || yn > 1.05f)
                {
                    return -1;
                }
                int i = Mathf.FloorToInt(xn * n) + 1;
                return i < 1 ? 1 : (i > n ? n : i);
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
                // 0.6.2: pick where the drag started, not where it crossed the drag threshold
                // (a fast pull from a short bar ended below it and scrolled the list instead).
                int bar = canEdit ? PickBar(e.pressPosition, e.pressEventCamera) : -1;
                _dragged = -1;
                _scrolling = false;
                switch (RouteDrag(canEdit, bar, ParentScroll() != null))
                {
                    case DragRoute.MoveBar:
                        _dragged = bar;
                        _dragMax = Owner.MaxRatio();
                        Owner._select(bar);
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
                float xn, yn;
                if (!Local(e, out xn, out yn))
                {
                    return;
                }
                float scale = ScaleForDrag(yn, _dragMax, Owner._stock(_dragged),
                    Settings.Limits.GearRatioMin, Settings.Limits.GearRatioMax);
                Owner._setScale(_dragged, scale);
                Owner.Refresh();
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
                _dragged = -1;
                Owner.Refresh();
            }

            public void OnPointerClick(PointerEventData e)
            {
                bool canEdit = Owner.CanEdit();
                int bar = canEdit ? PickBar(e.position, e.pressEventCamera) : -1;
                if (RouteClick(canEdit, e.dragging, e.button == PointerEventData.InputButton.Left, bar) == ClickAction.Select)
                {
                    Owner._select(bar);
                    Owner.Refresh();
                }
            }
        }
    }
}
