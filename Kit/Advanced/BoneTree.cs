using System;
using System.Collections.Generic;
using HSPE.AMModules;
using HSPE.Kit.Small;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HSPE.Kit.Advanced
{
    using HSUIKit;

    /// <summary>
    /// The Bones page's tree: the object's transform hierarchy (★ marks favourites, listed beside it). Only the
    /// rows in view exist (a pool re-bound while scrolling), so characters with thousands of bones stay fast. Opened
    /// nodes are Joan's own _openedBones, so the classic window shows the same tree. Search = flat list of every
    /// matching bone (name or alias); "Changed" = flat list of the bones HSPE holds values for.
    /// </summary>
    internal sealed class BoneTree : MonoBehaviour
    {
        private const float RowH = 26f, Indent = 14f, Pad = 2f;

        private struct Item
        {
            public Transform t;
            public int depth;
            public bool hasChildren;
            public string hint;
        }

        private sealed class Row : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerClickHandler
        {
            public BoneTree owner;
            public RectTransform rect;
            public Image bg, chevron, dot, star;
            public Text text, hint;
            public int index = -1;
            public bool hover;

            public void OnPointerEnter(PointerEventData e) { hover = true; owner.Paint(this); }
            public void OnPointerExit(PointerEventData e) { hover = false; owner.Paint(this); }
            public void OnPointerDown(PointerEventData e) { }

            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Left)
                    owner.Clicked(this, e);
                else if (e.button == PointerEventData.InputButton.Right)
                    owner.RightClicked(this, e.position);
            }
        }

        public BonesEditor bones;
        public MenuBuilder RowMenu; // built for the row's bone (it is selected first)

        private KitScroll _scroll;
        private Text _empty;
        private readonly List<Item> _items = new List<Item>();
        private readonly List<Row> _pool = new List<Row>();
        private List<Transform> _allBones;
        private string _search = "";
        private bool _changedOnly;
        private bool _dirty = true, _needsRebuild;
        private float _lastY = -1f, _lastViewH = -1f, _nextRepaint;
        private Transform _shownTarget;
        private readonly Dictionary<Transform, string> _names = new Dictionary<Transform, string>(); // display names, reset by Rebuild

        public static BoneTree Create(RectTransform parent)
        {
            RectTransform root = Kit.Rect(parent, "BoneTree");
            Kit.Fill(root);
            Image bg = root.gameObject.AddComponent<Image>();
            Kit.SetSprite(bg, Sprites.Rounded(Theme.RadiusSmall));
            KitThemeColor.Bind(bg, () => Theme.ListBg);
            bg.raycastTarget = false;
            BoneTree tree = root.gameObject.AddComponent<BoneTree>();
            Theme.Changed += () => { if (tree != null) tree._dirty = true; };   // rows pick their text colours on redraw
            tree._scroll = KitScroll.Create(root, 0, 0, 0f, true, false);
            tree._empty = Kit.MakeText(root, "Empty", "", Theme.FontSize, Theme.TextDim, TextAnchor.MiddleCenter);
            Kit.Fill(tree._empty.rectTransform);
            tree._empty.fontStyle = FontStyle.Italic;
            return tree;
        }

        // ---------------------------------------------------------------- items

        public void Bind(BonesEditor editor)
        {
            bones = editor;
            _allBones = null;
            _shownTarget = null;
            Rebuild();
            _scroll.ScrollToTop();
        }

        public void SetSearch(string s)
        {
            s = (s ?? "").Trim();
            if (s == _search)
                return;
            _search = s;
            Rebuild();
            _scroll.ScrollToTop();
        }

        public void SetChangedOnly(bool on)
        {
            _changedOnly = on;
            Rebuild();
            _scroll.ScrollToTop();
        }

        /// <summary>Re-reads the hierarchy (clothes / accessories changed, a favourite added, ...).</summary>
        public void Rebuild()
        {
            _items.Clear();
            _names.Clear(); // aliases may have changed
            _dirty = true;
            if (bones == null)
                return;
            Transform root = bones.kitRoot;
            if (_search.Length != 0)
            {
                if (_allBones == null)
                {
                    _allBones = new List<Transform>();
                    foreach (Transform child in root)
                        Collect(child, _allBones);
                }
                foreach (Transform t in _allBones)
                {
                    if (t == null)
                        continue;
                    string alias = BonesEditor.KitAlias(t.name);
                    if (t.name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 || (alias != null && alias.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0))
                        _items.Add(new Item { t = t, hint = t.parent != null ? t.parent.name : "" });
                }
                _empty.text = "No bone matches \"" + _search + "\"";
            }
            else if (_changedOnly)
            {
                List<Transform> dirty = bones.KitDirtyBones();
                dirty.Sort((a, b) => string.CompareOrdinal(bones.KitPath(a), bones.KitPath(b)));
                foreach (Transform t in dirty)
                    _items.Add(new Item { t = t, hint = t.parent != null ? t.parent.name : "" });
                _empty.text = "Nothing changed on this object yet";
            }
            else
            {
                // favourites live in their own column now (FavouritesPanel), the tree is the hierarchy only
                foreach (Transform child in root)
                    Walk(child, 0);
                _empty.text = "";
            }
            _empty.enabled = _items.Count == 0;
            _scroll.Content.sizeDelta = new Vector2(0f, _items.Count * RowH + Pad * 2f);
        }

        private void Collect(Transform t, List<Transform> list)
        {
            if (bones.KitIsHidden(t.gameObject))
                return;
            list.Add(t);
            foreach (Transform child in t)
                Collect(child, list);
        }

        private void Walk(Transform t, int depth)
        {
            if (bones.KitIsHidden(t.gameObject))
                return;
            bool hasChildren = false;
            foreach (Transform child in t)
                if (bones.KitIsHidden(child.gameObject) == false)
                {
                    hasChildren = true;
                    break;
                }
            _items.Add(new Item { t = t, depth = depth, hasChildren = hasChildren });
            if (hasChildren && bones.KitIsOpen(t.gameObject))
                foreach (Transform child in t)
                    Walk(child, depth + 1);
        }

        /// <summary>Opens the bone's parents and scrolls it into view.</summary>
        public void Reveal(Transform t)
        {
            if (t == null)
                return;
            if (_search.Length == 0 && _changedOnly == false)
            {
                bones.KitReveal(t.gameObject);
                Rebuild();
            }
            int index = -1;
            for (int i = 0; i < _items.Count; i++)
                if (_items[i].t == t)
                {
                    index = i;
                    break;
                }
            if (index < 0)
                return;
            float view = _scroll.ViewportHeight;
            float top = Pad + index * RowH;
            Vector2 p = _scroll.Content.anchoredPosition;
            if (top < p.y || top + RowH > p.y + view)
                p.y = Mathf.Max(0f, top - view * 0.4f);
            _scroll.Content.anchoredPosition = p;
            _dirty = true;
        }

        // ---------------------------------------------------------------- rows

        private void LateUpdate()
        {
            if (bones == null)
                return;
            if (_needsRebuild)
            {
                _needsRebuild = false;
                Rebuild();
            }
            // selection changed elsewhere (a click here, Joan's "go to bone" from colliders, the classic window)
            if (bones.kitTarget != _shownTarget)
            {
                _shownTarget = bones.kitTarget;
                _dirty = true;
            }
            float y = _scroll.Content.anchoredPosition.y;
            float vh = _scroll.ViewportHeight;
            bool moved = !Mathf.Approximately(y, _lastY) || !Mathf.Approximately(vh, _lastViewH);
            // changed marks follow edits a few times a second
            if (!_dirty && !moved && Time.unscaledTime < _nextRepaint)
                return;
            _dirty = false;
            _nextRepaint = Time.unscaledTime + 0.25f;
            _lastY = y;
            _lastViewH = vh;

            int first = Mathf.Max(0, Mathf.FloorToInt((y - Pad) / RowH));
            int visible = Mathf.CeilToInt(vh / RowH) + 2;
            while (_pool.Count < visible)
                _pool.Add(CreateRow());
            for (int j = 0; j < _pool.Count; j++)
            {
                Row row = _pool[j];
                int i = first + j;
                if (j >= visible || i >= _items.Count)
                {
                    if (row.gameObject.activeSelf)
                        row.gameObject.SetActive(false);
                    row.index = -1;
                    continue;
                }
                if (!row.gameObject.activeSelf)
                    row.gameObject.SetActive(true);
                row.index = i;
                row.rect.anchoredPosition = new Vector2(0f, -(Pad + i * RowH));
                Bind(row, _items[i]);
            }
        }

        private Row CreateRow()
        {
            RectTransform rt = Kit.Rect(_scroll.Content, "Row");
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-8f, RowH - 2f);
            Row r = rt.gameObject.AddComponent<Row>();
            r.owner = this;
            r.rect = rt;
            r.bg = rt.gameObject.AddComponent<Image>();
            Kit.SetSprite(r.bg, Sprites.Rounded(Mathf.Max(0, Theme.RadiusSmall - 2)));
            r.bg.color = Color.clear;
            r.chevron = Kit.Image(rt, "Chevron", Sprites.Chevron, Theme.TextDim);
            Kit.Anchor(r.chevron.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(10f, 0f), new Vector2(12f, 12f));
            r.dot = Kit.Image(rt, "Changed", Sprites.Circle, Theme.Warning);
            Kit.Anchor(r.dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22f, 0f), new Vector2(6f, 6f));
            r.text = Kit.MakeText(rt, "Name", "", Theme.FontSize, Theme.Text, TextAnchor.MiddleLeft);
            r.text.horizontalOverflow = HorizontalWrapMode.Wrap;
            r.text.verticalOverflow = VerticalWrapMode.Truncate;
            r.hint = Kit.MakeText(rt, "Hint", "", Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleRight);
            r.hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            r.hint.verticalOverflow = VerticalWrapMode.Truncate;
            r.hint.rectTransform.anchorMin = new Vector2(1f, 0f);
            r.hint.rectTransform.anchorMax = new Vector2(1f, 1f);
            r.hint.rectTransform.pivot = new Vector2(1f, 0.5f);
            r.hint.rectTransform.sizeDelta = new Vector2(96f, 0f);
            r.hint.rectTransform.anchoredPosition = new Vector2(-8f, 0f);
            r.star = Kit.Image(rt, "Star", HspeIcons.Star, Theme.Warning);
            Kit.Anchor(r.star.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(12f, 12f));
            return r;
        }

        private void Bind(Row r, Item it)
        {
            Transform t = it.t;
            if (t == null)
            {
                _needsRebuild = true; // destroyed (clothes / accessory changed): rebuild next frame, not mid-loop
                r.text.text = "";
                return;
            }
            float x = 4f + it.depth * Indent;
            r.chevron.enabled = it.hasChildren;
            if (it.hasChildren)
            {
                r.chevron.rectTransform.anchoredPosition = new Vector2(x + 6f, 0f);
                r.chevron.rectTransform.localEulerAngles = new Vector3(0f, 0f, bones.KitIsOpen(t.gameObject) ? 0f : 90f);
            }
            bool changed = bones.KitIsDirty(t.gameObject);
            r.dot.enabled = changed;
            r.dot.rectTransform.anchoredPosition = new Vector2(x + 18f, 0f);
            bool fav = bones.KitIsFavourite(t);
            r.star.enabled = fav;
            string hint = it.hint ?? "";
            if (bones.KitIsCollider(t))
                hint = ColliderHint(hint);
            r.hint.text = hint;
            r.hint.rectTransform.anchoredPosition = new Vector2(fav ? -24f : -8f, 0f);
            string name;
            if (_names.TryGetValue(t, out name) == false)
                _names.Add(t, name = BoneNames.Display(t));
            if (r.text.text != name)
                r.text.text = name;
            r.text.fontSize = Theme.FontSize;
            Insets(r.text, x + 26f, hint.Length != 0 ? 104f : (fav ? 26f : 8f));
            bool selected = t == bones.kitTarget;
            r.text.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
            r.text.color = selected ? Color.white : Theme.Text;
            Paint(r);
        }

        private static readonly Dictionary<string, string> _colliderHints = new Dictionary<string, string>();

        /// <summary>"x · collider", built once per hint (rows repaint 4x/s).</summary>
        private static string ColliderHint(string hint)
        {
            if (hint.Length == 0)
                return "collider";
            string s;
            if (_colliderHints.TryGetValue(hint, out s) == false)
                _colliderHints.Add(hint, s = hint + " · collider");
            return s;
        }

        private static void Insets(Text t, float left, float right)
        {
            RectTransform rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(left, 0f);
            rt.offsetMax = new Vector2(-right, 0f);
        }

        private void Paint(Row r)
        {
            if (r.index < 0 || r.index >= _items.Count)
                return;
            Item it = _items[r.index];
            bool selected = it.t == bones.kitTarget;
            Color c = selected ? Kit.Alpha(Theme.Accent, r.hover ? 0.3f : 0.22f) : (r.hover ? Theme.SurfaceHover : Color.clear);
            Tween.Tint(r.bg, c, 0.1f, Ease.OutQuad);
        }

        private void Clicked(Row r, PointerEventData e)
        {
            if (r.index < 0 || r.index >= _items.Count)
                return;
            Item it = _items[r.index];
            if (it.t == null)
                return;
            // the chevron (or a double-click) opens / closes; anything else selects
            Vector2 local = Kit.ScreenToLocal(r.rect, e.position);
            float chevronX = r.rect.rect.xMin + 4f + it.depth * Indent + 6f;
            bool onChevron = it.hasChildren && Mathf.Abs(local.x - chevronX) < 10f;
            if (onChevron || (e.clickCount >= 2 && it.hasChildren))
            {
                bones.KitSetOpen(it.t.gameObject, !bones.KitIsOpen(it.t.gameObject));
                Rebuild();
                if (onChevron)
                    return;
            }
            if (it.t != bones.kitTarget)
            {
                bones.KitSelect(it.t);
                _dirty = true;
            }
        }

        private void RightClicked(Row r, Vector2 screen)
        {
            if (r.index < 0 || r.index >= _items.Count || RowMenu == null)
                return;
            Item it = _items[r.index];
            if (it.t == null)
                return;
            if (it.t != bones.kitTarget)
            {
                bones.KitSelect(it.t);
                _dirty = true;
            }
            KitMenu menu = RowMenu();
            if (menu != null)
                KitContextMenu.Show(menu, screen);
        }
    }

    /// <summary>Readable bone names: Joan's alias if set, else the name without the "cf_J_" style prefix.</summary>
    internal static class BoneNames
    {
        public static string Display(Transform t)
        {
            string alias = BonesEditor.KitAlias(t.name);
            return alias ?? Readable(t.name);
        }

        public static string Readable(string name)
        {
            string n = name;
            // cf_J_Hips / cm_J_ArmUp00_L / cf_s_... / cf_N_height
            if (n.Length > 5 && (n.StartsWith("cf_") || n.StartsWith("cm_")) && n[4] == '_')
                n = n.Substring(5);
            else if (n.Length > 3 && (n.StartsWith("cf_") || n.StartsWith("cm_")))
                n = n.Substring(3);
            string side = "";
            if (n.EndsWith("_L")) { side = " (L)"; n = n.Substring(0, n.Length - 2); }
            else if (n.EndsWith("_R")) { side = " (R)"; n = n.Substring(0, n.Length - 2); }
            n = n.Replace('_', ' ').Trim();
            return n.Length == 0 ? name : n + side;
        }
    }
}
