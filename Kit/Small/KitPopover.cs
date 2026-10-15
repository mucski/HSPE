using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Small
{
    // inside the namespace: here "Kit" would otherwise resolve to the namespace HSPE.Kit, not HSUIKit.Kit
    using HSUIKit;

    /// <summary>
    /// A small panel opened under a header button (Options, Presets). Fill <see cref="Content"/> (a vertical layout),
    /// then call <see cref="Place"/>. Pressing outside, hiding the window or opening another popup closes it.
    /// </summary>
    internal sealed class KitPopover
    {
        private const float Pad = 12f;
        private readonly VoidHandler _close;
        private RectTransform _blocker;
        private RectTransform _panel;
        private CanvasGroup _group;
        public RectTransform Content;
        public VoidHandler Closed;

        public bool IsOpen { get { return _panel != null; } }

        private KitPopover()
        {
            _close = Close;
        }

        public static KitPopover Open(float width)
        {
            Kit.Ensure();
            KitTooltip.HideNow();
            KitPopover p = new KitPopover();
            KitPopup.Open(p._close);
            p._blocker = KitPopup.CreateBlocker(p._close);

            p._panel = Kit.Rect(Kit.PopupLayer, "HSPE Popover");
            p._panel.sizeDelta = new Vector2(width, 10f);
            p._group = p._panel.gameObject.AddComponent<CanvasGroup>();
            p._group.alpha = 0f;

            int blur = Mathf.Max(1, Theme.ShadowSize / 2);
            Image shadow = Kit.Image(p._panel, "Shadow", Sprites.Shadow(Theme.Radius, blur), new Color(0f, 0f, 0f, Theme.ShadowAlpha));
            Kit.Fill(shadow.rectTransform, -blur, -blur - 3f, -blur, -blur + 3f);
            Image bg = Kit.Image(p._panel, "Bg", Sprites.Rounded(Theme.Radius), Kit.Alpha(Theme.Background, 0.98f), true);
            Kit.Fill(bg.rectTransform);
            Image outline = Kit.Image(p._panel, "Outline", Sprites.Outline(Theme.Radius, 1), Theme.Border);
            Kit.Fill(outline.rectTransform);

            p.Content = Kit.Rect(p._panel, "Content");
            Kit.Fill(p.Content, Pad, Pad, Pad, Pad);
            VerticalLayoutGroup v = p.Content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6f;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return p;
        }

        /// <summary>Sizes the panel to its content and places it under (or above) the anchor.</summary>
        public void Place(RectTransform anchor)
        {
            float width = _panel.sizeDelta.x;
            Kit.Fill(Content, Pad, Pad, Pad, Pad);
            LayoutRebuilder.ForceRebuildLayoutImmediate(Content);
            float height = LayoutUtility.GetPreferredHeight(Content) + 2f * Pad;
            KitPopup.PlaceNear(_panel, anchor, new Vector2(width, height), 6f);
            Kit.Fill(Content, Pad, Pad, Pad, Pad);
            _panel.localScale = new Vector3(0.96f, 0.96f, 1f);
            Tween.Fade(_group, 1f, 0.14f, Ease.OutQuad);
            Tween.Scale(_panel, 1f, 0.22f, Ease.OutBack);
        }

        public void Close()
        {
            if (_panel == null)
                return;
            KitPopup.Forget(_close);
            if (_blocker != null)
                Object.Destroy(_blocker.gameObject);
            RectTransform panel = _panel;
            _panel = null;
            _blocker = null;
            _group.blocksRaycasts = false;
            Tween.Fade(_group, 0f, 0.1f, Ease.InQuad, delegate { if (panel != null) Object.Destroy(panel.gameObject); });
            if (Closed != null)
                Closed();
        }
    }
}
