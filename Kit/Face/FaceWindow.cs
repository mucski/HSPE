using System.Collections.Generic;
using HSPE.AMModules;
using HSPE.Kit.Advanced;
using HSPE.Kit.Small;
using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Face
{
    using HSUIKit;

    /// <summary>
    /// The Face window (layout C): face diagram + extra bones on the left, mood pad + vowels on the right. Follows the
    /// Studio selection; everything it sets goes through the character's FaceSession (HSPE's own modules underneath).
    /// Undo / redo are header buttons - Ctrl+Z is Studio's own undo.
    /// </summary>
    internal sealed class FaceWindow : MonoBehaviour
    {
        private const float RightW = 236f, ExtrasH = 132f;
        private static readonly string[] VowelNames = { "none", "A", "I", "U", "E", "O" };

        private static FaceWindow _self;
        private static FaceInput _clipboard;

        private KitWindow _window;
        private Text _title, _empty, _mix, _banner;
        private CanvasGroup _body;
        private FaceDiagram _diagram;
        private MoodPad _pad;
        private KitSegmented _vowels;
        private KitSlider _eyeOpen, _mouthOpen;
        private bool _sliding;
        private RectTransform _diagramHost, _extrasBox;
        private KitIconButton _undo, _redo;
        private RectTransform _extrasContent;
        private Text _extrasEmpty;
        private readonly List<ExtraRow> _extraRows = new List<ExtraRow>();

        private PoseController _shownTarget;
        private FaceSession _session;
        private bool _tampered, _overwriteOk, _gesture;
        private float _nextCheck, _nextVersionCheck;
        private int _shownVersion;

        private sealed class ExtraRow
        {
            public string Key, Bone;
            public AxisCell X, Y;
        }

        public static bool Visible { get { return _self != null && _self._window.Visible; } }
        /// <summary>The window's rect while shown, else null (HspeUndo leaves face edits to the Face window's own undo).</summary>
        internal static RectTransform WindowRect { get { return Visible ? _self._window.Rect : null; } }

        public static void Toggle()
        {
            if (_self == null)
            {
                GameObject go = new GameObject("HSPE.FaceWindow");
                DontDestroyOnLoad(go);
                _self = go.AddComponent<FaceWindow>();
                _self.Build();
            }
            _self._window.Toggle();
            if (_self._window.Visible)
                _self.Follow(true);
        }

        // ---------------------------------------------------------------- build

        private void Build()
        {
            // 470 px tall looked crammed
            _window = KitWindow.Create("Face", 740f, 600f).Resizable(new Vector2(680f, 560f), new Vector2(2400f, 2000f)).Remember("HSPE.Face2");
            RectTransform bar = (RectTransform)_window.Rect.Find("TitleBar");
            _undo = HeaderButton(bar, HspeIcons.Undo, -74f, "Undo the last face change", Undo);
            _redo = HeaderButton(bar, HspeIcons.Redo, -106f, "Redo", Redo);
            HeaderButton(bar, Sprites.Reset, -138f, "Reset the face (hands it back to the game)", ResetFace);
            HeaderButton(bar, HspeIcons.Paste, -170f, "Paste a copied face", Paste);
            HeaderButton(bar, HspeIcons.Copy, -202f, "Copy this face", Copy);
            KitIconButton more = HeaderButton(bar, HspeIcons.More, -234f, "More", null);
            more.OnClick = () => OpenMore(more.Rect);
            KitIconButton presets = HeaderButton(bar, HspeIcons.Presets, -266f, "Face presets", null);
            presets.OnClick = () => OpenPresets(presets.Rect);
            _title = bar.Find("Title").GetComponent<Text>();
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            _title.verticalOverflow = VerticalWrapMode.Truncate;
            _title.rectTransform.offsetMax = new Vector2(-284f, _title.rectTransform.offsetMax.y);

            RectTransform content = _window.Content;
            RectTransform body = Kit.Rect(content, "Body");
            Kit.Fill(body);
            _body = body.gameObject.AddComponent<CanvasGroup>();

            // left: diagram above, extra bones below
            RectTransform left = Kit.Rect(body, "Left");
            Kit.Fill(left, 12f, 12f, RightW + 24f, 10f);
            _diagramHost = Kit.Rect(left, "DiagramHost");
            RectTransform diagramHost = _diagramHost;
            Kit.Fill(diagramHost, 0f, ExtrasH + 8f, 0f, 0f);
            _diagram = FaceDiagram.Create(diagramHost);
            _diagram.OnBegin = BeginEdit;
            _diagram.OnChanged = Changed;
            _diagram.OnEnd = EndEdit;
            BuildExtras(left);

            // right: pad, mix readout, vowels, banner
            RectTransform right = Kit.Rect(body, "Right");
            right.anchorMin = new Vector2(1f, 0f);
            right.anchorMax = new Vector2(1f, 1f);
            right.pivot = new Vector2(1f, 0.5f);
            right.offsetMin = new Vector2(-12f - RightW, 12f);
            right.offsetMax = new Vector2(-12f, -10f);
            VerticalLayoutGroup v = right.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6f;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            Label(right, "MOOD");
            RectTransform padHost = Kit.Rect(right, "PadHost");
            Kit.Layout(padHost.gameObject, -1f, RightW - 16f);
            _pad = MoodPad.Create(padHost);
            _pad.OnBegin = BeginEdit;
            _pad.OnChanged = Changed;
            _pad.OnEnd = EndEdit;
            _mix = Kit.MakeText(right, "Mix", "", Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleCenter);
            _mix.horizontalOverflow = HorizontalWrapMode.Wrap;
            Kit.Layout(_mix.gameObject, -1f, 30f);

            Label(right, "VOWEL");
            _vowels = KitSegmented.Create(right, VowelNames, 0, SetVowel);
            Kit.Layout(_vowels.gameObject, -1f, 30f);
            KitTooltip.Attach(_vowels.gameObject, "Replaces only the mouth of the mood");

            // Studio's own eye / mouth sliders can't move shapes HSPE holds - these do it the game's way
            _eyeOpen = KitSlider.Create(right, "Eyes open", 0f, 1f, 1f, false, f => SetOpen(true, f));
            KitTooltip.Attach(_eyeOpen.gameObject, "Closes the eyes of the current face (Studio's eye slider can't while the face is held)");
            _mouthOpen = KitSlider.Create(right, "Mouth open", 0f, 1f, 0f, false, f => SetOpen(false, f));
            KitTooltip.Attach(_mouthOpen.gameObject, "Opens the mouth of the current face (Studio's mouth slider can't while the face is held)");

            _banner = Kit.MakeText(right, "Banner", "", Theme.SmallSize, Theme.Warning, TextAnchor.UpperLeft);
            _banner.horizontalOverflow = HorizontalWrapMode.Wrap;
            LayoutElement bl = Kit.Layout(_banner.gameObject, -1f, 44f);
            bl.flexibleHeight = 1f;

            _empty = Kit.MakeText(content, "Empty", "Select a character in Studio", Theme.FontSize, Theme.TextDim, TextAnchor.MiddleCenter);
            _empty.fontStyle = FontStyle.Italic;
            Kit.Fill(_empty.rectTransform);
            ShowBody(false);
        }

        private static KitIconButton HeaderButton(RectTransform bar, Sprite icon, float x, string tip, VoidHandler onClick)
        {
            KitIconButton b = KitIconButton.Create(bar, icon, 28f, false, onClick);
            Kit.Anchor(b.Rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(x, 0f), new Vector2(28f, 28f));
            KitTooltip.Attach(b.gameObject, tip);
            return b;
        }

        private static void Label(RectTransform parent, string text)
        {
            Text t = Kit.MakeText(parent, text, text, Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleLeft);
            t.fontStyle = FontStyle.Bold;
            Kit.Layout(t.gameObject, -1f, 18f);
        }

        private void BuildExtras(RectTransform left)
        {
            RectTransform box = _extrasBox = Kit.Rect(left, "Extras");
            box.anchorMin = new Vector2(0f, 0f);
            box.anchorMax = new Vector2(1f, 0f);
            box.pivot = new Vector2(0.5f, 0f);
            box.sizeDelta = new Vector2(0f, ExtrasH);
            Image bg = box.gameObject.AddComponent<Image>();
            Kit.SetSprite(bg, Sprites.Rounded(Theme.RadiusSmall));
            bg.color = Kit.Shade(Theme.Surface, -0.12f);
            bg.raycastTarget = false;
            Text title = Kit.MakeText(box, "Title", "EXTRA FACE BONES", Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.offsetMin = new Vector2(10f, -24f);
            title.rectTransform.offsetMax = new Vector2(-10f, -2f);
            KitTooltip.Attach(title.gameObject, "Bones under the face root that no recipe uses (mods add these). X / Y move them; right-click a row to pin it to the face");
            RectTransform listHost = Kit.Rect(box, "List");
            Kit.Fill(listHost, 0f, 2f, 0f, 24f);
            KitScroll scroll = KitScroll.Create(listHost, 6, 2, 2f, true, true);
            _extrasContent = scroll.Content;
            _extrasEmpty = Kit.MakeText(listHost, "Empty", "None on this head", Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleCenter);
            _extrasEmpty.fontStyle = FontStyle.Italic;
            Kit.Fill(_extrasEmpty.rectTransform);
        }

        private void RebuildExtras()
        {
            for (int i = _extrasContent.childCount - 1; i >= 0; i--)
                Destroy(_extrasContent.GetChild(i).gameObject);
            _extraRows.Clear();
            if (_session != null)
                foreach (FacePoint p in _session.Input.ExtraPoints)
                    AddExtraRow(p);
            _extrasEmpty.enabled = _extraRows.Count == 0;
            // stock heads have no extra bones: give the face the whole height
            bool any = _extraRows.Count != 0;
            _extrasBox.gameObject.SetActive(any);
            _diagramHost.offsetMin = new Vector2(_diagramHost.offsetMin.x, any ? ExtrasH + 8f : 0f);
            RefreshExtras();
        }

        private void AddExtraRow(FacePoint p)
        {
            ExtraRow r = new ExtraRow { Key = p.Name, Bone = p.Bone };
            RectTransform row = KitLayout.Row(_extrasContent, 6f);
            Kit.Layout(row.gameObject, -1f, 26f);
            Text name = Kit.MakeText(row, "Name", p.Bone, Theme.SmallSize, Theme.Text, TextAnchor.MiddleLeft);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.verticalOverflow = VerticalWrapMode.Truncate;
            Kit.Layout(name.gameObject, 10f, 22f).flexibleWidth = 1f; // same preferred width on every row: X / Y line up
            r.X = Cell(row, "X", new Color32(0xF0, 0x61, 0x6D, 0xFF), r, 0);
            r.Y = Cell(row, "Y", new Color32(0x3D, 0xD6, 0x8C, 0xFF), r, 1);
            KitContextMenu.Attach(row.gameObject, () =>
            {
                KitMenu m = new KitMenu();
                if (FacePins.Has(r.Bone))
                    m.Item("Unpin from the face", () => _diagram.Unpin(r.Bone));
                else
                    m.Item("Pin to the face...", () => _diagram.BeginPin(r.Bone));
                m.Item("Reset", () => { if (BeginEdit()) { _session.Input.Drags.Remove(r.Key); Changed(); EndEdit(); } });
                return m;
            });
            _extraRows.Add(r);
        }

        private AxisCell Cell(RectTransform row, string axis, Color color, ExtraRow r, int index)
        {
            AxisCell c = AxisCell.Create(row, axis, color, "Drag / wheel to move the bone (4 mm at 1) · click to type");
            Kit.Layout(c.gameObject, 64f, 22f);
            c.Step = 0.05f;
            c.Format = "0.00";
            c.OnActive = on =>
            {
                if (on)
                    _gesture = BeginEdit();
                else if (_gesture)
                {
                    _gesture = false;
                    EndEdit();
                }
            };
            c.OnDelta = d => SetExtra(r, index, d, true);
            c.OnTyped = value => { if (BeginEdit()) { SetExtra(r, index, value, false); EndEdit(); } };
            return c;
        }

        private void SetExtra(ExtraRow r, int index, float value, bool delta)
        {
            if (_session == null)
                return;
            float[] d;
            if (_session.Input.Drags.TryGetValue(r.Key, out d) == false || d == null)
                d = new float[2];
            else
                d = (float[])d.Clone();
            d[index] = Mathf.Clamp(delta ? d[index] + value : value, -1f, 1f);
            _session.Input.Drags[r.Key] = d;
            Changed();
        }

        private void RefreshExtras()
        {
            foreach (ExtraRow r in _extraRows)
            {
                float[] d;
                if (_session == null || _session.Input.Drags.TryGetValue(r.Key, out d) == false || d == null)
                    d = Zero;
                r.X.Show(d[0]);
                r.Y.Show(d[1]);
            }
        }

        private static readonly float[] Zero = { 0f, 0f };

        // ---------------------------------------------------------------- following the selection

        private void Update()
        {
            if (_window == null || _window.Visible == false)
                return;
            if (_sliding && Input.GetMouseButton(0) == false)
            {
                _sliding = false; // one undo step per slider drag
                EndEdit();
            }
            Follow(false);
            // the Timeline "Face" track moved the face: show it (a few times a second is enough)
            if (_session != null && _session.Version != _shownVersion && Time.unscaledTime >= _nextVersionCheck)
            {
                _shownVersion = _session.Version;
                _nextVersionCheck = Time.unscaledTime + 0.2f;
                RefreshAll();
            }
            if (_session != null && Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 1f;
                bool t = _session.Applier.Owns && _session.Applier.Tampered(_session.Rig);
                if (t != _tampered)
                {
                    _tampered = t;
                    _overwriteOk = false;
                    _banner.text = t ? "Changed elsewhere (Blend Shapes / Bones page, paste or Timeline). The next face edit asks before overwriting." : "";
                }
            }
        }

        private void Follow(bool force)
        {
            MainWindow main = MainWindow._self;
            PoseController target = main != null ? main.poseTarget : null;
            if (force == false && ReferenceEquals(target, _shownTarget) && (_session == null || _session.Valid))
                return;
            _shownTarget = target;
            _session = FaceSessions.Get(target, true);
            if (_session != null && _session.Valid == false)
                _session.Rebind();
            _tampered = _overwriteOk = false;
            _mixA = -2; // another character / sex: the readout's mood names may differ
            _banner.text = "";
            ShowBody(_session != null);
            _title.text = _session != null ? "Face  ·  " + target.name.Replace("(Clone)", "") : "Face";
            _diagram.Bind(_session);
            _pad.Bind(_session);
            RebuildExtras();
            RefreshAll();
        }

        private void ShowBody(bool on)
        {
            _body.alpha = on ? 1f : 0f;
            _body.interactable = _body.blocksRaycasts = on;
            _empty.enabled = !on;
        }

        // ---------------------------------------------------------------- edits

        /// <summary>Before any change: ask when the face was changed elsewhere, then push undo.</summary>
        private bool BeginEdit()
        {
            if (_session == null)
                return false;
            if (_tampered && _overwriteOk == false)
            {
                KitModal.Show("Face changed elsewhere", "Some of the face's shapes or bones were changed outside the Face window. Overwrite them with the Face window's face?",
                    new[] { "Overwrite", "Cancel" }, i =>
                    {
                        if (i == 0)
                        {
                            _overwriteOk = true;
                            _banner.text = "";
                        }
                    });
                return false;
            }
            _session.Undo.Push(_session.Input);
            return true;
        }

        private void Changed()
        {
            if (_session == null)
                return;
            _session.Apply();
            RefreshMix();
            RefreshExtras();
        }

        private void EndEdit()
        {
            _tampered = false;
            RefreshButtons();
        }

        private void SetVowel(int i)
        {
            if (_session == null)
                return;
            string v = i == 0 ? null : VowelNames[i];
            if (v == _session.Input.Vowel)
                return;
            if (BeginEdit() == false)
            {
                _vowels.Select(IndexOfVowel(_session.Input.Vowel), false);
                return;
            }
            _session.Input.Vowel = v;
            Changed();
            EndEdit();
        }

        private void SetOpen(bool eyes, float value)
        {
            if (_session == null)
                return;
            if (_sliding == false)
            {
                if (BeginEdit() == false)
                {
                    RefreshSliders();
                    return;
                }
                _sliding = true;
            }
            if (eyes)
                _session.Input.EyeOpen = value;
            else
                _session.Input.MouthOpen = value;
            Changed();
        }

        private void RefreshSliders()
        {
            if (_session == null)
                return;
            _eyeOpen.SetValue(_session.Input.EyeOpen, false, false);
            _mouthOpen.SetValue(_session.Input.MouthOpen, false, false);
        }

        private static int IndexOfVowel(string v)
        {
            for (int i = 1; i < VowelNames.Length; i++)
                if (VowelNames[i] == v)
                    return i;
            return 0;
        }

        private void Undo()
        {
            if (_session != null && _session.Undo.Undo(_session.Input))
                AfterJump();
        }

        private void Redo()
        {
            if (_session != null && _session.Undo.Redo(_session.Input))
                AfterJump();
        }

        private void AfterJump()
        {
            _session.Apply();
            _tampered = false;
            _banner.text = "";
            RefreshAll();
        }

        private void ResetFace()
        {
            if (_session == null || _session.Input.IsNeutral && _session.Applier.Owns == false)
                return;
            _session.Undo.Push(_session.Input);
            _session.Reset();
            _tampered = false;
            _banner.text = "";
            RefreshAll();
        }

        private void Copy()
        {
            if (_session == null)
                return;
            _clipboard = _session.Input.Clone();
            KitToast.Show("Face copied", "Paste it onto another character from its Face window", ToastKind.Info, 2.5f);
        }

        private void Paste()
        {
            if (_session == null || _clipboard == null)
                return;
            if (BeginEdit() == false)
                return;
            _session.Input.CopyFrom(_clipboard);
            _session.Apply();
            EndEdit();
            RefreshAll();
        }

        private void OpenMore(RectTransform anchor)
        {
            KitMenu m = new KitMenu();
            m.Item("Reload recipes", () =>
            {
                FaceSessions.Reload(true);
                Follow(true);
            });
            Vector3[] c = new Vector3[4];
            anchor.GetWorldCorners(c);
            KitContextMenu.Show(m, RectTransformUtility.WorldToScreenPoint(null, c[0]));
        }

        // ---------------------------------------------------------------- presets

        private KitPopover _popover;
        private readonly List<Texture2D> _thumbs = new List<Texture2D>();
        private static bool _saveEyeLook;
        private const float TileW = 96f;

        private void OpenPresets(RectTransform anchor)
        {
            if (_session == null)
                return;
            if (_popover != null && _popover.IsOpen)
            {
                _popover.Close();
                return;
            }
            _popover = KitPopover.Open(4f * TileW + 3f * 8f + 48f);
            _popover.Closed = FreeThumbs;
            RectTransform c = _popover.Content;

            RectTransform top = KitLayout.Row(c, 8f);
            Kit.Layout(top.gameObject, -1f, 32f);
            KitButton save = KitButton.Create(top, "Save current face...", ButtonStyle.Secondary, SavePrompt, Sprites.Plus);
            Kit.Layout(save.gameObject, 190f, 30f);
            KitCheckbox look = KitCheckbox.Create(top, "Eye direction", _saveEyeLook, on => _saveEyeLook = on);
            Kit.Layout(look.gameObject, 150f, 28f);
            KitTooltip.Attach(look.gameObject, "Also store where the eyes look (Studio's eye-look setting)");

            List<FacePreset> defaults = FacePresets.List(true), yours = FacePresets.List(false);
            int rows = Rows(defaults.Count) + Rows(yours.Count);
            RectTransform holder = Kit.Rect(c, "Presets");
            Kit.Layout(holder.gameObject, -1f, Mathf.Min(430f, 2f * 26f + Mathf.Max(1, rows) * (TileW + 38f)));
            KitScroll scroll = KitScroll.Create(holder, 0, 0, 6f, true, true);

            KitLayout.Header(scroll.Content, "Defaults");
            Grid(scroll.Content, defaults);
            KitLayout.Header(scroll.Content, "Yours");
            if (yours.Count == 0)
                KitLayout.Label(scroll.Content, "None yet - set up a face and press \"Save current face\".", TextRole.Dim);
            else
                Grid(scroll.Content, yours);
            _popover.Place(anchor);
        }

        private static int Rows(int n) { return (n + 3) / 4; }

        private void Grid(RectTransform parent, List<FacePreset> presets)
        {
            RectTransform grid = Kit.Rect(parent, "Grid");
            GridLayoutGroup g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(TileW, TileW + 30f);
            g.spacing = new Vector2(8f, 8f);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 4;
            g.padding = new RectOffset(6, 6, 4, 4); // room for the hover scale: the list's mask clipped the left tile
            foreach (FacePreset p in presets)
            {
                FacePreset preset = p;
                Texture2D tex = preset.Default ? null : LoadThumb(preset.Name);
                KitImageButton card = KitImageButton.CreateCard(grid, tex, new Vector2(TileW, TileW), preset.Name, null, () => ApplyPreset(preset));
                if (tex == null)
                    Glyph(card, preset.Input);
                KitTooltip.Attach(card.gameObject, preset.Default ? "Default preset - click to apply" : "Click to apply · right-click: rename, overwrite, delete");
                if (preset.Default == false)
                    KitContextMenu.Attach(card.gameObject, () => new KitMenu()
                        .Item("Rename...", () => RenamePrompt(preset.Name))
                        .Item("Overwrite with the current face", () => SavePreset(preset.Name))
                        .Separator()
                        .Danger("Delete \"" + preset.Name + "\"", () => DeletePreset(preset.Name)));
            }
        }

        /// <summary>Presets without a photo (the defaults) show where their mood sits on the pad.</summary>
        private static void Glyph(KitImageButton card, FaceInput input)
        {
            Transform frame = card.Rect.Find("Frame");
            RectTransform host = Kit.Rect(frame != null ? frame : card.Rect, "Glyph");
            Kit.Fill(host, 14f, 14f, 14f, 14f);
            Image pad = Kit.Image(host, "Pad", MoodPad.PadSprite, Theme.TextDim);
            Kit.Fill(pad.rectTransform);
            pad.raycastTarget = false;
            Image dot = Kit.Image(host, "Dot", Sprites.Circle, Theme.Accent);
            dot.raycastTarget = false;
            float a = input.Angle * Mathf.Deg2Rad;
            float r = input.Radius * (TileW - 28f) * 0.42f;
            Kit.Anchor(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, new Vector2(10f, 10f));
        }

        private Texture2D LoadThumb(string name)
        {
            string path = FacePresets.ThumbnailPath(name);
            if (System.IO.File.Exists(path) == false)
                return null;
            Texture2D t = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (t.LoadImage(System.IO.File.ReadAllBytes(path)) == false)
            {
                Destroy(t);
                return null;
            }
            _thumbs.Add(t);
            return t;
        }

        private void FreeThumbs()
        {
            foreach (Texture2D t in _thumbs)
                if (t != null)
                    Destroy(t);
            _thumbs.Clear();
        }

        private void ApplyPreset(FacePreset p)
        {
            if (_session == null || BeginEdit() == false)
                return;
            if (_popover != null)
                _popover.Close();
            FacePresets.Apply(p, _session);
            EndEdit();
            RefreshAll();
            KitToast.Show("Face: " + p.Name, "Applied" + (p.Tears >= 0 || p.Blush >= 0f ? " (with tears / blush)" : ""), ToastKind.Success, 3f, "Undo", Undo);
        }

        private void SavePrompt()
        {
            KitModal.Prompt("Save face preset", "A name for this face.", "", "Name", "Save", text =>
            {
                string name = FacePresets.CleanName(text);
                if (name.Length == 0)
                    return;
                if (FacePresets.Exists(name))
                    KitModal.Show("Overwrite?", "A face preset called \"" + name + "\" exists. Overwrite it?", new[] { "Overwrite", "Cancel" }, i => { if (i == 0) SavePreset(name); }, true);
                else
                    SavePreset(name);
            });
        }

        private void SavePreset(string name)
        {
            if (_session == null)
                return;
            if (_popover != null)
                _popover.Close();
            FacePresets.Save(name, _session, _saveEyeLook, Camera.main);
            KitToast.Show("Face preset saved", name, ToastKind.Success, 2.5f);
        }

        private void RenamePrompt(string from)
        {
            KitModal.Prompt("Rename face preset", "New name for \"" + from + "\".", from, "Name", "Rename", text =>
            {
                string to = FacePresets.CleanName(text);
                if (to.Length == 0 || to == from)
                    return;
                if (FacePresets.Exists(to))
                {
                    KitToast.Show("Rename", "\"" + to + "\" already exists", ToastKind.Warning, 3f);
                    return;
                }
                FacePresets.Rename(from, to);
                if (_popover != null)
                    _popover.Close();
            });
        }

        private void DeletePreset(string name)
        {
            KitModal.Show("Delete face preset", "Delete \"" + name + "\" and its thumbnail?", new[] { "Delete", "Cancel" }, i =>
            {
                if (i != 0)
                    return;
                FacePresets.Delete(name);
                if (_popover != null)
                    _popover.Close();
            }, true);
        }

        // ---------------------------------------------------------------- display

        private void RefreshAll()
        {
            _diagram.Refresh();
            _pad.Refresh();
            if (_session != null)
                _vowels.Select(IndexOfVowel(_session.Input.Vowel), false);
            RefreshSliders();
            RefreshMix();
            RefreshExtras();
            RefreshButtons();
        }

        private void RefreshButtons()
        {
            bool u = _session != null && _session.Undo.CanUndo, r = _session != null && _session.Undo.CanRedo;
            _undo.Icon.color = u ? Theme.Text : Kit.Alpha(Theme.TextDim, 0.4f);
            _redo.Icon.color = r ? Theme.Text : Kit.Alpha(Theme.TextDim, 0.4f);
        }

        private readonly List<FaceEntry> _moods = new List<FaceEntry>();
        private int _mixA = -2, _mixB = -2, _mixPa, _mixPb;
        private List<FaceEntry> _mixMoods;

        private void RefreshMix()
        {
            if (_session == null)
            {
                _mix.text = "";
                return;
            }
            FaceMix.Moods(FaceSessions.Recipes, _session.Rig.Sex, _moods);
            int a, b;
            float wa, wb;
            FaceMix.PadWeights(_moods, _session.Input.Angle, _session.Input.Radius, out a, out b, out wa, out wb);
            // text only when what it says changed (Timeline playback refreshes 5x/s, drags every event)
            int pa = a < 0 ? -1 : Mathf.RoundToInt(wa * 100f), pb = b < 0 ? -1 : Mathf.RoundToInt(wb * 100f);
            if (a == _mixA && b == _mixB && pa == _mixPa && pb == _mixPb && ReferenceEquals(_moods, _mixMoods) && _mix.text.Length != 0)
                return;
            _mixA = a; _mixB = b; _mixPa = pa; _mixPb = pb; _mixMoods = _moods;
            if (a < 0)
                _mix.text = "Neutral";
            else if (b == a || wb < 0.005f)
                _mix.text = _moods[a].Name + "  " + Mathf.RoundToInt(wa * 100f) + "%";
            else if (wa < 0.005f)
                _mix.text = _moods[b].Name + "  " + Mathf.RoundToInt(wb * 100f) + "%";
            else
                _mix.text = _moods[a].Name + " " + Mathf.RoundToInt(wa * 100f) + "% + " + _moods[b].Name + " " + Mathf.RoundToInt(wb * 100f) + "%";
        }
    }
}
