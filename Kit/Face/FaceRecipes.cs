using System;
using System.Collections.Generic;
using System.Globalization;

namespace HSPE.Kit.Face
{
    internal enum FacePart { Brows, Eyes, Mouth }

    internal struct V3
    {
        public float X, Y, Z;
        public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static V3 operator +(V3 a, V3 b) { return new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static V3 operator *(V3 a, float s) { return new V3(a.X * s, a.Y * s, a.Z * s); }
        public bool IsZero { get { return X == 0f && Y == 0f && Z == 0f; } }
    }

    /// <summary>A blend shape: Mesh == null = a PATTERN key applied to every mesh group of the part; else one exact name.</summary>
    internal sealed class ShapeLine
    {
        public FacePart Part; public string Mesh; public string Name; public float Weight;
        private string _key;
        /// <summary>"part|key" for patterns, "mesh|name" for exact shapes - built once (the mixer runs per frame under Timeline).</summary>
        public string Key { get { return _key ?? (_key = (Mesh ?? FaceRecipes.PartName(Part)) + "|" + Name); } }
    }
    internal sealed class BoneLine { public FacePart Part; public string Bone; public bool Rotation; public V3 Value; }

    internal sealed class FaceEntry
    {
        public string Name;
        public float Angle;
        public string Sex; // null = both
        public readonly List<ShapeLine> Shapes = new List<ShapeLine>();
        public readonly List<BoneLine> Bones = new List<BoneLine>();
    }

    /// <summary>A blend shape driven by one direction of a point's drag (x+ / x- / y+ / y- lines).</summary>
    internal sealed class PointShape { public int Axis; public int Sign; public ShapeLine Line; }

    internal sealed class FacePoint
    {
        public string Name, Bone, Twin;
        public float AtX, AtY;
        public BoneLine X, Y; // offset at full drag right / up
        public readonly List<PointShape> Shapes = new List<PointShape>();
    }

    /// <summary>
    /// The face recipes file (Unity-free, unit-tested): moods, vowels, drag points and ignored keys. Blend shapes are
    /// referenced by pattern KEY ("e01_yorokobi_op" - every face mesh of the part that has it) or by exact name, bones
    /// by pattern ("*_J_X" = cf_J_X or cm_J_X), so other heads with other numbering still work. Bad lines are
    /// collected in Errors and skipped; everything valid still loads.
    /// </summary>
    internal sealed class FaceRecipes
    {
        public readonly List<FaceEntry> Moods = new List<FaceEntry>();
        public readonly List<FaceEntry> Vowels = new List<FaceEntry>();
        public readonly List<FaceEntry> Neutrals = new List<FaceEntry>(); // the default face: takes the weight the moods leave
        public readonly List<FaceEntry> Closed = new List<FaceEntry>();   // eyes fully closed (for shapes without a _cl partner)
        public readonly List<FaceEntry> Opened = new List<FaceEntry>();   // mouth fully open (for shapes without an _op partner)

        /// <summary>The entry of this sex (or the sex-less one) from a list, or null.</summary>
        public static FaceEntry ForSex(List<FaceEntry> list, string sex)
        {
            FaceEntry any = null;
            foreach (FaceEntry e in list)
            {
                if (e.Sex == sex)
                    return e;
                if (e.Sex == null && any == null)
                    any = e;
            }
            return any;
        }
        public readonly List<FacePoint> Points = new List<FacePoint>();
        public readonly List<string> Ignores = new List<string>();
        public readonly List<string> Errors = new List<string>();

        private static readonly char[] Space = { ' ', '\t' };

        public static string PartName(FacePart p)
        {
            return p == FacePart.Brows ? "brows" : p == FacePart.Eyes ? "eyes" : "mouth";
        }

        public static FaceRecipes Parse(string text)
        {
            FaceRecipes r = new FaceRecipes();
            FaceEntry entry = null;
            FacePoint point = null;
            string[] lines = (text ?? "").Replace("\r", "").Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n];
                int hash = line.IndexOf('#');
                if (hash >= 0)
                    line = line.Substring(0, hash);
                line = line.Trim();
                if (line.Length == 0)
                    continue;
                string[] w = line.Split(Space, StringSplitOptions.RemoveEmptyEntries);
                string where = "line " + (n + 1) + ": ";
                if (line[0] == '[')
                {
                    entry = null;
                    point = null;
                    string[] h = line.Trim('[', ']').Split(Space, StringSplitOptions.RemoveEmptyEntries);
                    if (h.Length >= 3 && h[0] == "mood")
                    {
                        float angle;
                        if (TryFloat(h[2], out angle))
                        {
                            entry = new FaceEntry { Name = h[1], Angle = Wrap(angle), Sex = h.Length >= 4 ? h[3] : null };
                            r.Moods.Add(entry);
                            continue;
                        }
                    }
                    else if (h.Length >= 1 && (h[0] == "neutral" || h[0] == "closed" || h[0] == "open"))
                    {
                        entry = new FaceEntry { Name = h[0], Sex = h.Length >= 2 ? h[1] : null };
                        (h[0] == "neutral" ? r.Neutrals : h[0] == "closed" ? r.Closed : r.Opened).Add(entry);
                        continue;
                    }
                    else if (h.Length >= 2 && h[0] == "vowel")
                    {
                        entry = new FaceEntry { Name = h[1], Sex = h.Length >= 3 ? h[2] : null };
                        r.Vowels.Add(entry);
                        continue;
                    }
                    else if (h.Length >= 2 && h[0] == "point")
                    {
                        point = new FacePoint { Name = h[1] };
                        r.Points.Add(point);
                        continue;
                    }
                    r.Errors.Add(where + "bad header \"" + line + "\"");
                    continue;
                }
                if (w.Length == 2 && w[0] == "ignore")
                {
                    r.Ignores.Add(w[1]);
                    continue;
                }
                if (point != null && ParsePointLine(point, w))
                    continue;
                FacePart part;
                if (entry != null && TryPart(w[0], out part))
                {
                    float weight;
                    if (w.Length == 4 && w[1] == "pattern" && TryFloat(w[3], out weight))
                    {
                        entry.Shapes.Add(new ShapeLine { Part = part, Mesh = null, Name = w[2], Weight = weight });
                        continue;
                    }
                    if (w.Length == 5 && w[1] == "shape" && TryFloat(w[4], out weight))
                    {
                        entry.Shapes.Add(new ShapeLine { Part = part, Mesh = w[2], Name = w[3], Weight = weight });
                        continue;
                    }
                    if (w.Length == 7 && w[1] == "bone" && (w[3] == "pos" || w[3] == "rot"))
                    {
                        V3 v;
                        if (TryV3(w, 4, out v))
                        {
                            entry.Bones.Add(new BoneLine { Part = part, Bone = w[2], Rotation = w[3] == "rot", Value = v });
                            continue;
                        }
                    }
                }
                r.Errors.Add(where + "can't read \"" + line + "\"");
            }
            return r;
        }

        private static bool ParsePointLine(FacePoint p, string[] w)
        {
            if (w.Length == 2 && w[0] == "bone") { p.Bone = w[1]; return true; }
            if (w.Length == 2 && w[0] == "twin") { p.Twin = w[1]; return true; }
            if (w.Length == 3 && w[0] == "at")
                return TryFloat(w[1], out p.AtX) && TryFloat(w[2], out p.AtY);
            if (w.Length == 5 && (w[0] == "x" || w[0] == "y") && (w[1] == "pos" || w[1] == "rot"))
            {
                V3 v;
                if (TryV3(w, 2, out v) == false)
                    return false;
                BoneLine axis = new BoneLine { Bone = p.Bone, Rotation = w[1] == "rot", Value = v };
                if (w[0] == "x") p.X = axis; else p.Y = axis;
                return true;
            }
            // x+ / x- / y+ / y- : a blend shape driven by one direction of the drag (0..1 of the weight)
            if (w.Length >= 5 && w[0].Length == 2 && (w[0][0] == 'x' || w[0][0] == 'y') && (w[0][1] == '+' || w[0][1] == '-'))
            {
                FacePart part;
                float weight;
                ShapeLine line = null;
                if (w.Length == 5 && w[1] == "pattern" && TryPart(w[2], out part) && TryFloat(w[4], out weight))
                    line = new ShapeLine { Part = part, Mesh = null, Name = w[3], Weight = weight };
                else if (w.Length == 6 && w[1] == "shape" && TryPart(w[2], out part) && TryFloat(w[5], out weight))
                    line = new ShapeLine { Part = part, Mesh = w[3], Name = w[4], Weight = weight };
                if (line == null)
                    return false;
                p.Shapes.Add(new PointShape { Axis = w[0][0] == 'x' ? 0 : 1, Sign = w[0][1] == '+' ? 1 : -1, Line = line });
                return true;
            }
            return false;
        }

        private static bool TryPart(string s, out FacePart part)
        {
            switch (s)
            {
                case "brows": part = FacePart.Brows; return true;
                case "eyes": part = FacePart.Eyes; return true;
                case "mouth": part = FacePart.Mouth; return true;
                default: part = FacePart.Eyes; return false;
            }
        }

        private static bool TryV3(string[] w, int start, out V3 v)
        {
            v = new V3();
            return TryFloat(w[start], out v.X) && TryFloat(w[start + 1], out v.Y) && TryFloat(w[start + 2], out v.Z);
        }

        private static bool TryFloat(string s, out float f)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f);
        }

        private static float Wrap(float a)
        {
            a %= 360f;
            return a < 0f ? a + 360f : a;
        }

        /// <summary>"*_J_X" matches cf_J_X and cm_J_X; anything else must match exactly.</summary>
        public static bool BoneMatches(string pattern, string name)
        {
            if (pattern == null || name == null)
                return false;
            if (pattern.StartsWith("*_", StringComparison.Ordinal))
            {
                string rest = pattern.Substring(1); // "_J_X"
                return name.Length == rest.Length + 2 && name.EndsWith(rest, StringComparison.Ordinal) && (name.StartsWith("cf", StringComparison.Ordinal) || name.StartsWith("cm", StringComparison.Ordinal));
            }
            return pattern == name;
        }

        /// <summary>
        /// A group's pattern keys: the text after the last '.', minus the suffix every name of the group shares
        /// ("_mu", "_ha" ...; none for the male head). "bs02_mayuge.e01_yorokobi_op_mu" -> "e01_yorokobi_op".
        /// </summary>
        public static string[] Keys(IList<string> groupNames)
        {
            string[] keys = new string[groupNames.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                string n = groupNames[i];
                int dot = n.LastIndexOf('.');
                keys[i] = dot >= 0 ? n.Substring(dot + 1) : n;
            }
            string suffix = CommonSuffix(keys);
            if (suffix.Length != 0)
                for (int i = 0; i < keys.Length; i++)
                    keys[i] = keys[i].Substring(0, keys[i].Length - suffix.Length);
            return keys;
        }

        private static string CommonSuffix(string[] keys)
        {
            if (keys.Length < 2)
                return "";
            string first = keys[0];
            int len = first.Length;
            for (int i = 1; i < keys.Length; i++)
            {
                string k = keys[i];
                int m = 0;
                while (m < len && m < k.Length && first[first.Length - 1 - m] == k[k.Length - 1 - m])
                    m++;
                len = m;
            }
            // only a whole "_xxx" part counts (never eat into the key itself), and never the whole key
            string s = first.Substring(first.Length - len);
            int us = s.IndexOf('_');
            if (us < 0)
                return "";
            s = s.Substring(us);
            for (int i = 0; i < keys.Length; i++)
                if (keys[i].Length <= s.Length)
                    return "";
            return s;
        }
    }
}
