using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HSPE.Kit.Face
{
    using HSUIKit;

    /// <summary>
    /// The Face window's left side: a drawn face with one point per recipe drag point (plus pinned extra bones).
    /// Drag = move within the point's range, Shift = the twin mirrors, Ctrl = fine; right-click = reset the point;
    /// double-click empty space = reset every drag. Values live in FaceSession.Input.Drags (-1..1 per axis).
    /// </summary>
    internal sealed class FaceDiagram : MonoBehaviour
    {
        private const float Reach = 0.07f; // a full drag moves the point this fraction of the diagram
        private const float DotSize = 14f;

        internal delegate bool BeginHandler(); // false = don't start (the window asked before overwriting)

        private sealed class Handle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public FaceDiagram owner;
            public FacePoint point;
            public bool pinned;
            public Vector2 at; // 0..1 from top-left
            public RectTransform rect;
            public Image dot, ring;
            public bool hover, dragging, allowed;
            public Vector2 last;

            public void OnPointerEnter(PointerEventData e) { hover = true; owner.Paint(this); }
            public void OnPointerExit(PointerEventData e) { hover = false; owner.Paint(this); }
            public void OnBeginDrag(PointerEventData e) { owner.BeginDrag(this, e); }
            public void OnDrag(PointerEventData e) { owner.Drag(this, e); }
            public void OnEndDrag(PointerEventData e) { owner.EndDrag(this); }

            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Right && e.dragging == false)
                    owner.ResetPoint(this);
            }
        }

        private sealed class Background : MonoBehaviour, IPointerClickHandler
        {
            public FaceDiagram owner;
            public void OnPointerClick(PointerEventData e) { owner.BackgroundClick(e); }
        }

        public BeginHandler OnBegin;   // before a change: the window pushes undo (and may refuse)
        public VoidHandler OnChanged;  // the input changed: mix + write
        public VoidHandler OnEnd;      // a gesture ended

        private RectTransform _area;
        private Text _hint;
        private FaceSession _session;
        private readonly List<Handle> _handles = new List<Handle>();
        private string _pinBone; // waiting for a click to place this extra bone

        public static FaceDiagram Create(RectTransform parent)
        {
            RectTransform host = Kit.Rect(parent, "FaceDiagram");
            Kit.Fill(host);
            Image bg = host.gameObject.AddComponent<Image>();
            Kit.SetSprite(bg, Sprites.Rounded(Theme.RadiusSmall));
            bg.color = Kit.Shade(Theme.Surface, -0.12f);
            bg.raycastTarget = false;
            FaceDiagram d = host.gameObject.AddComponent<FaceDiagram>();

            // square drawing area, as large as fits above the hint strip (so the hint never covers the chin)
            RectTransform areaHost = Kit.Rect(host, "AreaHost");
            Kit.Fill(areaHost, 8f, 34f, 8f, 8f);
            d._area = Kit.Rect(areaHost, "Area");
            d._area.anchorMin = new Vector2(0.5f, 0.5f);
            d._area.anchorMax = new Vector2(0.5f, 0.5f);
            AspectRatioFitter fit = d._area.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1f;
            Image face = Kit.Image(d._area, "Face", FaceSprite, Kit.Alpha(Theme.TextDim, 0.9f), true);
            Kit.Fill(face.rectTransform);
            face.gameObject.AddComponent<Background>().owner = d;

            d._hint = Kit.MakeText(host, "Hint", "Drag points · Shift mirrors · Ctrl fine · right-click resets · double-click empty space resets all", Theme.SmallSize, Theme.TextDim, TextAnchor.LowerCenter);
            d._hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            d._hint.rectTransform.anchorMin = new Vector2(0f, 0f);
            d._hint.rectTransform.anchorMax = new Vector2(1f, 0f);
            d._hint.rectTransform.offsetMin = new Vector2(8f, 4f);
            d._hint.rectTransform.offsetMax = new Vector2(-8f, 32f);
            d._hint.alignment = TextAnchor.MiddleCenter;
            return d;
        }

        // ---------------------------------------------------------------- binding

        public void Bind(FaceSession session)
        {
            _session = session;
            _pinBone = null;
            Rebuild();
        }

        public void Rebuild()
        {
            foreach (Handle h in _handles)
                Destroy(h.gameObject);
            _handles.Clear();
            if (_session == null)
                return;
            foreach (FacePoint p in FaceSessions.Recipes.Points)
                if (p.Bone != null && _session.Rig.FindBone(p.Bone) != null)
                    AddHandle(p, new Vector2(p.AtX, p.AtY), false);
            foreach (FacePoint p in _session.Input.ExtraPoints)
            {
                Vector2 at;
                if (FacePins.TryGet(p.Bone, out at))
                    AddHandle(p, at, true);
            }
            Refresh();
        }

        private void AddHandle(FacePoint p, Vector2 at, bool pinned)
        {
            RectTransform rt = Kit.Rect(_area, "Point " + p.Name);
            rt.anchorMin = rt.anchorMax = new Vector2(at.x, 1f - at.y);
            rt.sizeDelta = new Vector2(DotSize + 10f, DotSize + 10f); // generous hit area
            Handle h = rt.gameObject.AddComponent<Handle>();
            h.owner = this;
            h.point = p;
            h.pinned = pinned;
            h.at = at;
            h.rect = rt;
            Image hit = rt.gameObject.AddComponent<Image>();
            hit.color = Color.clear; // raycast target for the handle
            h.ring = Kit.Image(rt, "Ring", Sprites.Ring, Theme.Warning);
            Kit.Anchor(h.ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(DotSize + 8f, DotSize + 8f));
            h.dot = Kit.Image(rt, "Dot", Sprites.Circle, DotColor(p, pinned));
            Kit.Anchor(h.dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(DotSize, DotSize));
            KitTooltip.Attach(rt.gameObject, (pinned ? "Extra bone " : "") + Nice(p.Name) + "  ·  " + p.Bone.Replace("*_", "") + "\nDrag · Shift mirrors · Ctrl fine · right-click resets");
            _handles.Add(h);
        }

        private static string Nice(string name)
        {
            return name.StartsWith("x:") ? name.Substring(2) : name;
        }

        private static readonly Color Blue = new Color32(0x5B, 0x8C, 0xFF, 0xFF);
        private static readonly Color Pink = new Color32(0xF0, 0x61, 0x6D, 0xFF);
        private static readonly Color Orange = new Color32(0xF0, 0xA3, 0x5B, 0xFF);
        private static readonly Color Grey = new Color32(0x97, 0x9D, 0xAB, 0xFF);

        private static Color DotColor(FacePoint p, bool pinned)
        {
            if (pinned)
                return Grey;
            if (p.AtY >= 0.84f)
                return Orange; // jaw
            return p.AtY >= 0.6f ? Pink : Blue; // mouth : brows / eyes
        }

        /// <summary>Moves every point to its current drag value (after undo, paste, reset...).</summary>
        public void Refresh()
        {
            foreach (Handle h in _handles)
                Place(h);
        }

        private void Place(Handle h)
        {
            float[] d = Value(h.point.Name);
            float size = _area.rect.width;
            h.rect.anchoredPosition = new Vector2(d[0], d[1]) * (size * Reach);
            Paint(h);
        }

        private float[] Value(string key)
        {
            float[] d;
            if (_session != null && _session.Input.Drags.TryGetValue(key, out d) && d != null)
                return d;
            return Zero;
        }

        private static readonly float[] Zero = { 0f, 0f };

        private void Paint(Handle h)
        {
            float[] d = Value(h.point.Name);
            bool changed = d[0] != 0f || d[1] != 0f;
            h.ring.enabled = changed;
            h.dot.rectTransform.localScale = Vector3.one * (h.hover || h.dragging ? 1.3f : 1f);
        }

        private float _lastWidth = -1f;

        private void LateUpdate()
        {
            // the area is sized by its AspectRatioFitter: re-place points when the window is resized
            float w = _area.rect.width;
            if (Mathf.Approximately(w, _lastWidth) == false)
            {
                _lastWidth = w;
                Refresh();
            }
        }

        // ---------------------------------------------------------------- dragging

        private void BeginDrag(Handle h, PointerEventData e)
        {
            h.allowed = e.button == PointerEventData.InputButton.Left && _session != null && (OnBegin == null || OnBegin());
            if (h.allowed == false)
                return;
            h.dragging = true;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, e.position, null, out h.last);
        }

        private void Drag(Handle h, PointerEventData e)
        {
            if (h.allowed == false)
                return;
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, e.position, null, out local);
            Vector2 delta = local - h.last;
            h.last = local;
            float scale = 1f / Mathf.Max(1f, _area.rect.width * Reach);
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                scale *= 0.25f;
            float[] d = (float[])Value(h.point.Name).Clone();
            d[0] = Mathf.Clamp(d[0] + delta.x * scale, -1f, 1f);
            d[1] = Mathf.Clamp(d[1] + delta.y * scale, -1f, 1f);
            _session.Input.Drags[h.point.Name] = d;
            if (h.point.Twin != null && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                _session.Input.Drags[h.point.Twin] = FaceMix.MirrorDrag(d);
            Refresh();
            if (OnChanged != null)
                OnChanged();
        }

        private void EndDrag(Handle h)
        {
            if (h.allowed == false)
                return;
            h.dragging = false;
            h.allowed = false;
            Paint(h);
            if (OnEnd != null)
                OnEnd();
        }

        private void ResetPoint(Handle h)
        {
            if (_session == null || _session.Input.Drags.ContainsKey(h.point.Name) == false || (OnBegin != null && OnBegin() == false))
                return;
            _session.Input.Drags.Remove(h.point.Name);
            Refresh();
            if (OnChanged != null)
                OnChanged();
            if (OnEnd != null)
                OnEnd();
        }

        private void BackgroundClick(PointerEventData e)
        {
            if (_session == null || e.button != PointerEventData.InputButton.Left)
                return;
            if (_pinBone != null)
            {
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, e.position, null, out local);
                Rect r = _area.rect;
                FacePins.Set(_pinBone, new Vector2((local.x - r.xMin) / r.width, 1f - (local.y - r.yMin) / r.height));
                _pinBone = null;
                _hint.text = "Drag points · Shift mirrors · Ctrl fine · right-click resets · double-click empty space resets all";
                Rebuild();
                return;
            }
            if (e.clickCount >= 2 && _session.Input.Drags.Count != 0 && (OnBegin == null || OnBegin()))
            {
                _session.Input.Drags.Clear();
                Refresh();
                if (OnChanged != null)
                    OnChanged();
                if (OnEnd != null)
                    OnEnd();
            }
        }

        /// <summary>The next click on the face places this extra bone's point.</summary>
        public void BeginPin(string bone)
        {
            _pinBone = bone;
            _hint.text = "Click on the face where " + bone + " should go";
        }

        public void Unpin(string bone)
        {
            FacePins.Remove(bone);
            Rebuild();
        }

        // ---------------------------------------------------------------- drawing

        /// <summary>A plain line drawing of a face (distance-field lines), 256 px; y is up in the texture.</summary>
        private static Sprite FaceSprite
        {
            get
            {
                return Sprites.Custom("hspe_face_outline", 256, (x, y) =>
                {
                    float d = Ellipse(x, y, 128f, 116f, 88f, 112f);                 // head
                    d = Mathf.Min(d, Ellipse(x, y, 92f, 154f, 19f, 8f));             // eyes
                    d = Mathf.Min(d, Ellipse(x, y, 164f, 154f, 19f, 8f));
                    d = Mathf.Min(d, Sprites.SegmentDistance(x, y, 70f, 174f, 104f, 181f)); // brows
                    d = Mathf.Min(d, Sprites.SegmentDistance(x, y, 152f, 181f, 186f, 174f));
                    d = Mathf.Min(d, Sprites.SegmentDistance(x, y, 128f, 146f, 122f, 112f)); // nose
                    d = Mathf.Min(d, Sprites.SegmentDistance(x, y, 122f, 112f, 132f, 110f));
                    d = Mathf.Min(d, Sprites.SegmentDistance(x, y, 104f, 73f, 128f, 67f));   // mouth
                    d = Mathf.Min(d, Sprites.SegmentDistance(x, y, 128f, 67f, 152f, 73f));
                    return Mathf.Clamp01(1.6f - d);
                });
            }
        }

        private static float Ellipse(float x, float y, float cx, float cy, float rx, float ry)
        {
            float nx = (x - cx) / rx, ny = (y - cy) / ry;
            float k = Mathf.Sqrt(nx * nx + ny * ny);
            return Mathf.Abs(k - 1f) * Mathf.Min(rx, ry);
        }
    }

    /// <summary>Where extra bones were pinned on the diagram: UserData\HSPE\Face\face_pins.txt ("bone x y").</summary>
    internal static class FacePins
    {
        private static Dictionary<string, Vector2> _pins;

        private static string FilePath { get { return Path.Combine(Application.dataPath, "../UserData/HSPE/Face/face_pins.txt"); } }

        private static void Load()
        {
            if (_pins != null)
                return;
            _pins = new Dictionary<string, Vector2>();
            try
            {
                if (File.Exists(FilePath) == false)
                    return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] w = line.Trim().Split(' ');
                    float x, y;
                    if (w.Length == 3 && float.TryParse(w[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)
                        && float.TryParse(w[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y))
                        _pins[w[0]] = new Vector2(x, y);
                }
            }
            catch (System.Exception e)
            {
                Log.Write("face pins: " + e.Message);
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, Vector2> p in _pins)
                    lines.Add(p.Key + " " + p.Value.x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " " + p.Value.y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (System.Exception e)
            {
                Log.Write("face pins: " + e.Message);
            }
        }

        public static bool TryGet(string bone, out Vector2 at)
        {
            Load();
            return _pins.TryGetValue(bone, out at);
        }

        public static bool Has(string bone)
        {
            Vector2 at;
            return TryGet(bone, out at);
        }

        public static void Set(string bone, Vector2 at)
        {
            Load();
            _pins[bone] = new Vector2(Mathf.Clamp01(at.x), Mathf.Clamp01(at.y));
            Save();
        }

        public static void Remove(string bone)
        {
            Load();
            if (_pins.Remove(bone))
                Save();
        }
    }
}
