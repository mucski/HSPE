using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using HSPE.AMModules;
using HSPE.Kit.Small;
using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Advanced
{
    using HSUIKit;

    /// <summary>
    /// The new Advanced window: resizable, sidebar (Joan's modules with changed counts, then Presets) | page. Shown and
    /// hidden with Joan's own flag (PoseController._drawAdvancedMode, toggled by ToggleAdvancedMode) so every existing
    /// way of opening Advanced mode keeps working; the sidebar calls Joan's EnableModule so gizmos behave the same.
    /// Modules without a new page offer Joan's classic window ("open in classic window").
    /// </summary>
    internal sealed class KitAdvancedWindow : MonoBehaviour
    {
        private const float SidebarWidth = 184f, RowH = 34f;

        private sealed class SideRow
        {
            public AdvancedModeModule module; // null = Presets
            public RectTransform rect;
            public Image bg, bar;
            public Text label, count;
            public bool hover, selected, drawn, shownHover, shownSelected;
            public int shownCount = int.MinValue;
        }

        private MainWindow _main;
        private KitWindow _window;
        private RectTransform _sidebarList, _pageHost, _disabledPage;
        private KitSwitch _activeSwitch;
        private readonly List<SideRow> _rows = new List<SideRow>();
        private PresetsPage _presetsPage;
        private BonesPage _bonesPage;
        private DynamicBonesPage _dynamicBonesPage;
        private BoobsPage _boobsPage;
        private CollidersPage _collidersPage;
        private BlendShapesPage _blendShapesPage;
        private IKPage _ikPage;
        private IAdvancedPage _page;
        private SideRow _selectedRow;
        private PoseController _shownTarget;
        private bool _syncing;
        private float _nextCounts;
        private bool _shownEnabled = true;
        private int _moduleMask = -1;

        public static KitAdvancedWindow TryCreate(MainWindow main)
        {
            KitWindow window = null;
            try
            {
                window = KitWindow.Create("Advanced", 780f, 540f).Resizable(new Vector2(680f, 420f), new Vector2(4000f, 4000f)) /* narrower pushed the Bones drawer's Reset out */.Remember("HSPE.Advanced");
                KitAdvancedWindow w = window.gameObject.AddComponent<KitAdvancedWindow>();
                w._main = main;
                w._window = window;
                w.Build();
                window.VisibilityChanged += w.OnVisibilityChanged;
                return w;
            }
            catch (Exception e)
            {
                Debug.LogError("HSPE: the new Advanced window could not start, using the classic one.\n" + e);
                global::HSPE.Kit.Log.Write("kit Advanced window failed, falling back to Joan's: " + e);
                if (window != null)
                    Destroy(window.gameObject);
                return null;
            }
        }

        // ---------------------------------------------------------------- build

        private void Build()
        {
            RectTransform content = _window.Content;

            Image side = Kit.Image(content, "Sidebar", Sprites.Rounded(Theme.RadiusSmall), Kit.Alpha(Theme.Surface, 0.55f));
            KitThemeColor.Bind(side, () => Kit.Alpha(Theme.Surface, 0.55f));
            RectTransform sideRect = side.rectTransform;
            sideRect.anchorMin = new Vector2(0f, 0f);
            sideRect.anchorMax = new Vector2(0f, 1f);
            sideRect.pivot = new Vector2(0f, 0.5f);
            sideRect.offsetMin = new Vector2(10f, 10f);
            sideRect.offsetMax = new Vector2(10f + SidebarWidth, -10f);

            _sidebarList = Kit.Rect(sideRect, "List");
            Kit.Fill(_sidebarList, 6f, 50f, 6f, 6f);
            VerticalLayoutGroup v = _sidebarList.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 2f;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            RectTransform bottom = Kit.Rect(sideRect, "Bottom");
            bottom.anchorMin = new Vector2(0f, 0f);
            bottom.anchorMax = new Vector2(1f, 0f);
            bottom.pivot = new Vector2(0.5f, 0f);
            bottom.sizeDelta = new Vector2(-20f, 36f);
            bottom.anchoredPosition = new Vector2(0f, 8f);
            _activeSwitch = KitSwitch.Create(bottom, "Active", true, OnActiveChanged);
            Kit.Fill((RectTransform)_activeSwitch.transform);
            KitTooltip.Attach(_activeSwitch.gameObject, "Advanced mode on this object. Off saves a little per-frame work (Joan's option).");

            _pageHost = Kit.Rect(content, "Page");
            Kit.Fill(_pageHost, SidebarWidth + 22f, 12f, 12f, 12f);

            _presetsPage = new PresetsPage();
            _presetsPage.Build(_pageHost);
            _presetsPage.Root.gameObject.SetActive(false);
            _bonesPage = new BonesPage();
            _bonesPage.Build(_pageHost);
            _bonesPage.Root.gameObject.SetActive(false);
            _dynamicBonesPage = new DynamicBonesPage();
            _dynamicBonesPage.Build(_pageHost);
            _dynamicBonesPage.Root.gameObject.SetActive(false);
            _boobsPage = new BoobsPage();
            _boobsPage.Build(_pageHost);
            _boobsPage.Root.gameObject.SetActive(false);
            _collidersPage = new CollidersPage();
            _collidersPage.Build(_pageHost);
            _collidersPage.Root.gameObject.SetActive(false);
            _blendShapesPage = new BlendShapesPage();
            _blendShapesPage.Build(_pageHost);
            _blendShapesPage.Root.gameObject.SetActive(false);
            _ikPage = new IKPage();
            _ikPage.Build(_pageHost);
            _ikPage.Root.gameObject.SetActive(false);

            _disabledPage = Kit.Rect(_pageHost, "Disabled");
            Kit.Fill(_disabledPage);
            RectTransform box = Kit.Rect(_disabledPage, "Box");
            Kit.Anchor(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(360f, 120f));
            VerticalLayoutGroup bv = box.gameObject.AddComponent<VerticalLayoutGroup>();
            bv.spacing = 10f;
            bv.childForceExpandWidth = true;
            bv.childForceExpandHeight = false;
            KitText t = KitText.Create(box, "Advanced mode is off for this object, to save a little work every frame.", TextRole.Dim, true);
            t.Text.alignment = TextAnchor.MiddleCenter;
            RectTransform row = KitLayout.Row(box, 0f, TextAnchor.MiddleCenter);
            Kit.Layout(row.gameObject, -1f, 34f);
            KitButton enable = KitButton.Create(row, "Turn it on", ButtonStyle.Primary, () => OnActiveChanged(true));
            Kit.Layout(enable.gameObject, 140f, 34f);
            _disabledPage.gameObject.SetActive(false);
        }

        private void BuildSidebar(PoseController target)
        {
            for (int i = _sidebarList.childCount - 1; i >= 0; i--)
            {
                GameObject child = _sidebarList.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            _rows.Clear();
            _selectedRow = null;
            _moduleMask = target != null ? ModuleMask(target) : -1;
            if (target == null)
                return;
            foreach (AdvancedModeModule module in target.kitModules)
                if (module.shouldDisplay)
                    AddRow(module, module.displayName);
            // divider with room above / below, inset from the edges (a line glued under the last row looked like an underline)
            RectTransform gap = Kit.Rect(_sidebarList, "Divider");
            Kit.Layout(gap.gameObject, -1f, 17f);
            Image sep = Kit.Image(gap, "Line", null, Theme.Border); // the theme's own divider colour (Kit.Alpha set 0.7 absolute = near white)
            sep.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            sep.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            sep.rectTransform.sizeDelta = new Vector2(-20f, 1f);
            sep.rectTransform.anchoredPosition = Vector2.zero;
            AddRow(null, "Presets");
            if (target is CharaPoseController)
                AddActionRow("Face...","Opens the Face window (expressions, mood pad, vowels)", global::HSPE.Kit.Face.FaceWindow.Toggle);
        }

        /// <summary>A sidebar row that runs an action instead of showing a page (Face opens its own window).</summary>
        private void AddActionRow(string label, string tip, VoidHandler action)
        {
            RectTransform rect = Kit.Rect(_sidebarList, "Row " + label);
            Kit.Layout(rect.gameObject, -1f, RowH);
            Image bg = Kit.Image(rect, "Bg", Sprites.Rounded(Theme.RadiusSmall), Kit.Alpha(Theme.SurfaceHover, 0f), true);
            Kit.Fill(bg.rectTransform);
            Text text = Kit.MakeText(rect, "Label", label, Theme.FontSize, Theme.Text, TextAnchor.MiddleLeft);
            Kit.Fill(text.rectTransform, 12f, 0f, 10f, 0f);
            KitHoverClick hc = KitHoverClick.On(bg.gameObject);
            hc.OnEnter = delegate { Tween.Tint(bg, Theme.SurfaceHover, 0.1f, Ease.OutQuad); };
            hc.OnExit = delegate { Tween.Tint(bg, Kit.Alpha(Theme.SurfaceHover, 0f), 0.18f, Ease.OutQuad); };
            hc.OnClick = action;
            KitTooltip.Attach(bg.gameObject, tip);
        }

        private void AddRow(AdvancedModeModule module, string label)
        {
            SideRow r = new SideRow { module = module };
            r.rect = Kit.Rect(_sidebarList, "Row " + label);
            Kit.Layout(r.rect.gameObject, -1f, RowH);
            r.bg = Kit.Image(r.rect, "Bg", Sprites.Rounded(Theme.RadiusSmall), Kit.Alpha(Theme.Accent, 0f), true);
            Kit.Fill(r.bg.rectTransform);
            r.bar = Kit.Image(r.rect, "Bar", Sprites.Pill(3f), Kit.Alpha(Theme.Accent, 0f));
            Kit.Anchor(r.bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(3f, 18f));
            r.label = Kit.MakeText(r.rect, "Label", label, Theme.FontSize, Theme.Text, TextAnchor.MiddleLeft);
            Kit.Fill(r.label.rectTransform, 12f, 0f, 40f, 0f);
            r.count = Kit.MakeText(r.rect, "Count", "", Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleRight);
            Kit.Fill(r.count.rectTransform, 0f, 0f, 10f, 0f);
            KitHoverClick hc = KitHoverClick.On(r.bg.gameObject);
            hc.OnEnter = delegate { r.hover = true; };
            hc.OnExit = delegate { r.hover = false; };
            hc.OnClick = delegate { Select(r); };
            if (module != null)
                KitTooltip.Attach(r.bg.gameObject, "The number is how many entries this module has changed on this object");
            _rows.Add(r);
        }

        // ---------------------------------------------------------------- selection

        private void Select(SideRow r)
        {
            PoseController target = _main.poseTarget;
            if (target == null || r == null)
                return;
            if (r.module != null)
                target.EnableModule(r.module); // Joan's call: gizmos follow the shown module
            _selectedRow = r;
            ShowPage(PageFor(r.module), r.module);
        }

        /// <summary>The new page of a module (every HSPE module has one).</summary>
        private IAdvancedPage PageFor(AdvancedModeModule module)
        {
            if (module == null)
                return _presetsPage;
            if (module is BonesEditor)
                return _bonesPage;
            if (module is DynamicBonesEditor)
                return _dynamicBonesPage;
            if (module is BoobsEditor)
                return _boobsPage;
            if (module is CollidersEditor)
                return _collidersPage;
            if (module is BlendShapesEditor)
                return _blendShapesPage;
            return _ikPage; // IKEditor, the last module
        }

        private void ShowPage(IAdvancedPage page, AdvancedModeModule module)
        {
            if (_page != null && _page != page)
            {
                _page.Hide();
                _page.Root.gameObject.SetActive(false);
            }
            _page = page;
            PoseController target = _main.poseTarget;
            page.Root.gameObject.SetActive(target == null || target.enabled); // the "turned off" page covers it otherwise
            page.Show(_main.poseTarget);
        }

        private SideRow RowOf(AdvancedModeModule module)
        {
            foreach (SideRow r in _rows)
                if (r.module == module)
                    return r;
            return null;
        }

        // ---------------------------------------------------------------- visibility (every frame from MainWindow.Update)

        public void Sync()
        {
            PoseController target = _main.poseTarget;
            bool want = target != null && PoseController._drawAdvancedMode;

            if (target != _shownTarget)
            {
                _shownTarget = target;
                BuildSidebar(target);
                if (target != null)
                {
                    // the module Joan's window would show; keep Presets if that page is open
                    SideRow r = _page == _presetsPage ? RowOf(null) : RowOf(target.kitCurrentModule);
                    Select(r ?? (_rows.Count != 0 ? _rows[0] : null));
                    _window.Title = "Advanced - " + TransferActions.NameOf(target);
                }
                _nextCounts = 0f;
            }

            if (want != _window.Visible)
            {
                _syncing = true;
                if (want)
                    _window.Show();
                else
                    _window.Hide();
                _syncing = false;
            }
        }

        private void OnVisibilityChanged(bool visible)
        {
            // closed with its own X: that is "leave Advanced mode", like Joan's Close button
            if (visible == false && _syncing == false && PoseController._drawAdvancedMode && _main.poseTarget != null)
                _main.poseTarget.ToggleAdvancedMode();
            // opened from outside (NeoUIManager tray, kit inspector): enter Advanced mode instead of being closed again
            if (visible && _syncing == false && PoseController._drawAdvancedMode == false && _main.poseTarget != null)
                _main.poseTarget.ToggleAdvancedMode();
            if (visible == false && _page != null)
                _page.Hide();
        }

        private void OnActiveChanged(bool on)
        {
            PoseController target = _main.poseTarget;
            if (target != null)
                target.enabled = on; // Joan's Disable / Enable buttons
        }

        // ---------------------------------------------------------------- per frame while shown

        private void Update()
        {
            PoseController target = _main.poseTarget;
            if (target == null)
                return;

            bool enabled = target.enabled;
            if (enabled != _shownEnabled || _activeSwitch.Value != enabled)
            {
                _shownEnabled = enabled;
                _activeSwitch.SetValue(enabled, true, false);
                _disabledPage.gameObject.SetActive(!enabled);
                if (_page != null)
                    _page.Root.gameObject.SetActive(enabled);
            }

            // Joan's modules can switch the shown module themselves (e.g. "go to bone" from colliders)
            AdvancedModeModule current = target.kitCurrentModule;
            if (_selectedRow != null && _selectedRow.module != null && current != _selectedRow.module)
            {
                SideRow r = RowOf(current);
                if (r != null)
                    Select(r);
            }

            if (Time.unscaledTime >= _nextCounts)
            {
                _nextCounts = Time.unscaledTime + 1f;
                RefreshModules(target);
                RefreshCounts(target);
            }

            foreach (SideRow r in _rows)
                DrawRow(r);

            if (_page != null && enabled)
                _page.Tick(target);
        }

        /// <summary>
        /// Which modules the object shows can change after the sidebar was built (Joan fills the mesh / dynamic-bone
        /// lists a few frames after the character loads: Blend Shapes went missing otherwise) - rebuild when it does.
        /// </summary>
        private void RefreshModules(PoseController target)
        {
            int mask = ModuleMask(target);
            if (mask == _moduleMask)
                return;
            AdvancedModeModule selected = _selectedRow != null ? _selectedRow.module : null;
            bool presets = _page == _presetsPage;
            BuildSidebar(target);
            SideRow r = presets ? RowOf(null) : RowOf(selected ?? target.kitCurrentModule);
            Select(r ?? (_rows.Count != 0 ? _rows[0] : null));
        }

        private static int ModuleMask(PoseController target)
        {
            int mask = 0;
            List<AdvancedModeModule> modules = target.kitModules;
            for (int i = 0; i < modules.Count && i < 31; i++)
                if (modules[i].shouldDisplay)
                    mask |= 1 << i;
            return mask;
        }

        /// <summary>Changed entries per module (each module's own dirty count; the first version serialized every
        /// module through a throwaway XmlTextWriter once a second - garbage every second while the window is open).</summary>
        private void RefreshCounts(PoseController target)
        {
            foreach (SideRow r in _rows)
            {
                if (r.module == null)
                    continue;
                int n = ChangedCount(r.module);
                if (n != r.shownCount)
                {
                    r.shownCount = n;
                    r.count.text = n > 0 ? n.ToString() : "";
                }
            }
        }

        private static int ChangedCount(AdvancedModeModule m)
        {
            try
            {
                BonesEditor b = m as BonesEditor; if (b != null) return b.KitChangedCount();
                CollidersEditor c = m as CollidersEditor; if (c != null) return c.KitChangedCount();
                DynamicBonesEditor d = m as DynamicBonesEditor; if (d != null) return d.KitChangedCount();
                BlendShapesEditor s = m as BlendShapesEditor; if (s != null) return s.KitChangedCount();
                IKEditor i = m as IKEditor; if (i != null) return i.KitChangedCount();
                BoobsEditor o = m as BoobsEditor; if (o != null) return o.KitChangedCount();
            }
            catch (Exception)
            {
            }
            return 0;
        }

        private void DrawRow(SideRow r)
        {
            r.selected = r == _selectedRow;
            if (r.drawn && r.shownHover == r.hover && r.shownSelected == r.selected)
                return;
            r.drawn = true;
            r.shownHover = r.hover;
            r.shownSelected = r.selected;
            Color bg = r.selected ? Kit.Alpha(Theme.Accent, 0.2f) : (r.hover ? Kit.Alpha(Theme.SurfaceHover, 0.9f) : Kit.Alpha(Theme.SurfaceHover, 0f));
            Tween.Tint(r.bg, bg, 0.12f, Ease.OutQuad);
            Tween.Tint(r.bar, Kit.Alpha(Theme.Accent, r.selected ? 1f : 0f), 0.15f, Ease.OutQuad);
            r.label.fontStyle = r.selected ? FontStyle.Bold : FontStyle.Normal;
            r.label.color = r.selected ? Color.white : Theme.Text;
        }
    }
}
