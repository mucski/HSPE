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
    /// The Bones page's right column: Joan's shortcuts as a list of their own (his old panel had them beside the
    /// tree). Built-in ones (hands, feet, face) first, then your own. Click = select, double-click or right-click
    /// = rename (Joan's alias), × = remove, + = add the selected bone. Rows are rebuilt only when the set changes.
    /// </summary>
    internal sealed class FavouritesPanel : MonoBehaviour
    {
        public const float Width = 190f;
        private const float RowH = 26f;

        private sealed class Row : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerClickHandler
        {
            public FavouritesPanel owner;
            public Transform bone;
            public bool builtIn, hover;
            public Image bg;
            public Text text;
            public KitIconButton remove;

            public void OnPointerEnter(PointerEventData e) { hover = true; owner.Paint(this); }
            public void OnPointerExit(PointerEventData e) { hover = false; owner.Paint(this); }
            public void OnPointerDown(PointerEventData e) { }

            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Left)
                {
                    owner.Select(bone);
                    if (e.clickCount >= 2)
                        owner.Rename(bone);
                }
                else if (e.button == PointerEventData.InputButton.Right)
                    owner.RowMenu(this, e.position);
            }
        }

        public BonesEditor bones;
        public VoidHandler Changed; // a favourite was added / removed / renamed: the tree repaints its stars and names

        private KitScroll _scroll;
        private KitIconButton _add;
        private Text _empty;
        private readonly List<Row> _rows = new List<Row>();
        private int _signature = -1;
        private Transform _shownTarget;
        private float _nextCheck;

        public static FavouritesPanel Create(RectTransform parent)
        {
            RectTransform root = Kit.Rect(parent, "Favourites");
            Kit.Fill(root);
            Image bg = root.gameObject.AddComponent<Image>();
            Kit.SetSprite(bg, Sprites.Rounded(Theme.RadiusSmall));
            KitThemeColor.Bind(bg, () => Theme.ListBg);
            bg.raycastTarget = false;
            FavouritesPanel panel = root.gameObject.AddComponent<FavouritesPanel>();
            Theme.Changed += () => { if (panel != null) panel.Rebuild(); };

            Text title = Kit.MakeText(root, "Title", "FAVOURITES", Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.offsetMin = new Vector2(10f, -30f);
            title.rectTransform.offsetMax = new Vector2(-36f, -4f);

            panel._add = KitIconButton.Create(root, Sprites.Plus, 24f, false, panel.AddSelected);
            Kit.Anchor(panel._add.Rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-6f, -5f), new Vector2(24f, 24f));
            KitTooltip.Attach(panel._add.gameObject, "Add the selected bone");

            RectTransform listHost = Kit.Rect(root, "List");
            Kit.Fill(listHost, 0f, 0f, 0f, 32f);
            panel._scroll = KitScroll.Create(listHost, 4, 2, 0f, true, true);

            panel._empty = Kit.MakeText(listHost, "Empty", "Press + or ★ on a bone\nto list it here", Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleCenter);
            Kit.Fill(panel._empty.rectTransform);
            panel._empty.fontStyle = FontStyle.Italic;
            return panel;
        }

        public void Bind(BonesEditor editor)
        {
            bones = editor;
            Rebuild();
        }

        // ---------------------------------------------------------------- rows

        /// <summary>Count + alias version: cheap, no allocation; a change rebuilds the rows.</summary>
        private int Signature()
        {
            if (bones == null)
                return 0;
            return bones.kitBuiltInFavourites.Count * 7919 + bones.kitFavourites.Count * 31 + BonesEditor.kitAliasVersion * 104729;
        }

        public void Rebuild()
        {
            for (int i = 0; i < _rows.Count; i++)
                Destroy(_rows[i].gameObject);
            _rows.Clear();
            _signature = Signature();
            _shownTarget = null;
            if (bones != null)
            {
                foreach (KeyValuePair<Transform, string> p in bones.kitBuiltInFavourites)
                    if (p.Key != null)
                        AddRow(p.Key, true);
                Transform root = bones.kitRoot;
                foreach (KeyValuePair<string, string> p in bones.kitFavourites)
                {
                    Transform t = root.Find(p.Key);
                    if (t != null)
                        AddRow(t, false);
                }
            }
            _empty.enabled = _rows.Count == 0;
        }

        private void AddRow(Transform bone, bool builtIn)
        {
            RectTransform rt = Kit.Rect(_scroll.Content, "Row");
            Kit.Layout(rt.gameObject, -1f, RowH);
            Row r = rt.gameObject.AddComponent<Row>();
            r.owner = this;
            r.bone = bone;
            r.builtIn = builtIn;
            r.bg = rt.gameObject.AddComponent<Image>();
            Kit.SetSprite(r.bg, Sprites.Rounded(Mathf.Max(0, Theme.RadiusSmall - 2)));
            r.bg.color = Color.clear;

            Image star = Kit.Image(rt, "Star", HspeIcons.Star, builtIn ? Theme.TextDim : Theme.Warning);
            Kit.Anchor(star.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(12f, 0f), new Vector2(11f, 11f));
            star.raycastTarget = false;

            r.text = Kit.MakeText(rt, "Name", BoneNames.Display(bone), Theme.FontSize, Theme.Text, TextAnchor.MiddleLeft);
            KitEllipsis.Set(r.text, BoneNames.Display(bone));   // one line, "…" when long (wrapping cut the 2nd line)
            RectTransform tr = r.text.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(24f, 0f);
            tr.offsetMax = new Vector2(builtIn ? -6f : -26f, 0f);
            KitTooltip.Attach(rt.gameObject, bone.name + (builtIn ? "  ·  built-in" : "") + "\nDouble-click or right-click to rename");

            if (builtIn == false)
            {
                Transform b = bone;
                r.remove = KitIconButton.Create(rt, Sprites.Cross, 20f, true, () => Remove(b));
                Kit.Anchor(r.remove.Rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(20f, 20f));
                KitTooltip.Attach(r.remove.gameObject, "Remove from favourites");
                r.remove.gameObject.SetActive(false);
            }
            _rows.Add(r);
        }

        private void LateUpdate()
        {
            if (bones == null)
                return;
            // the classic window or the tree's ★ can change the set too
            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 0.5f;
                if (Signature() != _signature)
                    Rebuild();
            }
            Transform target = bones.kitTarget;
            if (target != _shownTarget)
            {
                _shownTarget = target;
                for (int i = 0; i < _rows.Count; i++)
                    Paint(_rows[i]);
                PaintAdd();
            }
        }

        /// <summary>+ is dimmed when there is nothing to add (no bone, or it is listed already).</summary>
        private void PaintAdd()
        {
            Transform target = bones != null ? bones.kitTarget : null;
            bool can = target != null && bones.KitIsFavourite(target) == false;
            _add.Icon.color = can ? Theme.Text : Kit.Alpha(Theme.TextDim, 0.4f);
        }

        private void Paint(Row r)
        {
            bool selected = r.bone != null && r.bone == bones.kitTarget;
            Color c = selected ? Kit.Alpha(Theme.Accent, r.hover ? 0.3f : 0.22f) : (r.hover ? Theme.SurfaceHover : Color.clear);
            Tween.Tint(r.bg, c, 0.1f, Ease.OutQuad);
            r.text.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
            r.text.color = selected ? Color.white : Theme.Text;
            if (r.remove != null && r.remove.gameObject.activeSelf != r.hover)
                r.remove.gameObject.SetActive(r.hover);
        }

        // ---------------------------------------------------------------- actions

        private void Select(Transform bone)
        {
            if (bones != null && bone != null && bone != bones.kitTarget)
                bones.KitSelect(bone);
        }

        private void AddSelected()
        {
            if (bones == null || bones.kitTarget == null || bones.KitIsFavourite(bones.kitTarget))
                return;
            bones.KitToggleFavourite(bones.kitTarget);
            Rebuild();
            if (Changed != null)
                Changed();
        }

        private void Remove(Transform bone)
        {
            if (bones == null || bone == null || bones.KitIsBuiltInFavourite(bone) || bones.KitIsFavourite(bone) == false)
                return;
            bones.KitToggleFavourite(bone);
            Rebuild();
            if (Changed != null)
                Changed();
        }

        public delegate void BoneHandler(Transform bone);
        public BoneHandler RenameRequested; // the page owns the rename prompt (same one as the tree's)

        private void Rename(Transform bone)
        {
            if (RenameRequested != null && bone != null)
                RenameRequested(bone);
        }

        private void RowMenu(Row r, Vector2 screen)
        {
            if (r.bone == null)
                return;
            Select(r.bone);
            Transform b = r.bone;
            KitMenu m = new KitMenu();
            m.Item("Rename (alias)...", () => Rename(b));
            if (r.builtIn == false)
                m.Item("Remove from favourites", () => Remove(b));
            KitContextMenu.Show(m, screen);
        }
    }
}
