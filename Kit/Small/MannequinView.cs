using System.Collections.Generic;
using RootMotion.FinalIK;
using Studio;
using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Small
{
    using HSUIKit;

    /// <summary>
    /// The IK tab's stick figure, facing the viewer (the character's right side is on the left). Effectors are circles,
    /// bend goals (elbows, knees) diamonds; click selects, Ctrl-click adds / removes, exactly like Joan's buttons.
    /// Right-click a limb for copy-to-other-side and swap.
    /// </summary>
    internal sealed class MannequinView : MonoBehaviour
    {
        public const float Width = FigureDraw.Width, Height = FigureDraw.Height;
        private const float JointSize = 18f, SelectedSize = 22f;

        private sealed class Joint
        {
            public bool isBendGoal;
            public FullBodyBipedEffector effector;
            public FullBodyBipedChain chain;
            public string label;
            public RectTransform rect;
            public Image glow, fill, ring;
            public Text text; // Body only
            public bool hover, selected, enabled = true;
            public bool shownSelected, shownEnabled = true, shownHover, drawn;
        }

        private readonly List<Joint> _joints = new List<Joint>();
        private PoseInput _input;
        private MainWindow _main;
        private Text _selectionText;
        private int _shownState = -1;
        private float _nextEnabledCheck;
        private string _shownFirst;

        public static MannequinView Create(Transform parent, MainWindow main)
        {
            RectTransform root = Kit.Rect(parent, "Mannequin");
            Kit.Layout(root.gameObject, Width, Height);
            MannequinView v = root.gameObject.AddComponent<MannequinView>();
            v._main = main;
            v._input = main._input;

            // right-click on empty space: the whole menu
            Kit.HitArea(root.gameObject);
            KitContextMenu.Attach(root.gameObject, () => v.BuildMenu(null));

            FigureDraw.Body(root);

            v.AddEffector(root, FullBodyBipedEffector.RightShoulder, "Right shoulder", 80f, 58f);
            v.AddEffector(root, FullBodyBipedEffector.LeftShoulder, "Left shoulder", 150f, 58f);
            v.AddBendGoal(root, FullBodyBipedChain.RightArm, "Right elbow", 62f, 98f);
            v.AddBendGoal(root, FullBodyBipedChain.LeftArm, "Left elbow", 168f, 98f);
            v.AddEffector(root, FullBodyBipedEffector.RightHand, "Right hand", 50f, 138f);
            v.AddEffector(root, FullBodyBipedEffector.LeftHand, "Left hand", 180f, 138f);
            v.AddEffector(root, FullBodyBipedEffector.Body, "Body", 115f, 112f);
            v.AddEffector(root, FullBodyBipedEffector.RightThigh, "Right thigh", 96f, 128f);
            v.AddEffector(root, FullBodyBipedEffector.LeftThigh, "Left thigh", 134f, 128f);
            v.AddBendGoal(root, FullBodyBipedChain.RightLeg, "Right knee", 92f, 180f);
            v.AddBendGoal(root, FullBodyBipedChain.LeftLeg, "Left knee", 138f, 180f);
            v.AddEffector(root, FullBodyBipedEffector.RightFoot, "Right foot", 90f, 228f);
            v.AddEffector(root, FullBodyBipedEffector.LeftFoot, "Left foot", 140f, 228f);

            return v;
        }

        /// <summary>A text (owned by the window) that shows what is selected.</summary>
        public void SetSelectionText(Text text)
        {
            _selectionText = text;
        }

        private void AddEffector(RectTransform root, FullBodyBipedEffector effector, string label, float x, float y)
        {
            Joint j = new Joint { effector = effector, label = label };
            Build(root, j, x, y);
        }

        private void AddBendGoal(RectTransform root, FullBodyBipedChain chain, string label, float x, float y)
        {
            Joint j = new Joint { isBendGoal = true, chain = chain, label = label };
            Build(root, j, x, y);
        }

        private void Build(RectTransform root, Joint j, float x, float y)
        {
            bool body = !j.isBendGoal && j.effector == FullBodyBipedEffector.Body;
            // hit area larger than the drawing, for easy clicking
            j.rect = Kit.Rect(root, "Joint " + j.label);
            FigureDraw.Place(j.rect, x, y, body ? 44f : 26f, body ? 26f : 26f);
            Kit.HitArea(j.rect.gameObject);

            j.glow = Kit.Image(j.rect, "Glow", Sprites.CircleShadow, Kit.Alpha(Theme.Accent, 0f));
            Kit.Anchor(j.glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, body ? new Vector2(64f, 44f) : new Vector2(40f, 40f));
            if (body)
            {
                j.fill = Kit.Image(j.rect, "Fill", Sprites.Rounded(Theme.RadiusSmall), Theme.Surface);
                Kit.Anchor(j.fill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 20f));
                j.ring = Kit.Image(j.rect, "Ring", Sprites.Outline(Theme.RadiusSmall, 1), Theme.TextDim);
                Kit.Anchor(j.ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 20f));
                j.text = Kit.MakeText(j.rect, "Label", "Body", Theme.SmallSize, Theme.Text, TextAnchor.MiddleCenter);
                Kit.Fill(j.text.rectTransform);
            }
            else
            {
                j.fill = Kit.Image(j.rect, "Fill", j.isBendGoal ? HspeIcons.Diamond : HspeIcons.Disc, Theme.Surface);
                Kit.Anchor(j.fill.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(JointSize, JointSize));
                j.ring = Kit.Image(j.rect, "Ring", j.isBendGoal ? HspeIcons.DiamondRing : HspeIcons.RingThin, Theme.TextDim);
                Kit.Anchor(j.ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(JointSize, JointSize));
            }

            KitHoverClick hc = KitHoverClick.On(j.rect.gameObject);
            hc.OnEnter = delegate { j.hover = true; };
            hc.OnExit = delegate { j.hover = false; };
            hc.OnClick = delegate { OnJointClicked(j); };
            KitTooltip.Attach(j.rect.gameObject, j.label + (j.isBendGoal ? " (bend goal)" : ""));
            KitContextMenu.Attach(j.rect.gameObject, () => BuildMenu(j));
            _joints.Add(j);
        }

        private void OnJointClicked(Joint j)
        {
            if (!j.enabled)
                return;
            if (j.isBendGoal)
                _input.SelectBendGoal(j.chain, PoseInput.CtrlHeld());
            else
                _input.SelectEffector(j.effector, PoseInput.CtrlHeld());
        }

        // ---------------------------------------------------------------- right-click menu

        private enum Side { None, Right, Left }

        private static Side SideOf(Joint j)
        {
            if (j == null)
                return Side.None;
            if (j.isBendGoal)
                return j.chain == FullBodyBipedChain.RightArm || j.chain == FullBodyBipedChain.RightLeg ? Side.Right : Side.Left;
            switch (j.effector)
            {
                case FullBodyBipedEffector.RightShoulder:
                case FullBodyBipedEffector.RightHand:
                case FullBodyBipedEffector.RightThigh:
                case FullBodyBipedEffector.RightFoot:
                    return Side.Right;
                case FullBodyBipedEffector.Body:
                    return Side.None;
                default:
                    return Side.Left;
            }
        }

        private static bool IsArm(Joint j)
        {
            if (j.isBendGoal)
                return j.chain == FullBodyBipedChain.RightArm || j.chain == FullBodyBipedChain.LeftArm;
            return j.effector == FullBodyBipedEffector.RightShoulder || j.effector == FullBodyBipedEffector.LeftShoulder ||
                   j.effector == FullBodyBipedEffector.RightHand || j.effector == FullBodyBipedEffector.LeftHand;
        }

        /// <summary>Joan's copy-limb / swap buttons. CopyLimbToTwin(X) copies limb X onto the other side.</summary>
        private KitMenu BuildMenu(Joint j)
        {
            CharaPoseController chara = _main.poseTarget as CharaPoseController;
            if (chara == null)
                return null;
            KitMenu menu = new KitMenu();
            Side side = SideOf(j);
            bool arm = j != null && IsArm(j);
            bool leg = j != null && !arm && side != Side.None;
            LimbMenu.Append(menu, chara, side == Side.Right ? LimbMenu.Side.Right : side == Side.Left ? LimbMenu.Side.Left : LimbMenu.Side.None, arm, leg);
            return menu;
        }

        // ---------------------------------------------------------------- per-frame state

        /// <summary>Called by the window each frame while visible.</summary>
        public void Refresh(PoseController target)
        {
            CharaPoseController chara = target as CharaPoseController;
            // Joan's IsPartEnabled looks up an enum-keyed Dictionary (boxes on this Mono): parts switch rarely, 5x/s is plenty
            bool checkEnabled = Time.unscaledTime >= _nextEnabledCheck;
            if (checkEnabled)
                _nextEnabledCheck = Time.unscaledTime + 0.2f;
            int selectedCount = 0;
            string firstSelected = null;
            for (int i = 0; i < _joints.Count; i++)
            {
                Joint j = _joints[i];
                if (j.isBendGoal)
                {
                    if (checkEnabled)
                        j.enabled = chara != null && chara.IsPartEnabled(j.chain);
                    j.selected = _input.IsBendGoalSelected(j.chain);
                }
                else
                {
                    if (checkEnabled)
                        j.enabled = chara != null && chara.IsPartEnabled(j.effector);
                    j.selected = _input.IsEffectorSelected(j.effector);
                }
                if (j.selected)
                {
                    selectedCount++;
                    if (firstSelected == null)
                        firstSelected = j.label;
                }
                Draw(j);
            }

            // the text only changes with the selection / IK state: building it every frame was garbage
            int state = selectedCount * 4 + (chara == null ? 0 : chara.target.ikEnabled ? 1 : 2);
            if (_selectionText != null && (state != _shownState || !ReferenceEquals(firstSelected, _shownFirst)))
            {
                _shownState = state;
                _shownFirst = firstSelected;
                string s;
                if (chara == null)
                    s = "IK needs a character";
                else if (chara.target.ikEnabled == false)
                    s = "IK is off for this character";
                else if (selectedCount == 0)
                    s = "Click a joint";
                else if (selectedCount == 1)
                    s = firstSelected;
                else
                    s = firstSelected + " + " + (selectedCount - 1);
                if (_selectionText.text != s)
                    _selectionText.text = s;
            }
        }

        private void Draw(Joint j)
        {
            if (j.drawn && j.shownSelected == j.selected && j.shownEnabled == j.enabled && j.shownHover == j.hover)
                return;
            j.drawn = true;
            j.shownSelected = j.selected;
            j.shownEnabled = j.enabled;
            j.shownHover = j.hover;

            bool body = j.text != null;
            Color fill = j.selected ? Theme.Accent : (j.hover && j.enabled ? Theme.SurfaceHover : Theme.Surface);
            Color ring = j.selected ? Color.white : (j.hover && j.enabled ? Theme.Text : Theme.TextDim);
            float alpha = j.enabled ? 1f : 0.3f;
            Tween.Tint(j.fill, Kit.Alpha(fill, alpha), 0.12f, Ease.OutQuad);
            Tween.Tint(j.ring, Kit.Alpha(ring, alpha), 0.12f, Ease.OutQuad);
            Tween.Tint(j.glow, Kit.Alpha(Theme.Accent, j.selected ? 0.55f : 0f), 0.18f, Ease.OutQuad);
            if (j.text != null)
                j.text.color = Kit.Alpha(j.selected ? Color.white : Theme.Text, alpha);
            if (!body)
            {
                float size = j.selected ? SelectedSize : JointSize;
                if (j.hover && j.enabled && !j.selected)
                    size += 2f;
                Tween.Size(j.fill.rectTransform, new Vector2(size, size), 0.18f, Ease.OutBack);
                Tween.Size(j.ring.rectTransform, new Vector2(size, size), 0.18f, Ease.OutBack);
            }
        }
    }
}
