using System;
using System.Collections.Generic;
using HSPE.Kit.Transfer;
using IllusionPlugin;
using Studio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HSPE.Kit.Small
{
    using HSUIKit;

    /// <summary>
    /// The new small window ("mannequin"), replacing Joan's HSPECanvas when UseKitUI is on (ModPrefs HSPE / UseKitUI,
    /// default on). All pose logic is Joan's, through <see cref="PoseInput"/> and MainWindow; this class only builds and
    /// refreshes controls. Joan's canvas still exists (hidden): it keeps his Options values that MainWindow saves.
    /// </summary>
    internal sealed class KitSmallWindow : MonoBehaviour
    {
        // 262 wide clipped the bottom row (Local/World + gear + Advanced ~272 px). The mannequin is scaled to the
        // content width (276). Height = title 45 + paddings 22 + top row 34 + 7 gaps x 8 + fixed rows 174 + figure.
        private const float WindowWidth = 300f, MannequinScale = 1.2f;
        private const float WindowHeight = 331f + MannequinView.Height * MannequinScale;
        private static readonly Color AxisX = new Color32(0xF0, 0x61, 0x6D, 0xFF);
        private static readonly Color AxisY = new Color32(0x3D, 0xD6, 0x8C, 0xFF);
        private static readonly Color AxisZ = new Color32(0x5B, 0x8C, 0xFF, 0xFF);

        private MainWindow _main;
        private PoseInput _input;
        private KitWindow _window;
        private CanvasGroup _body;
        private Text _selectionText;
        private KitSegmented _modeTabs;
        private RectTransform[] _pages; // IK, FK body, Hands, More (tab order)
        private MannequinView _mannequin;
        private FkFigureView _fkBody, _fkHands;
        private GuideObject _labelSel;
        private int _labelExtras = -1;
        private bool _labelFk;
        private FkMoreList _fkMore;
        private PoseController _boundTarget;
        private int _boundCount = -1;
        private GuideObject _lastSelected;
        private Text _titleText;
        private DragPad[] _movePads, _rotatePads;
        private Text _intensityText;
        private KitCheckbox _optimizeIK;
        private CanvasGroup _optimizeIKGroup;
        private RectTransform _optionsButton, _presetsButton;
        private KitPopover _popover;
        private Text _hotkeyLabel;
        private KeyCode _shownKey = (KeyCode)(-1); // forces the first label
        private bool _shownRegister;

        public bool visible { get { return _window != null && _window.Visible; } }
        /// <summary>The window's rect while shown, else null (HspeUndo hit-tests it).</summary>
        internal RectTransform WindowRect { get { return visible ? _window.Rect : null; } }

        public static bool UseKitUI
        {
            get { return ModPrefs.GetBool("HSPE", "UseKitUI", true, true); }
        }

        /// <summary>Builds the window, or returns null (Joan's canvas is then used) when disabled or on any failure.</summary>
        public static KitSmallWindow TryCreate(MainWindow main)
        {
            if (UseKitUI == false)
                return null;
            KitWindow window = null;
            try
            {
                // resizable: never smaller than the layout needs; the figures grow with the space
                window = KitWindow.Create("HSPE", WindowWidth, WindowHeight).Resizable(new Vector2(WindowWidth, WindowHeight), new Vector2(1600f, 2400f)).Remember("HSPE.Small");
                KitSmallWindow w = window.gameObject.AddComponent<KitSmallWindow>();
                w._main = main;
                w._input = main._input;
                w._window = window;
                w.Build();
                window.VisibilityChanged += main.SetToolbarButtonOn;
                w.OnTargetChanged();
                return w;
            }
            catch (Exception e)
            {
                Debug.LogError("HSPE: the new window could not start, using the classic one.\n" + e);
                global::HSPE.Kit.Log.Write("kit small window failed, falling back to Joan's canvas: " + e);
                if (window != null)
                    Destroy(window.gameObject);
                return null;
            }
        }

        public void Toggle()
        {
            _window.Toggle();
        }

        /// <summary>True while a text field has keyboard focus (the hotkey must not fire while typing).</summary>
        public static bool TypingInField()
        {
            EventSystem es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null)
                return false;
            InputField field = es.currentSelectedGameObject.GetComponent<InputField>();
            return field != null && field.isFocused;
        }

        // ---------------------------------------------------------------- build

        private void Build()
        {
            RectTransform bar = (RectTransform)_window.Rect.Find("TitleBar");
            _presetsButton = HeaderButton(bar, HspeIcons.Presets, -74f, "Presets, and which modules copy / paste carry", OpenPresets).Rect;
            HeaderButton(bar, HspeIcons.Paste, -106f, "Paste onto the selected character(s)", TransferActions.Paste);
            HeaderButton(bar, HspeIcons.Copy, -138f, "Copy the selected character's settings", TransferActions.Copy);
            HeaderButton(bar, HspeIcons.Face, -170f, "Face: expressions, mood pad, vowels", global::HSPE.Kit.Face.FaceWindow.Toggle);

            RectTransform content = _window.Content;
            VerticalLayoutGroup v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(12, 12, 10, 12);
            v.spacing = 8f;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            // the target's name is the window title (the tab row needs the full width); keep it clear of the icons
            _titleText = bar.Find("Title").GetComponent<Text>();
            _titleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _titleText.verticalOverflow = VerticalWrapMode.Truncate;
            _titleText.rectTransform.offsetMax = new Vector2(-188f, _titleText.rectTransform.offsetMax.y);

            // tabs: IK, then the three FK pages
            _modeTabs = KitSegmented.Create(content, new[] { "IK", "FK", "Hands", "More" }, _input.modeIK ? 0 : 1, SetMode);
            Kit.Layout(_modeTabs.gameObject, -1f, 34f);

            // everything below greys out without a target
            RectTransform bodyRect = Kit.Rect(content, "Body");
            bodyRect.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            VerticalLayoutGroup bv = bodyRect.gameObject.AddComponent<VerticalLayoutGroup>();
            bv.spacing = 8f;
            bv.childForceExpandWidth = true;
            bv.childForceExpandHeight = false;
            _body = bodyRect.gameObject.AddComponent<CanvasGroup>();

            // pages
            RectTransform pages = Kit.Rect(bodyRect, "Pages");
            Kit.Layout(pages.gameObject, -1f, MannequinView.Height * MannequinScale).flexibleHeight = 1f; // takes the extra height
            pages.gameObject.AddComponent<FigureFitter>().MinScale = MannequinScale;
            _pages = new RectTransform[4];
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i] = Kit.Rect(pages, "Page " + i);
                Kit.Fill(_pages[i]);
            }
            _mannequin = MannequinView.Create(_pages[0], _main);
            Figure(_mannequin.transform);
            _fkBody = FkFigureView.CreateBody(_pages[1], _main);
            Figure(_fkBody.transform);
            _fkHands = FkFigureView.CreateHands(_pages[2], _main);
            Figure(_fkHands.transform);
            _fkMore = FkMoreList.Create(_pages[3]);
            ShowPage(_input.modeIK ? 0 : 1);

            _selectionText = Kit.MakeText(bodyRect, "Selection", "", Theme.FontSize, Theme.Accent, TextAnchor.MiddleCenter);
            KitThemeColor.Bind(_selectionText, () => Theme.Accent);
            _selectionText.fontStyle = FontStyle.Bold;
            Kit.Layout(_selectionText.gameObject, -1f, 20f);
            _mannequin.SetSelectionText(_selectionText);

            // drag pads
            _movePads = PadRow(bodyRect, "Move", false, "Drag up / down to move along {0}.\nShift: faster, Ctrl: slower.");
            _rotatePads = PadRow(bodyRect, "Rotate", true, "Drag left / right to rotate around {0}.\nShift: faster, Ctrl: slower.");

            // intensity
            RectTransform intensity = KitLayout.Row(bodyRect, 8f);
            Kit.Layout(intensity.gameObject, -1f, 28f);
            Text il = Kit.MakeText(intensity, "Label", "Intensity", Theme.FontSize, Theme.TextDim, TextAnchor.MiddleLeft);
            Kit.Layout(il.gameObject, 60f, 20f);
            KitSlider slider = KitSlider.Create(intensity, null, 0f, 14f, 7f, true, OnIntensity);
            LayoutElement sle = slider.transform.parent.GetComponent<LayoutElement>();
            if (sle != null)
                sle.flexibleWidth = 1f;
            _intensityText = Kit.MakeText(intensity, "Value", PoseInput.IntensityText(_input.intensity), Theme.FontSize, Theme.Text, TextAnchor.MiddleRight);
            Kit.Layout(_intensityText.gameObject, 40f, 20f);
            KitTooltip.Attach(slider.gameObject, "How far a drag moves / rotates");
            OnIntensity(7f);

            // optimize IK
            _optimizeIK = KitCheckbox.Create(bodyRect, "Optimize IK", true, OnOptimizeIK);
            _optimizeIKGroup = _optimizeIK.gameObject.AddComponent<CanvasGroup>();
            KitTooltip.Attach(_optimizeIK.gameObject, "HSPE's IK solver settings for this character (Joan's option)");

            // World / Local, options, Advanced
            RectTransform bottom = KitLayout.Row(bodyRect, 8f);
            Kit.Layout(bottom.gameObject, -1f, 34f);
            KitSegmented space = KitSegmented.Create(bottom, new[] { "Local", "World" }, _input.positionWorld ? 1 : 0, i => _input.positionWorld = i == 1);
            Kit.Layout(space.gameObject, 118f, 34f);
            KitTooltip.Attach(space.gameObject, "Move pads work in the character's local or in world axes");
            KitLayout.Flexible(bottom);
            KitButton options = KitButton.Create(bottom, "", ButtonStyle.Ghost, OpenOptions, HspeIcons.Gear);
            Kit.Layout(options.gameObject, 34f, 34f);
            KitTooltip.Attach(options.gameObject, "Options");
            _optionsButton = options.Rect;
            KitButton advanced = KitButton.Create(bottom, "Advanced", ButtonStyle.Primary, _main.ToggleAdvancedModeForTarget);
            KitTooltip.Attach(advanced.gameObject, "Advanced mode: bones, colliders, dynamic bones, blend shapes, ...");
        }

        /// <summary>Centers a 230 x 250 figure in its page, scaled to the content width.</summary>
        private static void Figure(Transform figure)
        {
            Kit.Anchor((RectTransform)figure, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(FigureDraw.Width, FigureDraw.Height));
            figure.localScale = new Vector3(MannequinScale, MannequinScale, 1f);
        }

        private void ShowPage(int index)
        {
            for (int i = 0; i < _pages.Length; i++)
                _pages[i].gameObject.SetActive(i == index);
        }

        private static KitIconButton HeaderButton(RectTransform bar, Sprite icon, float x, string tip, VoidHandler onClick)
        {
            KitIconButton b = KitIconButton.Create(bar, icon, 28f, false, onClick);
            Kit.Anchor(b.Rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(x, 0f), new Vector2(28f, 28f));
            KitTooltip.Attach(b.gameObject, tip);
            return b;
        }

        private DragPad[] PadRow(RectTransform parent, string label, bool rotate, string tipFormat)
        {
            RectTransform row = KitLayout.Row(parent, 6f);
            Kit.Layout(row.gameObject, -1f, 32f);
            Text t = Kit.MakeText(row, "Label", label, Theme.FontSize, Theme.TextDim, TextAnchor.MiddleLeft);
            Kit.Layout(t.gameObject, 54f, 20f);
            DragPad[] pads = new DragPad[3];
            string[] axes = { "X", "Y", "Z" };
            Color[] colors = { AxisX, AxisY, AxisZ };
            for (int i = 0; i < 3; i++)
            {
                int axis = i;
                pads[i] = DragPad.Create(row, axes[i], colors[i], delegate { StartDrag(rotate, axis); });
                KitTooltip.Attach(pads[i].gameObject, string.Format(tipFormat, axes[i]));
            }
            return pads;
        }

        private void StartDrag(bool rotate, int axis)
        {
            if (rotate)
            {
                if (axis == 0) _input.xRot = true;
                else if (axis == 1) _input.yRot = true;
                else _input.zRot = true;
            }
            else
            {
                if (axis == 0) _input.xMove = true;
                else if (axis == 1) _input.yMove = true;
                else _input.zMove = true;
            }
        }

        // ---------------------------------------------------------------- handlers

        private void SetMode(int index)
        {
            _input.modeIK = index == 0;
            ShowPage(index);
            _lastSelected = FkBones.SelectedGuide(); // a tab click must not be undone by FollowSelection
            if (index == 3)
                _fkMore.Reveal(FkBones.SelectedGuide());
        }

        private void OnIntensity(float value)
        {
            _input.intensity = PoseInput.IntensityFromSlider(value);
            if (_intensityText != null)
                _intensityText.text = PoseInput.IntensityText(_input.intensity);
        }

        private void OnOptimizeIK(bool on)
        {
            CharaPoseController chara = _main.poseTarget as CharaPoseController;
            if (chara != null)
                chara.optimizeIK = on;
        }

        /// <summary>From MainWindow.OnTargetChange (and once at start).</summary>
        public void OnTargetChanged()
        {
            PoseController target = _main.poseTarget;
            CharaPoseController chara = target as CharaPoseController;
            string name = target != null ? TransferActions.NameOf(target) : "";
            _window.Title = string.IsNullOrEmpty(name) ? "HSPE" : name;
            if (chara != null)
                _optimizeIK.SetValue(chara.optimizeIK, false, false);
            _optimizeIKGroup.alpha = chara != null ? 1f : 0.35f;
            _optimizeIKGroup.interactable = chara != null;
            _optimizeIKGroup.blocksRaycasts = chara != null;
            _body.alpha = target != null ? 1f : 0.35f;
            _body.interactable = target != null;
            _body.blocksRaycasts = target != null;
            if (target == null)
                _selectionText.text = "Select a character or item";
        }

        // ---------------------------------------------------------------- per frame (only while shown)

        private void Update()
        {
            PoseController target = _main.poseTarget;
            if (target == null)
                return;
            if (_input.modeIK)
                _mannequin.Refresh(target);
            else
                RefreshFK(target);

            bool canMove = _input.modeIK && _input.CanMove(target);
            bool canRotate = _input.CanRotate(target);
            _movePads[0].Refresh(canMove, _input.xMove);
            _movePads[1].Refresh(canMove, _input.yMove);
            _movePads[2].Refresh(canMove, _input.zMove);
            _rotatePads[0].Refresh(canRotate, _input.xRot);
            _rotatePads[1].Refresh(canRotate, _input.yRot);
            _rotatePads[2].Refresh(canRotate, _input.zRot);

            if (_hotkeyLabel != null && _popover != null && _popover.IsOpen)
            {
                // enum ToString boxes + allocates: only when the key or the mode changed
                if (_main._mainWindowKeyCode != _shownKey || _main._shortcutRegisterMode != _shownRegister)
                {
                    _shownKey = _main._mainWindowKeyCode;
                    _shownRegister = _main._shortcutRegisterMode;
                    _hotkeyLabel.text = _shownRegister ? "Press a key..." : _shownKey.ToString();
                }
            }
        }

        private void RefreshFK(PoseController target)
        {
            if (target != _boundTarget || target.target.fkObjects.Count != _boundCount)
                BindFK(target);
            _input.SyncFkSelection();
            GuideObject sel = FkBones.SelectedGuide();
            // non-short-circuit: every page refreshes; a vanished bone (hair / clothes swapped) triggers a rebind
            bool valid = _fkBody.Refresh(target, sel) & _fkHands.Refresh(target, sel) & _fkMore.Refresh(target, sel);
            if (!valid)
            {
                target.target.RefreshFKBones(); // what Joan's OnTargetChange does
                _boundTarget = null;
            }

            if (sel != _lastSelected)
            {
                _lastSelected = sel;
                FollowSelection(sel);
            }

            // rebuilt only when the selection changes (string building every frame is garbage)
            int extras = _input.fkExtras.Count;
            if (sel != _labelSel || extras != _labelExtras || target.target.fkEnabled != _labelFk)
            {
                _labelSel = sel;
                _labelExtras = extras;
                _labelFk = target.target.fkEnabled;
                string label = _fkBody.LabelOf(sel) ?? _fkHands.LabelOf(sel) ?? _fkMore.LabelOf(sel);
                if (label != null && extras > 0)
                    label += " + " + extras + " more";
                string s = label ?? (target.target.fkEnabled ? "Click a joint" : "FK is off for this object");
                if (_selectionText.text != s)
                    _selectionText.text = s;
            }
        }

        private void BindFK(PoseController target)
        {
            Dictionary<string, OCIChar.BoneInfo> map = FkBones.Map(target);
            HashSet<string> placed = new HashSet<string>();
            _fkBody.Bind(map, placed);
            _fkHands.Bind(map, placed);
            _fkMore.Bind(map, placed);
            _boundTarget = target;
            _boundCount = target.target.fkObjects.Count;
        }

        /// <summary>A bone picked elsewhere (the viewport): show the page that has it, like Joan's list scrolled to it.</summary>
        private void FollowSelection(GuideObject sel)
        {
            if (sel == null)
                return;
            int page = _modeTabs.Selected;
            if ((page == 1 && _fkBody.Has(sel)) || (page == 2 && _fkHands.Has(sel)))
                return;
            if (page == 3 && _fkMore.Has(sel))
            {
                _fkMore.Reveal(sel);
                return;
            }
            int target = _fkBody.Has(sel) ? 1 : _fkHands.Has(sel) ? 2 : _fkMore.Has(sel) ? 3 : -1;
            if (target >= 0)
                _modeTabs.Select(target, true);
        }

        // ---------------------------------------------------------------- popovers

        private void OpenOptions()
        {
            _popover = KitPopover.Open(250f);
            RectTransform c = _popover.Content;
            KitLayout.Header(c, "Options");

            RectTransform keyRow = KitLayout.Row(c, 8f);
            Kit.Layout(keyRow.gameObject, -1f, 34f);
            Text kl = Kit.MakeText(keyRow, "Label", "Window key", Theme.FontSize, Theme.Text, TextAnchor.MiddleLeft);
            Kit.Layout(kl.gameObject, 100f, 20f);
            KitLayout.Flexible(keyRow);
            KitButton key = KitButton.Create(keyRow, _main._mainWindowKeyCode.ToString(), ButtonStyle.Secondary, delegate { _main._shortcutKeyButton.onClick.Invoke(); });
            Kit.Layout(key.gameObject, 110f, 34f);
            _hotkeyLabel = key.GetComponentInChildren<Text>();
            KitTooltip.Attach(key.gameObject, "Click, then press the key that shows / hides this window");

            KitCheckbox.Create(c, "Crotch correction by default", _main._crotchCorrectionByDefaultToggle.isOn, b => _main._crotchCorrectionByDefaultToggle.isOn = b);
            KitCheckbox.Create(c, "Ankle correction by default", _main._anklesCorrectionByDefaultToggle.isOn, b => _main._anklesCorrectionByDefaultToggle.isOn = b);

            KitSwitch.Create(c, "New HSPE window (restart)", UseKitUI, b => ModPrefs.SetBool("HSPE", "UseKitUI", b));
            _popover.Closed = delegate
            {
                if (_main._shortcutRegisterMode)
                    _main._shortcutKeyButton.onClick.Invoke(); // leave "press a key" mode, like Joan's Options button
                _hotkeyLabel = null;
            };
            _popover.Place(_optionsButton);
        }

        private void OpenPresets()
        {
            _popover = KitPopover.Open(240f);
            RectTransform c = _popover.Content;

            KitLayout.Label(c, "Copy / paste / presets carry", TextRole.Dim);
            foreach (ModuleMask single in ModuleNodes.Singles)
            {
                ModuleMask m = single;
                KitCheckbox.Create(c, TransferActions.Label(m), (TransferClipboard.LastModules & m) != 0, on =>
                {
                    TransferClipboard.LastModules = on ? TransferClipboard.LastModules | m : TransferClipboard.LastModules & ~m;
                });
            }

            KitLayout.Space(c, 4f);
            KitLayout.Header(c, "Presets");
            string[] names = PresetStore.List();
            if (names.Length == 0)
                KitLayout.Label(c, "No presets yet.", TextRole.Dim);
            else
            {
                const float rowH = 30f, gap = 2f;
                RectTransform holder = Kit.Rect(c, "PresetList");
                Kit.Layout(holder.gameObject, -1f, Mathf.Min(names.Length, 7) * (rowH + gap));
                KitScroll scroll = KitScroll.Create(holder, 0, 0, gap, names.Length > 7);
                foreach (string n in names)
                {
                    string name = n;
                    KitButton row = KitButton.Create(scroll.Content, name, ButtonStyle.Ghost, delegate
                    {
                        _popover.Close();
                        TransferActions.ApplyPreset(name);
                    });
                    Kit.Layout(row.gameObject, -1f, rowH);
                    KitTooltip.Attach(row.gameObject, "Apply to the selected character(s). Right-click: delete");
                    KitContextMenu.Attach(row.gameObject, () => new KitMenu().Danger("Delete \"" + name + "\"", delegate { ConfirmDelete(name); }));
                }
            }
            KitButton save = KitButton.Create(c, "Save selected as preset...", ButtonStyle.Secondary, SavePresetPrompt, Sprites.Plus);
            Kit.Layout(save.gameObject, -1f, 34f);
            _popover.Place(_presetsButton);
        }

        private void SavePresetPrompt()
        {
            _popover.Close();
            KitModal.Prompt("Save preset", "Saves the selected character's " + TransferActions.ModulesText(TransferClipboard.LastModules) + ".", "", "Preset name", "Save", name =>
            {
                name = PresetStore.CleanName(name ?? "");
                if (name.Length == 0)
                    return;
                if (Array.IndexOf(PresetStore.List(), name) >= 0)
                    KitModal.Show("Overwrite preset?", "\"" + name + "\" already exists.", new[] { "Cancel", "Overwrite" }, i => { if (i == 1) TransferActions.SavePreset(name); }, true);
                else
                    TransferActions.SavePreset(name);
            });
        }

        private void ConfirmDelete(string name)
        {
            _popover.Close();
            KitModal.Show("Delete preset?", "\"" + name + "\" and its thumbnail will be deleted.", new[] { "Cancel", "Delete" }, i =>
            {
                if (i != 1)
                    return;
                PresetStore.Delete(name);
                KitToast.Show("Deleted \"" + name + "\"", null, ToastKind.Info, 2.5f);
            }, true);
        }

        // ---------------------------------------------------------------- drag pad

        /// <summary>One X / Y / Z pad: press and drag (the mouse delta drives Joan's move / rotate math).</summary>
        private sealed class DragPad : MonoBehaviour
        {
            private Image _bg, _bar;
            private Text _text;
            private CanvasGroup _group;
            private bool _hover, _enabled = true, _active, _drawn;
            private VoidHandler _onDown;

            public static DragPad Create(Transform parent, string label, Color axis, VoidHandler onDown)
            {
                RectTransform rt = Kit.Rect(parent, "Pad " + label);
                LayoutElement le = Kit.Layout(rt.gameObject, -1f, 32f);
                le.flexibleWidth = 1f;
                le.minWidth = 40f;
                DragPad p = rt.gameObject.AddComponent<DragPad>();
                p._onDown = onDown;
                p._group = rt.gameObject.AddComponent<CanvasGroup>();
                p._bg = Kit.Image(rt, "Bg", Sprites.Rounded(Theme.RadiusSmall), Theme.Surface, true);
                Kit.Fill(p._bg.rectTransform);
                p._bar = Kit.Image(rt, "Axis", Sprites.Pill(3f), axis);
                p._bar.rectTransform.anchorMin = new Vector2(0f, 0f);
                p._bar.rectTransform.anchorMax = new Vector2(1f, 0f);
                p._bar.rectTransform.pivot = new Vector2(0.5f, 0f);
                p._bar.rectTransform.sizeDelta = new Vector2(-16f, 3f);
                p._bar.rectTransform.anchoredPosition = new Vector2(0f, 3f);
                p._text = Kit.MakeText(rt, "Label", label, Theme.FontSize, Theme.Text, TextAnchor.MiddleCenter);
                p._text.fontStyle = FontStyle.Bold;
                Kit.Fill(p._text.rectTransform);
                KitHoverClick hc = KitHoverClick.On(p._bg.gameObject);
                hc.OnEnter = delegate { p._hover = true; p._drawn = false; };
                hc.OnExit = delegate { p._hover = false; p._drawn = false; };
                hc.OnDown = delegate
                {
                    if (!p._enabled || p._onDown == null)
                        return;
                    EventSystem.current.SetSelectedGameObject(null);
                    p._onDown();
                };
                return p;
            }

            public void Refresh(bool enabled, bool active)
            {
                if (_drawn && enabled == _enabled && active == _active)
                    return;
                _drawn = true;
                _enabled = enabled;
                _active = active;
                _group.alpha = enabled ? 1f : 0.35f;
                Color c = active ? Theme.SurfaceActive : (_hover && enabled ? Theme.SurfaceHover : Theme.Surface);
                Tween.Tint(_bg, c, 0.1f, Ease.OutQuad);
                _bar.rectTransform.sizeDelta = new Vector2(active ? -8f : -16f, active ? 4f : 3f);
            }
        }
    }

    /// <summary>
    /// Scales the figures (230 x 250 design space) to the page they sit on, keeping their proportions, never below
    /// MinScale - so a bigger window gets a bigger, easier to click mannequin.
    /// </summary>
    internal sealed class FigureFitter : MonoBehaviour
    {
        public float MinScale = 1f;
        private float _shown = -1f;

        private void LateUpdate()
        {
            Rect r = ((RectTransform)transform).rect;
            if (r.width <= 0f || r.height <= 0f)
                return;
            float s = Mathf.Max(MinScale, Mathf.Min(r.width / FigureDraw.Width, r.height / FigureDraw.Height));
            if (Mathf.Abs(s - _shown) < 0.001f)
                return;
            _shown = s;
            // every page holds at most one figure, directly under the page
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform page = transform.GetChild(i);
                for (int j = 0; j < page.childCount; j++)
                {
                    Transform f = page.GetChild(j);
                    if (f.GetComponent<MannequinView>() != null || f.GetComponent<FkFigureView>() != null)
                        f.localScale = new Vector3(s, s, 1f);
                }
            }
        }
    }
}
