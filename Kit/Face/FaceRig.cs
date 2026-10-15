using System.Collections.Generic;
using HSPE.AMModules;
using UnityEngine;

namespace HSPE.Kit.Face
{
    /// <summary>
    /// One character's face as found at bind time - nothing hardcoded to one skeleton: face meshes by name ending,
    /// their blend shapes grouped by the prefix before '.' ("bs00_eye", "bs05_kuchi"...) and keyed by pattern key,
    /// bones discovered under *_J_FaceRoot (accessories, hair and Studio-attached objects skipped).
    /// </summary>
    internal sealed class FaceRig
    {
        internal sealed class Group
        {
            public SkinnedMeshRenderer Renderer;
            public string Mesh;   // alias: head, brows, lashes, teeth, tongue, tears1, tears2, beard
            public FacePart Part;
            public string[] Keys;
            public string[] FullKeys; // "part|key", as FaceResult.Patterns uses them (built once)
            public int[] Indices;
        }

        public PoseController Target;
        public string Sex;
        public readonly List<Group> Groups = new List<Group>();
        public readonly List<Transform> Bones = new List<Transform>();      // every usable bone under the face root
        public readonly List<Transform> ExtraBones = new List<Transform>(); // non-stock bones no recipe line / point uses (mods)
        public readonly HashSet<string> Known = new HashSet<string>();       // "part|key" of every pattern key (for _cl / _op partners)
        public Transform FaceRoot;

        private readonly Dictionary<string, SkinnedMeshRenderer> _meshes = new Dictionary<string, SkinnedMeshRenderer>();
        private readonly Dictionary<string, Transform> _boneCache = new Dictionary<string, Transform>();
        private readonly Dictionary<string, int> _exactCache = new Dictionary<string, int>();

        private static readonly string[] Endings = { "_O_head", "_O_mayuge", "_O_matuge", "_O_ha", "_O_sita", "_O_namida01", "_O_namida02" };
        private static readonly string[] Aliases = { "head", "brows", "lashes", "teeth", "tongue", "tears1", "tears2" };

        public static FaceRig Bind(PoseController target, FaceRecipes recipes)
        {
            FaceRig rig = new FaceRig { Target = target };
            BlendShapesEditor bs = target._blendShapesEditor;
            rig.Sex = bs != null && bs.kitIsFemale ? "female" : "male";
            Transform root = target.transform;

            foreach (SkinnedMeshRenderer r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMesh == null || r.sharedMesh.blendShapeCount == 0)
                    continue;
                string alias = AliasOf(r.name);
                if (alias == null || rig._meshes.ContainsKey(alias))
                    continue;
                rig._meshes.Add(alias, r);
                rig.AddGroups(r, alias);
            }

            rig.FaceRoot = FindDeep(root, "cf_J_FaceRoot") ?? FindDeep(root, "cm_J_FaceRoot");
            if (rig.FaceRoot != null)
                rig.Collect(rig.FaceRoot, target._bonesEditor);
            rig.FindExtras(recipes);
            return rig;
        }

        private static string AliasOf(string name)
        {
            for (int i = 0; i < Endings.Length; i++)
                if (name.EndsWith(Endings[i]))
                    return Aliases[i];
            if (name.StartsWith("O_hige"))
                return "beard";
            return null;
        }

        private void AddGroups(SkinnedMeshRenderer r, string alias)
        {
            Mesh mesh = r.sharedMesh;
            Dictionary<string, List<int>> byPrefix = new Dictionary<string, List<int>>();
            List<string> order = new List<string>();
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string n = mesh.GetBlendShapeName(i);
                int dot = n.LastIndexOf('.');
                string prefix = dot >= 0 ? n.Substring(0, dot) : "";
                List<int> list;
                if (byPrefix.TryGetValue(prefix, out list) == false)
                {
                    byPrefix.Add(prefix, list = new List<int>());
                    order.Add(prefix);
                }
                list.Add(i);
            }
            foreach (string prefix in order)
            {
                List<int> indices = byPrefix[prefix];
                List<string> names = new List<string>(indices.Count);
                foreach (int i in indices)
                    names.Add(mesh.GetBlendShapeName(i));
                string[] keys = FaceRecipes.Keys(names);
                if (keys.Length == 0)
                    continue;
                char family = keys[0][0];
                FacePart part;
                if (family == 'e')
                    part = alias == "brows" ? FacePart.Brows : FacePart.Eyes;
                else if (family == 'k')
                    part = FacePart.Mouth;
                else
                    continue; // body shapes ("bs9.cf_O_head_shape1", "s00_face01"): never touched
                if (part == FacePart.Eyes && (alias == "teeth" || alias == "tongue" || alias == "beard"))
                    continue;
                if (part == FacePart.Mouth && (alias == "brows" || alias == "lashes" || alias == "tears1" || alias == "tears2"))
                    continue;
                string[] full = new string[keys.Length];
                for (int i = 0; i < keys.Length; i++)
                {
                    full[i] = FaceRecipes.PartName(part) + "|" + keys[i];
                    Known.Add(full[i]);
                }
                Groups.Add(new Group { Renderer = r, Mesh = alias, Part = part, Keys = keys, FullKeys = full, Indices = indices.ToArray() });
            }
        }

        // ---------------------------------------------------------------- bones

        private void Collect(Transform t, BonesEditor bones)
        {
            foreach (Transform c in t)
            {
                if (Skip(c, bones))
                    continue;
                Bones.Add(c);
                Collect(c, bones);
            }
        }

        private static bool Skip(Transform t, BonesEditor bones)
        {
            string n = t.name;
            if (n.StartsWith("ca_slot") || n.StartsWith("N_") || n.StartsWith("ChildOf-") || n.Contains("_hair") || n.Contains("_O_") || n.StartsWith("O_"))
                return true;
            return bones != null && bones.KitIsHidden(t.gameObject);
        }

        private void FindExtras(FaceRecipes recipes)
        {
            ExtraBones.Clear();
            foreach (Transform t in Bones)
                if (Stock(t.name) == false && Referenced(recipes, t.name) == false)
                    ExtraBones.Add(t);
        }

        /// <summary>HS's own face bones (cf_J_ / cm_J_ / cf_s_ / cm_s_): reachable on the Bones page, not listed as extras.</summary>
        private static bool Stock(string name)
        {
            return name.StartsWith("cf_J_") || name.StartsWith("cm_J_") || name.StartsWith("cf_s_") || name.StartsWith("cm_s_");
        }

        private static bool Referenced(FaceRecipes r, string name)
        {
            if (r == null)
                return false;
            foreach (FacePoint p in r.Points)
                if (FaceRecipes.BoneMatches(p.Bone, name))
                    return true;
            foreach (FaceEntry e in r.Moods)
                foreach (BoneLine b in e.Bones)
                    if (FaceRecipes.BoneMatches(b.Bone, name))
                        return true;
            foreach (FaceEntry e in r.Vowels)
                foreach (BoneLine b in e.Bones)
                    if (FaceRecipes.BoneMatches(b.Bone, name))
                        return true;
            return false;
        }

        /// <summary>The bone a pattern names ("*_J_Mayu_L"), cached; null when this head has none.</summary>
        public Transform FindBone(string pattern)
        {
            Transform t;
            if (_boneCache.TryGetValue(pattern, out t))
                return t;
            t = null;
            foreach (Transform b in Bones)
                if (b != null && FaceRecipes.BoneMatches(pattern, b.name))
                {
                    t = b;
                    break;
                }
            if (t == null)
                Log.Write("face: no bone \"" + pattern + "\" on " + Target.name);
            _boneCache.Add(pattern, t);
            return t;
        }

        /// <summary>An exact shape name on one mesh alias (recipe "shape" lines).</summary>
        public bool FindShape(string meshAlias, string name, out SkinnedMeshRenderer r, out int index)
        {
            index = -1;
            if (_meshes.TryGetValue(meshAlias, out r) == false)
                return false;
            string key = meshAlias + "|" + name;
            if (_exactCache.TryGetValue(key, out index) == false)
            {
                index = r.sharedMesh.GetBlendShapeIndex(name);
                _exactCache.Add(key, index);
                if (index < 0)
                    Log.Write("face: no shape \"" + name + "\" on " + meshAlias + " of " + Target.name);
            }
            return index >= 0;
        }

        private struct ShapeRef { public SkinnedMeshRenderer Renderer; public int Index; }
        private readonly Dictionary<string, ShapeRef> _byFullKey = new Dictionary<string, ShapeRef>();

        /// <summary>FindShape by a "mesh|name" key (FaceResult.Shapes), cached - no string splitting per frame.</summary>
        public bool FindShape(string fullKey, out SkinnedMeshRenderer r, out int index)
        {
            ShapeRef s;
            if (_byFullKey.TryGetValue(fullKey, out s) == false)
            {
                int bar = fullKey.IndexOf('|');
                FindShape(fullKey.Substring(0, bar), fullKey.Substring(bar + 1), out s.Renderer, out s.Index);
                _byFullKey.Add(fullKey, s);
            }
            r = s.Renderer;
            index = s.Index;
            return r != null && index >= 0;
        }

        public bool HasMesh(string alias) { return _meshes.ContainsKey(alias); }

        /// <summary>Still the same character with the same meshes (a head / clothes change destroys renderers).</summary>
        public bool Valid
        {
            get
            {
                if (Target == null)
                    return false;
                foreach (Group g in Groups)
                    if (g.Renderer == null)
                        return false;
                return true;
            }
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name)
                return t;
            foreach (Transform c in t)
            {
                Transform f = FindDeep(c, name);
                if (f != null)
                    return f;
            }
            return null;
        }
    }
}
