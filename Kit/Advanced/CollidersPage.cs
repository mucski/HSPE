using System.Collections.Generic;
using HSPE.AMModules;
using HSPE.Kit.Small;
using UnityEngine;
// HS1 has no collider base class; Joan aliases it the same way
using DynamicBoneColliderBase = DynamicBoneCollider;
using UnityEngine.UI;

namespace HSPE.Kit.Advanced
{
    using HSUIKit;

    /// <summary>
    /// Colliders page: the object's dynamic-bone colliders (list) and a drawer for the selected one - center, direction,
    /// bound, radius, height (each with reset), step, go to bone, reset. A Studio collider item ("lone collider") also
    /// lists every dynamic bone in the scene it can push, with a checkbox each (Joan's "Affected Dynamic Bones").
    /// </summary>
    internal sealed class CollidersPage : IAdvancedPage
    {

        private sealed class Link
        {
            public KitAffectedRef owner;
            public object bone;
            public KitCheckbox check;
        }

        private sealed class KitAffectedRef
        {
            public PoseController owner;
        }

        private RectTransform _root, _affected;
        private CollidersEditor _editor;
        private ItemPicker _picker;
        private readonly List<DynamicBoneColliderBase> _items = new List<DynamicBoneColliderBase>();
        private readonly List<Link> _links = new List<Link>();
        private int _affectedSignature;
        private float _nextCheck;

        private CanvasGroup _drawer;
        private Text _noCollider, _realName;
        private VectorRow _center;
        private ChoiceRow _direction, _bound;
        private FloatRow _radius, _height;
        private KitDropdown _step;
        private RectTransform _affectedSection;
        private DynamicBoneColliderBase _shown;

        public RectTransform Root { get { return _root; } }

        public void Build(RectTransform parent)
        {
            _root = Kit.Rect(parent, "Colliders");
            Kit.Fill(_root);

            RectTransform toolbar = PageUi.Toolbar(_root, 0f);
            _picker = ItemPicker.Create(toolbar, "The collider to edit (● = changed)");
            _picker.OnPicked = i => { if (_editor != null && i >= 0 && i < _items.Count) _editor.kitTarget = _items[i]; };
            PageUi.MoreButton(toolbar, "Reset all", () => _editor == null ? null : new KitMenu().Danger("Reset all colliders...", ConfirmResetAll));

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
            _noCollider = Kit.MakeText(drawerHost, "None", "Select a collider in the list", Theme.FontSize, Theme.TextDim, TextAnchor.MiddleCenter);
            _noCollider.fontStyle = FontStyle.Italic;
            Kit.Fill(_noCollider.rectTransform);
        }

        private void BuildDrawer(RectTransform c)
        {
            RectTransform head = KitLayout.Row(c, 6f);
            Kit.Layout(head.gameObject, -1f, 22f);
            _realName = Kit.MakeText(head, "Real", "", Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleLeft);
            _realName.horizontalOverflow = HorizontalWrapMode.Wrap;
            _realName.verticalOverflow = VerticalWrapMode.Truncate;
            Kit.Layout(_realName.gameObject, -1f, 22f).flexibleWidth = 1f;

            _center = VectorRow.Create(c, "Center", () => _shown != null ? _shown.m_Center : Vector3.zero,
                v => { if (_shown != null) _editor.KitSetCenter(_shown, v); }, () => ResetField(CollidersEditor.ColliderField.Center));

            _direction = ChoiceRow.Create(c, "Direction", CollidersEditor.kitDirectionNames, i => { if (_shown != null) _editor.KitSetDirection(_shown, i); },
                () => ResetField(CollidersEditor.ColliderField.Direction));
            _bound = ChoiceRow.Create(c, "Bound", CollidersEditor.kitBoundNames, i => { if (_shown != null) _editor.KitSetBound(_shown, i); },
                () => ResetField(CollidersEditor.ColliderField.Bound));

            _radius = FloatRow.Create(c, "Radius", 0f, 1f, v => { DynamicBoneCollider n = _shown as DynamicBoneCollider; if (n != null) _editor.KitSetRadius(n, v); },
                () => ResetField(CollidersEditor.ColliderField.Radius));
            _height = FloatRow.Create(c, "Height", 0f, 4f, v => { DynamicBoneCollider n = _shown as DynamicBoneCollider; if (n != null) _editor.KitSetHeight(n, v); },
                () => ResetField(CollidersEditor.ColliderField.Height));

            _step = StepChoice.Row(c, "Center step", -2, i => { if (_editor != null) { _editor.kitStepIndex = i; _center.SetStep(_editor.kitStep); } });

            RectTransform actions = KitLayout.Row(c, 6f);
            Kit.Layout(actions.gameObject, -1f, 32f);
            KitButton bone = KitButton.Create(actions, "Go to bone", ButtonStyle.Ghost, () => { if (_shown != null) _editor.KitGoToBone(_shown); });
            Kit.Layout(bone.gameObject, 100f, 30f);
            KitLayout.Flexible(actions);
            KitButton reset = KitButton.Create(actions, "Reset", ButtonStyle.Secondary, () => { if (_shown != null) _editor.KitReset(_shown); });
            Kit.Layout(reset.gameObject, 80f, 30f);
            KitTooltip.Attach(reset.gameObject, "This collider back to its original settings (and pushing every dynamic bone again)");

            // lone collider only: which dynamic bones it pushes
            _affectedSection = Kit.Rect(c, "Affected");
            VerticalLayoutGroup av = _affectedSection.gameObject.AddComponent<VerticalLayoutGroup>();
            av.spacing = 4f;
            av.childForceExpandWidth = true;
            av.childForceExpandHeight = false;
            RectTransform ah = KitLayout.Row(_affectedSection, 6f);
            Kit.Layout(ah.gameObject, -1f, 30f);
            Text at = Kit.MakeText(ah, "Label", "Pushes these dynamic bones", Theme.FontSize, Theme.Text, TextAnchor.MiddleLeft);
            at.fontStyle = FontStyle.Bold;
            Kit.Layout(at.gameObject, -1f, 22f).flexibleWidth = 1f;
            KitButton none = KitButton.Create(ah, "None", ButtonStyle.Ghost, () => { if (_shown != null) { _editor.KitSetAffectsAll(_shown, false); } });
            Kit.Layout(none.gameObject, 70f, 28f);
            KitButton all = KitButton.Create(ah, "All", ButtonStyle.Ghost, () => { if (_shown != null) { _editor.KitSetAffectsAll(_shown, true); } });
            Kit.Layout(all.gameObject, 60f, 28f);
            _affected = Kit.Rect(_affectedSection, "List");
            VerticalLayoutGroup lv = _affected.gameObject.AddComponent<VerticalLayoutGroup>();
            lv.spacing = 2f;
            lv.childForceExpandWidth = true;
            lv.childForceExpandHeight = false;
            _affectedSection.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- page

        public void Show(PoseController target)
        {
            Bind(target);
        }

        public void Hide() { }

        private void Bind(PoseController target)
        {
            CollidersEditor e = target != null ? target._collidersEditor : null;
            if (e == _editor)
                return;
            _editor = e;
            _nextCheck = 0f;
            _affectedSignature = -1;
            _shown = null;
            if (_editor != null)
            {
                _editor.kitStepIndex = _editor.kitStepIndex; // only Joan's IMGUI slider set _inc
                _step.Selected = _editor.kitStepIndex + 5;
                _center.SetStep(_editor.kitStep);
            }
        }

        public void Tick(PoseController target)
        {
            Bind(target);
            if (_editor == null)
                return;

            bool check = Time.unscaledTime >= _nextCheck;
            if (check)
            {
                _nextCheck = Time.unscaledTime + 0.5f;
                RefreshPicker();
            }

            DynamicBoneColliderBase cur = _editor.kitTarget;
            if (cur != _shown)
            {
                _shown = cur;
                _affectedSignature = -1;
                _drawer.alpha = cur != null ? 1f : 0f;
                _drawer.interactable = _drawer.blocksRaycasts = cur != null;
                _noCollider.enabled = cur == null;
                _realName.text = cur != null ? cur.name : "";
                bool normal = cur is DynamicBoneCollider;
                _radius.Root.gameObject.SetActive(normal);
                _height.Root.gameObject.SetActive(normal);
                _affectedSection.gameObject.SetActive(cur != null && _editor.kitIsLone);
            }
            if (_shown == null)
                return;

            _center.Show(_shown.m_Center, _editor.KitIsFieldDirty(_shown, CollidersEditor.ColliderField.Center));
            _direction.Show((int)_shown.m_Direction, _editor.KitIsFieldDirty(_shown, CollidersEditor.ColliderField.Direction));
            _bound.Show((int)_shown.m_Bound, _editor.KitIsFieldDirty(_shown, CollidersEditor.ColliderField.Bound));
            DynamicBoneCollider n = _shown as DynamicBoneCollider;
            if (n != null)
            {
                _radius.Show(n.m_Radius, _editor.KitIsFieldDirty(_shown, CollidersEditor.ColliderField.Radius));
                // Joan's height range: from twice the radius up to 4 (or 4 x radius)
                _height.SetRange(2f * n.m_Radius, Mathf.Max(4f, 4f * n.m_Radius));
                _height.Show(n.m_Height, _editor.KitIsFieldDirty(_shown, CollidersEditor.ColliderField.Height));
            }

            if (_editor.kitIsLone)
                RefreshAffected(check);
        }

        private void ResetField(CollidersEditor.ColliderField f)
        {
            if (_editor != null && _shown != null)
                _editor.KitResetField(_shown, f);
        }

        private void ConfirmResetAll()
        {
            if (_editor == null)
                return;
            KitModal.Show("Reset all colliders?", "Every changed collider of this object goes back to its original settings.", new[] { "Cancel", "Reset all" }, r =>
            {
                if (r == 1 && _editor != null)
                    _editor.KitResetAll();
            }, true);
        }

        // ---------------------------------------------------------------- lone collider links

        private void RefreshAffected(bool check)
        {
            if (check)
            {
                int sig = _editor.KitAffectedSignature(_shown);
                if (sig != _affectedSignature)
                {
                    _affectedSignature = sig;
                    RebuildAffected(_editor.KitAffectedList(_shown));
                }
            }
            foreach (Link l in _links)
            {
                if (l.owner.owner == null)
                    continue;
                bool on = _editor.KitAffects(_shown, l.owner.owner, l.bone);
                if (l.check.Value != on)
                    l.check.SetValue(on, true, false);
            }
        }

        private void RebuildAffected(List<CollidersEditor.KitAffected> list)
        {
            for (int i = _affected.childCount - 1; i >= 0; i--)
            {
                GameObject child = _affected.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
            _links.Clear();
            foreach (CollidersEditor.KitAffected a in list)
            {
                KitAffectedRef owner = new KitAffectedRef { owner = a.owner };
                RectTransform head = KitLayout.Row(_affected, 6f);
                Kit.Layout(head.gameObject, -1f, 28f);
                Text t = Kit.MakeText(head, "Owner", a.label, Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleLeft);
                t.fontStyle = FontStyle.Bold;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                Kit.Layout(t.gameObject, -1f, 20f).flexibleWidth = 1f;
                KitButton cam = KitButton.Create(head, "Look at", ButtonStyle.Ghost, () =>
                {
                    if (owner.owner != null)
                        Studio.Studio.Instance.cameraCtrl.targetPos = owner.owner.target.oci.guideObject.transformTarget.position;
                });
                Kit.Layout(cam.gameObject, 80f, 26f);
                KitTooltip.Attach(cam.gameObject, "Center the camera on this object (Joan's \"Center camera on\")");

                RectTransform grid = KitLayout.Grid(_affected, new Vector2(150f, 26f), 4f);
                for (int i = 0; i < a.bones.Count; i++)
                {
                    object bone = a.bones[i];
                    Link l = new Link { owner = owner, bone = bone };
                    l.check = KitCheckbox.Create(grid, BoneNames.Readable(a.names[i]), true, on =>
                    {
                        if (_shown != null && owner.owner != null)
                            _editor.KitSetAffects(_shown, owner.owner, bone, on);
                    });
                    KitTooltip.Attach(l.check.gameObject, a.names[i]);
                    _links.Add(l);
                }
            }
            if (list.Count == 0)
            {
                Text t = Kit.MakeText(_affected, "Empty", "No dynamic bones in the scene", Theme.FontSize, Theme.TextDim, TextAnchor.MiddleLeft);
                Kit.Layout(t.gameObject, -1f, 26f);
            }
        }

        // ---------------------------------------------------------------- picker

        private void RefreshPicker()
        {
            _editor.KitColliders(_items);
            int fp = _items.Count * 7919 + _items.IndexOf(_editor.kitTarget);
            foreach (DynamicBoneColliderBase c in _items)
                fp = (fp * 31 + c.GetInstanceID()) * 2 + (_editor.KitIsDirty(c) ? 1 : 0);
            if (_picker.Changed(fp) == false)
                return;
            List<string> labels = new List<string>(_items.Count);
            List<bool> dirty = new List<bool>(_items.Count);
            foreach (DynamicBoneColliderBase c in _items)
            {
                labels.Add(BoneNames.Display(c.transform));
                dirty.Add(_editor.KitIsDirty(c));
            }
            _picker.Set(labels, dirty, _items.IndexOf(_editor.kitTarget), "This object has no colliders");
        }
    }
}
