using HSPE.AMModules;
using HSPE.Kit.Small;
using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Advanced
{
    using HSUIKit;

    /// <summary>
    /// The Bones page: search + "Changed" / "Gizmos" chips, the bone tree with the favourites column beside it, and the editor drawer
    /// for the selected bone - Position / Rotation / Scale / Around, X / Y / Z cells (drag, type, wheel), symmetrical,
    /// copy / paste, Reset menu and a ⋯ menu (step, alias, lock). All edits go through Joan's BonesEditor.
    /// </summary>
    internal sealed class BonesPage : IAdvancedPage
    {
        private const float DrawerH = 166f;
        private static readonly Color AxisX = new Color32(0xF0, 0x61, 0x6D, 0xFF);
        private static readonly Color AxisY = new Color32(0x3D, 0xD6, 0x8C, 0xFF);
        private static readonly Color AxisZ = new Color32(0x5B, 0x8C, 0xFF, 0xFF);
        private static readonly Color AxisAll = new Color32(0x97, 0x9D, 0xAB, 0xFF);

        // step choices per tab (value per wheel notch; a drag moves a tenth of it per pixel)
        private static readonly float[][] Steps =
        {
            new[] { 0.001f, 0.01f, 0.1f, 1f },  // position
            new[] { 0.1f, 1f, 5f, 15f },        // rotation
            new[] { 0.001f, 0.01f, 0.1f },      // scale
            new[] { 0.1f, 1f, 5f, 15f },        // rotate around
        };
        private static readonly float[] Step = { 0.01f, 1f, 0.01f, 1f };
        private static readonly string[] Formats = { "0.0000", "0.00", "0.000", "0.00" };

        private RectTransform _root;
        private BoneTree _tree;
        private FavouritesPanel _favourites;
        private BonesEditor _bones;
        private KitButton _changedChip, _gizmosChip;
        private bool _changedOnly;

        private CanvasGroup _drawerBody;
        private Text _noBone, _name, _realName;
        private KitIconButton _star, _more;
        private KitSegmented _tabs;
        private AxisCell[] _cells;
        private Image _stripe;
        private KitCheckbox _symmetrical;
        private KitButton _copy, _paste, _reset;
        private BonesEditor.KitCoord _coord = BonesEditor.KitCoord.Rotation;
        private Transform _shownBone;
        private int _shownStar = -1;

        public RectTransform Root { get { return _root; } }

        public void Build(RectTransform parent)
        {
            _root = Kit.Rect(parent, "Bones");
            Kit.Fill(_root);

            // toolbar
            RectTransform toolbar = KitLayout.Row(_root, 6f);
            toolbar.anchorMin = new Vector2(0f, 1f);
            toolbar.anchorMax = new Vector2(1f, 1f);
            toolbar.pivot = new Vector2(0.5f, 1f);
            toolbar.sizeDelta = new Vector2(0f, 34f);
            toolbar.anchoredPosition = Vector2.zero;
            KitInput search = KitInput.Create(toolbar, "Search bones...", "", s => _tree.SetSearch(s));
            LayoutElement sl = Kit.Layout(search.gameObject, -1f, 32f);
            sl.flexibleWidth = 1f;
            _changedChip = KitButton.Create(toolbar, "Changed", ButtonStyle.Ghost, ToggleChanged);
            Kit.Layout(_changedChip.gameObject, 92f, 32f);
            KitTooltip.Attach(_changedChip.gameObject, "Only the bones HSPE has changed on this object");
            _gizmosChip = KitButton.Create(toolbar, "Gizmos", ButtonStyle.Ghost, ToggleGizmos);
            Kit.Layout(_gizmosChip.gameObject, 86f, 32f);
            KitTooltip.Attach(_gizmosChip.gameObject, "Draw the selected bone's axes in the viewport");

            // tree | favourites (the drawer below spans both)
            RectTransform treeHost = Kit.Rect(_root, "TreeHost");
            Kit.Fill(treeHost, 0f, DrawerH + 8f, FavouritesPanel.Width + 8f, 42f);
            _tree = BoneTree.Create(treeHost);
            _tree.RowMenu = RowMenu;

            RectTransform favHost = Kit.Rect(_root, "FavouritesHost");
            favHost.anchorMin = new Vector2(1f, 0f);
            favHost.anchorMax = new Vector2(1f, 1f);
            favHost.pivot = new Vector2(1f, 0.5f);
            favHost.offsetMin = new Vector2(-FavouritesPanel.Width, DrawerH + 8f);
            favHost.offsetMax = new Vector2(0f, -42f);
            _favourites = FavouritesPanel.Create(favHost);
            _favourites.RenameRequested = Rename;
            _favourites.Changed = () => { _shownStar = -1; _tree.Rebuild(); };

            BuildDrawer();
        }

        private void BuildDrawer()
        {
            RectTransform drawer = Kit.Rect(_root, "Drawer");
            drawer.anchorMin = new Vector2(0f, 0f);
            drawer.anchorMax = new Vector2(1f, 0f);
            drawer.pivot = new Vector2(0.5f, 0f);
            drawer.sizeDelta = new Vector2(0f, DrawerH);
            drawer.anchoredPosition = Vector2.zero;
            Image bg = drawer.gameObject.AddComponent<Image>();
            Kit.SetSprite(bg, Sprites.Rounded(Theme.RadiusSmall));
            KitThemeColor.Bind(bg, () => Kit.Alpha(Theme.Surface, 0.55f));
            bg.raycastTarget = false;

            RectTransform body = Kit.Rect(drawer, "Body");
            Kit.Fill(body);
            _drawerBody = body.gameObject.AddComponent<CanvasGroup>();
            VerticalLayoutGroup v = body.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(10, 10, 8, 10);
            v.spacing = 6f;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            // header: name, real name, favourite, more
            RectTransform head = KitLayout.Row(body, 6f);
            Kit.Layout(head.gameObject, -1f, 26f);
            _name = Kit.MakeText(head, "Name", "", Theme.FontSize + 1, Theme.Text, TextAnchor.MiddleLeft);
            _name.fontStyle = FontStyle.Bold;
            _name.horizontalOverflow = HorizontalWrapMode.Wrap;
            _name.verticalOverflow = VerticalWrapMode.Truncate;
            Kit.Layout(_name.gameObject, -1f, 22f).flexibleWidth = 1f;
            _realName = Kit.MakeText(head, "Real", "", Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleRight);
            _realName.horizontalOverflow = HorizontalWrapMode.Wrap;
            _realName.verticalOverflow = VerticalWrapMode.Truncate;
            Kit.Layout(_realName.gameObject, -1f, 22f).flexibleWidth = 1f;
            _star = KitIconButton.Create(head, HspeIcons.StarOutline, 26f, false, ToggleFavourite);
            KitTooltip.Attach(_star.gameObject, "Favourite: listed at the top of the tree");
            _more = KitIconButton.Create(head, HspeIcons.More, 26f, false, OpenMore);
            KitTooltip.Attach(_more.gameObject, "Step, rename, lock");

            _tabs = KitSegmented.Create(body, new[] { "Position", "Rotation", "Scale", "Around" }, (int)_coord, SetCoord);
            Kit.Layout(_tabs.gameObject, -1f, 32f);
            KitTooltip.Attach(_tabs.gameObject, "Around = rotate the bone around the camera's target point");

            RectTransform axes = KitLayout.Row(body, 6f);
            Kit.Layout(axes.gameObject, -1f, 32f);
            // in the drawer's left padding, outside the layout: as a layout slot it pushed X right of the tabs
            _stripe = Kit.Image(axes, "Changed", Sprites.Pill(3f), Color.clear);
            Kit.IgnoreLayout(_stripe.gameObject);
            Kit.Anchor(_stripe.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(3f, 22f));
            KitTooltip.Attach(_stripe.gameObject, "HSPE holds a value for this");
            string[] names = { "X", "Y", "Z", "All" };
            Color[] colors = { AxisX, AxisY, AxisZ, AxisAll };
            _cells = new AxisCell[4];
            for (int i = 0; i < 4; i++)
            {
                int axis = i;
                _cells[i] = AxisCell.Create(axes, names[i], colors[i], "Drag to change (Shift x10, Ctrl x0.1) · click to type · wheel = one step");
                _cells[i].OnDelta = d => ApplyDelta(axis, d);
                _cells[i].OnTyped = value => ApplyTyped(axis, value);
                _cells[i].OnActive = on => { if (!on) EndEdit(); };
            }

            RectTransform actions = KitLayout.Row(body, 6f);
            Kit.Layout(actions.gameObject, -1f, 32f);
            _symmetrical = KitCheckbox.Create(actions, "Symmetrical", false, on => { if (_bones != null) _bones.kitSymmetrical = on; });
            Kit.Layout(_symmetrical.gameObject, 130f, 28f);
            KitLayout.Flexible(actions);
            _copy = KitButton.Create(actions, "Copy", ButtonStyle.Ghost, () => { if (_bones != null) _bones.KitCopy(_coord); });
            Kit.Layout(_copy.gameObject, 70f, 30f);
            _paste = KitButton.Create(actions, "Paste", ButtonStyle.Ghost, () => { if (_bones != null) _bones.KitPaste(_coord); });
            Kit.Layout(_paste.gameObject, 70f, 30f);
            _reset = KitButton.Create(actions, "Reset", ButtonStyle.Secondary, OpenReset, Sprites.Chevron);
            Kit.Layout(_reset.gameObject, 92f, 30f);

            _noBone = Kit.MakeText(drawer, "NoBone", "Select a bone in the list", Theme.FontSize, Theme.TextDim, TextAnchor.MiddleCenter);
            Kit.Fill(_noBone.rectTransform);
            _noBone.fontStyle = FontStyle.Italic;
            ApplyCoordLayout();
            // start in the "no bone" state: _shownBone and the target are both null on the first frame, so Tick
            // would never switch it (the hint was drawn over live controls)
            _drawerBody.gameObject.SetActive(false);
            _noBone.enabled = true;
        }

        // ---------------------------------------------------------------- page

        public void Show(PoseController target)
        {
            Bind(target);
        }

        public void Hide() { }

        private void Bind(PoseController target)
        {
            BonesEditor b = target != null ? target._bonesEditor : null;
            if (b == _bones)
                return;
            _bones = b;
            _tree.Bind(b);
            _favourites.Bind(b);
            _shownBone = null;
            _shownStar = -1;
        }

        public void Tick(PoseController target)
        {
            Bind(target);
            if (_bones == null)
                return;
            Paint(_changedChip, _changedOnly);
            Paint(_gizmosChip, _bones.kitDrawGizmos);

            Transform bone = _bones.kitTarget;
            if (bone != _shownBone)
            {
                _shownBone = bone;
                _shownStar = -1;
                if (bone != null)
                {
                    _tree.Reveal(bone);
                    _name.text = BoneNames.Display(bone);
                    _realName.text = bone.name;
                    Transform twin = _bones.kitTwin;
                    KitTooltip.Attach(_symmetrical.gameObject, twin != null ? "Mirror every change onto " + twin.name : "This bone has no left / right twin");
                }
                _drawerBody.gameObject.SetActive(bone != null);
                _noBone.enabled = bone == null;
                if (bone == null)
                    _name.text = _realName.text = "";
            }
            if (bone == null)
                return;

            if (_symmetrical.Value != _bones.kitSymmetrical)
                _symmetrical.SetValue(_bones.kitSymmetrical, false, false);

            int star = _bones.KitIsFavourite(bone) ? 1 : 0;
            if (star != _shownStar)
            {
                _shownStar = star;
                _star.Icon.sprite = star == 1 ? HspeIcons.Star : HspeIcons.StarOutline;
                _star.Icon.color = star == 1 ? Theme.Warning : Theme.TextDim;
            }

            // values (cells being typed into keep their text)
            int c = (int)_coord;
            switch (_coord)
            {
                case BonesEditor.KitCoord.Position:
                    ShowVector(_bones.KitGetPosition());
                    break;
                case BonesEditor.KitCoord.Rotation:
                    ShowVector(_bones.KitGetRotation().eulerAngles);
                    break;
                case BonesEditor.KitCoord.Scale:
                    Vector3 s = _bones.KitGetScale();
                    ShowVector(s);
                    _cells[3].Show((s.x + s.y + s.z) / 3f);
                    break;
            }
            bool p, r, sc;
            _bones.KitDirtyParts(bone.gameObject, out p, out r, out sc);
            bool dirty = c == 0 ? p : c == 1 ? r : c == 2 ? sc : (p || r);
            Color stripe = dirty ? Theme.Warning : Color.clear;
            if (_stripe.color != stripe)
                _stripe.color = stripe;
            _paste.Interactable = _bones.KitCanPaste(_coord);
        }

        private void ShowVector(Vector3 v)
        {
            _cells[0].Show(v.x);
            _cells[1].Show(v.y);
            _cells[2].Show(v.z);
        }

        private static void Paint(KitButton chip, bool on)
        {
            ButtonStyle style = on ? ButtonStyle.Primary : ButtonStyle.Ghost;
            if (chip.Style != style)
                chip.Style = style;
        }

        // ---------------------------------------------------------------- toolbar

        private void ToggleChanged()
        {
            _changedOnly = !_changedOnly;
            _tree.SetChangedOnly(_changedOnly);
        }

        private void ToggleGizmos()
        {
            if (_bones != null)
                _bones.kitDrawGizmos = !_bones.kitDrawGizmos;
        }

        // ---------------------------------------------------------------- editing

        private void SetCoord(int index)
        {
            _coord = (BonesEditor.KitCoord)index;
            ApplyCoordLayout();
        }

        private void ApplyCoordLayout()
        {
            int c = (int)_coord;
            bool around = _coord == BonesEditor.KitCoord.RotateAround;
            for (int i = 0; i < 4; i++)
            {
                _cells[i].Step = Step[c];
                _cells[i].Format = Formats[c];
                if (around)
                    _cells[i].SetNoValue("drag");
                else
                    _cells[i].SetHasValue();
            }
            _cells[3].gameObject.SetActive(_coord == BonesEditor.KitCoord.Scale);
            // Joan's window has no symmetry / copy / paste for Rotate Around either
            _symmetrical.gameObject.SetActive(!around);
            _copy.gameObject.SetActive(!around);
            _paste.gameObject.SetActive(!around);
        }

        private static Vector3 Axis(int axis)
        {
            return axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        }

        private void ApplyDelta(int axis, float d)
        {
            if (_bones == null || _bones.kitTarget == null)
                return;
            switch (_coord)
            {
                case BonesEditor.KitCoord.Position:
                    {
                        Vector3 v = _bones.KitGetPosition();
                        v[axis] += d;
                        _bones.KitSetPosition(v);
                        break;
                    }
                case BonesEditor.KitCoord.Scale:
                    {
                        Vector3 v = _bones.KitGetScale();
                        if (axis == 3)
                            v += Vector3.one * d;
                        else
                            v[axis] += d;
                        _bones.KitSetScale(v);
                        break;
                    }
                case BonesEditor.KitCoord.Rotation:
                    // like Joan's +/- buttons: turn around the bone's own axis
                    _bones.KitSetRotation(_bones.KitGetRotation() * Quaternion.AngleAxis(d, Axis(axis)), false);
                    break;
                case BonesEditor.KitCoord.RotateAround:
                    _bones.KitRotateAround(axis, d);
                    break;
            }
        }

        private void ApplyTyped(int axis, float value)
        {
            if (_bones == null || _bones.kitTarget == null)
                return;
            switch (_coord)
            {
                case BonesEditor.KitCoord.Position:
                    {
                        Vector3 v = _bones.KitGetPosition();
                        v[axis] = value;
                        _bones.KitSetPosition(v);
                        break;
                    }
                case BonesEditor.KitCoord.Scale:
                    {
                        Vector3 v = _bones.KitGetScale();
                        if (axis == 3)
                            v = Vector3.one * value;
                        else
                            v[axis] = value;
                        _bones.KitSetScale(v);
                        break;
                    }
                case BonesEditor.KitCoord.Rotation:
                    {
                        Vector3 e = _bones.KitGetRotation().eulerAngles;
                        e[axis] = value;
                        _bones.KitSetRotation(Quaternion.Euler(e), true);
                        break;
                    }
            }
        }

        /// <summary>A drag or wheel notch ended: FK rotations push Studio's undo, like Joan's editor.</summary>
        private void EndEdit()
        {
            if (_bones == null)
                return;
            if (_coord == BonesEditor.KitCoord.Rotation)
                _bones.KitEndRotation();
            else if (_coord == BonesEditor.KitCoord.RotateAround)
                _bones.KitEndRotateAround();
        }

        // ---------------------------------------------------------------- menus

        private void ToggleFavourite()
        {
            if (_bones == null || _bones.kitTarget == null)
                return;
            if (_bones.KitIsBuiltInFavourite(_bones.kitTarget))
            {
                KitToast.Show("Built-in favourite", "Hands, feet and face are always listed.", ToastKind.Info, 2.5f);
                return;
            }
            _bones.KitToggleFavourite(_bones.kitTarget);
            _tree.Rebuild();
            _favourites.Rebuild();
        }

        private void OpenReset()
        {
            if (_bones == null || _bones.kitTarget == null)
                return;
            bool fk = _bones.kitRotationIsFK;
            KitMenu m = new KitMenu();
            m.Item("Reset position", () => _bones.KitReset(BonesEditor.KitCoord.Position, false));
            m.Item("Reset rotation" + (fk ? "  (FK drives it)" : ""), () => _bones.KitReset(BonesEditor.KitCoord.Rotation, false), null, !fk);
            m.Item("Reset scale", () => _bones.KitReset(BonesEditor.KitCoord.Scale, false));
            m.Separator();
            m.Item("Reset all", () => ResetAll(false));
            m.Item("Reset all, with children", () => ResetAll(true));
            m.Separator();
            m.Item("Set to default (0 · 0° · 1)", () => _bones.KitDefault());
            ShowMenuAt(m, _reset.Rect);
        }

        private void ResetAll(bool withChildren)
        {
            _bones.KitReset(BonesEditor.KitCoord.Position, withChildren);
            _bones.KitReset(BonesEditor.KitCoord.Rotation, withChildren);
            _bones.KitReset(BonesEditor.KitCoord.Scale, withChildren);
        }

        private void OpenMore()
        {
            if (_bones == null || _bones.kitTarget == null)
                return;
            int c = (int)_coord;
            KitMenu m = new KitMenu();
            foreach (float s in Steps[c])
            {
                float step = s;
                string label = (Mathf.Approximately(Step[c], step) ? "✓  " : "     ") + "Step " + step.ToString(c == 1 || c == 3 ? "0.#" : "0.###") + (c == 1 || c == 3 ? "°" : "");
                m.Item(label, () => { Step[c] = step; ApplyCoordLayout(); });
            }
            m.Separator();
            m.Item("Rename (alias)...", () => Rename(_bones.kitTarget));
            if (_coord != BonesEditor.KitCoord.RotateAround)
            {
                bool fk = _coord == BonesEditor.KitCoord.Rotation && _bones.kitRotationIsFK;
                m.Item("Lock current " + _coord.ToString().ToLowerInvariant(), () => _bones.KitLock(_coord, false), null, !fk);
            }
            m.Separator();
            m.Item("Unfold changed bones", () =>
            {
                _bones.KitOpenChanged();
                _tree.Rebuild();
            });
            ShowMenuAt(m, _more.Rect);
        }

        private KitMenu RowMenu()
        {
            if (_bones == null || _bones.kitTarget == null)
                return null;
            Transform t = _bones.kitTarget;
            KitMenu m = new KitMenu();
            if (_bones.KitIsBuiltInFavourite(t) == false)
                m.Item(_bones.KitIsFavourite(t) ? "Remove from favourites" : "Add to favourites", ToggleFavourite);
            m.Item("Rename (alias)...", () => Rename(t));
            m.Separator();
            m.Item("Reset all", () => ResetAll(false));
            return m;
        }

        private void Rename(Transform t)
        {
            if (t == null)
                return;
            string name = t.name;
            KitModal.Prompt("Rename bone", "Shown name for " + name + ". Leave empty to remove the alias.", BonesEditor.KitAlias(name) ?? "", "Alias", "Save", s =>
            {
                if (s == null)
                    return;
                BonesEditor.KitSetAlias(name, s);
                _shownBone = null; // refresh the header
                _tree.Rebuild();
                _favourites.Rebuild();
            });
        }

        private static void ShowMenuAt(KitMenu m, RectTransform anchor)
        {
            Vector3[] c = new Vector3[4];
            anchor.GetWorldCorners(c);
            KitContextMenu.Show(m, RectTransformUtility.WorldToScreenPoint(null, c[0]));
        }
    }
}
