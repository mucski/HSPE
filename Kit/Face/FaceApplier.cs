using System.Collections.Generic;
using HSPE.AMModules;
using UnityEngine;

namespace HSPE.Kit.Face
{
    /// <summary>
    /// Writes a FaceResult through HSPE's own Blend Shapes / Bones modules (so scene save, both HSPE windows and
    /// Timeline see ordinary entries) and remembers what it owns: shapes / bone coordinates it stops using are handed
    /// back. A part the mix drives gets every other key of its groups written 0 - the game's defaults sit at 100 and
    /// would otherwise stack under the mood. Only called when an input changed.
    /// </summary>
    internal sealed class FaceApplier
    {
        private struct Rest { public Vector3 Position; public Quaternion Rotation; public bool OwnPos, OwnRot; public Vector3 WrotePos; public Quaternion WroteRot; }

        private readonly Dictionary<SkinnedMeshRenderer, Dictionary<int, float>> _owned = new Dictionary<SkinnedMeshRenderer, Dictionary<int, float>>();
        private readonly Dictionary<SkinnedMeshRenderer, Dictionary<int, float>> _next = new Dictionary<SkinnedMeshRenderer, Dictionary<int, float>>();
        private readonly Dictionary<Transform, Rest> _bones = new Dictionary<Transform, Rest>();
        private readonly List<Transform> _scratch = new List<Transform>();
        private readonly List<SkinnedMeshRenderer> _scratchR = new List<SkinnedMeshRenderer>();
        private readonly List<int> _scratchI = new List<int>();

        public bool Owns
        {
            get
            {
                if (_bones.Count != 0)
                    return true;
                foreach (KeyValuePair<SkinnedMeshRenderer, Dictionary<int, float>> p in _owned)
                    if (p.Value.Count != 0)
                        return true;
                return false;
            }
        }

        public void Apply(FaceRig rig, FaceRecipes recipes, FaceResult result)
        {
            BlendShapesEditor bs = rig.Target._blendShapesEditor;
            BonesEditor bones = rig.Target._bonesEditor;
            if (bs == null || bones == null)
                return;

            // ---- shapes: build the full target set, write it, release what fell out
            foreach (KeyValuePair<SkinnedMeshRenderer, Dictionary<int, float>> p in _next)
                p.Value.Clear();
            foreach (FaceRig.Group g in rig.Groups)
            {
                if (result.Uses(g.Part) == false)
                    continue;
                // a group none of the mix's keys exist in (male teeth: k00_open etc., the mouth uses k00_smile1...)
                // stays with the game - holding it all at 0 left the teeth clenched
                bool any = false;
                for (int i = 0; i < g.FullKeys.Length && any == false; i++)
                {
                    float w;
                    any = result.Patterns.TryGetValue(g.FullKeys[i], out w) && w > 0f;
                }
                if (any == false)
                    continue;
                Dictionary<int, float> next = Next(g.Renderer);
                for (int i = 0; i < g.Keys.Length; i++)
                {
                    if (recipes.Ignores.Contains(g.Keys[i]))
                        continue;
                    float w;
                    result.Patterns.TryGetValue(g.FullKeys[i], out w);
                    next[g.Indices[i]] = Mathf.Clamp(w, 0f, 100f);
                }
            }
            foreach (KeyValuePair<string, float> p in result.Shapes)
            {
                SkinnedMeshRenderer r;
                int index;
                if (rig.FindShape(p.Key, out r, out index))
                    Next(r)[index] = Mathf.Clamp(p.Value, 0f, 100f);
            }
            foreach (KeyValuePair<SkinnedMeshRenderer, Dictionary<int, float>> p in _next)
                foreach (KeyValuePair<int, float> s in p.Value)
                    bs.KitFaceSetWeight(p.Key, s.Key, s.Value);
            // release owned shapes not in the new set
            _scratchR.Clear();
            foreach (KeyValuePair<SkinnedMeshRenderer, Dictionary<int, float>> p in _owned)
                _scratchR.Add(p.Key);
            foreach (SkinnedMeshRenderer r in _scratchR)
            {
                Dictionary<int, float> now;
                _next.TryGetValue(r, out now);
                _scratchI.Clear();
                foreach (KeyValuePair<int, float> s in _owned[r])
                    if (now == null || now.ContainsKey(s.Key) == false)
                        _scratchI.Add(s.Key);
                foreach (int i in _scratchI)
                    if (r != null)
                        bs.KitFaceRelease(r, i);
            }
            // _owned = _next, reusing the dictionaries (Timeline playback applies every frame: no garbage)
            foreach (KeyValuePair<SkinnedMeshRenderer, Dictionary<int, float>> p in _owned)
                p.Value.Clear();
            foreach (KeyValuePair<SkinnedMeshRenderer, Dictionary<int, float>> p in _next)
            {
                if (p.Value.Count == 0)
                    continue;
                Dictionary<int, float> owned;
                if (_owned.TryGetValue(p.Key, out owned) == false)
                    _owned.Add(p.Key, owned = new Dictionary<int, float>());
                foreach (KeyValuePair<int, float> s in p.Value)
                    owned[s.Key] = s.Value;
            }

            // ---- bones
            _scratch.Clear();
            foreach (KeyValuePair<Transform, Rest> p in _bones)
                _scratch.Add(p.Key);
            foreach (Transform t in _scratch) // start from "nothing owned this time", re-own below
            {
                Rest rest = _bones[t];
                rest.OwnPos = rest.OwnRot = false;
                _bones[t] = rest;
            }
            foreach (KeyValuePair<string, V3> p in result.BonePos)
                if (p.Value.IsZero == false)
                    SetBone(rig, bones, p.Key, p.Value, false);
            foreach (KeyValuePair<string, V3> p in result.BoneRot)
                if (p.Value.IsZero == false)
                    SetBone(rig, bones, p.Key, p.Value, true);
            foreach (Transform t in _scratch)
            {
                Rest rest = _bones[t];
                if (t == null)
                {
                    _bones.Remove(t);
                    continue;
                }
                if (rest.OwnPos == false)
                    bones.KitFaceRelease(t, false);
                if (rest.OwnRot == false)
                    bones.KitFaceRelease(t, true);
                if (rest.OwnPos == false && rest.OwnRot == false)
                    _bones.Remove(t);
            }
        }

        private Dictionary<int, float> Next(SkinnedMeshRenderer r)
        {
            Dictionary<int, float> d;
            if (_next.TryGetValue(r, out d) == false)
                _next.Add(r, d = new Dictionary<int, float>());
            return d;
        }

        private void SetBone(FaceRig rig, BonesEditor bones, string pattern, V3 v, bool rotation)
        {
            Transform t = rig.FindBone(pattern);
            if (t == null)
                return;
            Rest rest;
            if (_bones.TryGetValue(t, out rest) == false)
            {
                bones.KitFaceRest(t, out rest.Position, out rest.Rotation);
                rest.OwnPos = rest.OwnRot = false;
            }
            if (rotation)
            {
                rest.WroteRot = rest.Rotation * Quaternion.Euler(v.X, v.Y, v.Z);
                bones.KitFaceSetRotation(t, rest.WroteRot);
                rest.OwnRot = true;
            }
            else
            {
                rest.WrotePos = rest.Position + new Vector3(v.X, v.Y, v.Z);
                bones.KitFaceSetPosition(t, rest.WrotePos);
                rest.OwnPos = true;
            }
            _bones[t] = rest;
        }

        /// <summary>
        /// True when something else (Blend Shapes / Bones page, a paste, Timeline) changed or dropped a value this
        /// applier wrote. Iterates without allocating; the window asks about once a second.
        /// </summary>
        public bool Tampered(FaceRig rig)
        {
            if (rig == null || rig.Target == null)
                return false;
            BlendShapesEditor bs = rig.Target._blendShapesEditor;
            BonesEditor bones = rig.Target._bonesEditor;
            if (bs != null)
                foreach (KeyValuePair<SkinnedMeshRenderer, Dictionary<int, float>> p in _owned)
                {
                    if (p.Key == null)
                        continue;
                    foreach (KeyValuePair<int, float> s in p.Value)
                    {
                        float held;
                        if (bs.KitFaceHolds(p.Key, s.Key, out held) == false || Mathf.Abs(held - s.Value) > 0.01f)
                            return true;
                    }
                }
            if (bones != null)
                foreach (KeyValuePair<Transform, Rest> p in _bones)
                {
                    if (p.Key == null)
                        continue;
                    Vector3 pos;
                    Quaternion rot;
                    if (p.Value.OwnPos && (bones.KitFaceHolds(p.Key, false, out pos, out rot) == false || (pos - p.Value.WrotePos).sqrMagnitude > 1e-10f))
                        return true;
                    // not Quaternion.Angle: it reports ~0.02-0.04 degrees for IDENTICAL rotations (acos precision) - that
                    // was a false "changed elsewhere" on every rotated face point
                    if (p.Value.OwnRot && (bones.KitFaceHolds(p.Key, true, out pos, out rot) == false || 1f - Mathf.Abs(Quaternion.Dot(rot, p.Value.WroteRot)) > 1e-6f))
                        return true;
                }
            return false;
        }

        /// <summary>Stops owning anything without writing (HSPE's own load already reset / replaced the values).</summary>
        public void Forget()
        {
            foreach (KeyValuePair<SkinnedMeshRenderer, Dictionary<int, float>> p in _owned)
                p.Value.Clear();
            _bones.Clear();
        }

        /// <summary>Hands everything back to HSPE's original values.</summary>
        public void Release(FaceRig rig)
        {
            if (rig == null || rig.Target == null)
                return;
            BlendShapesEditor bs = rig.Target._blendShapesEditor;
            BonesEditor bones = rig.Target._bonesEditor;
            if (bs != null)
                foreach (KeyValuePair<SkinnedMeshRenderer, Dictionary<int, float>> p in _owned)
                    if (p.Key != null)
                        foreach (KeyValuePair<int, float> s in p.Value)
                            bs.KitFaceRelease(p.Key, s.Key);
            foreach (KeyValuePair<SkinnedMeshRenderer, Dictionary<int, float>> p in _owned)
                p.Value.Clear();
            if (bones != null)
                foreach (KeyValuePair<Transform, Rest> p in _bones)
                    if (p.Key != null)
                    {
                        if (p.Value.OwnPos) bones.KitFaceRelease(p.Key, false);
                        if (p.Value.OwnRot) bones.KitFaceRelease(p.Key, true);
                    }
            _owned.Clear();
            _bones.Clear();
        }
    }
}
