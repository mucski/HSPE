using System.Collections.Generic;
using Studio;
using UnityEngine;

namespace HSPE.Kit.Small
{
    /// <summary>
    /// Studio's FK bones of the current target, keyed by a sex-independent name ("cf_J_ArmUp00_L" and "cm_J_ArmUp00_L"
    /// both become "armup00_l"), plus readable names and the More-list grouping (Studio's own FK groups).
    /// </summary>
    internal static class FkBones
    {
        public static string Key(string boneName)
        {
            string n = boneName.ToLowerInvariant();
            if (n.StartsWith("cf_j_") || n.StartsWith("cm_j_"))
                n = n.Substring(5);
            return n;
        }

        /// <summary>key -> bone, for every FK bone of the target that still exists.</summary>
        public static Dictionary<string, OCIChar.BoneInfo> Map(PoseController target)
        {
            Dictionary<string, OCIChar.BoneInfo> map = new Dictionary<string, OCIChar.BoneInfo>();
            if (target == null)
                return map;
            foreach (KeyValuePair<GameObject, OCIChar.BoneInfo> p in target.target.fkObjects)
            {
                if (p.Key == null || p.Value == null || p.Value.guideObject == null)
                    continue;
                string key = Key(p.Key.name);
                if (map.ContainsKey(key) == false)
                    map.Add(key, p.Value);
            }
            return map;
        }

        private static System.Reflection.FieldInfo _hashSelectObject;

        /// <summary>
        /// = GuideObjectManager.selectObject without its garbage: that getter copies the whole selection into a new array
        /// on every call (hashSelectObject.ToArray()[0]) and the FK pages ask every frame. Same element (enumeration order).
        /// </summary>
        public static GuideObject SelectedGuide()
        {
            GuideObjectManager m = GuideObjectManager.Instance;
            if (m == null)
                return null;
            if (ReferenceEquals(_hashSelectObject, null))
                _hashSelectObject = typeof(GuideObjectManager).GetField("hashSelectObject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            HashSet<GuideObject> set = ReferenceEquals(_hashSelectObject, null) ? null : _hashSelectObject.GetValue(m) as HashSet<GuideObject>;
            if (set == null)
                return m.selectObject;
            foreach (GuideObject g in set)
                return g;
            return null;
        }

        public static void Select(OCIChar.BoneInfo bone)
        {
            // what Joan's FK list does on click (MainWindow.RefreshFKBonesList)
            GuideObjectManager.Instance.selectObject = bone.guideObject;
        }

        /// <summary>Replaces Studio's selection even while Ctrl is held (its Ctrl-add refuses FK guides).</summary>
        public static void SelectGuide(GuideObject guide)
        {
            GuideObjectManager.Instance.SetSelectObject(guide, false);
        }

        /// <summary>Joan's rule: a bone can be rotated when FK is on and Studio shows its guide.</summary>
        public static bool Usable(PoseController target, OCIChar.BoneInfo bone)
        {
            return target != null && target.target.fkEnabled && bone != null && bone.active;
        }

        public enum Group { Hair, Breasts, Skirt, Other }

        public static Group GroupOf(OCIChar.BoneInfo bone)
        {
            switch (bone.boneGroup)
            {
                case OIBoneInfo.BoneGroup.Hair: return Group.Hair;
                case OIBoneInfo.BoneGroup.Breast: return Group.Breasts;
                case OIBoneInfo.BoneGroup.Skirt: return Group.Skirt;
                default: return Group.Other;
            }
        }

        public static string GroupLabel(Group g)
        {
            switch (g)
            {
                case Group.Hair: return "Hair";
                case Group.Breasts: return "Breasts";
                case Group.Skirt: return "Skirt";
                default: return "Other";
            }
        }

        /// <summary>A readable name for bones that are not on a figure (the figure slots have their own labels).</summary>
        public static string Readable(string boneName)
        {
            string k = Key(boneName);
            string side = "";
            if (k.EndsWith("_l")) { side = "Left "; k = k.Substring(0, k.Length - 2); }
            else if (k.EndsWith("_r")) { side = "Right "; k = k.Substring(0, k.Length - 2); }
            if (k.StartsWith("sk_"))
                k = "skirt " + k.Substring(3).Replace('_', ' ');
            else if (k.StartsWith("mune"))
                k = "breast " + k.Substring(4).Replace('_', ' ');
            else
                k = k.Replace('_', ' ');
            string s = (side + k).Trim();
            return s.Length == 0 ? boneName : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
