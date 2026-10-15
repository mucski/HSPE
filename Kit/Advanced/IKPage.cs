using System.Collections.Generic;
using HSPE.AMModules;
using HSPE.Kit.Small;
using RootMotion.FinalIK;
using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Advanced
{
    using HSUIKit;

    /// <summary>
    /// IK page: toolbar (Copy to FK, refresh, reset all), the object's IK solvers, and a drawer with the selected
    /// solver's fields - the common ones (enabled for items, fix transforms, position weight) plus its type's own:
    /// FABRIK / CCD / Aim (tolerance, iterations, Aim axis / clamp / pole), FABRIK Root, Limb, and for a character's
    /// full-body IK every effector's position / rotation weight and the four bend weights, right | left.
    /// </summary>
    internal sealed class IKPage : IAdvancedPage
    {

        private delegate float FloatGetter();
        private delegate bool BoolGetter();

        private sealed class Binding
        {
            public FloatRow row;
            public VectorRow vector;
            public FloatGetter get;
            public Vector3Getter getVector;
            public BoolGetter dirty;
        }

        private sealed class BoolBinding
        {
            public BoolRow row;
            public IKEditor.IKField field;
        }

        private RectTransform _root;
        private IKEditor _editor;
        private ItemPicker _picker;
        private readonly List<IK> _items = new List<IK>();
        private float _nextCheck;

        private CanvasGroup _drawer;
        private Text _none, _type, _help;
        private BoolBinding _enabled, _fix;
        private readonly List<Binding> _common = new List<Binding>();
        private readonly Dictionary<IKEditor.KitIKType, RectTransform> _sections = new Dictionary<IKEditor.KitIKType, RectTransform>();
        private readonly Dictionary<IKEditor.KitIKType, List<Binding>> _bindings = new Dictionary<IKEditor.KitIKType, List<Binding>>();
        private RectTransform _polePositionRow;
        private KitDropdown _step;
        private readonly List<VectorRow> _vectors = new List<VectorRow>();
        private IK _shown;
        private IKEditor.KitIKType _shownType;
        private List<Binding> _shownBindings; // resolved on change: an enum-keyed Dictionary lookup boxes every frame

        public RectTransform Root { get { return _root; } }

        public void Build(RectTransform parent)
        {
            _root = Kit.Rect(parent, "IK");
            Kit.Fill(_root);

            RectTransform toolbar = PageUi.Toolbar(_root, 0f);
            _picker = ItemPicker.Create(toolbar, "The IK solver to edit (● = changed)");
            _picker.OnPicked = i => { if (_editor != null && i >= 0 && i < _items.Count) _editor.kitTarget = _items[i]; };
            PageUi.MoreButton(toolbar, "Copy to FK, refresh, reset all", () =>
            {
                if (_editor == null)
                    return null;
                return new KitMenu()
                    .Item("Copy to FK", () => _editor.KitCopyToFK())
                    .Item("Refresh the list", () => { _editor.KitRefresh(); _nextCheck = 0f; })
                    .Separator()
                    .Danger("Reset all IK settings...", ConfirmResetAll);
            });

            RectTransform drawerHost = Kit.Rect(_root, "Drawer");
            Kit.Fill(drawerHost, 0f, 0f, 0f, 42f);
            Image dbg = drawerHost.gameObject.AddComponent<Image>();
            Kit.SetSprite(dbg, Sprites.Rounded(Theme.RadiusSmall));
            KitThemeColor.Bind(dbg, () => Kit.Alpha(Theme.Surface, 0.55f));
            dbg.raycastTarget = false;
            KitScroll ds = KitScroll.Create(drawerHost, 10, 8, 6f, true);
            _drawer = ds.Content.gameObject.AddComponent<CanvasGroup>();
            _drawer.alpha = 0f; // start empty: shown and target are both null on the first frame
            _drawer.interactable = _drawer.blocksRaycasts = false;
            BuildDrawer(ds.Content);
            _none = Kit.MakeText(drawerHost, "None", "Select an IK solver in the list", Theme.FontSize, Theme.TextDim, TextAnchor.MiddleCenter);
            _none.fontStyle = FontStyle.Italic;
            Kit.Fill(_none.rectTransform);
        }

        // ---------------------------------------------------------------- drawer

        private void BuildDrawer(RectTransform c)
        {
            RectTransform head = KitLayout.Row(c, 6f);
            Kit.Layout(head.gameObject, -1f, 28f);
            _type = Kit.MakeText(head, "Type", "", Theme.FontSize, Theme.TextDim, TextAnchor.MiddleLeft);
            Kit.Layout(_type.gameObject, -1f, 22f).flexibleWidth = 1f;
            KitButton reset = KitButton.Create(head, "Reset", ButtonStyle.Secondary, () => { if (_shown != null) _editor.KitReset(_shown); });
            Kit.Layout(reset.gameObject, 80f, 28f);
            KitTooltip.Attach(reset.gameObject, "Every setting of this solver back to the original");

            _help = Kit.MakeText(c, "Help", "", Theme.SmallSize + 1, Theme.TextDim, TextAnchor.UpperLeft);
            _help.horizontalOverflow = HorizontalWrapMode.Wrap;
            _help.gameObject.AddComponent<LayoutElement>();
            ContentSizeFitter fit = _help.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _enabled = Bool(c, "Enabled", IKEditor.IKField.Enabled, "Joan shows this for items only; a character's IK is switched by Studio");
            _fix = Bool(c, "Fix transforms", IKEditor.IKField.FixTransforms, "Reset the bones before solving each frame");
            _common.Add(Float(c, "Position weight", IKEditor.IKField.PositionWeight, 0f, 1f, false));

            // FABRIK / CCD / Aim share tolerance + iterations
            foreach (IKEditor.KitIKType t in new[] { IKEditor.KitIKType.FABRIK, IKEditor.KitIKType.CCDIK, IKEditor.KitIKType.AimIK })
            {
                RectTransform s = Section(c, t);
                Add(t, Float(s, "Tolerance", IKEditor.IKField.Tolerance, 0f, 1f, false));
                Add(t, Float(s, "Max iterations", IKEditor.IKField.MaxIterations, 0f, 100f, true));
            }
            RectTransform aim = _sections[IKEditor.KitIKType.AimIK];
            Add(IKEditor.KitIKType.AimIK, Vector(aim, "Axis", IKEditor.IKField.Axis));
            Add(IKEditor.KitIKType.AimIK, Float(aim, "Clamp weight", IKEditor.IKField.ClampWeight, 0f, 1f, false));
            Add(IKEditor.KitIKType.AimIK, Float(aim, "Clamp smoothing", IKEditor.IKField.ClampSmoothing, 0f, 2f, true));
            Add(IKEditor.KitIKType.AimIK, Vector(aim, "Pole axis", IKEditor.IKField.PoleAxis));
            Binding pole = Vector(aim, "Pole position", IKEditor.IKField.PolePosition);
            _polePositionRow = pole.vector.Root;
            Add(IKEditor.KitIKType.AimIK, pole);
            Add(IKEditor.KitIKType.AimIK, Float(aim, "Pole weight", IKEditor.IKField.PoleWeight, 0f, 1f, false));
            _step = StepChoice.Row(aim, "Vector step", -1, i => { if (_editor != null) { _editor.kitStepIndex = i; ApplyStep(); } });

            RectTransform root = Section(c, IKEditor.KitIKType.FABRIKRoot);
            Add(IKEditor.KitIKType.FABRIKRoot, Float(root, "Root pin", IKEditor.IKField.RootPin, 0f, 1f, false));
            Add(IKEditor.KitIKType.FABRIKRoot, Float(root, "Iterations", IKEditor.IKField.Iterations, 0f, 100f, true));

            RectTransform limb = Section(c, IKEditor.KitIKType.LimbIK);
            Add(IKEditor.KitIKType.LimbIK, Float(limb, "Rotation weight", IKEditor.IKField.LimbRotationWeight, 0f, 1f, false));
            Add(IKEditor.KitIKType.LimbIK, Float(limb, "Bend modifier weight", IKEditor.IKField.LimbBendModifierWeight, 0f, 1f, false));

            BuildFullBody(Section(c, IKEditor.KitIKType.FullBodyBipedIK));
        }

        /// <summary>Right | left columns like Joan's layout (character facing you: right on the left).</summary>
        private void BuildFullBody(RectTransform s)
        {
            IKEditor.KitIKType t = IKEditor.KitIKType.FullBodyBipedIK;
            PairHeader(s, "Shoulders");
            RectTransform[] cols = Columns(s);
            Add(t, Effector(cols[0], "Right position", IKEditor.FbbEffector.RightShoulder, false));
            Add(t, Effector(cols[1], "Left position", IKEditor.FbbEffector.LeftShoulder, false));
            PairHeader(s, "Arms (bend)");
            cols = Columns(s);
            Add(t, BendRow(cols[0], "Right bend", IKEditor.FbbBend.RightArm));
            Add(t, BendRow(cols[1], "Left bend", IKEditor.FbbBend.LeftArm));
            PairHeader(s, "Hands");
            cols = Columns(s);
            Add(t, Effector(cols[0], "Right position", IKEditor.FbbEffector.RightHand, false));
            Add(t, Effector(cols[0], "Right rotation", IKEditor.FbbEffector.RightHand, true));
            Add(t, Effector(cols[1], "Left position", IKEditor.FbbEffector.LeftHand, false));
            Add(t, Effector(cols[1], "Left rotation", IKEditor.FbbEffector.LeftHand, true));
            PairHeader(s, "Body");
            Add(t, Effector(s, "Body position", IKEditor.FbbEffector.Body, false));
            PairHeader(s, "Thighs");
            cols = Columns(s);
            Add(t, Effector(cols[0], "Right position", IKEditor.FbbEffector.RightThigh, false));
            Add(t, Effector(cols[1], "Left position", IKEditor.FbbEffector.LeftThigh, false));
            PairHeader(s, "Legs (bend)");
            cols = Columns(s);
            Add(t, BendRow(cols[0], "Right bend", IKEditor.FbbBend.RightLeg));
            Add(t, BendRow(cols[1], "Left bend", IKEditor.FbbBend.LeftLeg));
            PairHeader(s, "Feet");
            cols = Columns(s);
            Add(t, Effector(cols[0], "Right position", IKEditor.FbbEffector.RightFoot, false));
            Add(t, Effector(cols[0], "Right rotation", IKEditor.FbbEffector.RightFoot, true));
            Add(t, Effector(cols[1], "Left position", IKEditor.FbbEffector.LeftFoot, false));
            Add(t, Effector(cols[1], "Left rotation", IKEditor.FbbEffector.LeftFoot, true));
        }

        private static void PairHeader(Transform parent, string text)
        {
            Text t = Kit.MakeText(parent, "Header", text.ToUpperInvariant(), Theme.SmallSize, Theme.TextDim, TextAnchor.LowerLeft);
            t.fontStyle = FontStyle.Bold;
            Kit.Layout(t.gameObject, -1f, 22f);
        }

        /// <summary>Right | left, side by side when wide enough, stacked otherwise.</summary>
        private static RectTransform[] Columns(Transform parent)
        {
            return ResponsiveColumns.Create(parent, 0f).Columns;
        }

        private RectTransform Section(Transform parent, IKEditor.KitIKType t)
        {
            RectTransform s = Kit.Rect(parent, "Section " + t);
            VerticalLayoutGroup v = s.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6f;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            _sections[t] = s;
            _bindings[t] = new List<Binding>();
            s.gameObject.SetActive(false);
            return s;
        }

        private void Add(IKEditor.KitIKType t, Binding b)
        {
            _bindings[t].Add(b);
        }

        private Binding Float(Transform parent, string label, IKEditor.IKField f, float min, float max, bool whole)
        {
            Binding b = new Binding();
            b.row = FloatRow.Create(parent, label, min, max, v => { if (_shown != null) _editor.KitSetFloat(_shown, f, v); }, () => { if (_shown != null) _editor.KitResetField(_shown, f); }, whole);
            b.get = () => _editor.KitGetFloat(_shown, f);
            b.dirty = () => _editor.KitIsFieldDirty(_shown, f);
            return b;
        }

        private Binding Vector(Transform parent, string label, IKEditor.IKField f)
        {
            Binding b = new Binding();
            b.vector = VectorRow.Create(parent, label, () => _shown != null ? _editor.KitGetVector(_shown, f) : Vector3.zero,
                v => { if (_shown != null) _editor.KitSetVector(_shown, f, v); }, () => { if (_shown != null) _editor.KitResetField(_shown, f); });
            b.getVector = () => _editor.KitGetVector(_shown, f);
            b.dirty = () => _editor.KitIsFieldDirty(_shown, f);
            _vectors.Add(b.vector);
            return b;
        }

        private Binding Effector(Transform parent, string label, IKEditor.FbbEffector e, bool rotation)
        {
            Binding b = new Binding();
            b.row = FloatRow.Create(parent, label, 0f, 1f, v => { if (_shown != null) _editor.KitSetEffector(_shown, e, rotation, v); }, () => { if (_shown != null) _editor.KitResetEffector(_shown, e, rotation); }, false,
                e == IKEditor.FbbEffector.Body ? FieldGrid.LabelW : FieldGrid.ColumnLabelW);
            b.get = () => _editor.KitGetEffector(_shown, e, rotation);
            b.dirty = () => _editor.KitEffectorDirty(_shown, e, rotation);
            return b;
        }

        private Binding BendRow(Transform parent, string label, IKEditor.FbbBend bend)
        {
            Binding b = new Binding();
            b.row = FloatRow.Create(parent, label, 0f, 1f, v => { if (_shown != null) _editor.KitSetBend(_shown, bend, v); }, () => { if (_shown != null) _editor.KitResetBend(_shown, bend); }, false, FieldGrid.ColumnLabelW);
            b.get = () => _editor.KitGetBend(_shown, bend);
            b.dirty = () => _editor.KitBendDirty(_shown, bend);
            return b;
        }

        private BoolBinding Bool(Transform parent, string label, IKEditor.IKField f, string tip)
        {
            BoolBinding b = new BoolBinding { field = f };
            b.row = BoolRow.Create(parent, label, on => { if (_shown != null) _editor.KitSetBool(_shown, f, on); },
                () => { if (_shown != null) _editor.KitResetField(_shown, f); }, tip);
            return b;
        }

        private void ApplyStep()
        {
            float step = _editor != null ? _editor.kitStep : 0.1f;
            foreach (VectorRow v in _vectors)
                v.SetStep(step);
        }

        // ---------------------------------------------------------------- page

        public void Show(PoseController target)
        {
            Bind(target);
        }

        public void Hide() { }

        private void Bind(PoseController target)
        {
            IKEditor e = target != null ? target._ikEditor : null;
            if (e == _editor)
                return;
            _editor = e;
            _nextCheck = 0f;
            _shown = null;
            if (_editor != null)
            {
                _editor.kitStepIndex = _editor.kitStepIndex; // only Joan's IMGUI slider set _inc
                _step.Selected = _editor.kitStepIndex + 5;
                ApplyStep();
            }
        }

        public void Tick(PoseController target)
        {
            Bind(target);
            if (_editor == null)
                return;
            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 0.3f;
                RefreshPicker();
            }

            IK cur = _editor.kitTarget;
            if (cur != _shown)
            {
                _shown = cur;
                _shownType = cur != null ? _editor.KitType(cur) : IKEditor.KitIKType.Unknown;
                _drawer.alpha = cur != null ? 1f : 0f;
                _drawer.interactable = _drawer.blocksRaycasts = cur != null;
                _none.enabled = cur == null;
                _type.text = cur != null ? TypeName(_shownType) : "";
                _help.text = Help(_shownType);
                if (_bindings.TryGetValue(_shownType, out _shownBindings) == false)
                    _shownBindings = null;
                foreach (KeyValuePair<IKEditor.KitIKType, RectTransform> p in _sections)
                    p.Value.gameObject.SetActive(p.Key == _shownType);
                _enabled.row.Root.gameObject.SetActive(!_editor.kitIsCharacter);
            }
            if (_shown == null)
                return;

            RefreshBool(_enabled);
            RefreshBool(_fix);
            foreach (Binding b in _common)
                Refresh(b);
            if (_shownBindings != null)
                for (int i = 0; i < _shownBindings.Count; i++)
                    Refresh(_shownBindings[i]);
            if (_shownType == IKEditor.KitIKType.AimIK)
                _polePositionRow.gameObject.SetActive(!_editor.KitHasPoleTarget(_shown));
        }

        private void Refresh(Binding b)
        {
            if (b.row != null)
                b.row.Show(b.get(), b.dirty());
            else if (b.vector != null && b.vector.Root.gameObject.activeInHierarchy)
                b.vector.Show(b.getVector(), b.dirty());
        }

        private void RefreshBool(BoolBinding b)
        {
            b.row.Show(_editor.KitGetBool(_shown, b.field), _editor.KitIsFieldDirty(_shown, b.field));
        }

        /// <summary>One line on what the selected solver's settings do.</summary>
        private static string Help(IKEditor.KitIKType t)
        {
            switch (t)
            {
                case IKEditor.KitIKType.FullBodyBipedIK:
                    return "How strongly each body part follows its IK handle: 1 = fully, 0 = not at all (it keeps its animation / FK pose). " +
                           "Bend = how much elbows / knees point at their bend handle. Lower a hand's weight to let the arm hang naturally, for example.";
                case IKEditor.KitIKType.AimIK:
                    return "Turns a chain of bones so its axis points at the target (e.g. a head or a gun). Weights blend it with the original pose.";
                case IKEditor.KitIKType.LimbIK:
                    return "Solves a three-bone limb towards its target; the weights blend it with the original pose.";
                default:
                    return "Solves a chain of bones towards its target. Position weight blends it with the original pose; tolerance / iterations trade accuracy for speed.";
            }
        }

        private static string TypeName(IKEditor.KitIKType t)
        {
            switch (t)
            {
                case IKEditor.KitIKType.FullBodyBipedIK: return "Full body IK";
                case IKEditor.KitIKType.FABRIKRoot: return "FABRIK root";
                case IKEditor.KitIKType.AimIK: return "Aim IK";
                case IKEditor.KitIKType.CCDIK: return "CCD IK";
                case IKEditor.KitIKType.LimbIK: return "Limb IK";
                default: return t.ToString();
            }
        }

        private void ConfirmResetAll()
        {
            if (_editor == null)
                return;
            KitModal.Show("Reset all IK settings?", "Every changed IK solver of this object goes back to its original settings.", new[] { "Cancel", "Reset all" }, r =>
            {
                if (r == 1 && _editor != null)
                    _editor.KitResetAll();
            }, true);
        }

        // ---------------------------------------------------------------- picker

        private void RefreshPicker()
        {
            _editor.KitIKs(_items);
            int fp = _items.Count * 7919 + _items.IndexOf(_editor.kitTarget);
            foreach (IK ik in _items)
                fp = (fp * 31 + ik.GetInstanceID()) * 2 + (_editor.KitIsDirty(ik) ? 1 : 0);
            if (_picker.Changed(fp) == false)
                return;
            List<string> labels = new List<string>(_items.Count);
            List<bool> dirty = new List<bool>(_items.Count);
            foreach (IK ik in _items)
            {
                Transform root = _editor.KitRoot(ik);
                labels.Add(root != null ? BoneNames.Display(root) : ik.name);
                dirty.Add(_editor.KitIsDirty(ik));
            }
            _picker.Set(labels, dirty, _items.IndexOf(_editor.kitTarget), "This object has no IK solvers");
        }
    }
}
