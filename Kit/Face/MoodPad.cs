using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HSPE.Kit.Face
{
    using HSUIKit;

    /// <summary>
    /// The mood pad: moods around a circle (from the recipes, per sex), neutral in the centre. Drag the dot: the angle
    /// picks the two neighbouring moods, the distance from the centre is the intensity. Right-click = centre.
    /// </summary>
    internal sealed class MoodPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private const float Edge = 0.42f; // full intensity at this fraction of the pad size from the centre

        public FaceDiagram.BeginHandler OnBegin;
        public VoidHandler OnChanged;
        public VoidHandler OnEnd;

        private RectTransform _area;
        private Image _dot;
        private readonly List<Text> _labels = new List<Text>();
        private readonly List<float> _angles = new List<float>(); // per label, radians (was parsed back from the name each refresh)
        private FaceSession _session;
        private bool _dragging;
        private float _lastWidth = -1f;

        public static MoodPad Create(RectTransform parent)
        {
            RectTransform host = Kit.Rect(parent, "MoodPad");
            Kit.Fill(host);
            RectTransform area = Kit.Rect(host, "Area");
            area.anchorMin = area.anchorMax = new Vector2(0.5f, 0.5f);
            AspectRatioFitter fit = area.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 1f;
            Image bg = Kit.Image(area, "Pad", PadSprite, Theme.TextDim, true);
            Kit.Fill(bg.rectTransform);
            MoodPad pad = area.gameObject.AddComponent<MoodPad>();
            pad._area = area;
            pad._dot = Kit.Image(area, "Dot", Sprites.Circle, Theme.Accent);
            pad._dot.raycastTarget = false;
            Kit.Anchor(pad._dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 16f));
            Image dotRing = Kit.Image(pad._dot.rectTransform, "Ring", Sprites.Ring, Color.white);
            dotRing.raycastTarget = false;
            Kit.Fill(dotRing.rectTransform, -2f, -2f, -2f, -2f);
            KitTooltip.Attach(area.gameObject, "Drag the dot: direction = mood, distance from the centre = strength\nRight-click = neutral");
            return pad;
        }

        public void Bind(FaceSession session)
        {
            _session = session;
            foreach (Text t in _labels)
                Destroy(t.gameObject);
            _labels.Clear();
            _angles.Clear();
            if (session == null)
                return;
            List<FaceEntry> moods = new List<FaceEntry>();
            FaceMix.Moods(FaceSessions.Recipes, session.Rig.Sex, moods);
            foreach (FaceEntry m in moods)
            {
                Text t = Kit.MakeText(_area, "Label " + m.Name, m.Name, Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleCenter);
                t.raycastTarget = false;
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                t.rectTransform.sizeDelta = new Vector2(70f, 18f);
                _labels.Add(t);
                _angles.Add(m.Angle * Mathf.Deg2Rad);
            }
            _lastWidth = -1f;
            Refresh();
        }

        /// <summary>Dot and labels to the current input (after undo, paste, reset, resize).</summary>
        public void Refresh()
        {
            float size = _area.rect.width;
            float r = size * 0.36f; // labels sit inside the rim
            for (int i = 0; i < _labels.Count; i++)
                _labels[i].rectTransform.anchoredPosition = new Vector2(Mathf.Cos(_angles[i]), Mathf.Sin(_angles[i])) * r;
            if (_session == null)
            {
                _dot.rectTransform.anchoredPosition = Vector2.zero;
                return;
            }
            float ang = _session.Input.Angle * Mathf.Deg2Rad;
            _dot.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (_session.Input.Radius * size * Edge);
        }

        private void LateUpdate()
        {
            float w = _area.rect.width;
            if (Mathf.Approximately(w, _lastWidth) == false)
            {
                _lastWidth = w;
                Refresh();
            }
        }

        // ---------------------------------------------------------------- input

        public void OnPointerDown(PointerEventData e)
        {
            if (_session == null)
                return;
            if (e.button == PointerEventData.InputButton.Right)
            {
                if (_session.Input.Radius > 0f && (OnBegin == null || OnBegin()))
                {
                    _session.Input.Radius = 0f;
                    Refresh();
                    if (OnChanged != null) OnChanged();
                    if (OnEnd != null) OnEnd();
                }
                return;
            }
            if (e.button != PointerEventData.InputButton.Left || (OnBegin != null && OnBegin() == false))
                return;
            _dragging = true;
            Set(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (_dragging)
                Set(e);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (_dragging == false)
                return;
            _dragging = false;
            if (OnEnd != null)
                OnEnd();
        }

        private void Set(PointerEventData e)
        {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_area, e.position, null, out local);
            Vector2 v = local - _area.rect.center;
            float radius = Mathf.Clamp01(v.magnitude / Mathf.Max(1f, _area.rect.width * Edge));
            float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            if (angle < 0f)
                angle += 360f;
            _session.Input.Angle = angle;
            _session.Input.Radius = radius;
            Refresh();
            if (OnChanged != null)
                OnChanged();
        }

        // ---------------------------------------------------------------- drawing

        internal static Sprite PadSprite
        {
            get
            {
                return Sprites.Custom("hspe_mood_pad", 256, (x, y) =>
                {
                    float dx = x - 128f, dy = y - 128f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r > 126f)
                        return 0f;
                    float rim = Mathf.Clamp01(1.5f - Mathf.Abs(r - 108f));                     // full-strength ring
                    float mid = Mathf.Clamp01(1.2f - Mathf.Abs(r - 54f)) * 0.5f;               // half-strength ring
                    float cross = Mathf.Clamp01(1.0f - Mathf.Min(Mathf.Abs(dx), Mathf.Abs(dy))) * 0.35f * (r < 108f ? 1f : 0f);
                    float fill = 0.10f;                                                          // faint disc
                    return Mathf.Max(Mathf.Max(rim * 0.9f, mid), Mathf.Max(cross, fill));
                });
            }
        }
    }
}
