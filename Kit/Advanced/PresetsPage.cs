using System;
using System.Collections.Generic;
using System.IO;
using HSPE.Kit.Small;
using HSPE.Kit.Transfer;
using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Advanced
{
    using HSUIKit;

    /// <summary>
    /// Presets page (same layout as HSLRE's): toolbar (Save new, Select) over one row per preset with a rounded
    /// thumbnail, its name and the modules it holds. Clicking a row asks which of its modules to apply; the row icons
    /// overwrite it with the selected character or delete it. Select mode deletes several at once.
    /// </summary>
    internal sealed class PresetsPage : IAdvancedPage
    {
        private const float ThumbHeight = 44f, ThumbAspect = 16f / 9f;

        private sealed class Info
        {
            public DateTime written;
            public ModuleMask modules;
            public string summary;
        }

        private RectTransform _root, _list;
        private KitScroll _scroll;
        private KitButton _saveNew, _select, _deleteSelected;
        private KitText _empty;
        private string[] _shown = new string[0];
        private bool _selectMode;
        private readonly HashSet<string> _selected = new HashSet<string>();
        private int _shownSelectedCount = -1;
        private float _nextListCheck;
        private DateTime _shownStamp;
        private readonly ThumbnailCache _thumbnails = new ThumbnailCache(ThumbAspect, ThumbHeight, Theme.RadiusSmall);
        private readonly Dictionary<string, Info> _info = new Dictionary<string, Info>(StringComparer.OrdinalIgnoreCase);
        private KitPopover _popover;

        public RectTransform Root { get { return _root; } }

        public void Build(RectTransform parent)
        {
            _root = Kit.Rect(parent, "Presets");
            Kit.Fill(_root);

            RectTransform toolbar = KitLayout.Row(_root, 6f);
            toolbar.anchorMin = new Vector2(0f, 1f);
            toolbar.anchorMax = new Vector2(1f, 1f);
            toolbar.pivot = new Vector2(0.5f, 1f);
            toolbar.sizeDelta = new Vector2(0f, 34f);
            toolbar.anchoredPosition = Vector2.zero;
            _saveNew = KitButton.Create(toolbar, "Save new...", ButtonStyle.Primary, SaveNew, Sprites.Plus);
            Kit.Layout(_saveNew.gameObject, 128f, 32f);
            KitTooltip.Attach(_saveNew.gameObject, "Save the selected character's settings as a new preset");
            _select = KitButton.Create(toolbar, "Select", ButtonStyle.Ghost, ToggleSelectMode);
            Kit.Layout(_select.gameObject, 84f, 32f);
            KitTooltip.Attach(_select.gameObject, "Select several presets to delete them together");
            KitLayout.Flexible(toolbar);
            _deleteSelected = KitButton.Create(toolbar, "Delete", ButtonStyle.Danger, DeleteSelected, HspeIcons.Trash);
            Kit.Layout(_deleteSelected.gameObject, 120f, 32f);
            _deleteSelected.gameObject.SetActive(false);

            RectTransform listHost = Kit.Rect(_root, "ListHost");
            Kit.Fill(listHost, 0f, 0f, 0f, 42f);
            _scroll = KitScroll.Create(listHost, 0, 0, 3f, true);
            _list = _scroll.Content;
            _empty = KitText.Create(_list, "No presets yet. Select a character and use \"Save new...\".", TextRole.Dim, true);
            Rebuild(PresetStore.List());
        }

        public void Show(PoseController target)
        {
            Rebuild(PresetStore.List());
        }

        public void Tick(PoseController target)
        {
            // other windows (the small window's Presets menu) can change the folder too
            if (Time.unscaledTime >= _nextListCheck)
            {
                _nextListCheck = Time.unscaledTime + 1f;
                // the folder's write time changes when a preset is added / removed / renamed: list only then
                DateTime stamp = System.IO.Directory.Exists(PresetStore.Folder) ? System.IO.Directory.GetLastWriteTimeUtc(PresetStore.Folder) : DateTime.MinValue;
                if (stamp != _shownStamp)
                {
                    _shownStamp = stamp;
                    string[] now = PresetStore.List();
                    if (SameNames(now, _shown) == false)
                        Rebuild(now);
                }
            }
            if (_selected.Count != _shownSelectedCount)
            {
                _shownSelectedCount = _selected.Count;
                _deleteSelected.Label = _selected.Count == 0 ? "Delete" : "Delete (" + _selected.Count + ")";
                _deleteSelected.Interactable = _selected.Count != 0;
            }
        }

        public void Hide()
        {
            if (_popover != null)
                _popover.Close();
        }

        private static bool SameNames(string[] a, string[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase) == false)
                    return false;
            return true;
        }

        // ---------------------------------------------------------------- list

        private void Rebuild(string[] names)
        {
            _shown = names;
            _thumbnails.Retain(names); // frees thumbnails of deleted / renamed presets
            for (int i = _list.childCount - 1; i >= 0; i--)
            {
                GameObject child = _list.GetChild(i).gameObject;
                if (child == _empty.gameObject)
                    continue;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
            _selected.RemoveWhere(n => Array.IndexOf(_shown, n) == -1);
            foreach (string preset in _shown)
                BuildRow(preset);
            _empty.gameObject.SetActive(_shown.Length == 0);
            _select.Interactable = _shown.Length != 0;
            if (_shown.Length == 0 && _selectMode)
                ToggleSelectMode();
        }

        private Info InfoOf(string preset)
        {
            string path = Path.Combine(PresetStore.Folder, preset + ".xml");
            DateTime written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            Info info;
            if (_info.TryGetValue(preset, out info) && info.written == written)
                return info;
            info = new Info { written = written, modules = ModuleMask.None, summary = "" };
            try
            {
                PresetDocument d = PresetStore.Load(preset);
                if (d != null)
                {
                    info.modules = d.Modules;
                    info.summary = TransferActions.ModulesText(d.Modules) + (d.Kind == "character" ? (d.IsFemale ? "  ·  female" : "  ·  male") : "  ·  item");
                }
            }
            catch (Exception e)
            {
                info.summary = "unreadable";
                global::HSPE.Kit.Log.Write("presets page: could not read " + preset + ": " + e.Message);
            }
            _info[preset] = info;
            return info;
        }

        private void BuildRow(string preset)
        {
            Info info = InfoOf(preset);
            RectTransform row = Kit.Rect(_list, "Preset " + preset);
            Kit.Layout(row.gameObject, -1f, ThumbHeight + 10f);
            Image bg = Kit.Image(row, "Bg", Sprites.Rounded(Theme.RadiusSmall), Kit.Alpha(Theme.Surface, 0.6f), true);
            Kit.Fill(bg.rectTransform);
            Kit.IgnoreLayout(bg.gameObject);
            HorizontalLayoutGroup h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(5, 8, 0, 0);
            h.spacing = 10f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;

            KitCheckbox check = null;
            if (_selectMode)
            {
                check = KitCheckbox.Create(row, "", _selected.Contains(preset), on =>
                {
                    if (on)
                        _selected.Add(preset);
                    else
                        _selected.Remove(preset);
                });
                Kit.Layout(check.gameObject, 26f, 26f);
            }

            Texture2D thumb = _thumbnails.Get(preset);
            RectTransform frame = Kit.Rect(row, "Thumb");
            Kit.Layout(frame.gameObject, Mathf.Round(ThumbHeight * ThumbAspect), ThumbHeight);
            Image frameBg = frame.gameObject.AddComponent<Image>();
            Kit.SetSprite(frameBg, Sprites.Rounded(Theme.RadiusSmall));
            frameBg.color = Kit.Alpha(Color.black, 0.35f);
            frameBg.raycastTarget = false;
            if (thumb != null)
            {
                RawImage img = new GameObject("Image", typeof(RectTransform)).AddComponent<RawImage>();
                img.transform.SetParent(frame, false);
                img.texture = thumb;
                img.raycastTarget = false;
                Kit.Fill(img.rectTransform);
            }

            RectTransform texts = Kit.Rect(row, "Texts");
            LayoutElement tl = Kit.Layout(texts.gameObject, 0f, ThumbHeight);
            tl.flexibleWidth = 1f;
            Text name = Kit.MakeText(texts, "Name", preset, Theme.FontSize, Theme.Text, TextAnchor.LowerLeft);
            name.fontStyle = FontStyle.Bold;
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.verticalOverflow = VerticalWrapMode.Truncate;
            Kit.Fill(name.rectTransform, 0f, ThumbHeight * 0.5f, 0f, 0f);
            Text summary = Kit.MakeText(texts, "Modules", info.summary, Theme.SmallSize, Theme.TextDim, TextAnchor.UpperLeft);
            summary.horizontalOverflow = HorizontalWrapMode.Wrap;
            summary.verticalOverflow = VerticalWrapMode.Truncate;
            Kit.Fill(summary.rectTransform, 0f, 0f, 0f, ThumbHeight * 0.5f + 1f);

            if (_selectMode == false)
            {
                KitIconButton overwrite = KitIconButton.Create(row, HspeIcons.Save, 26f, false, () => Overwrite(preset, info.modules));
                KitTooltip.Attach(overwrite.gameObject, "Overwrite with the selected character (same modules)");
                KitIconButton delete = KitIconButton.Create(row, HspeIcons.Trash, 26f, true, () => Delete(preset));
                KitTooltip.Attach(delete.gameObject, "Delete");
            }

            KitHoverClick hover = KitHoverClick.On(bg.gameObject);
            hover.OnEnter = () => Tween.Tint(bg, Theme.SurfaceHover, 0.1f, Ease.OutQuad);
            hover.OnExit = () => Tween.Tint(bg, Kit.Alpha(Theme.Surface, 0.6f), 0.18f, Ease.OutQuad);
            hover.OnClick = () =>
            {
                if (_selectMode)
                {
                    if (check != null)
                        check.SetValue(!check.Value, true, true);
                    return;
                }
                AskApply(preset, info.modules, row);
            };
            KitTooltip.Attach(bg.gameObject, _selectMode ? "Click to select" : "Click to apply to the selected character(s)");
        }

        // ---------------------------------------------------------------- actions

        /// <summary>The apply prompt: the preset's modules, all ticked; untick what should stay as it is.</summary>
        private void AskApply(string preset, ModuleMask contained, RectTransform anchor)
        {
            if (contained == ModuleMask.None)
            {
                KitToast.Show("Nothing to apply", "\"" + preset + "\" holds no modules.", ToastKind.Warning, 3f);
                return;
            }
            ModuleMask[] chosen = { contained };
            _popover = KitPopover.Open(260f);
            RectTransform c = _popover.Content;
            KitLayout.Header(c, "Apply \"" + preset + "\"");
            KitLayout.Label(c, "Modules to apply:", TextRole.Dim);
            foreach (ModuleMask single in ModuleNodes.Singles)
            {
                if ((contained & single) == 0)
                    continue;
                ModuleMask m = single;
                KitCheckbox.Create(c, TransferActions.Label(m), true, on => chosen[0] = on ? chosen[0] | m : chosen[0] & ~m);
            }
            RectTransform buttons = KitLayout.Row(c, 8f);
            Kit.Layout(buttons.gameObject, -1f, 34f);
            KitLayout.Flexible(buttons);
            KitButton cancel = KitButton.Create(buttons, "Cancel", ButtonStyle.Ghost, () => _popover.Close());
            Kit.Layout(cancel.gameObject, 80f, 32f);
            KitButton apply = KitButton.Create(buttons, "Apply", ButtonStyle.Primary, () =>
            {
                _popover.Close();
                if (chosen[0] == ModuleMask.None)
                    return;
                TransferActions.ApplyPreset(preset, chosen[0]);
            });
            Kit.Layout(apply.gameObject, 80f, 32f);
            _popover.Place(anchor);
        }

        /// <summary>Save new: a name plus the modules to keep (starts from the copy / paste choice).</summary>
        private void SaveNew()
        {
            List<PoseController> sel = TransferActions.Selected();
            if (sel.Count == 0)
            {
                KitToast.Show("Nothing to save", "Select a character or item first.", ToastKind.Warning, 3f);
                return;
            }
            ModuleMask available = PoseTransfer.Available(sel[0]);
            ModuleMask[] chosen = { TransferClipboard.LastModules & available };
            string[] name = { "" };
            _popover = KitPopover.Open(280f);
            RectTransform c = _popover.Content;
            KitLayout.Header(c, "Save preset");
            KitLayout.Label(c, "From " + TransferActions.NameOf(sel[0]), TextRole.Dim);
            KitInput input = KitInput.Create(c, "Preset name", "", s => name[0] = s);
            foreach (ModuleMask single in ModuleNodes.Singles)
            {
                if ((available & single) == 0)
                    continue;
                ModuleMask m = single;
                KitCheckbox.Create(c, TransferActions.Label(m), (chosen[0] & m) != 0, on => chosen[0] = on ? chosen[0] | m : chosen[0] & ~m);
            }
            RectTransform buttons = KitLayout.Row(c, 8f);
            Kit.Layout(buttons.gameObject, -1f, 34f);
            KitLayout.Flexible(buttons);
            KitButton cancel = KitButton.Create(buttons, "Cancel", ButtonStyle.Ghost, () => _popover.Close());
            Kit.Layout(cancel.gameObject, 80f, 32f);
            VoidHandler save = () =>
            {
                string clean = PresetStore.CleanName(name[0]);
                if (clean.Length == 0)
                {
                    KitToast.Show("A preset needs a name", null, ToastKind.Warning, 2f);
                    return;
                }
                if (chosen[0] == ModuleMask.None)
                {
                    KitToast.Show("Tick at least one module", null, ToastKind.Warning, 2f);
                    return;
                }
                _popover.Close();
                if (Array.Exists(PresetStore.List(), p => string.Equals(p, clean, StringComparison.OrdinalIgnoreCase)))
                {
                    KitModal.Show("Overwrite preset?", "A preset named \"" + clean + "\" already exists. Replace it?", new[] { "Cancel", "Overwrite" }, r =>
                    {
                        if (r == 1)
                            SaveAndRefresh(clean, chosen[0]);
                    }, true);
                    return;
                }
                SaveAndRefresh(clean, chosen[0]);
            };
            input.OnSubmit = s => save();
            KitButton ok = KitButton.Create(buttons, "Save", ButtonStyle.Primary, save);
            Kit.Layout(ok.gameObject, 80f, 32f);
            _popover.Place(_saveNew.Rect);
        }

        private void Overwrite(string preset, ModuleMask modules)
        {
            List<PoseController> sel = TransferActions.Selected();
            if (sel.Count == 0)
            {
                KitToast.Show("Nothing to save", "Select a character or item first.", ToastKind.Warning, 3f);
                return;
            }
            KitModal.Show("Overwrite preset?", "Replace \"" + preset + "\" with " + TransferActions.NameOf(sel[0]) + "'s " + TransferActions.ModulesText(modules) + "?", new[] { "Cancel", "Overwrite" }, r =>
            {
                if (r == 1)
                    SaveAndRefresh(preset, modules == ModuleMask.None ? TransferClipboard.LastModules : modules);
            }, true);
        }

        private void SaveAndRefresh(string name, ModuleMask modules)
        {
            TransferActions.SavePreset(name, modules);
            Rebuild(PresetStore.List());
        }

        private void Delete(string preset)
        {
            KitModal.Show("Delete preset?", "Delete \"" + preset + "\"? This removes the file and its thumbnail.", new[] { "Cancel", "Delete" }, r =>
            {
                if (r != 1)
                    return;
                PresetStore.Delete(preset);
                Rebuild(PresetStore.List());
            }, true);
        }

        private void ToggleSelectMode()
        {
            _selectMode = !_selectMode;
            _selected.Clear();
            _select.Label = _selectMode ? "Cancel" : "Select";
            _select.Style = _selectMode ? ButtonStyle.Secondary : ButtonStyle.Ghost;
            _saveNew.gameObject.SetActive(_selectMode == false);
            _deleteSelected.gameObject.SetActive(_selectMode);
            _shownSelectedCount = -1;
            Rebuild(PresetStore.List());
        }

        private void DeleteSelected()
        {
            if (_selected.Count == 0)
                return;
            List<string> names = new List<string>(_selected);
            string what = names.Count == 1 ? "\"" + names[0] + "\"" : names.Count + " presets";
            KitModal.Show("Delete presets?", "Delete " + what + "? This removes the files.", new[] { "Cancel", "Delete" }, r =>
            {
                if (r != 1)
                    return;
                foreach (string n in names)
                    PresetStore.Delete(n);
                ToggleSelectMode();
            }, true);
        }
    }
}
