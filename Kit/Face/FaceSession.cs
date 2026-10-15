using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HSPE.Kit.Face
{
    using HSUIKit;

    /// <summary>One character's face: its rig, what the Face window set (input), what got written (applier), undo.</summary>
    internal sealed class FaceSession
    {
        public readonly PoseController Target;
        public FaceRig Rig;
        public readonly FaceApplier Applier = new FaceApplier();
        public readonly FaceInput Input = new FaceInput();
        public readonly FaceUndo Undo = new FaceUndo();
        public int Version; // bumped when something other than the window changes the input (the Timeline track)
        private readonly FaceResult _result = new FaceResult();

        public FaceSession(PoseController target)
        {
            Target = target;
            Rebind();
        }

        public void Rebind()
        {
            Rig = FaceRig.Bind(Target, FaceSessions.Recipes);
            Input.ExtraPoints = ExtraPoints(Rig);
        }

        /// <summary>Every extra bone gets a runtime point "x:&lt;bone&gt;": x / y = 4 mm local position at full drag.</summary>
        private static List<FacePoint> ExtraPoints(FaceRig rig)
        {
            List<FacePoint> list = new List<FacePoint>();
            foreach (Transform t in rig.ExtraBones)
            {
                if (t == null)
                    continue;
                list.Add(new FacePoint
                {
                    Name = "x:" + t.name,
                    Bone = t.name,
                    X = new BoneLine { Bone = t.name, Value = new V3(0.004f, 0f, 0f) },
                    Y = new BoneLine { Bone = t.name, Value = new V3(0f, 0.004f, 0f) },
                });
            }
            return list;
        }

        public bool Valid { get { return Target != null && Rig != null && Rig.Valid; } }

        /// <summary>Mix the input and write it (or hand everything back when the input is neutral).</summary>
        public void Apply()
        {
            if (Valid == false)
                Rebind();
            if (Input.IsNeutral)
            {
                Applier.Release(Rig);
                return;
            }
            FaceMix.Mix(FaceSessions.Recipes, Rig.Sex, Input, _result, Rig.Known);
            Applier.Apply(Rig, FaceSessions.Recipes, _result);
        }

        /// <summary>Drops the face WITHOUT touching HSPE (a load already reset those values): neutral input, nothing owned.</summary>
        public void Forget()
        {
            Input.Radius = 0f;
            Input.Vowel = null;
            Input.EyeOpen = 1f;
            Input.MouthOpen = 0f;
            Input.Drags.Clear();
            Applier.Forget();
            Version++;
        }

        public void Reset()
        {
            Input.Radius = 0f;
            Input.Vowel = null;
            Input.EyeOpen = 1f;
            Input.MouthOpen = 0f;
            Input.Drags.Clear();
            Applier.Release(Rig);
        }
    }

    /// <summary>Undo / redo of the face input (30 steps; a snapshot is pushed BEFORE each change).</summary>
    internal sealed class FaceUndo
    {
        private const int Max = 30;
        private readonly List<FaceInput> _undo = new List<FaceInput>();
        private readonly List<FaceInput> _redo = new List<FaceInput>();

        public bool CanUndo { get { return _undo.Count != 0; } }
        public bool CanRedo { get { return _redo.Count != 0; } }

        public void Push(FaceInput before)
        {
            _undo.Add(before.Clone());
            if (_undo.Count > Max)
                _undo.RemoveAt(0);
            _redo.Clear();
        }

        public bool Undo(FaceInput current)
        {
            if (_undo.Count == 0)
                return false;
            _redo.Add(current.Clone());
            current.CopyFrom(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
            return true;
        }

        public bool Redo(FaceInput current)
        {
            if (_redo.Count == 0)
                return false;
            _undo.Add(current.Clone());
            current.CopyFrom(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
            return true;
        }
    }

    /// <summary>Shared recipes + one session per character (kept while the character exists).</summary>
    internal static class FaceSessions
    {
        private static FaceRecipes _recipes;
        private static readonly Dictionary<PoseController, FaceSession> _sessions = new Dictionary<PoseController, FaceSession>();
        private static readonly List<PoseController> _dead = new List<PoseController>();

        internal static string RecipePath { get { return Path.Combine(Application.dataPath, "../Plugins/HSPE/face_recipes.txt"); } }

        internal static FaceRecipes Recipes
        {
            get
            {
                if (_recipes == null)
                    Reload(false);
                return _recipes;
            }
        }

        /// <summary>Re-reads face_recipes.txt; on errors the valid lines still load (bad ones logged + toast).</summary>
        internal static void Reload(bool rebindAll)
        {
            string text = File.Exists(RecipePath) ? File.ReadAllText(RecipePath) : "";
            FaceRecipes r = FaceRecipes.Parse(text);
            foreach (string e in r.Errors)
                Log.Write("face recipes: " + e);
            if (text.Length == 0)
                Log.Write("face recipes: " + RecipePath + " missing or empty");
            _recipes = r;
            if (r.Errors.Count != 0 && global::HSPE.HSPE._kitAvailable)
                ToastRecipeErrors(r.Errors.Count);
            if (rebindAll)
                foreach (KeyValuePair<PoseController, FaceSession> p in _sessions)
                    if (p.Key != null)
                    {
                        p.Value.Rebind();
                        p.Value.Apply();
                    }
        }

        // own method: KitToast (HSUIKit) is only resolved when HSUIKit passed the version check
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ToastRecipeErrors(int count)
        {
            KitToast.Show("Face recipes", count + " bad line(s) skipped - see the log", ToastKind.Warning, 4f);
        }

        /// <summary>The character's session, or null for non-characters / missing HSPE modules.</summary>
        internal static FaceSession Get(PoseController target, bool create)
        {
            if (target == null || !(target is CharaPoseController) || target._blendShapesEditor == null || target._bonesEditor == null)
                return null;
            FaceSession s;
            if (_sessions.TryGetValue(target, out s))
                return s;
            if (create == false)
                return null;
            Prune();
            s = new FaceSession(target);
            _sessions.Add(target, s);
            return s;
        }

        /// <summary>Drops sessions of deleted characters (scene loads / deletes) - they pinned rigs, caches and undo.</summary>
        internal static void Prune()
        {
            _dead.Clear();
            foreach (KeyValuePair<PoseController, FaceSession> p in _sessions)
                if (p.Key == null)
                    _dead.Add(p.Key);
            foreach (PoseController k in _dead)
                _sessions.Remove(k);
            if (_dead.Count != 0)
                FaceTimeline.Forget();
        }
    }
}
