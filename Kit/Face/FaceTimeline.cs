using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml;
using Studio;
using UnityEngine;

namespace HSPE.Kit.Face
{
    /// <summary>
    /// The "Face" Timeline track (owner HSPE): one keyframe = the Face window's whole input (mood dot, vowel, eyes /
    /// mouth open, point drags). Playback blends two keyframes and writes the face exactly like the window does.
    /// HSPE's per-mesh blend-shape tracks can't do this: a face spans ~6 meshes + several bones. New track id, so
    /// existing scenes are untouched (they never reference it).
    /// </summary>
    internal static class FaceTimeline
    {
        private const string Parameter = "face"; // one Face track per character

        public static void Populate()
        {
            ToolBox.TimelineCompatibility.AddInterpolableModelDynamic(
                owner: HSPE._name,
                id: "face",
                name: "Face",
                interpolateBefore: Interpolate,
                interpolateAfter: null,
                isCompatibleWithTarget: oci => oci is OCIChar && Controller(oci) is CharaPoseController,
                getValue: (oci, parameter) =>
                {
                    FaceSession s = Session(oci, true);
                    return s != null ? s.Input.Clone() : null;
                },
                readValueFromXml: (parameter, node) => ReadInput(node),
                writeValueToXml: (parameter, writer, o) => WriteInput(writer, (FaceInput)o),
                getParameter: oci => Parameter,
                readParameterFromXml: (oci, node) => Parameter,
                writeParameterToXml: null,
                checkIntegrity: (oci, parameter, left, right) => left is FaceInput && right is FaceInput && Session(oci, true) != null,
                getFinalName: (name, oci, parameter) => "Face");
        }

        private static PoseController Controller(ObjectCtrlInfo oci)
        {
            if (oci == null || oci.guideObject == null || oci.guideObject.transformTarget == null)
                return null;
            return oci.guideObject.transformTarget.GetComponent<PoseController>();
        }

        private static FaceSession Session(ObjectCtrlInfo oci, bool create)
        {
            return FaceSessions.Get(Controller(oci), create);
        }

        // ---------------------------------------------------------------- playback

        private static object _lastLeft, _lastRight;
        private static float _lastFactor = -1f;
        private static FaceSession _lastSession;

        /// <summary>Releases the last-applied references (they pinned a deleted character's session).</summary>
        internal static void Forget()
        {
            _lastSession = null;
            _lastLeft = _lastRight = null;
            _lastFactor = -1f;
        }

        private static void Interpolate(ObjectCtrlInfo oci, object parameter, object leftValue, object rightValue, float factor)
        {
            FaceSession s = Session(oci, true);
            FaceInput l = leftValue as FaceInput, r = rightValue as FaceInput;
            if (s == null || l == null || r == null)
                return;
            // Timeline calls this every frame; skip when nothing moved (paused / scrubbing stopped)
            if (ReferenceEquals(s, _lastSession) && ReferenceEquals(l, _lastLeft) && ReferenceEquals(r, _lastRight) && factor == _lastFactor)
                return;
            _lastSession = s;
            _lastLeft = l;
            _lastRight = r;
            _lastFactor = factor;

            FaceInput o = s.Input;
            // mood dot: blend as a point on the pad (so Joy -> Surprise slides around the circle, not through random angles)
            float la = l.Angle * Mathf.Deg2Rad, ra = r.Angle * Mathf.Deg2Rad;
            Vector2 p = Vector2.Lerp(new Vector2(Mathf.Cos(la), Mathf.Sin(la)) * l.Radius, new Vector2(Mathf.Cos(ra), Mathf.Sin(ra)) * r.Radius, factor);
            o.Radius = Mathf.Clamp01(p.magnitude);
            if (o.Radius > 0.0001f)
            {
                float a = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
                o.Angle = a < 0f ? a + 360f : a;
            }
            o.Vowel = factor < 0.5f ? l.Vowel : r.Vowel;
            o.EyeOpen = Mathf.Lerp(l.EyeOpen, r.EyeOpen, factor);
            o.MouthOpen = Mathf.Lerp(l.MouthOpen, r.MouthOpen, factor);

            // drags: every key of either side (missing = 0), arrays reused
            foreach (KeyValuePair<string, float[]> d in o.Drags)
                d.Value[0] = d.Value[1] = 0f;
            BlendDrags(o, l, 1f - factor);
            BlendDrags(o, r, factor);

            s.Apply();
            s.Version++;
        }

        private static void BlendDrags(FaceInput into, FaceInput from, float w)
        {
            if (w == 0f)
                return;
            foreach (KeyValuePair<string, float[]> d in from.Drags)
            {
                float[] v;
                if (into.Drags.TryGetValue(d.Key, out v) == false || v == null)
                    into.Drags[d.Key] = v = new float[2];
                v[0] += d.Value[0] * w;
                v[1] += d.Value[1] * w;
            }
        }

        // ---------------------------------------------------------------- xml (attributes only, like HSPE's other tracks)

        internal static void WriteInput(XmlTextWriter w, FaceInput v)
        {
            w.WriteAttributeString("angle", v.Angle.ToString("R", CultureInfo.InvariantCulture));
            w.WriteAttributeString("radius", v.Radius.ToString("R", CultureInfo.InvariantCulture));
            if (v.Vowel != null)
                w.WriteAttributeString("vowel", v.Vowel);
            w.WriteAttributeString("eyeOpen", v.EyeOpen.ToString("R", CultureInfo.InvariantCulture));
            w.WriteAttributeString("mouthOpen", v.MouthOpen.ToString("R", CultureInfo.InvariantCulture));
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, float[]> d in v.Drags)
            {
                if (d.Value == null || (d.Value[0] == 0f && d.Value[1] == 0f))
                    continue;
                if (sb.Length != 0)
                    sb.Append(';');
                sb.Append(d.Key).Append('=').Append(d.Value[0].ToString("R", CultureInfo.InvariantCulture)).Append(',').Append(d.Value[1].ToString("R", CultureInfo.InvariantCulture));
            }
            if (sb.Length != 0)
                w.WriteAttributeString("drags", sb.ToString());
        }

        internal static FaceInput ReadInput(XmlNode node)
        {
            FaceInput v = new FaceInput
            {
                Angle = F(node, "angle", 0f),
                Radius = F(node, "radius", 0f),
                EyeOpen = F(node, "eyeOpen", 1f),
                MouthOpen = F(node, "mouthOpen", 0f),
            };
            XmlAttribute vowel = node.Attributes["vowel"];
            v.Vowel = vowel != null && vowel.Value.Length != 0 ? vowel.Value : null;
            XmlAttribute drags = node.Attributes["drags"];
            if (drags != null)
                foreach (string item in drags.Value.Split(';'))
                {
                    int eq = item.LastIndexOf('=');
                    if (eq <= 0)
                        continue;
                    string[] xy = item.Substring(eq + 1).Split(',');
                    float x, y;
                    if (xy.Length == 2 && float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) && float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                        v.Drags[item.Substring(0, eq)] = new[] { x, y };
                }
            return v;
        }

        private static float F(XmlNode node, string name, float fallback)
        {
            XmlAttribute a = node.Attributes[name];
            float f;
            return a != null && float.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : fallback;
        }
    }
}
