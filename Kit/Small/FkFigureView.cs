using System.Collections.Generic;
using Studio;
using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Small
{
    using HSUIKit;

    /// <summary>
    /// An FK figure (Body or Hands page): joints at fixed places, each bound to the target's FK bone with that key
    /// (see <see cref="FkBones.Key"/>). A joint whose bone the target doesn't have is hidden, so the figure shows
    /// exactly Studio's FK bones; bones without a place here go to the More list. Click selects the bone like Joan's
    /// FK list; Studio's own selection is what the Rotate pads turn.
    /// </summary>
    internal sealed class FkFigureView : MonoBehaviour
    {
        private const float JointSize = 14f, SelectedSize = 18f, HitSize = 18f;

        private sealed class Slot
        {
            public string key, label;
            public float x, y;
            public OCIChar.BoneInfo bone;
            public RectTransform rect;
            public Image glow, fill, ring;
            public bool hover, selected, enabled;
            public bool drawn, shownHover, shownSelected, shownEnabled;
        }

        private delegate void Background(RectTransform root);

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly Dictionary<string, Slot> _byKey = new Dictionary<string, Slot>();

        // ---------------------------------------------------------------- the two figures

        public static FkFigureView CreateBody(Transform parent)
        {
            FkFigureView v = Create(parent, "FK Body", FigureDraw.Body);
            v.Add("head", "Head", 115f, 28f);
            v.Add("neck", "Neck", 115f, 47f);
            v.Add("spine03", "Chest", 115f, 64f);
            v.Add("spine02", "Spine 2", 115f, 80f);
            v.Add("spine01", "Spine 1", 115f, 95f);
            v.Add("kosi01", "Waist", 115f, 109f);
            v.Add("hips", "Hips", 115f, 122f);
            v.Add("kosi02", "Pelvis", 115f, 137f);
            v.Side("r", "Right", 1f);
            v.Side("l", "Left", -1f);
            return v;
        }

        /// <summary>One side of the body page; mirror = 1 for the right side (screen left), -1 for the left.</summary>
        private void Side(string s, string name, float mirror)
        {
            Add("shoulder_" + s, name + " shoulder", X(97f, mirror), 58f);
            Add("armup00_" + s, name + " upper arm", X(80f, mirror), 62f);
            Add("armlow01_" + s, name + " forearm", X(62f, mirror), 98f);
            Add("hand_" + s, name + " wrist", X(50f, mirror), 138f);
            Add("legup00_" + s, name + " thigh", X(96f, mirror), 130f);
            Add("leglow01_" + s, name + " knee", X(92f, mirror), 180f);
            Add("foot01_" + s, name + " ankle", X(90f, mirror), 222f);
            Add("foot02_" + s, name + " foot", X(80f, mirror), 232f);
            Add("toes01_" + s, name + " toes", X(70f, mirror), 240f);
        }

        public static FkFigureView CreateHands(Transform parent)
        {
            FkFigureView v = Create(parent, "FK Hands", DrawHands);
            v.HandSlots("r", "Right", 1f);
            v.HandSlots("l", "Left", -1f);
            return v;
        }

        // Right hand on the left of the page (palm towards the viewer, thumb outward); the left hand mirrors it.
        private static readonly string[] Fingers = { "thumb", "index", "middle", "ring", "little" };
        private static readonly float[,] FingerJoints =
        {
            // x1, y1, x2, y2, x3, y3 (joints 01 - 03, right hand)
            { 34f, 196f, 24f, 180f, 16f, 166f },
            { 42f, 170f, 40f, 150f, 38f, 132f },
            { 56f, 168f, 56f, 146f, 56f, 126f },
            { 70f, 170f, 71f, 150f, 72f, 132f },
            { 82f, 176f, 84f, 160f, 86f, 146f },
        };
        private const float PalmX = 62f, PalmY = 196f, PalmW = 54f, PalmH = 40f;

        private void HandSlots(string s, string name, float mirror)
        {
            Add("hand_" + s, name + " wrist", X(PalmX, mirror), PalmY + 12f);
            for (int f = 0; f < Fingers.Length; f++)
            {
                for (int j = 0; j < 3; j++)
                    Add("hand_" + Fingers[f] + "0" + (j + 1) + "_" + s, name + " " + Fingers[f] + " " + (j + 1),
                        X(FingerJoints[f, j * 2], mirror), FingerJoints[f, j * 2 + 1]);
            }
        }

        private static void DrawHands(RectTransform root)
        {
            for (int side = 0; side < 2; side++)
            {
                float mirror = side == 0 ? 1f : -1f;
                Color bone = Theme.SurfaceActive;
                Image palm = Kit.Image(root, "Palm", Sprites.Rounded(Theme.Radius), Theme.SurfaceHover);
                FigureDraw.Place(palm.rectTransform, X(PalmX, mirror), PalmY, PalmW, PalmH);
                for (int f = 0; f < Fingers.Length; f++)
                {
                    float baseX = f == 0 ? X(PalmX - PalmW * 0.5f + 6f, mirror) : X(FingerJoints[f, 0], mirror);
                    float baseY = f == 0 ? PalmY + 8f : PalmY - PalmH * 0.5f;
                    FigureDraw.Line(root, baseX, baseY, X(FingerJoints[f, 0], mirror), FingerJoints[f, 1], 6f, bone);
                    FigureDraw.Line(root, X(FingerJoints[f, 0], mirror), FingerJoints[f, 1], X(FingerJoints[f, 2], mirror), FingerJoints[f, 3], 6f, bone);
                    FigureDraw.Line(root, X(FingerJoints[f, 2], mirror), FingerJoints[f, 3], X(FingerJoints[f, 4], mirror), FingerJoints[f, 5], 6f, bone);
                }
                Text t = Kit.MakeText(root, "Title", side == 0 ? "Right hand" : "Left hand", Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleCenter);
                FigureDraw.Place(t.rectTransform, X(PalmX, mirror), 96f, 100f, 18f);
            }
        }

        private static float X(float x, float mirror)
        {
            return mirror > 0f ? x : FigureDraw.Width - x;
        }

        // ---------------------------------------------------------------- building

        private static FkFigureView Create(Transform parent, string name, Background background)
        {
            RectTransform root = Kit.Rect(parent, name);
            Kit.Layout(root.gameObject, FigureDraw.Width, FigureDraw.Height);
            FkFigureView v = root.gameObject.AddComponent<FkFigureView>();
            background(root);
            return v;
        }

        private void Add(string key, string label, float x, float y)
        {
            Slot s = new Slot { key = key, label = label, x = x, y = y };
            RectTransform root = (RectTransform)transform;
            s.rect = Kit.Rect(root, "Joint " + label);
            FigureDraw.Place(s.rect, x, y, HitSize, HitSize);
            Kit.HitArea(s.rect.gameObject);
            s.glow = Kit.Image(s.rect, "Glow", Sprites.CircleShadow, Kit.Alpha(Theme.Accent, 0f));
            Kit.Anchor(s.glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34f, 34f));
            s.fill = Kit.Image(s.rect, "Fill", HspeIcons.Disc, Theme.Surface);
            Kit.Anchor(s.fill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(JointSize, JointSize));
            s.ring = Kit.Image(s.rect, "Ring", HspeIcons.RingThin, Theme.TextDim);
            Kit.Anchor(s.ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(JointSize, JointSize));

            KitHoverClick hc = KitHoverClick.On(s.rect.gameObject);
            hc.OnEnter = delegate { s.hover = true; };
            hc.OnExit = delegate { s.hover = false; };
            hc.OnClick = delegate
            {
                if (s.enabled && s.bone != null)
                    FkBones.Select(s.bone);
            };
            KitTooltip.Attach(s.rect.gameObject, label);
            s.rect.gameObject.SetActive(false); // shown once bound to a bone
            _slots.Add(s);
            _byKey[key] = s;
        }

        // ---------------------------------------------------------------- binding + state

        /// <summary>Binds the joints to the target's bones; returns the keys it placed (the More list skips them).</summary>
        public void Bind(Dictionary<string, OCIChar.BoneInfo> map, HashSet<string> placed)
        {
            foreach (Slot s in _slots)
            {
                OCIChar.BoneInfo bone;
                s.bone = map.TryGetValue(s.key, out bone) ? bone : null;
                s.rect.gameObject.SetActive(s.bone != null);
                s.drawn = false;
                if (s.bone != null && placed != null)
                    placed.Add(s.key);
            }
        }

        public bool Has(GuideObject guide)
        {
            return LabelOf(guide) != null;
        }

        public string LabelOf(GuideObject guide)
        {
            if (guide == null)
                return null;
            foreach (Slot s in _slots)
                if (s.bone != null && s.bone.guideObject == guide)
                    return s.label;
            return null;
        }

        /// <summary>False when a bound bone is gone (hair / clothes changed): the window rebinds.</summary>
        public bool Refresh(PoseController target, GuideObject selected)
        {
            bool valid = true;
            foreach (Slot s in _slots)
            {
                if (s.bone == null)
                    continue;
                if (s.bone.guideObject == null)
                {
                    valid = false;
                    continue;
                }
                s.enabled = FkBones.Usable(target, s.bone);
                s.selected = selected != null && s.bone.guideObject == selected;
                Draw(s);
            }
            return valid;
        }

        private static void Draw(Slot s)
        {
            if (s.drawn && s.shownSelected == s.selected && s.shownEnabled == s.enabled && s.shownHover == s.hover)
                return;
            s.drawn = true;
            s.shownSelected = s.selected;
            s.shownEnabled = s.enabled;
            s.shownHover = s.hover;
            Color fill = s.selected ? Theme.Accent : (s.hover && s.enabled ? Theme.SurfaceHover : Theme.Surface);
            Color ring = s.selected ? Color.white : (s.hover && s.enabled ? Theme.Text : Theme.TextDim);
            float alpha = s.enabled ? 1f : 0.3f;
            Tween.Tint(s.fill, Kit.Alpha(fill, alpha), 0.12f, Ease.OutQuad);
            Tween.Tint(s.ring, Kit.Alpha(ring, alpha), 0.12f, Ease.OutQuad);
            Tween.Tint(s.glow, Kit.Alpha(Theme.Accent, s.selected ? 0.55f : 0f), 0.18f, Ease.OutQuad);
            float size = s.selected ? SelectedSize : JointSize;
            if (s.hover && s.enabled && !s.selected)
                size += 2f;
            Tween.Size(s.fill.rectTransform, new Vector2(size, size), 0.18f, Ease.OutBack);
            Tween.Size(s.ring.rectTransform, new Vector2(size, size), 0.18f, Ease.OutBack);
        }
    }
}
