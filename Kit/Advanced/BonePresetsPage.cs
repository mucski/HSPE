using System.Collections.Generic;
using HSPE.AMModules;
using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Advanced
{
    using HSUIKit;

    /// <summary>
    /// Gru's bone presets (HSPE 2.15.0, merged in 2.16.0) as a kit page: Load / Blend / Create tabs over the same
    /// logic and state as his IMGUI pane in Joan's window (BonesEditor.Presets.Kit.cs is the bridge). The page keeps
    /// the Bones module shown, so his preview lifecycle (bonePresetsVisible) holds while it is open.
    /// </summary>
    internal sealed partial class BonePresetsPage : IAdvancedPage
    {
        // the list column is a share of the page (the window can be 680 px wide: a fixed 240 px left too little)
        private const float Split = 0.36f, Cell = 50f, RowH = 26f, StatusH = 34f;
        private static readonly string[] CellNames = { "Pos", "Rot", "Scale" };

        private RectTransform _root;
        private KitTabs _tabs;
        private KitText _status;
        private BonesEditor _bones;
        private bool _shown;
        private float _nextPrepare;
        private int _stamp = int.MinValue;
        private bool _building;

        // Load
        private string _loadSearch = "";
        private KitCheckbox _loadShowAll;
        private RectTransform _loadList, _loadRows, _loadDetail, _loadEmptyDetail;
        private KitText _loadTitle, _loadSource, _loadModeHint, _loadSummary;
        private KitSegmented _loadMode;
        private KitButton _loadButton, _deleteButton;

        // Create
        private RectTransform _saveRows;
        private KitText _saveCount, _saveCounts, _saveHint;
        private KitInput _saveName;
        private KitButton _saveButton;

        // Blend
        private string _blendSearch = "";
        private CanvasGroup _blendLeftGroup, _blendScopeGroup;
        private KitSegmented _blendChoose;
        private KitCheckbox _blendShowAll, _blendProtect, _blendRisk;
        private RectTransform _blendList, _blendRows, _endpointA, _endpointB;
        private Text _endpointAText, _endpointBText;
        private Image _endpointABg, _endpointBBg;
        private KitText _blendSummary, _blendPercent;
        private KitSlider _blendSlider;
        private CanvasGroup _blendSliderGroup;
        private KitButton _blendStart, _blendSave, _blendApply, _blendCancel, _blendA, _blendMid, _blendB;

        public RectTransform Root { get { return _root; } }

        /// <summary>The built page (one Advanced window), for the dev self-test.</summary>
        internal static BonePresetsPage Last;

        // ---------------------------------------------------------------- build

        public void Build(RectTransform parent)
        {
            _root = Kit.Rect(parent, "BonePresets");
            Kit.Fill(_root);
            Last = this;

            _tabs = KitTabs.Create(_root, new[] { "Load", "Blend", "Create" });
            Kit.Fill((RectTransform)_tabs.transform, 0f, StatusH + 4f, 0f, 0f);
            _tabs.OnChanged = i => { if (_building == false && _bones != null) { _bones.KitSetPresetPage((BonesEditor.KitPresetTab)i); Rebuild(); } };

            _status = KitText.Create(_root, "", TextRole.Dim, true);
            RectTransform sr = (RectTransform)_status.transform;
            sr.anchorMin = new Vector2(0f, 0f);
            sr.anchorMax = new Vector2(1f, 0f);
            sr.pivot = new Vector2(0.5f, 0f);
            sr.sizeDelta = new Vector2(0f, StatusH);
            sr.anchoredPosition = Vector2.zero;
            _status.Text.verticalOverflow = VerticalWrapMode.Truncate;

            BuildLoad(_tabs.Pages[0]);
            BuildBlend(_tabs.Pages[1]);
            BuildCreate(_tabs.Pages[2]);
        }

        /// <summary>Left (0 .. split) or right (split .. 1) column of a tab, 10 px apart.</summary>
        private static RectTransform Column(RectTransform parent, string name, float split, bool left)
        {
            RectTransform c = Kit.Rect(parent, name);
            c.anchorMin = new Vector2(left ? 0f : split, 0f);
            c.anchorMax = new Vector2(left ? split : 1f, 1f);
            c.offsetMin = new Vector2(left ? 0f : 5f, 0f);
            c.offsetMax = new Vector2(left ? -5f : 0f, -8f);
            VerticalLayoutGroup v = c.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6f;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return c;
        }

        private static RectTransform ListHost(RectTransform column, out RectTransform content)
        {
            RectTransform host = Kit.Rect(column, "ListHost");
            LayoutElement le = Kit.Layout(host.gameObject, -1f, 60f);
            le.flexibleHeight = 1f;
            Image bg = Kit.Image(host, "Bg", Sprites.Rounded(Theme.RadiusSmall), Theme.ListBg);
            Kit.Fill(bg.rectTransform);
            bg.raycastTarget = false;
            KitScroll scroll = KitScroll.Create(host, 4, 4, 2f, true);
            content = scroll.Content;
            return host;
        }

        private static KitButton Button(RectTransform row, string label, ButtonStyle style, VoidHandler click, float width, string tip = null)
        {
            KitButton b = KitButton.Create(row, label, style, click);
            Kit.Layout(b.gameObject, width, 30f);
            if (tip != null)
                KitTooltip.Attach(b.gameObject, tip);
            return b;
        }

        private static RectTransform Row(RectTransform parent, float height)
        {
            RectTransform row = KitLayout.Row(parent, 6f);
            Kit.Layout(row.gameObject, -1f, height);
            return row;
        }

        private static KitText Label(RectTransform parent, string text, TextRole role, float height)
        {
            KitText t = KitText.Create(parent, text, role, true);
            Kit.Layout(t.gameObject, -1f, height);
            t.Text.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        /// <summary>Bone | Pos | Rot | Scale header over a table.</summary>
        private static void TableHeader(RectTransform parent, string first)
        {
            RectTransform row = Row(parent, 20f);
            Text name = Kit.MakeText(row, "Bone", first, Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleLeft);
            Kit.Layout(name.gameObject, -1f, 20f).flexibleWidth = 1f;
            foreach (string n in CellNames)
            {
                Text t = Kit.MakeText(row, n, n, Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleCenter);
                Kit.Layout(t.gameObject, Cell, 20f);
            }
        }

        private void BuildLibraryColumn(RectTransform column, bool blend)
        {
            KitInput search = KitInput.Create(column, "Search presets...", "", s =>
            {
                if (blend) _blendSearch = s ?? ""; else _loadSearch = s ?? "";
                RebuildLibrary(blend);
            });
            Kit.Layout(search.gameObject, -1f, 32f);
            KitCheckbox showAll = KitCheckbox.Create(column, "Show all presets", false, on =>
            {
                if (_building || _bones == null) return;
                _bones.KitShowAll = on;
                Rebuild();
            });
            Kit.Layout(showAll.gameObject, -1f, 26f);
            if (blend) _blendShowAll = showAll; else _loadShowAll = showAll;
            RectTransform content;
            ListHost(column, out content);
            if (blend) _blendList = content; else _loadList = content;
            RectTransform buttons = Row(column, 32f);
            Button(buttons, "Refresh", ButtonStyle.Ghost, () => { if (_bones != null) { _bones.KitRefreshLibrary(); Rebuild(); } }, 60f, "Read the preset folder again (files changed outside Studio)").GetComponent<LayoutElement>().flexibleWidth = 1f;
            Button(buttons, "Folder", ButtonStyle.Ghost, () => { if (_bones != null) _bones.KitOpenPresetFolder(); }, 60f, "Open the preset folder (Plugins\\HSPE\\BonePresets)").GetComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private void BuildLoad(RectTransform page)
        {
            RectTransform left = Column(page, "Library", Split, true);
            BuildLibraryColumn(left, false);

            _loadDetail = Column(page, "Detail", Split, false);
            _loadTitle = Label(_loadDetail, "", TextRole.Header, 24f);
            _loadMode = KitSegmented.Create(_loadDetail, new[] { "Keep edits", "Overwrite" }, 0, i =>
            {
                if (_building || _bones == null) return;
                _bones.KitOverwriteMode = i == 1;
                _bones.KitPresetsPrepare();
                Rebuild();
            });
            KitTooltip.Attach(_loadMode.gameObject, "Keep current edits: values you already changed on this object stay. Overwrite edits: every saved value is applied.");
            _loadSource = Label(_loadDetail, "", TextRole.Small, 18f);
            _loadModeHint = Label(_loadDetail, "", TextRole.Dim, 34f);
            TableHeader(_loadDetail, "Bone");
            ListHost(_loadDetail, out _loadRows);
            _loadSummary = Label(_loadDetail, "", TextRole.Small, 34f);
            RectTransform actions = Row(_loadDetail, 32f);
            _loadButton = Button(actions, "Load selected", ButtonStyle.Primary, () => { if (_bones != null) { _bones.KitLoadSelected(); Rebuild(); } }, 120f, "Apply the preset's values as listed above");
            _loadButton.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _deleteButton = Button(actions, "Delete...", ButtonStyle.Danger, AskDelete, 86f);

            _loadEmptyDetail = Kit.Rect(page, "NoSelection");
            _loadEmptyDetail.anchorMin = new Vector2(Split, 0f);
            _loadEmptyDetail.anchorMax = new Vector2(1f, 1f);
            _loadEmptyDetail.offsetMin = new Vector2(5f, 0f);
            _loadEmptyDetail.offsetMax = new Vector2(0f, -8f);
            KitText hint = KitText.Create(_loadEmptyDetail, "Select a preset to see its bones and loading options.\n\nBones change only when you click Load selected.", TextRole.Dim, true);
            Kit.Fill((RectTransform)hint.transform, 10f, 0f, 10f, 10f);
        }

        private void BuildCreate(RectTransform page)
        {
            RectTransform left = Kit.Rect(page, "Bones");
            left.anchorMin = new Vector2(0f, 0f);
            left.anchorMax = new Vector2(0.62f, 1f);
            left.offsetMin = Vector2.zero;
            left.offsetMax = new Vector2(-5f, -8f);
            VerticalLayoutGroup v = left.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6f;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            KitInput search = KitInput.Create(left, "Search modified bones...", "", s => { if (_bones != null) { _bones.KitBoneSearch = s; Rebuild(); } });
            Kit.Layout(search.gameObject, -1f, 32f);
            KitTooltip.Attach(search.gameObject, "Matches a bone's own name or alias, not its parents");
            RectTransform bar = Row(left, 30f);
            _saveCount = KitText.Create(bar, "", TextRole.Body, false);
            Kit.Layout(_saveCount.gameObject, -1f, 26f).flexibleWidth = 1f;
            Button(bar, "All", ButtonStyle.Ghost, () => { if (_bones != null) { _bones.KitSelectShownForSave(true); Rebuild(); } }, 60f, "Tick every value shown by the search (hidden ones stay as they are)");
            Button(bar, "None", ButtonStyle.Ghost, () => { if (_bones != null) { _bones.KitSelectShownForSave(false); Rebuild(); } }, 60f, "Untick every value shown by the search (hidden ones stay as they are)");
            TableHeader(left, "Bone");
            ListHost(left, out _saveRows);

            RectTransform right = Kit.Rect(page, "Form");
            right.anchorMin = new Vector2(0.62f, 0f);
            right.anchorMax = new Vector2(1f, 1f);
            right.offsetMin = new Vector2(5f, 0f);
            right.offsetMax = new Vector2(0f, -8f);
            VerticalLayoutGroup rv = right.gameObject.AddComponent<VerticalLayoutGroup>();
            rv.spacing = 8f;
            rv.childForceExpandWidth = true;
            rv.childForceExpandHeight = false;
            Label(right, "Preset name", TextRole.Dim, 18f);
            _saveName = KitInput.Create(right, "Name", "", s => { if (_bones != null && _building == false) { _bones.KitPresetName = s; RefreshCreateForm(); } });
            Kit.Layout(_saveName.gameObject, -1f, 32f);
            _saveName.OnSubmit = s => Save();
            _saveCounts = Label(right, "", TextRole.Body, 20f);
            Label(right, "Tick the changed values to save. Ticks outside the search stay included.", TextRole.Dim, 72f);
            KitLayout.Flexible(right);
            _saveHint = Label(right, "", TextRole.Small, 36f);
            _saveButton = KitButton.Create(right, "Save preset", ButtonStyle.Primary, Save);
            Kit.Layout(_saveButton.gameObject, -1f, 32f);
        }

        private void BuildBlend(RectTransform page)
        {
            RectTransform left = Column(page, "Sources", Split, true);
            _blendLeftGroup = left.gameObject.AddComponent<CanvasGroup>();
            _blendChoose = KitSegmented.Create(left, new[] { "Choose A", "Choose B" }, 1, i =>
            {
                if (_building || _bones == null) return;
                _bones.KitChooseA = i == 0;
                Rebuild();
            });
            KitTooltip.Attach(_blendChoose.gameObject, "Which end the list below sets");
            KitButton current = KitButton.Create(left, "Use current as A", ButtonStyle.Secondary, () => { if (_bones != null) { _bones.KitChooseA = true; _bones.KitUseCurrentForA(); Rebuild(); } });
            Kit.Layout(current.gameObject, -1f, 30f);
            KitTooltip.Attach(current.gameObject, "A = the object as it is, captured once when Start preview is clicked");
            BuildLibraryColumn(left, true);

            RectTransform right = Column(page, "Blend", Split, false);
            _endpointA = Endpoint(right, true, out _endpointABg, out _endpointAText);
            _endpointB = Endpoint(right, false, out _endpointBBg, out _endpointBText);

            RectTransform scope = Row(right, 28f);
            _blendScopeGroup = scope.gameObject.AddComponent<CanvasGroup>();
            _blendProtect = KitCheckbox.Create(scope, "Preserve dirty", false, on => { if (_building == false && _bones != null) { _bones.KitProtectEdits = on; _bones.KitPresetsPrepare(); Rebuild(); } });
            Kit.Layout(_blendProtect.gameObject, 150f, 26f);
            KitTooltip.Attach(_blendProtect.gameObject, "Keep position, rotation or scale values already changed by HSPE before the preview. Leave off to blend your current edits.");
            KitLayout.Flexible(scope);
            Button(scope, "All", ButtonStyle.Ghost, () => { if (_bones != null) { _bones.KitBlendScope(true); _bones.KitPresetsPrepare(); Rebuild(); } }, 56f);
            Button(scope, "None", ButtonStyle.Ghost, () => { if (_bones != null) { _bones.KitBlendScope(false); _bones.KitPresetsPrepare(); Rebuild(); } }, 56f);

            _blendRisk = KitCheckbox.Create(right, "Allow scale through zero (collapse / invert)", false, on => { if (_building == false && _bones != null) { _bones.KitAcceptScaleRisk = on; Rebuild(); } });
            Kit.Layout(_blendRisk.gameObject, -1f, 26f);
            _blendRisk.gameObject.SetActive(false);

            TableHeader(right, "Bone");
            ListHost(right, out _blendRows);
            _blendSummary = Label(right, "", TextRole.Small, 34f);

            RectTransform sliderRow = Row(right, 32f);
            _blendSliderGroup = sliderRow.gameObject.AddComponent<CanvasGroup>();
            // compact (track only): the labelled slider is a 46 px full-width control
            _blendSlider = KitSlider.Create(sliderRow, null, -1f, 1f, -1f, false, w => { if (_building == false && _bones != null) { _bones.KitSetBlendWeight(w); ShowPercent(); } });
            Kit.Layout(_blendSlider.transform.parent.gameObject, -1f, 28f).flexibleWidth = 1f; // the component sits on its "Hit" child
            KitTooltip.Attach(_blendSlider.gameObject, "-1 = A, 0 = halfway, +1 = B (a live preview; Apply keeps it)");
            _blendA = Button(sliderRow, "A", ButtonStyle.Ghost, () => SetWeight(-1f), 32f, "-1: entirely A");
            _blendMid = Button(sliderRow, "50/50", ButtonStyle.Ghost, () => SetWeight(0f), 52f, "0: halfway between A and B");
            _blendB = Button(sliderRow, "B", ButtonStyle.Ghost, () => SetWeight(1f), 32f, "+1: entirely B");
            _blendPercent = KitText.Create(sliderRow, "0% B", TextRole.Body, false);
            Kit.Layout(_blendPercent.gameObject, 50f, 26f);

            RectTransform actions = Row(right, 32f);
            _blendStart = Button(actions, "Start preview", ButtonStyle.Primary, () => { if (_bones != null) { _bones.KitStartBlend(); Rebuild(); } }, 110f, "Captures the current values once, then the slider previews the blend");
            _blendStart.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _blendSave = Button(actions, "Save result...", ButtonStyle.Ghost, () => { if (_bones != null) { _bones.KitSaveBlendResult(); SelectTab(BonesEditor.KitPresetTab.Create); } }, 110f, "Open Create with only the blended values ticked");
            _blendApply = Button(actions, "Apply blend", ButtonStyle.Positive, () => { if (_bones != null) { _bones.KitApplyBlend(); Rebuild(); } }, 110f, "Keep the previewed values");
            _blendApply.GetComponent<LayoutElement>().flexibleWidth = 1f;
            _blendCancel = Button(actions, "Cancel", ButtonStyle.Secondary, () => { if (_bones != null) { _bones.KitCancelBlend(); Rebuild(); } }, 80f, "Restore the values from before the preview");
        }

        private RectTransform Endpoint(RectTransform parent, bool a, out Image bg, out Text text)
        {
            RectTransform row = Kit.Rect(parent, a ? "EndpointA" : "EndpointB");
            Kit.Layout(row.gameObject, -1f, 30f);
            Image b = Kit.Image(row, "Bg", Sprites.Rounded(Theme.RadiusSmall), Kit.Alpha(Theme.Surface, 0.6f), true);
            Kit.Fill(b.rectTransform);
            Text t = Kit.MakeText(row, "Label", "", Theme.FontSize, Theme.Text, TextAnchor.MiddleLeft);
            Kit.Fill(t.rectTransform, 10f, 0f, 10f, 0f);
            KitHoverClick hc = KitHoverClick.On(b.gameObject);
            hc.OnClick = () => { if (_bones != null && _bones.KitBlendActive == false) { _bones.KitChooseA = a; Rebuild(); } };
            bg = b;
            text = t;
            return row;
        }

        // ---------------------------------------------------------------- page

        public void Show(PoseController target)
        {
            Bind(target);
        }

        public void Hide()
        {
            if (_bones != null)
                _bones.KitPresetsShown(false);
            _shown = false;
        }

        private void Bind(PoseController target)
        {
            BonesEditor b = target != null ? target._bonesEditor : null;
            if (b != _bones)
            {
                if (_bones != null)
                    _bones.KitPresetsShown(false);
                _bones = b;
                _shown = false;
            }
            if (_bones != null && _shown == false)
            {
                _bones.KitPresetsShown(true);
                _shown = true;
                SelectTab(_bones.KitPresetPage);
            }
        }

        public void Tick(PoseController target)
        {
            Bind(target);
            if (_bones == null)
                return;
            if (Time.unscaledTime >= _nextPrepare)
            {
                _nextPrepare = Time.unscaledTime + 0.2f;
                _bones.KitPresetsPrepare();
                int stamp = _bones.KitPresetsStamp();
                if (stamp != _stamp)
                    Rebuild();
            }
            string status = _bones.KitPresetStatus;
            if (_status.Value != status)
                _status.Value = status;
            Color c = _bones.KitPresetStatusError ? Theme.Error : Theme.TextDim;
            if (_status.Text.color != c)
                _status.Text.color = c;
        }

        private void SelectTab(BonesEditor.KitPresetTab tab)
        {
            _building = true;
            if (_tabs.Active != (int)tab)
                _tabs.Select((int)tab, false);
            _building = false;
            if (_bones != null && _bones.KitPresetPage != tab)
                _bones.KitSetPresetPage(tab);
            Rebuild();
        }

        private void SetWeight(float w)
        {
            if (_bones == null || _bones.KitBlendActive == false)
                return;
            _bones.KitSetBlendWeight(w);
            _building = true;
            _blendSlider.SetValue(w, true, false);
            _building = false;
            ShowPercent();
        }

        private void ShowPercent()
        {
            if (_bones != null)
                _blendPercent.Value = ((_bones.KitBlendWeight + 1f) * 50f).ToString("0") + "% B";
        }

        // ---------------------------------------------------------------- rebuild

        private void Rebuild()
        {
            if (_bones == null)
                return;
            _stamp = _bones.KitPresetsStamp();
            _building = true;
            try
            {
                int tab = (int)_bones.KitPresetPage;
                if (_tabs.Active != tab)
                    _tabs.Select(tab, false);
                if (tab == 0) RebuildLoad();
                else if (tab == 1) RebuildBlend();
                else RebuildCreate();
            }
            finally
            {
                _building = false;
            }
        }

        private static void Clear(RectTransform content)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
        }

        private void RebuildLibrary(bool blend)
        {
            if (_bones == null)
                return;
            RectTransform list = blend ? _blendList : _loadList;
            KitCheckbox showAll = blend ? _blendShowAll : _loadShowAll;
            string search = blend ? _blendSearch : _loadSearch;
            Clear(list);
            bool was = _building;
            _building = true;
            if (showAll.Value != _bones.KitShowAll)
                showAll.SetValue(_bones.KitShowAll, false, false);
            _building = was;
            KitTooltip.Attach(showAll.gameObject, _bones.KitShowAllHint);
            List<BonesEditor.KitPresetFile> files = _bones.KitLibrary(search, blend);
            if (files.Count == 0)
            {
                KitText empty = KitText.Create(list, _bones.KitEmptyLibraryHint(search), TextRole.Dim, true);
                Kit.Layout(empty.gameObject, -1f, 60f);
            }
            foreach (BonesEditor.KitPresetFile f in files)
                LibraryRow(list, f, blend);
        }

        private void LibraryRow(RectTransform list, BonesEditor.KitPresetFile f, bool blend)
        {
            RectTransform row = Kit.Rect(list, "Preset " + f.Name);
            Kit.Layout(row.gameObject, -1f, 28f);
            Color normal = f.Selected ? Kit.Alpha(Theme.Accent, 0.35f) : Kit.Alpha(Theme.Surface, 0f);
            Image bg = Kit.Image(row, "Bg", Sprites.Rounded(Theme.RadiusSmall), normal, true);
            Kit.Fill(bg.rectTransform);
            Color textColor = f.Error ? Theme.Error : f.OtherSource ? Theme.Warning : Theme.Text;
            Text t = Kit.MakeText(row, "Name", f.Name, Theme.FontSize, textColor, TextAnchor.MiddleLeft);
            Kit.Fill(t.rectTransform, 8f, 0f, 6f, 0f);
            KitEllipsis.Set(t, f.Name);
            KitTooltip.Attach(bg.gameObject, f.Hint);
            if (blend && f.Error)
                return; // his Blend list disables unreadable files
            KitHoverClick hc = KitHoverClick.On(bg.gameObject);
            hc.OnEnter = () => Tween.Tint(bg, f.Selected ? Kit.Alpha(Theme.Accent, 0.45f) : Theme.SurfaceHover, 0.1f, Ease.OutQuad);
            hc.OnExit = () => Tween.Tint(bg, normal, 0.18f, Ease.OutQuad);
            string path = f.Path;
            hc.OnClick = () =>
            {
                if (_bones == null) return;
                if (blend) _bones.KitSetBlendSource(path);
                else _bones.KitSelectPreset(path);
                Rebuild();
            };
        }

        /// <summary>A Bone | Pos | Rot | Scale row; cells are filled by the caller.</summary>
        private static RectTransform TableRow(RectTransform list, string name, string tip, Color color, out RectTransform[] cells, out RectTransform nameSlot)
        {
            RectTransform row = KitLayout.Row(list, 0f);
            Kit.Layout(row.gameObject, -1f, RowH);
            nameSlot = Kit.Rect(row, "Name");
            Kit.Layout(nameSlot.gameObject, -1f, RowH).flexibleWidth = 1f;
            if (name.Length != 0) // empty: the caller puts a checkbox there
            {
                Text t = Kit.MakeText(nameSlot, "Text", name, Theme.FontSize, color, TextAnchor.MiddleLeft);
                Kit.Fill(t.rectTransform, 4f, 0f, 4f, 0f);
                KitEllipsis.Set(t, name);
                if (tip != null)
                {
                    t.raycastTarget = true;
                    KitTooltip.Attach(t.gameObject, tip);
                }
            }
            cells = new RectTransform[3];
            for (int i = 0; i < 3; i++)
            {
                cells[i] = Kit.Rect(row, CellNames[i]);
                Kit.Layout(cells[i].gameObject, Cell, RowH);
            }
            return row;
        }

        private static void OneLine(KitCheckbox box, string name)
        {
            Transform label = box.transform.Find("Label");
            Text t = label != null ? label.GetComponent<Text>() : null;
            if (t != null)
                KitEllipsis.Set(t, name);
        }

        private static void CellText(RectTransform cell, string text, Color color)
        {
            Text t = Kit.MakeText(cell, "Text", text, Theme.SmallSize, color, TextAnchor.MiddleCenter);
            Kit.Fill(t.rectTransform);
        }

        private static KitCheckbox CellCheck(RectTransform cell, bool value, BoolHandler changed, string tip)
        {
            KitCheckbox c = KitCheckbox.Create(cell, "", value, changed);
            Transform label = c.transform.Find("Label");
            if (label != null)
                label.gameObject.SetActive(false); // no text: the empty label stuck out of the 24 px cell
            RectTransform r = (RectTransform)c.transform;
            Kit.Anchor(r, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
            if (tip != null)
                KitTooltip.Attach(c.gameObject, tip);
            return c;
        }

        private void RebuildLoad()
        {
            RebuildLibrary(false);
            string name = _bones.KitSelectedPresetName;
            _loadDetail.gameObject.SetActive(name != null);
            _loadEmptyDetail.gameObject.SetActive(name == null);
            if (name == null)
                return;
            _loadTitle.Value = name;
            int mode = _bones.KitOverwriteMode ? 1 : 0;
            if (_loadMode.Selected != mode)
                _loadMode.Selected = mode;
            string source;
            int kind = _bones.KitSelectedSource(out source);
            KitEllipsis.Set(_loadSource.Text, source);
            _loadSource.Text.color = kind == 2 ? Theme.Error : kind == 1 ? Theme.Warning : Theme.TextDim;
            KitTooltip.Attach(_loadSource.gameObject, source + ". Bone paths are checked separately; no remapping is attempted.");
            _loadModeHint.Value = _bones.KitLoadModeHint;
            Clear(_loadRows);
            foreach (BonesEditor.KitLoadRow r in _bones.KitLoadRows())
            {
                RectTransform[] cells;
                RectTransform slot;
                TableRow(_loadRows, r.Name, r.Path, Theme.Text, out cells, out slot);
                OutcomeCell(cells[0], r.Position);
                OutcomeCell(cells[1], r.Rotation);
                OutcomeCell(cells[2], r.Scale);
            }
            _loadSummary.Value = _bones.KitLoadSummary;
            _loadButton.Interactable = _bones.KitCanLoad;
            ButtonStyle style = _bones.KitLoadReplaces ? ButtonStyle.Warm : ButtonStyle.Primary;
            if (_loadButton.Style != style)
                _loadButton.Style = style;
        }

        private static void OutcomeCell(RectTransform cell, BonesEditor.KitOutcome o)
        {
            switch (o)
            {
                case BonesEditor.KitOutcome.Apply: CellText(cell, "Apply", Theme.Success); break;
                case BonesEditor.KitOutcome.Keep: CellText(cell, "Keep", Theme.Warning); break;
                case BonesEditor.KitOutcome.Replace: CellText(cell, "Replace", Theme.Warning); break;
                case BonesEditor.KitOutcome.Missing: CellText(cell, "Missing", Theme.Error); break;
                default: CellText(cell, "-", Theme.TextDim); break;
            }
        }

        private void RebuildCreate()
        {
            _saveCount.Value = "Modified bones (" + _bones.KitModifiedBoneCount + ")";
            if (_saveName.Text != _bones.KitPresetName)
                _saveName.Text = _bones.KitPresetName;
            Clear(_saveRows);
            List<BonesEditor.KitSaveRow> rows = _bones.KitSaveRows();
            if (rows.Count == 0)
            {
                KitText empty = KitText.Create(_saveRows, _bones.KitModifiedBoneCount == 0 ? "No modified bones. Edit bones first, then come back here." : "No bones match your search.", TextRole.Dim, true);
                Kit.Layout(empty.gameObject, -1f, 50f);
            }
            foreach (BonesEditor.KitSaveRow r in rows)
            {
                RectTransform[] cells;
                RectTransform slot;
                TableRow(_saveRows, "", r.Path, Theme.Text, out cells, out slot);
                GameObject bone = r.Bone;
                KitCheckbox all = KitCheckbox.Create(slot, r.Name, r.All, on => { if (_building == false && _bones != null) { _bones.KitSetSaveChannel(bone, 0, on); Rebuild(); } });
                Kit.Fill((RectTransform)all.transform, 2f, 0f, 2f, 0f);
                OneLine(all, r.Name);
                KitTooltip.Attach(all.gameObject, r.Path);
                SaveCell(cells[0], bone, 1, r.HasPosition, r.Position, "Position");
                SaveCell(cells[1], bone, 2, r.HasRotation, r.Rotation, "Rotation");
                SaveCell(cells[2], bone, 3, r.HasScale, r.Scale, "Scale");
            }
            RefreshCreateForm();
        }

        private void SaveCell(RectTransform cell, GameObject bone, int channel, bool has, bool on, string tip)
        {
            if (has == false)
            {
                CellText(cell, "-", Theme.TextDim);
                return;
            }
            CellCheck(cell, on, v => { if (_building == false && _bones != null) { _bones.KitSetSaveChannel(bone, channel, v); Rebuild(); } }, tip);
        }

        private void RefreshCreateForm()
        {
            if (_bones == null)
                return;
            int bones, channels;
            _bones.KitSaveCounts(out bones, out channels);
            _saveCounts.Value = bones + " bones / " + channels + " values selected";
            bool exists;
            string hint;
            int state = _bones.KitSaveState(out exists, out hint);
            _saveHint.Value = hint;
            _saveHint.Text.color = state == 1 ? Theme.Success : exists && state == 0 ? Theme.Warning : Theme.TextDim;
            _saveButton.Label = state == 1 ? "Saved" : exists ? "Replace file..." : "Save preset";
            _saveButton.Interactable = state == 0;
            ButtonStyle style = exists && state == 0 ? ButtonStyle.Warm : ButtonStyle.Primary;
            if (_saveButton.Style != style)
                _saveButton.Style = style;
        }

        private void RebuildBlend()
        {
            bool active = _bones.KitBlendActive;
            int choose = _bones.KitChooseA ? 0 : 1;
            if (_blendChoose.Selected != choose)
                _blendChoose.Selected = choose;
            _blendLeftGroup.interactable = !active;
            _blendLeftGroup.alpha = active ? 0.55f : 1f;
            RebuildLibrary(true);

            PaintEndpoint(true, _endpointABg, _endpointAText);
            PaintEndpoint(false, _endpointBBg, _endpointBText);

            _blendScopeGroup.interactable = !active;
            if (_blendProtect.Value != _bones.KitProtectEdits)
                _blendProtect.SetValue(_bones.KitProtectEdits, false, false);
            int risky = _bones.KitRiskyScales;
            _blendRisk.gameObject.SetActive(risky > 0);
            if (risky > 0)
            {
                if (_blendRisk.Value != _bones.KitAcceptScaleRisk)
                    _blendRisk.SetValue(_bones.KitAcceptScaleRisk, false, false);
                KitTooltip.Attach(_blendRisk.gameObject, risky + " scale values reach or cross zero. Values are not clamped or silently changed.");
            }

            Clear(_blendRows);
            List<BonesEditor.KitBlendRow> rows = _bones.KitBlendRows();
            if (rows.Count == 0)
            {
                KitText empty = KitText.Create(_blendRows, "Choose B (and optionally A) in the list to review the blend.", TextRole.Dim, true);
                Kit.Layout(empty.gameObject, -1f, 50f);
            }
            foreach (BonesEditor.KitBlendRow r in rows)
            {
                RectTransform[] cells;
                RectTransform slot;
                TableRow(_blendRows, "", null, Theme.Text, out cells, out slot);
                int index = r.Index;
                if (r.Missing || active)
                {
                    Text t = Kit.MakeText(slot, "Text", r.Name, Theme.FontSize, r.Missing ? Theme.Error : Theme.Text, TextAnchor.MiddleLeft);
                    Kit.Fill(t.rectTransform, 4f, 0f, 4f, 0f);
                    KitEllipsis.Set(t, r.Name);
                    t.raycastTarget = true;
                    KitTooltip.Attach(t.gameObject, r.Tip);
                }
                else
                {
                    KitCheckbox all = KitCheckbox.Create(slot, r.Name, r.All, on => { if (_building == false && _bones != null) { _bones.KitSetBlendChannel(index, 0, on); _bones.KitPresetsPrepare(); Rebuild(); } });
                    Kit.Fill((RectTransform)all.transform, 2f, 0f, 2f, 0f);
                    OneLine(all, r.Name);
                    KitTooltip.Attach(all.gameObject, r.Tip);
                }
                BlendCell(cells[0], index, 1, r.Position, r.OnPosition, r.TipPosition, active);
                BlendCell(cells[1], index, 2, r.Rotation, r.OnRotation, r.TipRotation, active);
                BlendCell(cells[2], index, 3, r.Scale, r.OnScale, r.TipScale, active);
            }
            _blendSummary.Value = _bones.KitBlendSummary;

            _blendSliderGroup.interactable = active;
            _blendSliderGroup.alpha = active ? 1f : 0.5f;
            _blendSlider.SetValue(_bones.KitBlendWeight, false, false);
            ShowPercent();
            _blendStart.gameObject.SetActive(!active);
            _blendSave.gameObject.SetActive(!active);
            _blendApply.gameObject.SetActive(active);
            _blendCancel.gameObject.SetActive(active);
            _blendStart.Interactable = _bones.KitCanStartBlend;
            _blendSave.Interactable = _bones.KitCanSaveBlendResult;
        }

        private void BlendCell(RectTransform cell, int index, int channel, BonesEditor.KitBlendCell kind, bool on, string tip, bool active)
        {
            switch (kind)
            {
                case BonesEditor.KitBlendCell.Missing: CellText(cell, "Missing", Theme.Error); return;
                case BonesEditor.KitBlendCell.Keep: CellText(cell, "Keep", Theme.Warning); return;
                case BonesEditor.KitBlendCell.None: CellText(cell, "-", Theme.TextDim); return;
            }
            KitCheckbox c = CellCheck(cell, on, v => { if (_building == false && _bones != null) { _bones.KitSetBlendChannel(index, channel, v); _bones.KitPresetsPrepare(); Rebuild(); } }, tip);
            if (active)
            {
                CanvasGroup g = c.gameObject.AddComponent<CanvasGroup>();
                g.interactable = false; // reading the scope stays possible during the preview, changing it does not
            }
        }

        private void PaintEndpoint(bool a, Image bg, Text text)
        {
            string hint;
            bool other;
            KitEllipsis.Set(text, _bones.KitEndpoint(a, out hint, out other));
            text.color = other ? Theme.Warning : Theme.Text;
            bool choosing = _bones.KitChooseA == a && _bones.KitBlendActive == false;
            bg.color = choosing ? Kit.Alpha(Theme.Accent, 0.3f) : Kit.Alpha(Theme.Surface, 0.6f);
            KitTooltip.Attach(bg.gameObject, hint);
        }

        // ---------------------------------------------------------------- actions

        private void AskDelete()
        {
            if (_bones == null || _bones.KitSelectedPresetName == null)
                return;
            string name = _bones.KitSelectedPresetName;
            KitModal.Show("Delete bone preset?", "Delete \"" + name + "\"? This removes the file.", new[] { "Cancel", "Delete file" }, r =>
            {
                if (r == 1 && _bones != null && _bones.KitSelectedPresetName == name)
                {
                    _bones.KitDeleteSelected();
                    Rebuild();
                }
            }, true);
        }

        private void Save()
        {
            if (_bones == null)
                return;
            bool exists;
            string hint;
            if (_bones.KitSaveState(out exists, out hint) != 0)
                return;
            if (exists)
            {
                string name = _bones.KitSanitizedPresetName;
                KitModal.Show("Replace bone preset?", "A preset named \"" + name + "\" already exists. Replace that file?", new[] { "Cancel", "Replace file" }, r =>
                {
                    if (r == 1 && _bones != null)
                    {
                        _bones.KitSavePreset();
                        Rebuild();
                    }
                }, true);
                return;
            }
            _bones.KitSavePreset();
            Rebuild();
        }
    }
}
