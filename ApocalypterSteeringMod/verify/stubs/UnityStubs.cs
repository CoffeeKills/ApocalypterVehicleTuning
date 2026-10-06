// Compile-only stubs of the Unity / UI / TMP / InputSystem APIs the mod uses.
// Signatures mirror the real Unity 2020.3 API. Bodies are never executed.
#pragma warning disable CS0067, CS0626, CS0660, CS0661, CS1591
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public enum HideFlags { None = 0, HideAndDontSave = 61 }
    public enum CursorLockMode { None, Locked, Confined }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum HorizontalWrapMode { Wrap, Overflow }
    public enum VerticalWrapMode { Truncate, Overflow }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public enum KeyCode { None = 0, Backspace = 8, Return = 13, Escape = 27, Space = 32, Alpha0 = 48, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9, A = 97, D = 100, S = 115, W = 119, Home = 278, End = 279, Insert = 277, F1 = 282, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, F13, F14, F15, Mouse0 = 323, Mouse1, Mouse2, Mouse3, Mouse4, Mouse5, Mouse6, JoystickButton0 = 330 }

    public class SerializeField : Attribute { }

    public class Object
    {
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public static void Destroy(Object obj) { }
        public static void DestroyImmediate(Object obj) { }
        public static void DontDestroyOnLoad(Object target) { }
        public static T Instantiate<T>(T original, Transform parent) where T : Object { return null; }
        public static readonly List<Object> Registry = new List<Object>();
        public static T[] FindObjectsOfType<T>() where T : Object
        {
            var r = new List<T>();
            foreach (Object o in Registry) { if (o is T t && !o.TestDestroyed) r.Add(t); }
            return r.ToArray();
        }
        public static T FindObjectOfType<T>() where T : Object { T[] a = FindObjectsOfType<T>(); return a.Length > 0 ? a[0] : null; }
        // Test hook: Unity's "fake null" — a destroyed object compares equal to null while the
        // C# reference (and dictionary keys) stay alive.
        public bool TestDestroyed;
        private static bool IsNull(Object o) { return ReferenceEquals(o, null) || o.TestDestroyed; }
        public static bool operator ==(Object a, Object b)
        {
            bool an = IsNull(a), bn = IsNull(b);
            if (an || bn) return an && bn;
            return ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public static implicit operator bool(Object o) { return !IsNull(o); }
    }

    public class Component : Object
    {
        private GameObject _go;
        public GameObject gameObject { get { return _go ?? (_go = new GameObject("stub")); } }
        private Transform _t;
        public Transform transform { get { if (this is Transform self) return self; return _t ?? (_t = new Transform()); } }
        // Test hook: components "attached" to the same GameObject (GetComponent<T> finds them).
        private List<object> _attached;
        public void TestAttach(object component) { (_attached ?? (_attached = new List<object>())).Add(component); }
        public T GetComponent<T>()
        {
            if (_attached != null) { foreach (object o in _attached) { if (o is T t) return t; } }
            return default(T);
        }
        public T GetComponentInChildren<T>(bool includeInactive) { return default(T); }
        public T GetComponentInChildren<T>() { return default(T); }
        public T GetComponentInParent<T>() { return default(T); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) { return null; }
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> result) { result.Clear(); }
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public bool isActiveAndEnabled { get { return false; } }
    }

    public class MonoBehaviour : Behaviour { }

    public class Camera : Behaviour
    {
        public static Camera main { get { return null; } }
    }

    public static class RectTransformUtility
    {
        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector2 localPoint)
        {
            localPoint = default(Vector2);
            return false;
        }

        // 0.7.0 (digit hotkeys only over the panel). Test hook: the answer.
        public static bool TestContains;
        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint, Camera cam) { return TestContains; }
    }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public GameObject(string name, params Type[] components) { }
        public Transform transform { get { return null; } }
        public int layer { get; set; }
        public bool activeSelf { get { return false; } }
        public bool activeInHierarchy { get { return false; } }
        public void SetActive(bool value) { }
        public T AddComponent<T>() where T : Component { return null; }
        public T GetComponent<T>() { return default(T); }
        public T GetComponentInChildren<T>(bool includeInactive) { return default(T); }
        public T[] GetComponentsInChildren<T>(bool includeInactive) { return null; }
    }

    public class Transform : Component, IEnumerable
    {
        public Transform parent { get; set; }
        public int childCount { get { return 0; } }
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localEulerAngles { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 lossyScale { get { return default(Vector3); } }
        public Vector3 up { get { return default(Vector3); } }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public Transform GetChild(int index) { return null; }
        public Transform Find(string n) { return null; }
        public bool IsChildOf(Transform parent) { return false; }
        public void SetAsLastSibling() { }
        public Vector3 InverseTransformPoint(Vector3 p) { return p; }
        public Vector3 TransformPoint(Vector3 p) { return p; }
        public Vector3 InverseTransformDirection(Vector3 d) { return d; }
        public IEnumerator GetEnumerator() { return null; }
    }

    public struct Rect
    {
        public Vector2 size { get { return default(Vector2); } }
        public float width { get { return 0f; } }
        public float height { get { return 0f; } }
        public float x { get { return 0f; } }
        public float y { get { return 0f; } }
        public float xMin { get { return 0f; } }
        public float yMin { get { return 0f; } }
        public float xMax { get { return 0f; } }
        public float yMax { get { return 0f; } }
    }

    public sealed class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Rect rect { get { return default(Rect); } }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return default(Vector2); } }
        public static Vector2 one { get { return new Vector2(1, 1); } }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
        public static Vector2 operator -(Vector2 a) { return new Vector2(-a.x, -a.y); }
        public static Vector2 operator *(Vector2 a, float d) { return new Vector2(a.x * d, a.y * d); }
        public static Vector2 operator /(Vector2 a, float d) { return new Vector2(a.x / d, a.y / d); }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 zero { get { return default(Vector3); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float d) { return new Vector3(a.x * d, a.y * d, a.z * d); }
        public static Vector3 operator /(Vector3 a, float d) { return new Vector3(a.x / d, a.y / d, a.z / d); }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) : this(r, g, b, 1f) { }
        public static Color white { get { return new Color(1, 1, 1, 1); } }
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color32(Color c)
        {
            return new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(c.a * 255f));
        }
    }

    public class RectOffset
    {
        public RectOffset(int left, int right, int top, int bottom) { }
    }

    public struct Keyframe
    {
        public float time, value;
        public Keyframe(float time, float value) { this.time = time; this.value = value; }
        public Keyframe(float time, float value, float inTangent, float outTangent) { this.time = time; this.value = value; }
    }

    public class AnimationCurve
    {
        private readonly Keyframe[] _k;
        public AnimationCurve(params Keyframe[] keys) { _k = keys; }
        public float Evaluate(float t)
        {
            if (_k == null || _k.Length == 0) return 0f;
            if (t <= _k[0].time) return _k[0].value;
            for (int i = 1; i < _k.Length; i++)
            {
                if (t <= _k[i].time)
                {
                    float u = (t - _k[i - 1].time) / (_k[i].time - _k[i - 1].time);
                    return _k[i - 1].value + (_k[i].value - _k[i - 1].value) * u;
                }
            }
            return _k[_k.Length - 1].value;
        }
    }

    public static class Mathf
    {
        public const float Rad2Deg = 57.29578f;
        public const float Deg2Rad = 0.017453292f;
        public static float Abs(float f) { return Math.Abs(f); }
        public static float Atan(float f) { return (float)Math.Atan(f); }
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
        public static float Sin(float f) { return (float)Math.Sin(f); }
        public static float Cos(float f) { return (float)Math.Cos(f); }
        public static float Pow(float f, float p) { return (float)Math.Pow(f, p); }
        public static float Sqrt(float f) { return (float)Math.Sqrt(f); }
        public static float Clamp(float v, float min, float max) { if (v < min) v = min; else if (v > max) v = max; return v; }
        public static float Clamp01(float v) { return Clamp(v, 0f, 1f); }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static int Max(int a, int b) { return a > b ? a : b; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static int Min(int a, int b) { return a < b ? a : b; }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp(t, 0f, 1f); }
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta) return target;
            return current + Math.Sign(target - current) * maxDelta;
        }
        public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime)
        {
            // Test stand-in: snap to target (exact Unity damping is irrelevant to these tests).
            currentVelocity = 0f;
            return target;
        }
        public static int RoundToInt(float f) { return (int)Math.Round(f); }
        public static int CeilToInt(float f) { return (int)Math.Ceiling(f); }
        public static int FloorToInt(float f) { return (int)Math.Floor(f); }
        public static float Round(float f) { return (float)Math.Round(f); }
        public static float Sign(float f) { return f < 0f ? -1f : f > 0f ? 1f : 0f; }
        public static bool Approximately(float a, float b) { return Math.Abs(a - b) < 1e-5f; }
    }

    public static class Time
    {
        public static float time { get { return 0f; } }
        public static float unscaledTime { get; set; }
        public static float timeScale { get; set; }
        public static float deltaTime { get { return 0f; } }
        public static float fixedDeltaTime { get; set; }
    }

    public static class Screen
    {
        public static int width { get; set; } = 1920;
        public static int height { get; set; } = 1080;
    }

    public class GUIUtility
    {
        // Test hook: a null buffer simulates "no clipboard" (the getter throws).
        public static string TestBuffer = "";
        public static string systemCopyBuffer
        {
            get { if (TestBuffer == null) throw new InvalidOperationException("no clipboard"); return TestBuffer; }
            set { if (TestBuffer == null) throw new InvalidOperationException("no clipboard"); TestBuffer = value; }
        }
    }

    public static class Cursor
    {
        public static CursorLockMode lockState { get; set; }
        public static bool visible { get; set; }
    }

    public static class Input
    {
        public static bool GetKeyDown(KeyCode key) { return false; }
        public static bool GetKey(KeyCode key) { return false; }
        public static Vector3 mousePosition { get { return default(Vector3); } }
    }

    public static class Resources
    {
        public static T GetBuiltinResource<T>(string path) where T : Object { return null; }
        public static T Load<T>(string path) where T : Object { return null; }
    }

    public static class Application
    {
        public static string persistentDataPath { get { return ""; } }
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    public sealed class Font : Object
    {
        public static Font CreateDynamicFontFromOSFont(string fontname, int size) { return null; }
    }

    public sealed class Rigidbody : Component
    {
        public Vector3 velocity { get; set; }
        public Vector3 angularVelocity { get; set; }
        public float mass { get; set; }
        public Vector3 centerOfMass { get; set; }
        public Vector3 inertiaTensor { get; set; }
        // 0.11.0: AddForceAtPosition is a recorder so the harness can verify the
        // FixedUpdate lift delivery (force, world position) that the game sees.
        public readonly List<KeyValuePair<Vector3, Vector3>> ForceCalls = new List<KeyValuePair<Vector3, Vector3>>();
        public void AddForceAtPosition(Vector3 force, Vector3 position) { ForceCalls.Add(new KeyValuePair<Vector3, Vector3>(force, position)); }
    }

    public sealed class Canvas : Behaviour
    {
        public RenderMode renderMode { get; set; }
        public int sortingOrder { get; set; }
        public bool overrideSorting { get; set; }
    }

    public sealed class CanvasGroup : Behaviour
    {
        public float alpha { get; set; }
        public bool interactable { get; set; }
        public bool blocksRaycasts { get; set; }
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<T0>(T0 arg0);
    public delegate void UnityAction<T0, T1>(T0 arg0, T1 arg1);

    public class UnityEvent
    {
        public void AddListener(UnityAction call) { }
        public void Invoke() { }
    }

    public abstract class UnityEvent<T0>
    {
        public void AddListener(UnityAction<T0> call) { }
        public void Invoke(T0 arg0) { }
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name { get { return ""; } }
    }

    public enum LoadSceneMode { Single, Additive }

    public static class SceneManager
    {
        public static event Events.UnityAction<Scene, LoadSceneMode> sceneLoaded;
    }
}

namespace UnityEngine.EventSystems
{
    public abstract class UIBehaviour : MonoBehaviour { }

    public class EventSystem : UIBehaviour
    {
        public static EventSystem current { get; set; }
        public GameObject currentSelectedGameObject { get { return null; } }
        public void SetSelectedGameObject(GameObject selected) { }
    }

    public abstract class BaseInputModule : UIBehaviour { }
    public class StandaloneInputModule : BaseInputModule { }

    public class PointerEventData
    {
        public enum InputButton { Left, Right, Middle }
        public Vector2 position { get; set; }
        public Vector2 pressPosition { get; set; }   // PointerEventData.pressPosition (Unity 2020.3)
        public int clickCount { get; set; }
        private bool _used;
        public bool used { get { return _used; } }
        public void Use() { _used = true; }
        public bool dragging { get; set; }
        public Camera pressEventCamera { get; set; }
        public InputButton button { get; set; }
    }

    public interface IBeginDragHandler { void OnBeginDrag(PointerEventData eventData); }
    public interface IDragHandler { void OnDrag(PointerEventData eventData); }
    public interface IEndDragHandler { void OnEndDrag(PointerEventData eventData); }
    public interface IPointerClickHandler { void OnPointerClick(PointerEventData eventData); }
}

namespace UnityEngine.UI
{
    using UnityEngine.Events;
    using UnityEngine.EventSystems;

    public abstract class Graphic : UIBehaviour
    {
        public virtual Color color { get; set; }
        public virtual bool raycastTarget { get; set; }
        public RectTransform rectTransform { get { return null; } }
        protected virtual void OnPopulateMesh(VertexHelper vh) { }
        public void SetVerticesDirty() { }
    }

    public class VertexHelper
    {
        public int currentVertCount { get { return 0; } }
        public void Clear() { }
        public void AddVert(Vector3 position, Color32 color, Vector2 uv0) { }
        public void AddTriangle(int idx0, int idx1, int idx2) { }
    }

    public struct UIVertex
    {
        public Vector3 position;
        public Color32 color;
        public Vector2 uv0;
    }

    public abstract class MaskableGraphic : Graphic { }

    public class Image : MaskableGraphic { }

    public class Text : MaskableGraphic
    {
        public Font font { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
        public bool supportRichText { get; set; }
        public virtual string text { get; set; }
    }

    public struct ColorBlock
    {
        public Color normalColor { get; set; }
        public Color highlightedColor { get; set; }
        public Color pressedColor { get; set; }
        public Color selectedColor { get; set; }
        public Color disabledColor { get; set; }
        public float colorMultiplier { get; set; }
        public float fadeDuration { get; set; }
    }

    public struct Navigation
    {
        public enum Mode { None = 0, Horizontal = 1, Vertical = 2, Automatic = 3, Explicit = 4 }
        public Mode mode { get; set; }
    }

    public class Selectable : UIBehaviour
    {
        public enum Transition { None, ColorTint, SpriteSwap, Animation }
        public Transition transition { get; set; }
        public ColorBlock colors { get; set; }
        public Navigation navigation { get; set; }
        public bool interactable { get; set; }
        public Graphic targetGraphic { get; set; }
    }

    public class Button : Selectable
    {
        public class ButtonClickedEvent : UnityEvent { }
        public ButtonClickedEvent onClick { get; set; }
    }

    public class Slider : Selectable
    {
        public enum Direction { LeftToRight, RightToLeft, BottomToTop, TopToBottom }
        public class SliderEvent : UnityEvent<float> { }
        public Direction direction { get; set; }
        public RectTransform fillRect { get; set; }
        public RectTransform handleRect { get; set; }
        public float minValue { get; set; }
        public float maxValue { get; set; }
        public virtual float value { get; set; }
        public bool wholeNumbers { get; set; }
        public SliderEvent onValueChanged { get; set; }
    }

    public class Scrollbar : Selectable
    {
        public enum Direction { LeftToRight, RightToLeft, BottomToTop, TopToBottom }
        public Direction direction { get; set; }
        public RectTransform handleRect { get; set; }
    }

    // Real ScrollRect implements the drag interfaces; the curve editor forwards
    // list-scroll drags to these (UnityEngine.UI 2020.3: public virtual).
    public class ScrollRect : UIBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public virtual void OnBeginDrag(PointerEventData eventData) { }
        public virtual void OnDrag(PointerEventData eventData) { }
        public virtual void OnEndDrag(PointerEventData eventData) { }
        public enum MovementType { Unrestricted, Elastic, Clamped }
        public enum ScrollbarVisibility { Permanent, AutoHide, AutoHideAndExpandViewport }
        public bool horizontal { get; set; }
        public bool vertical { get; set; }
        public MovementType movementType { get; set; }
        public float scrollSensitivity { get; set; }
        public bool inertia { get; set; }
        public RectTransform viewport { get; set; }
        public RectTransform content { get; set; }
        public Scrollbar verticalScrollbar { get; set; }
        public ScrollbarVisibility verticalScrollbarVisibility { get; set; }
        public float verticalNormalizedPosition { get; set; }
    }

    public class CanvasScaler : UIBehaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public enum ScreenMatchMode { MatchWidthOrHeight, Expand, Shrink }
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public ScreenMatchMode screenMatchMode { get; set; }
        public float matchWidthOrHeight { get; set; }
        public float scaleFactor { get; set; }
    }

    public class GraphicRaycaster : MonoBehaviour { }
    public class RectMask2D : UIBehaviour { }

    public class LayoutElement : UIBehaviour
    {
        public bool ignoreLayout { get; set; }
        public float minWidth { get; set; }
        public float minHeight { get; set; }
        public float preferredWidth { get; set; }
        public float preferredHeight { get; set; }
        public float flexibleWidth { get; set; }
        public float flexibleHeight { get; set; }
    }

    public abstract class LayoutGroup : UIBehaviour
    {
        public RectOffset padding { get; set; }
        public TextAnchor childAlignment { get; set; }
    }

    public abstract class HorizontalOrVerticalLayoutGroup : LayoutGroup
    {
        public float spacing { get; set; }
        public bool childControlWidth { get; set; }
        public bool childControlHeight { get; set; }
        public bool childForceExpandWidth { get; set; }
        public bool childForceExpandHeight { get; set; }
    }

    public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup { }
    public class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup { }

    public class ContentSizeFitter : UIBehaviour
    {
        public enum FitMode { Unconstrained, MinSize, PreferredSize }
        public FitMode horizontalFit { get; set; }
        public FitMode verticalFit { get; set; }
    }
}

namespace TMPro
{
    public abstract class TMP_Text : UnityEngine.UI.MaskableGraphic
    {
        public virtual string text { get; set; }
    }
}

namespace UnityEngine.InputSystem
{
    public enum Key { None = 0, Space = 1, Enter = 2, Digit1 = 41, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0, Escape = 60, Home = 72, End = 73, Insert = 74, F1 = 94, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12 }

    public class KeyControl
    {
        public bool wasPressedThisFrame { get { return false; } }
    }

    public class Keyboard
    {
        public static Keyboard current { get { return null; } }
        public KeyControl this[Key key] { get { return null; } }
    }
}
