using System;
using System.Collections.Generic;
using HSPE.AMModules;
using HSPE.Kit.Small;
using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Advanced
{
    using HSUIKit;

    /// <summary>
    /// Dynamic Bones page: toolbar (Copy to FK, refresh, reset all), the list of the object's dynamic bones (changed
    /// dot), and a scrolling drawer for the selected one - enabled, the six physics values, gravity, force, freeze
    /// axis, step, go to root / tail, copy / paste settings, reset. All through Joan's DynamicBonesEditor.
    /// </summary>
    internal sealed class DynamicBonesPage : IAdvancedPage
    {

        private static readonly DynamicBonesEditor.DbField[] Floats =
        {
            DynamicBonesEditor.DbField.Weight, DynamicBonesEditor.DbField.Damping, DynamicBonesEditor.DbField.Elasticity,
            DynamicBonesEditor.DbField.Stiffness, DynamicBonesEditor.DbField.Inertia, DynamicBonesEditor.DbField.Radius
        };

        private RectTransform _root;
        private DynamicBonesEditor _editor;
        private ItemPicker _picker;
        private readonly List<DynamicBone> _items = new List<DynamicBone>();
        private float _nextListCheck;

        private CanvasGroup _drawer;
        private Text _noBone, _realName;
        private BoolRow _enabled;
        private FloatRow[] _floats;
        private VectorRow _gravity, _force;
        private ChoiceRow _freeze;
        private KitDropdown _step;
        private KitButton _paste;
        private DynamicBone _shown;

        public RectTransform Root { get { return _root; } }

        public void Build(RectTransform parent)
        {
            _root = Kit.Rect(parent, "DynamicBones");
            Kit.Fill(_root);

            RectTransform toolbar = PageUi.Toolbar(_root, 0f);
            _picker = ItemPicker.Create(toolbar, "The dynamic bone to edit (● = changed)");
            _picker.OnPicked = i => { if (_editor != null && i >= 0 && i < _items.Count) _editor.kitTarget = _items[i]; };
            PageUi.MoreButton(toolbar, "Copy to FK, refresh, reset all", () =>
            {
                if (_editor == null)
                    return null;
                return new KitMenu()
                    .Item("Copy to FK", () => _editor.KitCopyToFK())
                    .Item("Refresh the list", () => { _editor.KitRefreshList(); _nextListCheck = 0f; })
                    .Separator()
                    .Danger("Reset all dynamic bones...", ConfirmResetAll);
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
            _noBone = Kit.MakeText(drawerHost, "NoBone", "Select a dynamic bone in the list", Theme.FontSize, Theme.TextDim, TextAnchor.MiddleCenter);
            _noBone.fontStyle = FontStyle.Italic;
            Kit.Fill(_noBone.rectTransform);
        }

        private void BuildDrawer(RectTransform c)
        {
            RectTransform head = KitLayout.Row(c, 6f);
            Kit.Layout(head.gameObject, -1f, 22f);
            _realName = Kit.MakeText(head, "Real", "", Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleLeft);
            _realName.horizontalOverflow = HorizontalWrapMode.Wrap;
            _realName.verticalOverflow = VerticalWrapMode.Truncate;
            Kit.Layout(_realName.gameObject, -1f, 22f).flexibleWidth = 1f;
            _enabled = BoolRow.Create(c, "Enabled", on => { if (_shown != null) _editor.KitSetEnabled(_shown, on); }, () => ResetField(DynamicBonesEditor.DbField.Enabled), null);

            string[] labels = { "Weight", "Damping", "Elasticity", "Stiffness", "Inertia", "Radius" };
            _floats = new FloatRow[Floats.Length];
            for (int i = 0; i < Floats.Length; i++)
            {
                DynamicBonesEditor.DbField f = Floats[i];
                _floats[i] = FloatRow.Create(c, labels[i], 0f, 1f, v => { if (_shown != null) _editor.KitSetFloat(_shown, f, v); }, () => ResetField(f));
            }

            _gravity = VectorRow.Create(c, "Gravity", () => _shown != null ? _shown.m_Gravity : Vector3.zero,
                v => { if (_shown != null) _editor.KitSetGravity(_shown, v); }, () => ResetField(DynamicBonesEditor.DbField.Gravity));
            _force = VectorRow.Create(c, "Force", () => _shown != null ? _shown.m_Force : Vector3.zero,
                v => { if (_shown != null) _editor.KitSetForce(_shown, v); }, () => ResetField(DynamicBonesEditor.DbField.Force));

            _freeze = ChoiceRow.Create(c, "Freeze axis", Enum.GetNames(typeof(DynamicBone.FreezeAxis)), i => { if (_shown != null) _editor.KitSetFreezeAxis(_shown, (DynamicBone.FreezeAxis)i); },
                () => ResetField(DynamicBonesEditor.DbField.FreezeAxis));
            _step = StepChoice.Row(c, "Step / drag", -3, i => { if (_editor != null) { _editor.kitStepIndex = i; ApplyStep(); } });

            RectTransform actions = KitLayout.Row(c, 6f);
            Kit.Layout(actions.gameObject, -1f, 32f);
            KitButton root = KitButton.Create(actions, "Go to root", ButtonStyle.Ghost, () => { if (_shown != null) _editor.KitGoTo(_shown, false); });
            Kit.Layout(root.gameObject, 96f, 30f);
            KitButton tail = KitButton.Create(actions, "Go to tail", ButtonStyle.Ghost, () => { if (_shown != null) _editor.KitGoTo(_shown, true); });
            Kit.Layout(tail.gameObject, 90f, 30f);
            KitLayout.Flexible(actions);
            KitButton copy = KitButton.Create(actions, "Copy", ButtonStyle.Ghost, () => { if (_shown != null) _editor.CopySettings(_shown); });
            Kit.Layout(copy.gameObject, 70f, 30f);
            KitTooltip.Attach(copy.gameObject, "Copy all settings of this dynamic bone");
            _paste = KitButton.Create(actions, "Paste", ButtonStyle.Ghost, () => { if (_shown != null) _editor.PasteSettings(_shown); });
            Kit.Layout(_paste.gameObject, 70f, 30f);
            KitButton reset = KitButton.Create(actions, "Reset", ButtonStyle.Secondary, () => { if (_shown != null) _editor.KitReset(_shown); });
            Kit.Layout(reset.gameObject, 80f, 30f);
            KitTooltip.Attach(reset.gameObject, "Every setting of this dynamic bone back to the original");
        }

        // ---------------------------------------------------------------- page

        public void Show(PoseController target)
        {
            Bind(target);
        }

        public void Hide() { }

        private void Bind(PoseController target)
        {
            DynamicBonesEditor e = target != null ? target._dynamicBonesEditor : null;
            if (e == _editor)
                return;
            _editor = e;
            _nextListCheck = 0f;
            _shown = null;
            if (_editor != null)
            {
                // Joan's IMGUI step slider is what normally sets the drag strength; never drawn it would stay at 1
                _editor.kitStepIndex = _editor.kitStepIndex;
                _step.Selected = _editor.kitStepIndex + 5;
                ApplyStep();
            }
        }

        private void ApplyStep()
        {
            float step = _editor != null ? _editor.kitStep : 0.001f;
            _gravity.SetStep(step);
            _force.SetStep(step);
        }

        public void Tick(PoseController target)
        {
            Bind(target);
            if (_editor == null)
                return;

            if (Time.unscaledTime >= _nextListCheck)
            {
                _nextListCheck = Time.unscaledTime + 0.5f;
                RefreshPicker();
            }

            DynamicBone db2 = _editor.kitTarget;
            if (db2 != null && db2.m_Root == null)
                db2 = null;
            if (db2 != _shown)
            {
                _shown = db2;
                _drawer.alpha = db2 != null ? 1f : 0f;
                _drawer.interactable = _drawer.blocksRaycasts = db2 != null;
                _noBone.enabled = db2 == null;
                _realName.text = db2 != null ? db2.m_Root.name : "";
            }
            if (_shown == null)
                return;

            _enabled.Show(_shown.enabled, _editor.KitIsFieldDirty(_shown, DynamicBonesEditor.DbField.Enabled));
            for (int i = 0; i < Floats.Length; i++)
                _floats[i].Show(_editor.KitGetFloat(_shown, Floats[i]), _editor.KitIsFieldDirty(_shown, Floats[i]));
            _gravity.Show(_shown.m_Gravity, _editor.KitIsFieldDirty(_shown, DynamicBonesEditor.DbField.Gravity));
            _force.Show(_shown.m_Force, _editor.KitIsFieldDirty(_shown, DynamicBonesEditor.DbField.Force));
            _freeze.Show((int)_shown.m_FreezeAxis, _editor.KitIsFieldDirty(_shown, DynamicBonesEditor.DbField.FreezeAxis));
            _paste.Interactable = _editor.kitCanPaste;
        }

        private void ResetField(DynamicBonesEditor.DbField f)
        {
            if (_editor != null && _shown != null)
                _editor.ResetField(_shown, f);
        }

        private void ConfirmResetAll()
        {
            if (_editor == null)
                return;
            KitModal.Show("Reset all dynamic bones?", "Every changed dynamic bone of this object goes back to its original settings.", new[] { "Cancel", "Reset all" }, r =>
            {
                if (r == 1 && _editor != null)
                    _editor.KitResetAll();
            }, true);
        }

        // ---------------------------------------------------------------- picker

        private void RefreshPicker()
        {
            _editor.KitBones(_items);
            int fp = _items.Count * 7919 + _items.IndexOf(_editor.kitTarget);
            foreach (DynamicBone db in _items)
                fp = ((fp * 31 + db.GetInstanceID()) * 2 + (_editor.KitIsDirty(db) ? 1 : 0)) * 2 + (db.enabled ? 1 : 0);
            if (_picker.Changed(fp) == false)
                return;
            List<string> labels = new List<string>(_items.Count);
            List<bool> dirty = new List<bool>(_items.Count);
            foreach (DynamicBone db in _items)
            {
                labels.Add(BoneNames.Display(db.m_Root) + (db.enabled ? "" : "  (off)"));
                dirty.Add(_editor.KitIsDirty(db));
            }
            _picker.Set(labels, dirty, _items.IndexOf(_editor.kitTarget), "This object has no dynamic bones");
        }
    }
}
