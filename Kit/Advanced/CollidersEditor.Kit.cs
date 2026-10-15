using System.Collections.Generic;
using UnityEngine;
// HS1 has no collider base class; Joan aliases it the same way
using DynamicBoneColliderBase = DynamicBoneCollider;

namespace HSPE.AMModules
{
    // addition (HSPE fork): the kit Colliders page's access to Joan's CollidersEditor. His value changes,
    // reset and "affected dynamic bones" links already are methods; the one piece of logic in his IMGUI code (height
    // never below 2 x radius) is in KitSetHeight. Per-field reset is new (Joan only resets a whole collider) and uses
    // the same original values his reset uses.
    public partial class CollidersEditor
    {

        /// <summary>Changed entries, for the kit sidebar badge (a count, instead of serializing the module 1/s).</summary>
        internal int KitChangedCount() { return _dirtyColliders.Count; }
        internal enum ColliderField { Center, Direction, Bound, Radius, Height }

        /// <summary>Fills a caller-owned list (asked a few times a second: no new list each time).</summary>
        internal void KitColliders(List<DynamicBoneColliderBase> list)
        {
            list.Clear();
            foreach (KeyValuePair<Transform, DynamicBoneColliderBase> pair in _colliders)
                if (pair.Key != null && pair.Value != null)
                    list.Add(pair.Value);
        }

        internal DynamicBoneColliderBase kitTarget
        {
            get { return _colliderTarget; }
            set
            {
                _colliderTarget = value;
                UpdateDebugLinesState(this);
            }
        }

        internal bool kitIsLone { get { return _isLoneCollider; } }
        internal static string[] kitDirectionNames { get { return _directionNames; } }
        internal static string[] kitBoundNames { get { return _boundNames; } }

        internal bool KitIsDirty(DynamicBoneColliderBase c) { return c != null && IsColliderDirty(c); }

        internal bool KitIsFieldDirty(DynamicBoneColliderBase c, ColliderField field)
        {
            ColliderDataBase data;
            if (c == null || _dirtyColliders.TryGetValue(c, out data) == false)
                return false;
            ColliderData cd = data as ColliderData;
            switch (field)
            {
                case ColliderField.Center: return data.originalCenter.hasValue;
                case ColliderField.Direction: return data.originalDirection.hasValue;
                case ColliderField.Bound: return data.originalBound.hasValue;
                case ColliderField.Radius: return cd != null && cd.originalRadius.hasValue;
                case ColliderField.Height: return cd != null && cd.originalHeight.hasValue;
            }
            return false;
        }

        internal void KitSetCenter(DynamicBoneColliderBase c, Vector3 v) { SetColliderCenter(c, v); }
        internal void KitSetDirection(DynamicBoneColliderBase c, int d) { SetColliderDirection(c, (DynamicBoneColliderBase.Direction)d); }
        internal void KitSetBound(DynamicBoneColliderBase c, int b) { SetColliderBound(c, (DynamicBoneColliderBase.Bound)b); }
        internal void KitSetRadius(DynamicBoneCollider c, float r) { SetColliderRadius(c, r); }

        /// <summary>Joan's rule from his Height field: never below twice the radius.</summary>
        internal void KitSetHeight(DynamicBoneCollider c, float h)
        {
            if (h < c.m_Radius * 2)
                h = c.m_Radius * 2;
            SetColliderHeight(c, h);
        }

        internal void KitReset(DynamicBoneColliderBase c) { SetColliderNotDirty(c); }
        internal void KitResetAll() { ResetAll(); }

        /// <summary>One field back to its original; the collider stops being "changed" when nothing is left.</summary>
        internal void KitResetField(DynamicBoneColliderBase c, ColliderField field)
        {
            ColliderDataBase data;
            if (c == null || _dirtyColliders.TryGetValue(c, out data) == false)
                return;
            ColliderData cd = data as ColliderData;
            DynamicBoneCollider normal = c as DynamicBoneCollider;
            switch (field)
            {
                case ColliderField.Center:
                    if (data.originalCenter.hasValue) { c.m_Center = data.originalCenter; data.originalCenter.Reset(); }
                    break;
                case ColliderField.Direction:
                    if (data.originalDirection.hasValue) { c.m_Direction = data.originalDirection; data.originalDirection.Reset(); }
                    break;
                case ColliderField.Bound:
                    if (data.originalBound.hasValue) { c.m_Bound = data.originalBound; data.originalBound.Reset(); }
                    break;
                case ColliderField.Radius:
                    if (cd != null && normal != null && cd.originalRadius.hasValue) { normal.m_Radius = cd.originalRadius; cd.originalRadius.Reset(); }
                    break;
                case ColliderField.Height:
                    if (cd != null && normal != null && cd.originalHeight.hasValue) { normal.m_Height = cd.originalHeight; cd.originalHeight.Reset(); }
                    break;
            }
            if (data.hasValue == false && data.ignoredDynamicBones.Count == 0)
                _dirtyColliders.Remove(c);
        }

        internal int kitStepIndex
        {
            get { return _incIndex; }
            set
            {
                _incIndex = Mathf.Clamp(value, -5, 1);
                _inc = Mathf.Pow(10, _incIndex);
            }
        }

        internal float kitStep { get { return Mathf.Pow(10, _incIndex); } }

        internal void KitGoToBone(DynamicBoneColliderBase c)
        {
            if (c == null)
                return;
            _parent.EnableModule(_parent._bonesEditor);
            _parent._bonesEditor.GoToObject(c.gameObject);
        }

        // ---------------------------------------------------------------- lone colliders: affected dynamic bones

        internal sealed class KitAffected
        {
            public PoseController owner;
            public string label;
            public List<object> bones = new List<object>(); // DynamicBone or DynamicBone_Ver02 (breasts)
            public List<string> names = new List<string>();
        }

        /// <summary>Every object with dynamic bones this collider can act on (Joan's list).</summary>
        internal List<KitAffected> KitAffectedList(DynamicBoneColliderBase collider)
        {
            List<KitAffected> list = new List<KitAffected>();
            foreach (PoseController controller in PoseController._poseControllers)
            {
                if (controller == null)
                    continue;
                KitAffected a = new KitAffected { owner = controller };
                foreach (DynamicBone db in controller._dynamicBonesEditor._dynamicBones)
                {
                    if (db == null || db.m_Root == null)
                        continue;
                    a.bones.Add(db);
                    a.names.Add(db.m_Root.name);
                }
                CharaPoseController chara = controller as CharaPoseController;
                if (chara != null && CharaPoseController._charaPoseControllers.Contains(chara) && chara._boobsEditor != null && collider is DynamicBoneCollider)
                    foreach (DynamicBone_Ver02 b in chara._boobsEditor._dynamicBones)
                    {
                        a.bones.Add(b);
                        a.names.Add(b.Root != null ? b.Root.name : "breast");
                    }
                if (a.bones.Count == 0)
                    continue;
                a.label = controller.target.oci.treeNodeObject.textName + " (" + controller.target.oci.guideObject.transformTarget.name + ")";
                list.Add(a);
            }
            return list;
        }

        /// <summary>Changes when the scene's objects / dynamic bones do; lets the page skip rebuilding its list (no garbage).</summary>
        internal int KitAffectedSignature(DynamicBoneColliderBase collider)
        {
            int sig = collider != null ? collider.GetInstanceID() : 0;
            foreach (PoseController controller in PoseController._poseControllers)
            {
                if (controller == null)
                    continue;
                sig = sig * 31 + controller.GetInstanceID();
                sig = sig * 31 + controller._dynamicBonesEditor._dynamicBones.Count;
                CharaPoseController chara = controller as CharaPoseController;
                if (chara != null && chara._boobsEditor != null)
                    sig = sig * 31 + chara._boobsEditor._dynamicBones.Length;
            }
            return sig;
        }

        internal bool KitAffects(DynamicBoneColliderBase collider, PoseController owner, object bone)
        {
            ColliderDataBase data;
            HashSet<object> ignored;
            if (_dirtyColliders.TryGetValue(collider, out data) == false || data.ignoredDynamicBones.TryGetValue(owner, out ignored) == false)
                return true;
            return ignored.Contains(bone) == false;
        }

        internal void KitSetAffects(DynamicBoneColliderBase collider, PoseController owner, object bone, bool affects)
        {
            SetIgnoreDynamicBone(collider, owner, bone, !affects);
        }

        internal void KitSetAffectsAll(DynamicBoneColliderBase collider, bool affects)
        {
            SetIgnoreAllDynamicBones(collider, !affects);
        }
    }
}
