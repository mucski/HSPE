using System;
using System.Collections.Generic;
using System.Globalization;
using HSPE.AMModules;
using HSPE.Kit.Small;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HSPE.Kit.Advanced
{
    using HSUIKit;

    /// <summary>
    /// Blend Shapes page: toolbar (link eyes, Joan's presets, refresh, reset all), the meshes that have blend shapes,
    /// then the selected mesh's shapes as a searchable, virtualized list of sliders (0-100, changed ones marked, reset
    /// per shape, right-click to rename / type a value). All through Joan's BlendShapesEditor.
    /// </summary>
    internal sealed class BlendShapesPage : IAdvancedPage
    {
        private const float RowH = 30f, Pad = 2f;

        private struct Item
        {
            public bool header;
            public string text;
            public int index;
        }

        private sealed class ShapeRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerClickHandler
        {
            public BlendShapesPage owner;
            public RectTransform rect;
            public Image bg, track, fill, handle, dot;
            public Text label, value;
            public KitIconButton reset;
            public int item = -1;
            public bool hover, dragging;
            public float shownWeight = float.NaN;
            public string headerOf; // header text shown (null = a shape row)

            public void OnPointerEnter(PointerEventData e) { hover = true; owner.PaintRow(this); }
            public void OnPointerExit(PointerEventData e) { hover = false; owner.PaintRow(this); }
            public void OnPointerDown(PointerEventData e) { }

            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Right)
                    owner.RowMenu(this, e.position);
            }
        }

        private RectTransform _root;
        private BlendShapesEditor _editor;
        private ItemPicker _picker;
        private readonly List<SkinnedMeshRenderer> _meshes = new List<SkinnedMeshRenderer>();
        private float _nextCheck;
        private KitCheckbox _linkEyes;
        private RectTransform _presetsButton;
        private KitPopover _popover;

        private KitScroll _scroll;
        private Text _empty;
        private readonly List<Item> _items = new List<Item>();
        private readonly List<ShapeRow> _pool = new List<ShapeRow>();
        private string _search = "";
        private SkinnedMeshRenderer _shownMesh;
        private bool _dirty = true;
        private float _lastY = -1f, _lastViewH = -1f, _nextRepaint;

        public RectTransform Root { get { return _root; } }

        public void Build(RectTransform parent)
        {
            _root = Kit.Rect(parent, "BlendShapes");
            Kit.Fill(_root);

            RectTransform toolbar = PageUi.Toolbar(_root, 0f);
            _picker = ItemPicker.Create(toolbar, "The mesh whose shapes are listed (● = changed)");
            _picker.OnPicked = i => { if (_editor != null && i >= 0 && i < _meshes.Count) _editor.kitTarget = _meshes[i]; };
            _linkEyes = KitCheckbox.Create(toolbar, "Link eyes", true, on => { if (_editor != null) _editor.kitLinkEyes = on; });
            Kit.Layout(_linkEyes.gameObject, 100f, 28f);
            KitTooltip.Attach(_linkEyes.gameObject, "Eye shapes also move the eyelashes and tears (Joan's \"Link eyes components\")");
            KitButton presets = KitButton.Create(toolbar, "Presets", ButtonStyle.Ghost, OpenPresets, Sprites.Chevron);
            Kit.Layout(presets.gameObject, 96f, 30f);
            KitTooltip.Attach(presets.gameObject, "Joan's blend-shape presets: every mesh's shapes at once");
            _presetsButton = presets.Rect;
            PageUi.MoreButton(toolbar, "Refresh, reset all", () =>
            {
                if (_editor == null)
                    return null;
                return new KitMenu()
                    .Item("Refresh the mesh list", () => { _editor.KitRefresh(); _nextCheck = 0f; })
                    .Separator()
                    .Danger("Reset all blend shapes...", ConfirmResetAll);
            });

            RectTransform head = TopRow(_root, 42f, 32f);
            KitInput search = KitInput.Create(head, "Search shapes...", "", SetSearch);
            Kit.Layout(search.gameObject, -1f, 30f).flexibleWidth = 1f;
            KitButton reset = KitButton.Create(head, "Reset mesh", ButtonStyle.Secondary, () =>
            {
                if (_editor != null && _editor.kitTarget != null)
                    _editor.ResetRendererLinked(_editor.kitTarget);
            });
            Kit.Layout(reset.gameObject, 104f, 30f);
            KitTooltip.Attach(reset.gameObject, "Every shape of this mesh back (linked meshes too when Link eyes is on)");

            RectTransform listHost = Kit.Rect(_root, "Shapes");
            Kit.Fill(listHost, 0f, 0f, 0f, 42f + 40f);
            Image lbg = listHost.gameObject.AddComponent<Image>();
            Kit.SetSprite(lbg, Sprites.Rounded(Theme.RadiusSmall));
            KitThemeColor.Bind(lbg, () => Theme.ListBg);
            lbg.raycastTarget = false;
            _scroll = KitScroll.Create(listHost, 0, 0, 0f, true, false);
            _empty = Kit.MakeText(listHost, "Empty", "", Theme.FontSize, Theme.TextDim, TextAnchor.MiddleCenter);
            _empty.fontStyle = FontStyle.Italic;
            Kit.Fill(_empty.rectTransform);
        }

        private static RectTransform TopRow(RectTransform parent, float top, float height)
        {
            RectTransform row = KitLayout.Row(parent, 6f);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, height);
            row.anchoredPosition = new Vector2(0f, -top);
            return row;
        }

        // ---------------------------------------------------------------- page

        public void Show(PoseController target)
        {
            Bind(target);
        }

        public void Hide()
        {
            if (_popover != null)
                _popover.Close();
        }

        private void Bind(PoseController target)
        {
            BlendShapesEditor e = target != null ? target._blendShapesEditor : null;
            if (e == _editor)
                return;
            _editor = e;
            _nextCheck = 0f;
            _shownMesh = null;
            if (_editor != null)
            {
                _linkEyes.gameObject.SetActive(_editor.kitIsCharacter);
                _linkEyes.SetValue(_editor.kitLinkEyes, false, false);
            }
        }

        public void Tick(PoseController target)
        {
            Bind(target);
            if (_editor == null)
                return;
            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 0.5f;
                RefreshPicker();
            }

            SkinnedMeshRenderer mesh = _editor.kitTarget;
            if (mesh != null && (mesh.sharedMesh == null || mesh.sharedMesh.blendShapeCount == 0))
                mesh = null;
            if (mesh != _shownMesh)
            {
                _shownMesh = mesh;
                RebuildItems();
                _scroll.ScrollToTop();
            }
            BindRows();
        }

        private void ConfirmResetAll()
        {
            if (_editor == null)
                return;
            KitModal.Show("Reset all blend shapes?", "Every changed blend shape of this object goes back to its original weight.", new[] { "Cancel", "Reset all" }, r =>
            {
                if (r == 1 && _editor != null)
                    _editor.KitResetAll();
            }, true);
        }

        // ---------------------------------------------------------------- meshes

        private void RefreshPicker()
        {
            _editor.KitRenderers(_meshes);
            if (_editor.kitTarget == null && _meshes.Count != 0)
                _editor.kitTarget = _meshes[0];
            int fp = _meshes.Count * 7919 + _meshes.IndexOf(_editor.kitTarget);
            foreach (SkinnedMeshRenderer r in _meshes)
                fp = (fp * 31 + r.GetInstanceID()) * 131 + _editor.KitDirtyCount(r);
            if (_picker.Changed(fp) == false)
                return;
            List<string> labels = new List<string>(_meshes.Count);
            List<bool> dirty = new List<bool>(_meshes.Count);
            foreach (SkinnedMeshRenderer r in _meshes)
            {
                int n = _editor.KitDirtyCount(r);
                labels.Add(BlendShapesEditor.KitRendererName(r) + (n > 0 ? "  (" + n + " changed)" : ""));
                dirty.Add(n > 0);
            }
            _picker.Set(labels, dirty, _meshes.IndexOf(_editor.kitTarget), "This object has no blend shapes");
        }

        // ---------------------------------------------------------------- shapes (virtualized)

        private void SetSearch(string s)
        {
            s = (s ?? "").Trim();
            if (s == _search)
                return;
            _search = s;
            RebuildItems();
            _scroll.ScrollToTop();
        }

        private void RebuildItems()
        {
            _items.Clear();
            _dirty = true;
            SkinnedMeshRenderer mesh = _shownMesh;
            if (mesh == null)
            {
                _empty.text = "Select a mesh above";
                _empty.enabled = true;
                _scroll.Content.sizeDelta = new Vector2(0f, 0f);
                return;
            }
            Mesh m = mesh.sharedMesh;
            for (int i = 0; i < m.blendShapeCount; i++)
            {
                string name = m.GetBlendShapeName(i);
                string alias = BlendShapesEditor.KitShapeAlias(name);
                if (_search.Length != 0 && name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0 && (alias == null || alias.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                string sep = _search.Length == 0 ? _editor.KitSeparator(mesh, i) : null;
                if (sep != null)
                    _items.Add(new Item { header = true, text = sep });
                _items.Add(new Item { index = i, text = i + "  " + (alias ?? name) });
            }
            _empty.text = _search.Length != 0 ? "No shape matches \"" + _search + "\"" : "";
            _empty.enabled = _items.Count == 0;
            _scroll.Content.sizeDelta = new Vector2(0f, _items.Count * RowH + Pad * 2f);
        }

        private void BindRows()
        {
            float y = _scroll.Content.anchoredPosition.y;
            float vh = _scroll.ViewportHeight;
            bool moved = !Mathf.Approximately(y, _lastY) || !Mathf.Approximately(vh, _lastViewH);
            if (!_dirty && !moved && Time.unscaledTime < _nextRepaint)
                return;
            _dirty = false;
            _nextRepaint = Time.unscaledTime + 0.1f; // weights also move from outside (linked eyes, Studio's face)
            _lastY = y;
            _lastViewH = vh;
            int first = Mathf.Max(0, Mathf.FloorToInt((y - Pad) / RowH));
            int visible = Mathf.CeilToInt(vh / RowH) + 2;
            while (_pool.Count < visible)
                _pool.Add(CreateRow());
            for (int j = 0; j < _pool.Count; j++)
            {
                ShapeRow row = _pool[j];
                int i = first + j;
                if (j >= visible || i >= _items.Count)
                {
                    if (row.gameObject.activeSelf)
                        row.gameObject.SetActive(false);
                    row.item = -1;
                    continue;
                }
                if (!row.gameObject.activeSelf)
                    row.gameObject.SetActive(true);
                row.item = i;
                row.rect.anchoredPosition = new Vector2(0f, -(Pad + i * RowH));
                Bind(row, _items[i]);
            }
        }

        private ShapeRow CreateRow()
        {
            RectTransform rt = Kit.Rect(_scroll.Content, "Shape");
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-8f, RowH - 2f);
            ShapeRow r = rt.gameObject.AddComponent<ShapeRow>();
            r.owner = this;
            r.rect = rt;
            r.bg = rt.gameObject.AddComponent<Image>();
            Kit.SetSprite(r.bg, Sprites.Rounded(Mathf.Max(0, Theme.RadiusSmall - 2)));
            r.bg.color = Color.clear;

            r.dot = Kit.Image(rt, "Changed", Sprites.Circle, Theme.Warning);
            Kit.Anchor(r.dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(9f, 0f), new Vector2(6f, 6f));
            r.label = Kit.MakeText(rt, "Name", "", Theme.FontSize, Theme.Text, TextAnchor.MiddleLeft);
            r.label.horizontalOverflow = HorizontalWrapMode.Wrap;
            r.label.verticalOverflow = VerticalWrapMode.Truncate;
            r.label.rectTransform.anchorMin = new Vector2(0f, 0f);
            r.label.rectTransform.anchorMax = new Vector2(0.42f, 1f);
            r.label.rectTransform.offsetMin = new Vector2(18f, 0f);
            r.label.rectTransform.offsetMax = new Vector2(-6f, 0f);

            // slider: a drag area over a pill track (press anywhere on it to jump, drag to change)
            RectTransform area = Kit.Rect(rt, "Slider");
            area.anchorMin = new Vector2(0.42f, 0f);
            area.anchorMax = new Vector2(1f, 1f);
            area.offsetMin = new Vector2(0f, 0f);
            area.offsetMax = new Vector2(-72f, 0f);
            Kit.HitArea(area.gameObject);
            r.track = Kit.Image(area, "Track", Sprites.Pill(4f), Theme.SurfaceActive);
            r.track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            r.track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            r.track.rectTransform.sizeDelta = new Vector2(-12f, 4f);
            r.fill = Kit.Image(r.track.rectTransform, "Fill", Sprites.Pill(4f), Theme.Accent);
            r.fill.rectTransform.anchorMin = new Vector2(0f, 0f);
            r.fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            r.fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            r.fill.rectTransform.offsetMin = Vector2.zero;
            r.fill.rectTransform.offsetMax = Vector2.zero;
            r.handle = Kit.Image(r.track.rectTransform, "Handle", Sprites.Circle, Color.white);
            r.handle.rectTransform.sizeDelta = new Vector2(12f, 12f);
            KitDragArea drag = area.gameObject.AddComponent<KitDragArea>();
            drag.OnPoint = (x, y) => OnSlide(r, x);
            drag.OnRelease = () => { r.dragging = false; };

            r.value = Kit.MakeText(rt, "Value", "", Theme.FontSize, Theme.Text, TextAnchor.MiddleRight);
            r.value.rectTransform.anchorMin = new Vector2(1f, 0f);
            r.value.rectTransform.anchorMax = new Vector2(1f, 1f);
            r.value.rectTransform.pivot = new Vector2(1f, 0.5f);
            r.value.rectTransform.sizeDelta = new Vector2(40f, 0f);
            r.value.rectTransform.anchoredPosition = new Vector2(-30f, 0f);

            r.reset = KitIconButton.Create(rt, Sprites.Reset, 22f, false, () => ResetRow(r));
            Kit.Anchor(r.reset.Rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(22f, 22f));
            KitTooltip.Attach(r.reset.gameObject, "Back to the original weight");
            return r;
        }

        private void Bind(ShapeRow r, Item it)
        {
            bool header = it.header;
            r.track.transform.parent.gameObject.SetActive(!header);
            r.value.enabled = !header;
            if (header)
            {
                r.dot.enabled = false;
                r.reset.SetShown(false, false);
                if (r.headerOf != it.text) // ToUpperInvariant allocates: only when the row shows another header
                {
                    r.headerOf = it.text;
                    r.label.text = it.text.ToUpperInvariant();
                }
                r.label.fontSize = Theme.SmallSize;
                r.label.fontStyle = FontStyle.Bold;
                r.label.color = Theme.TextDim;
                r.label.rectTransform.anchorMax = new Vector2(1f, 1f);
                PaintRow(r);
                return;
            }
            r.headerOf = null;
            r.label.rectTransform.anchorMax = new Vector2(0.42f, 1f);
            r.label.fontSize = Theme.FontSize;
            r.label.fontStyle = FontStyle.Normal;
            if (r.label.text != it.text)
                r.label.text = it.text;
            bool dirty;
            float w = _editor.KitWeight(_shownMesh, it.index, out dirty);
            r.label.color = dirty ? Theme.Warning : Theme.Text;
            r.dot.enabled = dirty;
            r.reset.SetShown(dirty, false);
            if (w != r.shownWeight) // format only on change
            {
                r.shownWeight = w;
                r.value.text = w.ToString("0", CultureInfo.InvariantCulture);
            }
            float t = Mathf.Clamp01(w / 100f);
            r.fill.rectTransform.anchorMax = new Vector2(t, 1f);
            Vector2 a = new Vector2(t, 0.5f);
            r.handle.rectTransform.anchorMin = a;
            r.handle.rectTransform.anchorMax = a;
            r.handle.rectTransform.anchoredPosition = Vector2.zero;
            PaintRow(r);
        }

        private void PaintRow(ShapeRow r)
        {
            bool header = r.item >= 0 && r.item < _items.Count && _items[r.item].header;
            Tween.Tint(r.bg, !header && r.hover ? Kit.Alpha(Theme.SurfaceHover, 0.8f) : Color.clear, 0.1f, Ease.OutQuad);
        }

        private void OnSlide(ShapeRow r, float x)
        {
            if (_shownMesh == null || r.item < 0 || r.item >= _items.Count || _items[r.item].header)
                return;
            r.dragging = true;
            float w = Mathf.Round(Mathf.Clamp01(x) * 100f);
            _editor.SetWeightLinked(_shownMesh, _items[r.item].index, w);
            Bind(r, _items[r.item]);
        }

        private void ResetRow(ShapeRow r)
        {
            if (_shownMesh == null || r.item < 0 || r.item >= _items.Count || _items[r.item].header)
                return;
            _editor.ResetShapeLinked(_shownMesh, _items[r.item].index);
            _dirty = true;
        }

        private void RowMenu(ShapeRow r, Vector2 screen)
        {
            if (_shownMesh == null || r.item < 0 || r.item >= _items.Count || _items[r.item].header)
                return;
            int index = _items[r.item].index;
            SkinnedMeshRenderer mesh = _shownMesh;
            string name = mesh.sharedMesh.GetBlendShapeName(index);
            KitMenu m = new KitMenu();
            m.Item("Set value...", () =>
            {
                bool d;
                float current = _editor.KitWeight(mesh, index, out d);
                KitModal.Prompt("Set weight", name + " (0 - 100)", current.ToString("0.##", CultureInfo.InvariantCulture), "Weight", "Set", s =>
                {
                    float v;
                    if (s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    {
                        _editor.SetWeightLinked(mesh, index, Mathf.Clamp(v, 0f, 100f));
                        _dirty = true;
                    }
                });
            });
            m.Item("Rename...", () =>
            {
                KitModal.Prompt("Rename blend shape", "Shown name for " + name + ". Leave empty to remove the alias.", BlendShapesEditor.KitShapeAlias(name) ?? "", "Alias", "Save", s =>
                {
                    if (s == null)
                        return;
                    BlendShapesEditor.SetBlendShapeAlias(name, s);
                    RebuildItems();
                });
            });
            m.Separator();
            m.Item("Reset", () => { _editor.ResetShapeLinked(mesh, index); _dirty = true; });
            KitContextMenu.Show(m, screen);
        }

        // ---------------------------------------------------------------- Joan's presets

        private void OpenPresets()
        {
            if (_editor == null)
                return;
            string[] name = { "" };
            _popover = KitPopover.Open(270f);
            RectTransform c = _popover.Content;
            KitLayout.Header(c, "Blend shape presets");
            KitLayout.Label(c, "Every mesh's shapes at once. Click to load; right-click to delete.", TextRole.Dim);
            string[] presets = BlendShapesEditor.KitPresets();
            if (presets.Length == 0)
                KitLayout.Label(c, "No presets yet.", TextRole.Dim);
            else
            {
                RectTransform holder = Kit.Rect(c, "List");
                Kit.Layout(holder.gameObject, -1f, Mathf.Min(presets.Length, 7) * 32f);
                KitScroll scroll = KitScroll.Create(holder, 0, 0, 2f, presets.Length > 7);
                foreach (string p in presets)
                {
                    string preset = p;
                    KitButton row = KitButton.Create(scroll.Content, preset, ButtonStyle.Ghost, () =>
                    {
                        _popover.Close();
                        _editor.KitLoadPreset(preset);
                        KitToast.Show("Loaded \"" + preset + "\"", "Blend shapes replaced.", ToastKind.Success, 2.5f);
                    });
                    Kit.Layout(row.gameObject, -1f, 30f);
                    KitContextMenu.Attach(row.gameObject, () => new KitMenu().Danger("Delete \"" + preset + "\"", () =>
                    {
                        _popover.Close();
                        KitModal.Show("Delete preset?", "Delete \"" + preset + "\"?", new[] { "Cancel", "Delete" }, r => { if (r == 1) _editor.KitDeletePreset(preset); }, true);
                    }));
                }
            }
            RectTransform saveRow = KitLayout.Row(c, 6f);
            Kit.Layout(saveRow.gameObject, -1f, 32f);
            KitInput input = KitInput.Create(saveRow, "New preset name", "", s => name[0] = s);
            Kit.Layout(input.gameObject, -1f, 30f).flexibleWidth = 1f;
            VoidHandler save = () =>
            {
                string clean = string.Join("_", (name[0] ?? "").Trim().Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
                if (clean.Length == 0)
                    return;
                _popover.Close();
                if (Array.Exists(BlendShapesEditor.KitPresets(), x => string.Equals(x, clean, StringComparison.OrdinalIgnoreCase)))
                {
                    KitModal.Show("Overwrite preset?", "\"" + clean + "\" already exists.", new[] { "Cancel", "Overwrite" }, r => { if (r == 1) Save(clean); }, true);
                    return;
                }
                Save(clean);
            };
            input.OnSubmit = s => save();
            KitButton ok = KitButton.Create(saveRow, "Save", ButtonStyle.Primary, save);
            Kit.Layout(ok.gameObject, 70f, 30f);
            KitButton folder = KitButton.Create(c, "Open folder", ButtonStyle.Ghost, () =>
            {
                string dir = BlendShapesEditor.kitPresetsFolder;
                if (System.IO.Directory.Exists(dir) == false)
                    System.IO.Directory.CreateDirectory(dir);
                using (System.Diagnostics.Process.Start(dir)) { } // disposes the process handle
            });
            Kit.Layout(folder.gameObject, -1f, 30f);
            _popover.Place(_presetsButton);
        }

        private void Save(string name)
        {
            _editor.KitSavePreset(name);
            KitToast.Show("Saved \"" + name + "\"", null, ToastKind.Success, 2f);
        }
    }
}
