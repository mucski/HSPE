using System.Collections.Generic;
using Studio;
using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Small
{
    using HSUIKit;

    /// <summary>
    /// The FK tab's More page: every FK bone that is not on the Body or Hands figure, grouped by Studio's FK groups
    /// (hair, breasts, skirt, other). Rows of groups Studio has switched off are dimmed, as in Joan's list.
    /// </summary>
    internal sealed class FkMoreList : MonoBehaviour
    {
        private const float RowH = 26f, HeaderH = 24f;

        private sealed class Row
        {
            public OCIChar.BoneInfo bone;
            public string label;
            public RectTransform rect;
            public Image bg;
            public Text text;
            public bool hover, selected, enabled;
            public bool drawn, shownHover, shownSelected, shownEnabled;
        }

        private sealed class Header
        {
            public Text text;
            public string title;
            public int first, count;
            public bool shownOff, drawn;
        }

        private KitScroll _scroll;
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<Header> _headers = new List<Header>();

        public static FkMoreList Create(Transform parent)
        {
            RectTransform root = Kit.Rect(parent, "FK More");
            Kit.Fill(root);
            FkMoreList l = root.gameObject.AddComponent<FkMoreList>();
            l._scroll = KitScroll.Create(root, 2, 2, 2f, true);
            return l;
        }

        public void Bind(Dictionary<string, OCIChar.BoneInfo> map, HashSet<string> placed)
        {
            for (int i = _scroll.Content.childCount - 1; i >= 0; i--)
                Destroy(_scroll.Content.GetChild(i).gameObject);
            _rows.Clear();
            _headers.Clear();

            // plain string keys (sorted with StringComparer): no generic delegate over a value type on old Mono
            Dictionary<FkBones.Group, List<string>> groups = new Dictionary<FkBones.Group, List<string>>();
            foreach (KeyValuePair<string, OCIChar.BoneInfo> p in map)
            {
                if (placed.Contains(p.Key))
                    continue;
                FkBones.Group g = FkBones.GroupOf(p.Value);
                List<string> list;
                if (groups.TryGetValue(g, out list) == false)
                    groups.Add(g, list = new List<string>());
                list.Add(p.Key);
            }

            FkBones.Group[] order = { FkBones.Group.Hair, FkBones.Group.Breasts, FkBones.Group.Skirt, FkBones.Group.Other };
            foreach (FkBones.Group g in order)
            {
                List<string> list;
                if (groups.TryGetValue(g, out list) == false)
                    continue;
                list.Sort(System.StringComparer.Ordinal);
                Header h = new Header { title = FkBones.GroupLabel(g) + "  " + list.Count, first = _rows.Count, count = list.Count };
                h.text = Kit.MakeText(_scroll.Content, "Header", h.title, Theme.SmallSize, Theme.TextDim, TextAnchor.LowerLeft);
                h.text.fontStyle = FontStyle.Bold;
                Kit.Layout(h.text.gameObject, -1f, HeaderH);
                _headers.Add(h);
                foreach (string key in list)
                    AddRow(map[key]);
            }
            if (_rows.Count == 0)
            {
                Text t = Kit.MakeText(_scroll.Content, "Empty", "No other FK bones.", Theme.FontSize, Theme.TextDim, TextAnchor.MiddleCenter);
                Kit.Layout(t.gameObject, -1f, 40f);
            }
            _scroll.ScrollToTop();
        }

        private void AddRow(OCIChar.BoneInfo bone)
        {
            string name = bone.guideObject.transformTarget != null ? bone.guideObject.transformTarget.name : "?";
            Row r = new Row { bone = bone, label = FkBones.Readable(name) };
            r.rect = Kit.Rect(_scroll.Content, "Row " + name);
            Kit.Layout(r.rect.gameObject, -1f, RowH);
            r.bg = Kit.Image(r.rect, "Bg", Sprites.Rounded(Theme.RadiusSmall), Kit.Alpha(Theme.SurfaceHover, 0f), true);
            Kit.Fill(r.bg.rectTransform);
            r.text = Kit.MakeText(r.rect, "Label", r.label, Theme.FontSize, Theme.Text, TextAnchor.MiddleLeft);
            r.text.horizontalOverflow = HorizontalWrapMode.Wrap;
            r.text.verticalOverflow = VerticalWrapMode.Truncate;
            Kit.Fill(r.text.rectTransform, 10f, 0f, 6f, 0f);
            KitHoverClick hc = KitHoverClick.On(r.bg.gameObject);
            hc.OnEnter = delegate { r.hover = true; };
            hc.OnExit = delegate { r.hover = false; };
            hc.OnClick = delegate
            {
                if (r.enabled)
                    FkBones.Select(r.bone);
            };
            KitTooltip.Attach(r.bg.gameObject, name);
            _rows.Add(r);
        }

        public bool Has(GuideObject guide)
        {
            return RowOf(guide) != null;
        }

        public string LabelOf(GuideObject guide)
        {
            Row r = RowOf(guide);
            return r != null ? r.label : null;
        }

        private Row RowOf(GuideObject guide)
        {
            if (guide == null)
                return null;
            foreach (Row r in _rows)
                if (r.bone.guideObject == guide)
                    return r;
            return null;
        }

        /// <summary>Scrolls to the row of a bone picked in the viewport (Joan's list did the same).</summary>
        public void Reveal(GuideObject guide)
        {
            Row r = RowOf(guide);
            if (r != null)
            {
                Canvas.ForceUpdateCanvases();
                _scroll.ScrollIntoView(r.rect);
            }
        }

        /// <summary>False when a listed bone is gone (hair / clothes changed): the window rebinds.</summary>
        public bool Refresh(PoseController target, GuideObject selected)
        {
            bool valid = true;
            foreach (Row r in _rows)
            {
                if (r.bone.guideObject == null)
                {
                    valid = false;
                    continue;
                }
                r.enabled = FkBones.Usable(target, r.bone);
                r.selected = selected != null && r.bone.guideObject == selected;
                Draw(r);
            }
            foreach (Header h in _headers)
            {
                bool off = true;
                for (int i = h.first; i < h.first + h.count && i < _rows.Count; i++)
                    if (_rows[i].enabled)
                        off = false;
                if (h.drawn && h.shownOff == off)
                    continue;
                h.drawn = true;
                h.shownOff = off;
                h.text.text = h.title + (off ? "   (off in Studio's FK panel)" : "");
            }
            return valid;
        }

        private static void Draw(Row r)
        {
            if (r.drawn && r.shownSelected == r.selected && r.shownEnabled == r.enabled && r.shownHover == r.hover)
                return;
            r.drawn = true;
            r.shownSelected = r.selected;
            r.shownEnabled = r.enabled;
            r.shownHover = r.hover;
            Color bg = r.selected ? Theme.Accent : (r.hover && r.enabled ? Kit.Alpha(Theme.SurfaceHover, 0.8f) : Kit.Alpha(Theme.SurfaceHover, 0f));
            Tween.Tint(r.bg, bg, 0.12f, Ease.OutQuad);
            r.text.color = Kit.Alpha(r.selected ? Color.white : Theme.Text, r.enabled ? 1f : 0.35f);
            r.text.fontStyle = r.selected ? FontStyle.Bold : FontStyle.Normal;
        }
    }
}
