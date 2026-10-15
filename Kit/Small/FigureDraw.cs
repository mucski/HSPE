using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Small
{
    using HSUIKit;

    /// <summary>
    /// Drawing helpers for the figures. Coordinates are in a 230 x 250 design space, y DOWN, character
    /// facing the viewer (its right side on the left).
    /// </summary>
    internal static class FigureDraw
    {
        public const float Width = 230f, Height = 250f;

        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            Kit.Anchor(rt, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(x, -y), new Vector2(w, h));
        }

        public static Image Line(RectTransform root, float x1, float y1, float x2, float y2, float thickness, Color color)
        {
            Image img = Kit.Image(root, "Bone", Sprites.Pill(thickness), color);
            float dx = x2 - x1, dy = y2 - y1;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            Place(img.rectTransform, (x1 + x2) * 0.5f, (y1 + y2) * 0.5f, length + thickness, thickness);
            img.rectTransform.localEulerAngles = new Vector3(0f, 0f, -Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
            return img;
        }

        /// <summary>The stick figure behind both the IK and the FK body page.</summary>
        public static void Body(RectTransform root)
        {
            Color bone = Theme.SurfaceActive;
            Image head = Kit.Image(root, "Head", HspeIcons.Disc, Theme.SurfaceHover);
            Place(head.rectTransform, 115f, 28f, 30f, 30f);
            Line(root, 115f, 46f, 115f, 120f, 8f, bone);   // spine
            Line(root, 80f, 58f, 150f, 58f, 8f, bone);     // shoulders
            Line(root, 80f, 58f, 62f, 98f, 8f, bone);      // right upper arm
            Line(root, 62f, 98f, 50f, 138f, 8f, bone);     // right forearm
            Line(root, 150f, 58f, 168f, 98f, 8f, bone);    // left upper arm
            Line(root, 168f, 98f, 180f, 138f, 8f, bone);   // left forearm
            Line(root, 115f, 120f, 96f, 128f, 8f, bone);   // right hip
            Line(root, 115f, 120f, 134f, 128f, 8f, bone);  // left hip
            Line(root, 96f, 128f, 92f, 180f, 8f, bone);    // right thigh
            Line(root, 92f, 180f, 90f, 228f, 8f, bone);    // right shin
            Line(root, 134f, 128f, 138f, 180f, 8f, bone);  // left thigh
            Line(root, 138f, 180f, 140f, 228f, 8f, bone);  // left shin
            SideLabels(root, 10f);
        }

        public static void SideLabels(RectTransform root, float y)
        {
            Text r = Kit.MakeText(root, "SideR", "R", Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleCenter);
            Place(r.rectTransform, 22f, y, 20f, 16f);
            Text l = Kit.MakeText(root, "SideL", "L", Theme.SmallSize, Theme.TextDim, TextAnchor.MiddleCenter);
            Place(l.rectTransform, 208f, y, 20f, 16f);
        }
    }
}
