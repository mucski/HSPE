using UnityEngine;

namespace HSPE.Kit.Small
{
    // inside the namespace: here "Kit" would otherwise resolve to the namespace HSPE.Kit, not HSUIKit.Kit
    using HSUIKit;

    /// <summary>HSPE's procedurally drawn icons (white shapes, tinted by the Image). y = 0 is the bottom row.</summary>
    internal static class HspeIcons
    {
        /// <summary>Two overlapping rounded squares.</summary>
        public static Sprite Copy
        {
            get
            {
                return Sprites.Custom("hspe_copy", 32, (x, y) =>
                {
                    float front = Sprites.RoundBox(x - 19f, y - 13f, 7f, 7f, 2f);
                    float back = Sprites.RoundBox(x - 13f, y - 19f, 7f, 7f, 2f);
                    float frontLine = Stroke(front, 1.3f);
                    float backLine = Stroke(back, 1.3f) * Mathf.Clamp01(front - 0.5f); // hidden behind the front square
                    return Mathf.Max(frontLine, backLine);
                });
            }
        }

        /// <summary>A clipboard: board outline with a filled clip at the top.</summary>
        public static Sprite Paste
        {
            get
            {
                return Sprites.Custom("hspe_paste", 32, (x, y) =>
                {
                    float board = Stroke(Sprites.RoundBox(x - 16f, y - 14f, 9f, 11f, 2f), 1.3f);
                    float clip = Mathf.Clamp01(0.5f - Sprites.RoundBox(x - 16f, y - 25f, 4.5f, 2.5f, 1f));
                    float line1 = Mathf.Clamp01(1.1f - Sprites.SegmentDistance(x, y, 12f, 15f, 20f, 15f));
                    float line2 = Mathf.Clamp01(1.1f - Sprites.SegmentDistance(x, y, 12f, 10f, 18f, 10f));
                    return Mathf.Max(Mathf.Max(board, clip), Mathf.Max(line1, line2));
                });
            }
        }

        /// <summary>A face: round outline, two eyes, a smile (Face window).</summary>
        public static Sprite Face
        {
            get
            {
                return Sprites.Custom("hspe_face", 32, (x, y) =>
                {
                    float dx = x - 16f, dy = y - 16f;
                    float head = Stroke(Mathf.Sqrt(dx * dx + dy * dy) - 11.5f, 1.3f);
                    float eyes = Mathf.Max(Mathf.Clamp01(2f - Mathf.Sqrt((x - 12f) * (x - 12f) + (y - 19f) * (y - 19f))),
                                           Mathf.Clamp01(2f - Mathf.Sqrt((x - 20f) * (x - 20f) + (y - 19f) * (y - 19f))));
                    float mr = Mathf.Sqrt(dx * dx + (y - 16.5f) * (y - 16.5f));
                    float smile = y < 13.5f ? Stroke(mr - 6f, 1.2f) : 0f;
                    return Mathf.Max(head, Mathf.Max(eyes, smile));
                });
            }
        }

        /// <summary>A curved arrow turning left (undo); Redo is its mirror.</summary>
        public static Sprite Undo { get { return Arrow("hspe_undo", false); } }
        public static Sprite Redo { get { return Arrow("hspe_redo", true); } }

        private static Sprite Arrow(string key, bool mirror)
        {
            return Sprites.Custom(key, 32, (x, y) =>
            {
                if (mirror)
                    x = 32f - x;
                // upper half of a ring from the left tip over to the right, then an arrow head at the left end
                float dx = x - 17f, dy = y - 13f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float arc = dy >= 0f || dx > 0f ? Stroke(r - 8f, 1.3f) : 0f;
                if (dx > 0f && dy < -4f)
                    arc = 0f;
                float head = Triangle(x, y, 4f, 13f, 14f, 13f, 9f, 6.5f);
                return Mathf.Max(arc, head);
            });
        }

        /// <summary>A bookmark (presets).</summary>
        public static Sprite Presets
        {
            get
            {
                return Sprites.Custom("hspe_presets", 32, (x, y) =>
                {
                    float box = Mathf.Clamp01(0.5f - Sprites.RoundBox(x - 16f, y - 16f, 7f, 11f, 1.5f));
                    float notch = Triangle(x, y, 8f, 4f, 24f, 4f, 16f, 11.5f);
                    return Mathf.Clamp01(box - notch);
                });
            }
        }

        /// <summary>Settings gear (same drawing as Timeline's).</summary>
        public static Sprite Gear
        {
            get
            {
                return Sprites.Custom("hspe_gear", 32, (x, y) =>
                {
                    float dx = x - 16f, dy = y - 16f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Atan2(dy, dx);
                    float outer = 8f + 2.2f * Mathf.Clamp01(Mathf.Cos(a * 8f) * 3f);
                    float body = Mathf.Clamp01(outer - r + 0.5f);
                    float hole = Mathf.Clamp01(3.2f - r + 0.5f);
                    return Mathf.Clamp01(body - hole);
                });
            }
        }

        /// <summary>Filled disc filling the sprite (joint fill).</summary>
        public static Sprite Disc
        {
            get { return Sprites.Custom("hspe_disc", 32, (x, y) => Mathf.Clamp01(14.5f - Mathf.Sqrt((x - 16f) * (x - 16f) + (y - 16f) * (y - 16f)) + 0.5f)); }
        }

        /// <summary>Thin ring on the disc's edge (joint outline).</summary>
        public static Sprite RingThin
        {
            get { return Sprites.Custom("hspe_ring", 32, (x, y) => Stroke(Mathf.Sqrt((x - 16f) * (x - 16f) + (y - 16f) * (y - 16f)) - 13.5f, 1.2f)); }
        }

        /// <summary>Filled diamond (bend-goal fill).</summary>
        public static Sprite Diamond
        {
            get { return Sprites.Custom("hspe_diamond", 32, (x, y) => Mathf.Clamp01(14.5f - (Mathf.Abs(x - 16f) + Mathf.Abs(y - 16f)) + 0.5f)); }
        }

        /// <summary>Diamond outline (bend-goal outline).</summary>
        public static Sprite DiamondRing
        {
            get { return Sprites.Custom("hspe_diamondring", 32, (x, y) => Stroke((Mathf.Abs(x - 16f) + Mathf.Abs(y - 16f) - 13.5f) * 0.7071f, 1.2f)); }
        }

        /// <summary>Overwrite / save: an arrow into a tray (HSLRE's drawing).</summary>
        public static Sprite Save
        {
            get
            {
                return Sprites.Custom("hspe_save", 32, (x, y) =>
                {
                    float d = Mathf.Min(
                        Mathf.Min(Sprites.SegmentDistance(x, y, 16f, 25f, 16f, 12f), Mathf.Min(Sprites.SegmentDistance(x, y, 16f, 11f, 10.5f, 16.5f), Sprites.SegmentDistance(x, y, 16f, 11f, 21.5f, 16.5f))),
                        Mathf.Min(Sprites.SegmentDistance(x, y, 7f, 13f, 7f, 7f), Mathf.Min(Sprites.SegmentDistance(x, y, 7f, 7f, 25f, 7f), Sprites.SegmentDistance(x, y, 25f, 7f, 25f, 13f))));
                    return Mathf.Clamp01(1.7f - d);
                });
            }
        }

        /// <summary>Delete: a bin with a lid (HSLRE's drawing).</summary>
        public static Sprite Trash
        {
            get
            {
                return Sprites.Custom("hspe_trash", 32, (x, y) =>
                {
                    float lid = Mathf.Min(Sprites.SegmentDistance(x, y, 8f, 23f, 24f, 23f), Sprites.SegmentDistance(x, y, 13.5f, 26f, 18.5f, 26f));
                    float body = Mathf.Min(Mathf.Min(Sprites.SegmentDistance(x, y, 10f, 20f, 11f, 7f), Sprites.SegmentDistance(x, y, 22f, 20f, 21f, 7f)), Sprites.SegmentDistance(x, y, 11f, 7f, 21f, 7f));
                    float ribs = Mathf.Min(Sprites.SegmentDistance(x, y, 14f, 17f, 14.3f, 10f), Sprites.SegmentDistance(x, y, 18f, 17f, 17.7f, 10f));
                    return Mathf.Clamp01(1.6f - Mathf.Min(Mathf.Min(lid, body), ribs * 1.15f));
                });
            }
        }

        /// <summary>Favourite: a five-pointed star (HSLRE's drawing).</summary>
        public static Sprite Star
        {
            get
            {
                return Sprites.Custom("hspe_star", 32, (x, y) =>
                {
                    float dx = x - 16f, dy = y - 15f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                    float sector = Mathf.Repeat(a + 36f, 72f) - 36f;
                    float t = Mathf.Abs(sector) / 36f;
                    float edge = Mathf.Lerp(12.5f, 5.2f, Mathf.Pow(t, 0.8f));
                    return Mathf.Clamp01(edge - r + 0.5f);
                });
            }
        }

        /// <summary>Star outline (not a favourite).</summary>
        public static Sprite StarOutline
        {
            get
            {
                return Sprites.Custom("hspe_staroutline", 32, (x, y) =>
                {
                    float dx = x - 16f, dy = y - 15f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                    float sector = Mathf.Repeat(a + 36f, 72f) - 36f;
                    float t = Mathf.Abs(sector) / 36f;
                    float edge = Mathf.Lerp(12.5f, 5.2f, Mathf.Pow(t, 0.8f));
                    return Stroke((r - edge) * 0.8f, 1.1f);
                });
            }
        }

        /// <summary>Three dots (more menu).</summary>
        public static Sprite More
        {
            get
            {
                return Sprites.Custom("hspe_more", 32, (x, y) =>
                {
                    float best = 99f;
                    for (int i = 0; i < 3; i++)
                    {
                        float px = 8f + i * 8f;
                        best = Mathf.Min(best, Mathf.Sqrt((x - px) * (x - px) + (y - 16f) * (y - 16f)));
                    }
                    return Mathf.Clamp01(2.6f - best);
                });
            }
        }

        private static float Stroke(float distance, float halfWidth)
        {
            return Mathf.Clamp01(halfWidth - Mathf.Abs(distance) + 0.5f);
        }

        private static float Triangle(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float s = Edge(px, py, ax, ay, bx, by);
            float t = Edge(px, py, bx, by, cx, cy);
            float u = Edge(px, py, cx, cy, ax, ay);
            float inside = Mathf.Min(s, Mathf.Min(t, u));
            float insideFlipped = Mathf.Min(-s, Mathf.Min(-t, -u));
            return Mathf.Clamp01(Mathf.Max(inside, insideFlipped) + 0.5f);
        }

        private static float Edge(float px, float py, float ax, float ay, float bx, float by)
        {
            float ex = bx - ax, ey = by - ay;
            float len = Mathf.Sqrt(ex * ex + ey * ey);
            return ((px - ax) * ey - (py - ay) * ex) / len;
        }
    }
}
