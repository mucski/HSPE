using HSPE.Kit.Face;
using UnityEngine;

namespace HSPE
{
    /// <summary>
    /// Lets other plugins (HSRig's face controls) drive the Face window's state: its drag points and mouth openness.
    /// Values go through the same FaceSession as the window, so saving, presets, the window's diagram and the
    /// Timeline face track all see them. c = the character's guideObject.transformTarget (where PoseController is).
    /// </summary>
    public static class FaceApi
    {
        public const int Version = 1;

        private static FaceSession Session(Component c, bool create)
        {
            PoseController pc = c != null ? c.GetComponent<PoseController>() : null;
            if (pc == null)
                return null;
            FaceSession s = FaceSessions.Get(pc, create);
            if (s != null && s.Valid == false)
                s.Rebind();
            return s != null && s.Valid ? s : null;
        }

        private static FacePoint Point(string name)
        {
            foreach (FacePoint p in FaceSessions.Recipes.Points)
                if (p.Name == name)
                    return p;
            return null;
        }

        public static bool Has(Component c, string point)
        {
            FacePoint p = Point(point);
            FaceSession s = Session(c, true);
            if (p == null || s == null)
                return false;
            return p.Bone == null ? p.Shapes.Count > 0 : s.Rig.FindBone(p.Bone) != null;
        }

        public static void GetDrag(Component c, string point, out float x, out float y)
        {
            x = y = 0f;
            FaceSession s = Session(c, false);
            float[] d;
            if (s != null && s.Input.Drags.TryGetValue(point, out d) && d != null)
            {
                x = d[0];
                y = d[1];
            }
        }

        public static void SetDrag(Component c, string point, float x, float y)
        {
            FaceSession s = Session(c, true);
            if (s == null || Point(point) == null)
                return;
            float[] d;
            if (s.Input.Drags.TryGetValue(point, out d) == false || d == null)
                s.Input.Drags[point] = d = new float[2];
            d[0] = Mathf.Clamp(x, -1f, 1f);
            d[1] = Mathf.Clamp(y, -1f, 1f);
            s.Apply();
            s.Version++; // the window refreshes its diagram
        }

        public static float GetMouthOpen(Component c)
        {
            FaceSession s = Session(c, false);
            return s != null ? s.Input.MouthOpen : 0f;
        }

        public static void SetMouthOpen(Component c, float v)
        {
            FaceSession s = Session(c, true);
            if (s == null)
                return;
            s.Input.MouthOpen = Mathf.Clamp01(v);
            s.Apply();
            s.Version++;
        }

        public static string Twin(string point)
        {
            FacePoint p = Point(point);
            return p != null ? p.Twin : null;
        }

        // World displacement of the point's bone at a full drag on axis 0 (x) / 1 (y) when that axis is a
        // position line; zero for rotation / shape-only axes.
        public static Vector3 FullDrag(Component c, string point, int axis)
        {
            FacePoint p = Point(point);
            FaceSession s = Session(c, false);
            if (p == null || s == null || p.Bone == null)
                return Vector3.zero;
            BoneLine line = axis == 0 ? p.X : p.Y;
            Transform t = s.Rig.FindBone(p.Bone);
            if (line == null || line.Rotation || t == null || t.parent == null)
                return Vector3.zero;
            return t.parent.TransformVector(new Vector3(line.Value.X, line.Value.Y, line.Value.Z));
        }

        public static Transform Bone(Component c, string point)
        {
            FacePoint p = Point(point);
            FaceSession s = Session(c, true);
            return p != null && s != null && p.Bone != null ? s.Rig.FindBone(p.Bone) : null;
        }
    }
}
