using HSPE.AMModules;
using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Advanced
{
    using HSUIKit;

    /// <summary>
    /// Boobs page: the right and left breast side by side (character facing you: right on the left), each with
    /// enabled, gravity, force, go to root / tail and reset; below, Joan's alternative update mode, the step / drag
    /// strength and Copy to FK. All through Joan's BoobsEditor.
    /// </summary>
    internal sealed class BoobsPage : IAdvancedPage
    {
        private sealed class Side
        {
            public bool right;
            public Text title;
            public Image dot;
            public BoolRow enabled;
            public VectorRow gravity, force;
        }

        private RectTransform _root;
        private BoobsEditor _editor;
        private Side _right, _left;
        private BoolRow _alternative;
        private KitDropdown _step;

        public RectTransform Root { get { return _root; } }

        public void Build(RectTransform parent)
        {
            _root = Kit.Rect(parent, "Boobs");
            Kit.Fill(_root);

            KitScroll scroll = KitScroll.Create(_root, 2, 2, 10f, true);
            RectTransform c = scroll.Content;

            // right | left side by side when there's room, stacked otherwise (a narrow window cut Left off)
            ResponsiveColumns columns = ResponsiveColumns.Create(c, 0f);
            _right = BuildSide(columns.Columns[0], true);
            _left = BuildSide(columns.Columns[1], false);

            _alternative = BoolRow.Create(c, "Alternative update mode", on => { if (_editor != null) _editor.kitAlternativeUpdateMode = on; }, null,
                "Joan's option: with neither IK nor FK on, the physics updates in a different order (can fix jitter)");
            _step = StepChoice.Row(c, "Step / drag", -3, i =>
            {
                if (_editor == null)
                    return;
                _editor.kitStepIndex = i;
                ApplyStep();
            });
            RectTransform bottom = KitLayout.Row(c, 8f);
            Kit.Layout(bottom.gameObject, -1f, 34f);
            KitLayout.Flexible(bottom);
            KitButton toFk = KitButton.Create(bottom, "Copy to FK", ButtonStyle.Ghost, () => { if (_editor != null) _editor.KitCopyToFK(); });
            Kit.Layout(toFk.gameObject, 100f, 30f);
            KitTooltip.Attach(toFk.gameObject, "Write the current breast shape into Studio's FK bones (undoable)");
        }

        private Side BuildSide(Transform parent, bool right)
        {
            Side s = new Side { right = right };
            RectTransform col = Kit.Rect(parent, right ? "Right" : "Left");
            Image bg = col.gameObject.AddComponent<Image>();
            Kit.SetSprite(bg, Sprites.Rounded(Theme.RadiusSmall));
            KitThemeColor.Bind(bg, () => Kit.Alpha(Theme.Surface, 0.55f));
            bg.raycastTarget = false;
            VerticalLayoutGroup v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(10, 10, 8, 10);
            v.spacing = 8f;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            col.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            RectTransform head = KitLayout.Row(col, 6f);
            Kit.Layout(head.gameObject, -1f, 30f);
            s.dot = Kit.Image(head, "Changed", Sprites.Circle, Theme.Warning);
            Kit.Layout(s.dot.gameObject, 6f, 6f);
            s.title = Kit.MakeText(head, "Title", right ? "Right" : "Left", Theme.FontSize + 1, Theme.Text, TextAnchor.MiddleLeft);
            s.title.fontStyle = FontStyle.Bold;
            Kit.Layout(s.title.gameObject, -1f, 22f).flexibleWidth = 1f;
            s.enabled = BoolRow.Create(col, "Enabled", on => { DynamicBone_Ver02 b = Bone(s); if (b != null) _editor.KitSetEnabled(b, on); }, null, null);

            s.gravity = VectorRow.Create(col, "Gravity", () => Bone(s) != null ? _editor.KitGetGravity(Bone(s)) : Vector3.zero,
                g => { DynamicBone_Ver02 b = Bone(s); if (b != null) _editor.KitSetGravity(b, g); }, () => ResetPart(s, true), 70f);
            s.force = VectorRow.Create(col, "Force", () => Bone(s) != null ? _editor.KitGetForce(Bone(s)) : Vector3.zero,
                f => { DynamicBone_Ver02 b = Bone(s); if (b != null) _editor.KitSetForce(b, f); }, () => ResetPart(s, false), 70f);

            RectTransform actions = KitLayout.Row(col, 6f);
            Kit.Layout(actions.gameObject, -1f, 32f);
            KitButton root = KitButton.Create(actions, "Root", ButtonStyle.Ghost, () => { DynamicBone_Ver02 b = Bone(s); if (b != null) _editor.KitGoTo(b, false); });
            Kit.Layout(root.gameObject, 64f, 30f);
            KitTooltip.Attach(root.gameObject, "Go to the root bone in the Bones page");
            KitButton tail = KitButton.Create(actions, "Tail", ButtonStyle.Ghost, () => { DynamicBone_Ver02 b = Bone(s); if (b != null) _editor.KitGoTo(b, true); });
            Kit.Layout(tail.gameObject, 64f, 30f);
            KitTooltip.Attach(tail.gameObject, "Go to the last bone in the Bones page");
            KitLayout.Flexible(actions);
            KitButton reset = KitButton.Create(actions, "Reset", ButtonStyle.Secondary, () => { DynamicBone_Ver02 b = Bone(s); if (b != null) _editor.KitReset(b); });
            Kit.Layout(reset.gameObject, 80f, 30f);
            return s;
        }

        private DynamicBone_Ver02 Bone(Side s)
        {
            if (_editor == null)
                return null;
            return s.right ? _editor.kitRight : _editor.kitLeft;
        }

        /// <summary>Joan's Boobs editor only resets a whole side; keep that, but keep what the other part holds.</summary>
        private void ResetPart(Side s, bool gravity)
        {
            DynamicBone_Ver02 b = Bone(s);
            if (b == null)
                return;
            bool e, g, f;
            _editor.KitDirtyParts(b, out e, out g, out f);
            bool keepEnabled = e;
            bool enabledValue = _editor.KitGetEnabled(b);
            Vector3 keep = gravity ? _editor.KitGetForce(b) : _editor.KitGetGravity(b);
            bool keepOther = gravity ? f : g;
            _editor.KitReset(b);
            if (keepEnabled)
                _editor.KitSetEnabled(b, enabledValue);
            if (keepOther)
            {
                if (gravity)
                    _editor.KitSetForce(b, keep);
                else
                    _editor.KitSetGravity(b, keep);
            }
        }

        // ---------------------------------------------------------------- page

        public void Show(PoseController target)
        {
            Bind(target);
        }

        public void Hide() { }

        private void Bind(PoseController target)
        {
            CharaPoseController chara = target as CharaPoseController;
            BoobsEditor e = chara != null ? chara._boobsEditor : null;
            if (e == _editor)
                return;
            _editor = e;
            if (_editor != null)
            {
                _editor.kitStepIndex = _editor.kitStepIndex; // the drag strength only Joan's IMGUI slider set
                _step.Selected = _editor.kitStepIndex + 5;
                _alternative.Show(_editor.kitAlternativeUpdateMode, false);
                ApplyStep();
            }
        }

        private void ApplyStep()
        {
            float step = _editor != null ? _editor.kitStep : 0.001f;
            _right.gravity.SetStep(step);
            _right.force.SetStep(step);
            _left.gravity.SetStep(step);
            _left.force.SetStep(step);
        }

        public void Tick(PoseController target)
        {
            Bind(target);
            if (_editor == null)
                return;
            Refresh(_right);
            Refresh(_left);
        }

        private void Refresh(Side s)
        {
            DynamicBone_Ver02 b = Bone(s);
            if (b == null)
                return;
            bool e, g, f;
            _editor.KitDirtyParts(b, out e, out g, out f);
            s.dot.enabled = e || g || f;
            s.enabled.Show(_editor.KitGetEnabled(b), e);
            s.gravity.Show(_editor.KitGetGravity(b), g);
            s.force.Show(_editor.KitGetForce(b), f);
        }
    }
}
