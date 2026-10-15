using System.Collections.Generic;

namespace HSPE.Kit.Face
{
    internal sealed class FaceResult
    {
        public readonly Dictionary<string, float> Patterns = new Dictionary<string, float>(); // "eyes|e01_yorokobi_op" -> weight 0..100
        public readonly Dictionary<string, float> Shapes = new Dictionary<string, float>();   // "mesh|exact name" -> weight
        public readonly HashSet<string> MouthShapes = new HashSet<string>();                  // Shapes keys that belong to the mouth
        public readonly Dictionary<string, V3> BonePos = new Dictionary<string, V3>();        // bone pattern -> local position offset
        public readonly Dictionary<string, V3> BoneRot = new Dictionary<string, V3>();        // bone pattern -> local euler offset
        public bool UsesBrows, UsesEyes, UsesMouth; // a part the mix drives: its other keys get zeroed

        public void Clear()
        {
            Patterns.Clear();
            Shapes.Clear();
            MouthShapes.Clear();
            BonePos.Clear();
            BoneRot.Clear();
            UsesBrows = UsesEyes = UsesMouth = false;
        }

        public bool Uses(FacePart p)
        {
            return p == FacePart.Brows ? UsesBrows : p == FacePart.Eyes ? UsesEyes : UsesMouth;
        }
    }

    internal sealed class FaceInput
    {
        public float Angle;          // degrees, 0 = right, counter-clockwise
        public float Radius;         // 0 = neutral .. 1 = full mood
        public string Vowel;         // null = none
        public float EyeOpen = 1f;   // 1 = as the mood has them .. 0 = closed (Studio's eye slider can't reach held shapes)
        public float MouthOpen;      // 0 = as the mood has it .. 1 = open (same for Studio's mouth slider)
        public Dictionary<string, float[]> Drags = new Dictionary<string, float[]>(); // point -> {dx, dy}, -1..1
        public List<FacePoint> ExtraPoints = new List<FacePoint>(); // runtime points for bones the recipes don't know (not saved)

        public bool IsNeutral
        {
            get
            {
                if (Radius > 0f || Vowel != null || EyeOpen < 1f || MouthOpen > 0f)
                    return false;
                foreach (KeyValuePair<string, float[]> p in Drags)
                    if (p.Value != null && (p.Value[0] != 0f || p.Value[1] != 0f))
                        return false;
                return true;
            }
        }

        /// <summary>
        /// Value equality (tolerance 1e-4): Timeline's auto-key compares a track's old and new value with Equals -
        /// by reference it saw a "change" on every read and keyed constantly.
        /// </summary>
        public override bool Equals(object obj)
        {
            FaceInput o = obj as FaceInput;
            if (o == null)
                return false;
            if (ReferenceEquals(o, this))
                return true;
            if (Near(Radius, o.Radius) == false || Vowel != o.Vowel || Near(EyeOpen, o.EyeOpen) == false || Near(MouthOpen, o.MouthOpen) == false)
                return false;
            if (Radius > 0.0001f && Near(Angle, o.Angle) == false)
                return false; // the angle means nothing at the centre
            return SameDrags(this, o) && SameDrags(o, this);
        }

        private static bool SameDrags(FaceInput a, FaceInput b)
        {
            foreach (KeyValuePair<string, float[]> p in a.Drags)
            {
                float[] d;
                float bx = 0f, by = 0f;
                if (b.Drags.TryGetValue(p.Key, out d) && d != null)
                {
                    bx = d[0];
                    by = d[1];
                }
                float ax = p.Value != null ? p.Value[0] : 0f, ay = p.Value != null ? p.Value[1] : 0f;
                if (Near(ax, bx) == false || Near(ay, by) == false)
                    return false;
            }
            return true;
        }

        private static bool Near(float a, float b) { return (a > b ? a - b : b - a) <= 0.0001f; }

        public override int GetHashCode()
        {
            return (Vowel != null ? Vowel.GetHashCode() : 0) ^ Drags.Count;
        }

        /// <summary>A copy for undo / clipboard (drag arrays copied; ExtraPoints shared - they are rig data).</summary>
        public FaceInput Clone()
        {
            FaceInput c = new FaceInput { ExtraPoints = ExtraPoints };
            c.CopyFrom(this);
            return c;
        }

        /// <summary>Takes another input's values (keeps this one's ExtraPoints).</summary>
        public void CopyFrom(FaceInput o)
        {
            Angle = o.Angle;
            Radius = o.Radius;
            Vowel = o.Vowel;
            EyeOpen = o.EyeOpen;
            MouthOpen = o.MouthOpen;
            Drags.Clear();
            foreach (KeyValuePair<string, float[]> p in o.Drags)
                if (p.Value != null)
                    Drags[p.Key] = new[] { p.Value[0], p.Value[1] };
        }
    }

    /// <summary>
    /// Unity-free mixing: pad mood (two neighbouring moods by angle, scaled by radius), the default face for what the
    /// moods leave, the vowel replacing the MOUTH lines, eye closing / mouth opening, then drag offsets. Called only
    /// when an input changes, never per frame.
    /// </summary>
    internal static class FaceMix
    {
        private static FaceRecipes _moodsFor;
        private static string _moodsSex;

        /// <summary>Moods() for the mixer, re-sorted only when the recipes or the sex change.</summary>
        private static void CachedMoods(FaceRecipes r, string sex)
        {
            if (ReferenceEquals(r, _moodsFor) && sex == _moodsSex)
                return;
            Moods(r, sex, _moods);
            _moodsFor = r;
            _moodsSex = sex;
        }

        /// <summary>The moods usable for this sex (null-sex entries always), sorted by angle.</summary>
        public static void Moods(FaceRecipes r, string sex, List<FaceEntry> into)
        {
            into.Clear();
            foreach (FaceEntry e in r.Moods)
                if (e.Sex == null || e.Sex == sex)
                    into.Add(e);
            into.Sort((x, y) => x.Angle.CompareTo(y.Angle));
        }

        public static void PadWeights(List<FaceEntry> moods, float angle, float radius, out int a, out int b, out float wa, out float wb)
        {
            a = b = -1;
            wa = wb = 0f;
            if (moods.Count == 0 || radius <= 0f)
                return;
            if (radius > 1f)
                radius = 1f;
            angle %= 360f;
            if (angle < 0f)
                angle += 360f;
            if (moods.Count == 1)
            {
                a = b = 0;
                wa = radius;
                return;
            }
            // the last mood whose angle <= angle (wrapping to the last one when angle is before the first)
            a = moods.Count - 1;
            for (int i = 0; i < moods.Count; i++)
                if (moods[i].Angle <= angle)
                    a = i;
            b = (a + 1) % moods.Count;
            float span = moods[b].Angle - moods[a].Angle;
            if (span <= 0f)
                span += 360f;
            float into = angle - moods[a].Angle;
            if (into < 0f)
                into += 360f;
            float t = into / span;
            wa = (1f - t) * radius;
            wb = t * radius;
        }

        private static readonly List<FaceEntry> _moods = new List<FaceEntry>();
        private static readonly List<string> _keys = new List<string>();

        /// <param name="known">"part|key" of every pattern key the head has (to find _cl / _op partners); null = assume none.</param>
        public static void Mix(FaceRecipes r, string sex, FaceInput input, FaceResult into, HashSet<string> known = null)
        {
            into.Clear();
            FaceEntry vowel = null;
            if (input.Vowel != null)
                foreach (FaceEntry v in r.Vowels)
                    if (v.Name == input.Vowel && (v.Sex == null || v.Sex == sex))
                        vowel = v;
            FaceEntry neutral = FaceRecipes.ForSex(r.Neutrals, sex);

            CachedMoods(r, sex);
            int a, b;
            float wa, wb;
            PadWeights(_moods, input.Angle, input.Radius, out a, out b, out wa, out wb);
            if (a >= 0)
                Add(_moods[a], wa, vowel != null, into);
            if (b >= 0 && b != a)
                Add(_moods[b], wb, vowel != null, into);
            // the default face fills what the moods leave (half a mood = half mood + half default, not half-closed eyes)
            float total = a >= 0 ? (b != a ? wa + wb : wa) : 0f;
            if (neutral != null && total > 0f && total < 1f)
                Add(neutral, 1f - total, vowel != null, into);
            if (vowel != null)
                Add(vowel, 1f, false, into);

            float close = 1f - Clamp01(input.EyeOpen);
            if (close > 0f)
            {
                if (into.UsesEyes == false && neutral != null)
                    AddPart(neutral, 1f, FacePart.Eyes, true, into);
                Shift(into, "eyes|", close, false, known, FaceRecipes.ForSex(r.Closed, sex), FacePart.Eyes);
                // the game closes the brows with the eyes too (they have _cl / _op pairs on female heads)
                if (HasClosedKeys(known, "brows|"))
                {
                    if (into.UsesBrows == false && neutral != null)
                        AddPart(neutral, 1f, FacePart.Brows, true, into);
                    Shift(into, "brows|", close, false, known, FaceRecipes.ForSex(r.Closed, sex), FacePart.Brows);
                }
            }
            float open = Clamp01(input.MouthOpen);
            if (open > 0f)
            {
                if (into.UsesMouth == false && neutral != null)
                    AddPart(neutral, 1f, FacePart.Mouth, true, into);
                Shift(into, "mouth|", open, true, known, FaceRecipes.ForSex(r.Opened, sex), FacePart.Mouth);
            }

            AddDrags(r.Points, input, into);
            AddDrags(input.ExtraPoints, input, into);
        }

        /// <summary>
        /// Moves the fraction t of a part's weights to the other state: a key's partner when the head has it
        /// (eyes: X_op / X -> X_cl; mouth: X_cl -> X_op, X -> X_op), otherwise to the fallback entry's lines. For the
        /// mouth, exact shape lines (male teeth) fade to the fallback's shape lines the same way.
        /// </summary>
        private static void Shift(FaceResult into, string prefix, float t, bool opening, HashSet<string> known, FaceEntry fallback, FacePart part)
        {
            _keys.Clear();
            foreach (KeyValuePair<string, float> p in into.Patterns)
                if (p.Key.StartsWith(prefix, System.StringComparison.Ordinal))
                    _keys.Add(p.Key);
            bool fallbackHasPart = false;
            if (fallback != null)
                foreach (ShapeLine s in fallback.Shapes)
                    if (s.Part == part && s.Mesh == null)
                        fallbackHasPart = true;
            float unpaired = 0f;
            foreach (string k in _keys)
            {
                float w = into.Patterns[k];
                if (w == 0f)
                    continue;
                if (opening ? k.EndsWith("_op", System.StringComparison.Ordinal) : k.EndsWith("_cl", System.StringComparison.Ordinal))
                    continue; // already in the target state
                string partner = Partner(k, opening);
                if (known != null && known.Contains(partner))
                {
                    into.Patterns[k] = w * (1f - t);
                    float old;
                    into.Patterns.TryGetValue(partner, out old);
                    into.Patterns[partner] = old + w * t;
                }
                else if (fallbackHasPart)
                {
                    into.Patterns[k] = w * (1f - t);
                    unpaired += w * t;
                }
                // no partner and no fallback lines for this part: leave it (fading it out would break the face)
            }
            if (fallback == null)
                return;
            if (unpaired > 0f)
                AddPart(fallback, unpaired / 100f, part, false, into);
            if (part == FacePart.Mouth && into.MouthShapes.Count != 0 && HasShapeLines(fallback, part))
            {
                _keys.Clear();
                foreach (string k in into.MouthShapes)
                    _keys.Add(k);
                foreach (string k in _keys)
                    into.Shapes[k] = into.Shapes[k] * (1f - t);
                AddPart(fallback, t, part, false, into, true);
            }
        }

        private static bool HasClosedKeys(HashSet<string> known, string prefix)
        {
            if (known == null)
                return false;
            foreach (string k in known)
                if (k.StartsWith(prefix, System.StringComparison.Ordinal) && k.EndsWith("_cl", System.StringComparison.Ordinal))
                    return true;
            return false;
        }

        private static bool HasShapeLines(FaceEntry e, FacePart part)
        {
            foreach (ShapeLine s in e.Shapes)
                if (s.Part == part && s.Mesh != null)
                    return true;
            return false;
        }

        private static readonly Dictionary<string, string> _openPartner = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> _closePartner = new Dictionary<string, string>();

        private static string Partner(string key, bool opening)
        {
            Dictionary<string, string> cache = opening ? _openPartner : _closePartner;
            string p;
            if (cache.TryGetValue(key, out p))
                return p;
            if (opening)
                p = key.EndsWith("_cl") ? key.Substring(0, key.Length - 3) + "_op" : key + "_op";
            else
                p = key.EndsWith("_op") ? key.Substring(0, key.Length - 3) + "_cl" : key + "_cl";
            cache.Add(key, p);
            return p;
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }

        private static void AddDrags(List<FacePoint> points, FaceInput input, FaceResult into)
        {
            foreach (FacePoint p in points)
            {
                float[] d;
                if (input.Drags.TryGetValue(p.Name, out d) == false || d == null)
                    continue;
                if (p.Bone != null)
                {
                    if (p.X != null && d[0] != 0f)
                        AddBone(p.X.Rotation ? into.BoneRot : into.BonePos, p.Bone, p.X.Value * d[0]);
                    if (p.Y != null && d[1] != 0f)
                        AddBone(p.Y.Rotation ? into.BoneRot : into.BonePos, p.Bone, p.Y.Value * d[1]);
                }
                foreach (PointShape s in p.Shapes)
                {
                    float amount = d[s.Axis] * s.Sign;
                    if (amount > 0f)
                        AddShape(s.Line, amount > 1f ? 1f : amount, into);
                }
            }
        }

        private static void Add(FaceEntry e, float w, bool skipMouth, FaceResult into)
        {
            if (w == 0f)
                return;
            foreach (ShapeLine s in e.Shapes)
                if (skipMouth == false || s.Part != FacePart.Mouth)
                    AddShape(s, w, into);
            foreach (BoneLine bl in e.Bones)
                if (skipMouth == false || bl.Part != FacePart.Mouth)
                    AddBone(bl.Rotation ? into.BoneRot : into.BonePos, bl.Bone, bl.Value * w);
        }

        /// <summary>Only one part's lines of an entry (shapes only, or exact shapes only).</summary>
        private static void AddPart(FaceEntry e, float w, FacePart part, bool withShapes, FaceResult into, bool shapesOnly = false)
        {
            foreach (ShapeLine s in e.Shapes)
                if (s.Part == part && (s.Mesh == null ? shapesOnly == false : (withShapes || shapesOnly)))
                    AddShape(s, w, into);
        }

        private static void AddShape(ShapeLine s, float w, FaceResult into)
        {
            Dictionary<string, float> d = s.Mesh == null ? into.Patterns : into.Shapes;
            string key = s.Key;
            float old;
            d.TryGetValue(key, out old);
            d[key] = old + s.Weight * w;
            if (s.Mesh != null && s.Part == FacePart.Mouth)
                into.MouthShapes.Add(key);
            Use(into, s.Part);
        }

        private static void Use(FaceResult r, FacePart p)
        {
            if (p == FacePart.Brows) r.UsesBrows = true;
            else if (p == FacePart.Eyes) r.UsesEyes = true;
            else r.UsesMouth = true;
        }

        private static void AddBone(Dictionary<string, V3> d, string bone, V3 v)
        {
            V3 old;
            d.TryGetValue(bone, out old);
            d[bone] = old + v;
        }

        /// <summary>Shift-drag mirror: the twin moves the same amount, x flipped.</summary>
        public static float[] MirrorDrag(float[] d)
        {
            return new[] { -d[0], d[1] };
        }
    }
}
