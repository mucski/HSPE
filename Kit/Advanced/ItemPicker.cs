using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Advanced
{
    using HSUIKit;

    /// <summary>
    /// Picks the item a module page edits (solver, collider, dynamic bone, mesh) from a dropdown in the page's toolbar
    /// instead of a list. One item = a plain label.
    /// Changed items are marked "●". Options are only rebuilt when the items or their changed state differ.
    /// </summary>
    internal sealed class ItemPicker
    {
        public RectTransform Root;
        public IntHandler OnPicked;
        private KitDropdown _dropdown;
        private Text _single;
        private string _signature;
        private int _selected = -2;
        private int _fingerprint;
        private bool _hasFingerprint;

        /// <summary>
        /// Cheap early-out: pages hash their items / selection / changed state into an int and only build label strings
        /// when it differs (building them 2-3 times a second was garbage for nothing).
        /// </summary>
        public bool Changed(int fingerprint)
        {
            if (_hasFingerprint && fingerprint == _fingerprint)
                return false;
            _hasFingerprint = true;
            _fingerprint = fingerprint;
            return true;
        }

        public static ItemPicker Create(Transform parent, string tooltip)
        {
            ItemPicker p = new ItemPicker();
            p.Root = Kit.Rect(parent, "Picker");
            LayoutElement le = Kit.Layout(p.Root.gameObject, -1f, 32f);
            le.flexibleWidth = 1f;
            le.minWidth = 120f;
            p._dropdown = KitDropdown.Create(p.Root, new List<string> { "" }, 0, i => { if (p.OnPicked != null) p.OnPicked(i); });
            Kit.IgnoreLayout(p._dropdown.gameObject);
            Kit.Fill((RectTransform)p._dropdown.transform);
            p._dropdown.SearchThreshold = 10;
            if (tooltip != null)
                KitTooltip.Attach(p._dropdown.gameObject, tooltip);
            p._single = Kit.MakeText(p.Root, "Single", "", Theme.FontSize + 1, Theme.Text, TextAnchor.MiddleLeft);
            p._single.fontStyle = FontStyle.Bold;
            p._single.horizontalOverflow = HorizontalWrapMode.Wrap;
            p._single.verticalOverflow = VerticalWrapMode.Truncate;
            Kit.Fill(p._single.rectTransform, 4f, 0f, 4f, 0f);
            return p;
        }

        /// <summary>labels[i] of item i; dirty[i] marks it changed; selected = index or -1.</summary>
        public void Set(List<string> labels, List<bool> dirty, int selected, string emptyText)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < labels.Count; i++)
                sb.Append(labels[i]).Append(dirty[i] ? '1' : '0').Append('|');
            string sig = sb.ToString();
            if (sig == _signature && selected == _selected)
                return;
            _signature = sig;
            _selected = selected;

            if (labels.Count <= 1)
            {
                _dropdown.gameObject.SetActive(false);
                _single.gameObject.SetActive(true);
                _single.text = labels.Count == 1 ? (dirty[0] ? "● " : "") + labels[0] : emptyText;
                _single.color = labels.Count == 1 ? Theme.Text : Theme.TextDim;
                return;
            }
            List<string> options = new List<string>(labels.Count);
            for (int i = 0; i < labels.Count; i++)
                options.Add((dirty[i] ? "● " : "") + labels[i]);
            _single.gameObject.SetActive(false);
            _dropdown.gameObject.SetActive(true);
            _dropdown.SetOptions(options, Mathf.Max(0, selected));
        }
    }

    /// <summary>Shared small helpers of the module pages.</summary>
    internal static class PageUi
    {
        /// <summary>The ⋯ button of a page toolbar; the menu is built when clicked.</summary>
        public static KitIconButton MoreButton(Transform parent, string tooltip, MenuBuilder build)
        {
            KitIconButton b = null;
            b = KitIconButton.Create(parent, global::HSPE.Kit.Small.HspeIcons.More, 30f, false, () =>
            {
                KitMenu m = build();
                if (m != null)
                    MenuAt(m, b.Rect);
            });
            KitTooltip.Attach(b.gameObject, tooltip);
            return b;
        }

        public static void MenuAt(KitMenu m, RectTransform anchor)
        {
            Vector3[] c = new Vector3[4];
            anchor.GetWorldCorners(c);
            KitContextMenu.Show(m, RectTransformUtility.WorldToScreenPoint(null, c[0]));
        }

        /// <summary>A toolbar row across the top of a page.</summary>
        public static RectTransform Toolbar(RectTransform parent, float top)
        {
            RectTransform row = KitLayout.Row(parent, 6f);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, 34f);
            row.anchoredPosition = new Vector2(0f, -top);
            return row;
        }
    }

    /// <summary>
    /// Two columns (right | left) side by side when the page is wide enough, stacked otherwise - the Boobs and the
    /// full-body IK pages cut the left column off in a narrow window.
    /// </summary>
    internal sealed class ResponsiveColumns : MonoBehaviour
    {
        public float MinSideBySide = 520f;
        public RectTransform[] Columns;
        private int _mode = -1; // 0 stacked, 1 side by side

        public static ResponsiveColumns Create(Transform parent, float minSideBySide)
        {
            RectTransform root = Kit.Rect(parent, "Columns");
            ResponsiveColumns rc = root.gameObject.AddComponent<ResponsiveColumns>();
            rc.MinSideBySide = minSideBySide;
            rc.Columns = new RectTransform[2];
            for (int i = 0; i < 2; i++)
            {
                rc.Columns[i] = Kit.Rect(root, i == 0 ? "Right" : "Left");
                VerticalLayoutGroup v = rc.Columns[i].gameObject.AddComponent<VerticalLayoutGroup>();
                v.spacing = 4f;
                v.childForceExpandWidth = true;
                v.childForceExpandHeight = false;
                rc.Columns[i].gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }
            rc.Apply(1);
            return rc;
        }

        /// <summary>
        /// Side by side only when both columns really fit: their measured minimum widths (+20% so sliders aren't at
        /// their minimum). A fixed pixel threshold failed: the kit canvas is scaled, so the page measured wider
        /// than it looked and the left column was cut off.
        /// </summary>
        private void LateUpdate()
        {
            float w = ((RectTransform)transform).rect.width;
            if (w <= 0f)
                return;
            float need = (LayoutUtility.GetMinWidth(Columns[0]) + LayoutUtility.GetMinWidth(Columns[1])) * 1.2f + 12f;
            int mode = w >= Mathf.Max(need, MinSideBySide) ? 1 : 0;
            if (mode != _mode)
                Apply(mode);
        }

        private void Apply(int mode)
        {
            _mode = mode;
            // one LayoutGroup per GameObject: swap the group itself
            HorizontalOrVerticalLayoutGroup old = GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (old != null)
                DestroyImmediate(old);
            HorizontalOrVerticalLayoutGroup g = mode == 1 ? (HorizontalOrVerticalLayoutGroup)gameObject.AddComponent<HorizontalLayoutGroup>() : gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = mode == 1 ? 12f : 10f;
            g.childForceExpandWidth = true;
            g.childForceExpandHeight = false;
            g.childAlignment = TextAnchor.UpperLeft;
            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)transform);
        }
    }
}
