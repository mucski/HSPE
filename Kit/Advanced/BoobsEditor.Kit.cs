using ToolBox.Extensions;
using UnityEngine;

namespace HSPE.AMModules
{
    // addition (HSPE fork): the kit Boobs page's access to Joan's BoobsEditor (his getters / setters /
    // reset, which already work on the stored BoobData, so no logic had to move).
    public partial class BoobsEditor
    {

        /// <summary>Changed entries, for the kit sidebar badge (a count, instead of serializing the module 1/s).</summary>
        internal int KitChangedCount() { return _dirtyDynamicBones.Count; }
        internal DynamicBone_Ver02 kitRight { get { return _rightBoob; } }
        internal DynamicBone_Ver02 kitLeft { get { return _leftBoob; } }

        internal bool KitIsDirty(DynamicBone_Ver02 b) { return b != null && IsDirty(b); }

        internal void KitDirtyParts(DynamicBone_Ver02 b, out bool enabled, out bool gravity, out bool force)
        {
            BoobData data;
            if (b != null && _dirtyDynamicBones.TryGetValue(b, out data))
            {
                enabled = data.originalEnabled.hasValue;
                gravity = data.originalGravity.hasValue;
                force = data.originalForce.hasValue;
            }
            else
                enabled = gravity = force = false;
        }

        internal bool KitGetEnabled(DynamicBone_Ver02 b) { return GetEnabled(b); }
        internal Vector3 KitGetGravity(DynamicBone_Ver02 b) { return GetGravity(b); }
        internal Vector3 KitGetForce(DynamicBone_Ver02 b) { return GetForce(b); }
        internal void KitSetEnabled(DynamicBone_Ver02 b, bool on) { SetEnabled(b, on); }
        internal void KitSetGravity(DynamicBone_Ver02 b, Vector3 g) { SetGravity(b, g); }
        internal void KitSetForce(DynamicBone_Ver02 b, Vector3 f) { SetForce(b, f); }
        internal void KitReset(DynamicBone_Ver02 b) { SetNotDirty(b); }
        internal void KitCopyToFK() { CopyToFK(); }

        internal bool kitAlternativeUpdateMode
        {
            get { return _alternativeUpdateMode; }
            set { _alternativeUpdateMode = value; }
        }

        /// <summary>Joan's step: gravity / force step and gizmo drag strength (see DynamicBonesEditor.kitStepIndex).</summary>
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

        internal void KitGoTo(DynamicBone_Ver02 b, bool tail)
        {
            if (b == null || b.Root == null)
                return;
            _parent.EnableModule(_parent._bonesEditor);
            _parent._bonesEditor.GoToObject(tail ? b.Root.GetDeepestLeaf().gameObject : b.Root.gameObject);
        }
    }
}
