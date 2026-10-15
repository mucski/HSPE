using System.Collections.Generic;
using ToolBox.Extensions;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HSPE.AMModules
{
    // addition (HSPE fork): the kit Dynamic Bones page's access to Joan's DynamicBonesEditor. The per-field
    // resets and the copy / paste settings actions that lived inside his IMGUI code moved here; his GUILogic calls them.
    public partial class DynamicBonesEditor
    {

        /// <summary>Changed entries, for the kit sidebar badge (a count, instead of serializing the module 1/s).</summary>
        internal int KitChangedCount() { return _dirtyDynamicBones.Count; }
        internal enum DbField { Enabled, Weight, Gravity, Force, FreezeAxis, Damping, Elasticity, Stiffness, Inertia, Radius }

        // ---------------------------------------------------------------- moved from GUILogic (shared)

        /// <summary>
        /// One field back to its original value. Joan's Enabled / Gravity / Force / FreezeAxis buttons did this without
        /// checking the field was changed (resetting an untouched gravity set it to zero); now all fields check, like
        /// his Weight / Damping / ... resets already did.
        /// </summary>
        internal bool ResetField(DynamicBone db, DbField field)
        {
            DynamicBoneData data;
            if (db == null || _dirtyDynamicBones.TryGetValue(db, out data) == false || data == null)
                return false;
            switch (field)
            {
                case DbField.Enabled:
                    if (!data.originalEnabled.hasValue) return false;
                    db.enabled = data.originalEnabled;
                    data.currentEnabled = data.originalEnabled;
                    data.originalEnabled.Reset();
                    return true;
                case DbField.Weight:
                    if (!data.originalWeight.hasValue) return false;
                    db.SetWeight(data.originalWeight);
                    data.currentWeight = data.originalWeight;
                    data.originalWeight.Reset();
                    return true;
                case DbField.Gravity:
                    if (!data.originalGravity.hasValue) return false;
                    db.m_Gravity = data.originalGravity;
                    data.currentGravity = data.originalGravity;
                    data.originalGravity.Reset();
                    return true;
                case DbField.Force:
                    if (!data.originalForce.hasValue) return false;
                    db.m_Force = data.originalForce;
                    data.currentForce = data.originalForce;
                    data.originalForce.Reset();
                    return true;
                case DbField.FreezeAxis:
                    if (!data.originalFreezeAxis.hasValue) return false;
                    db.m_FreezeAxis = data.originalFreezeAxis;
                    data.currentFreezeAxis = data.originalFreezeAxis;
                    data.originalFreezeAxis.Reset();
                    return true;
                case DbField.Damping:
                    if (!data.originalDamping.hasValue) return false;
                    db.m_Damping = data.originalDamping;
                    data.currentDamping = data.originalDamping;
                    data.originalDamping.Reset();
                    NotifyDynamicBoneForUpdate(db);
                    return true;
                case DbField.Elasticity:
                    if (!data.originalElasticity.hasValue) return false;
                    db.m_Elasticity = data.originalElasticity;
                    data.currentElasticity = data.originalElasticity;
                    data.originalElasticity.Reset();
                    NotifyDynamicBoneForUpdate(db);
                    return true;
                case DbField.Stiffness:
                    if (!data.originalStiffness.hasValue) return false;
                    db.m_Stiffness = data.originalStiffness;
                    data.currentStiffness = data.originalStiffness;
                    data.originalStiffness.Reset();
                    NotifyDynamicBoneForUpdate(db);
                    return true;
                case DbField.Inertia:
                    if (!data.originalInert.hasValue) return false;
                    db.m_Inert = data.originalInert;
                    data.currentInert = data.originalInert;
                    data.originalInert.Reset();
                    NotifyDynamicBoneForUpdate(db);
                    return true;
                case DbField.Radius:
                    if (!data.originalRadius.hasValue) return false;
                    db.m_Radius = data.originalRadius;
                    data.currentRadius = data.originalRadius;
                    data.originalRadius.Reset();
                    NotifyDynamicBoneForUpdate(db);
                    return true;
            }
            return false;
        }

        /// <summary>For Joan's FloatEditor onReset: the restored value, or the unchanged one.</summary>
        internal float ResetFloatField(DynamicBone db, DbField field, float value)
        {
            if (ResetField(db, field) == false)
                return value;
            return KitGetFloat(db, field);
        }

        /// <summary>Joan's "Copy Settings" (runs on his next gizmo pass, as before).</summary>
        internal void CopySettings(DynamicBone db)
        {
            if (db == null)
                return;
            _preDragAction = () =>
            {
                _copiedDynamicBoneData = new DynamicBoneData
                {
                    currentEnabled = db.enabled,
                    currentWeight = db.GetWeight(),
                    currentGravity = db.m_Gravity,
                    currentForce = db.m_Force,
                    currentFreezeAxis = db.m_FreezeAxis,
                    currentDamping = db.m_Damping,
                    currentElasticity = db.m_Elasticity,
                    currentStiffness = db.m_Stiffness,
                    currentInert = db.m_Inert,
                    currentRadius = db.m_Radius
                };
            };
        }

        /// <summary>Joan's "Paste Settings".</summary>
        internal void PasteSettings(DynamicBone db)
        {
            if (db == null || _copiedDynamicBoneData == null)
                return;
            _preDragAction = () =>
            {
                if (db.enabled != _copiedDynamicBoneData.currentEnabled)
                    SetDynamicBoneEnabled(db, _copiedDynamicBoneData.currentEnabled);
                if (db.GetWeight() != _copiedDynamicBoneData.currentWeight)
                    SetDynamicBoneWeight(db, _copiedDynamicBoneData.currentWeight);
                if (db.m_Gravity != _copiedDynamicBoneData.currentGravity)
                    SetDynamicBoneGravity(db, _copiedDynamicBoneData.currentGravity);
                if (db.m_Force != _copiedDynamicBoneData.currentForce)
                    SetDynamicBoneForce(db, _copiedDynamicBoneData.currentForce);
                if (db.m_FreezeAxis != _copiedDynamicBoneData.currentFreezeAxis)
                    SetDynamicBoneFreezeAxis(db, _copiedDynamicBoneData.currentFreezeAxis);
                if (db.m_Damping != _copiedDynamicBoneData.currentDamping)
                    SetDynamicBoneDamping(db, _copiedDynamicBoneData.currentDamping);
                if (db.m_Elasticity != _copiedDynamicBoneData.currentElasticity)
                    SetDynamicBoneElasticity(db, _copiedDynamicBoneData.currentElasticity);
                if (db.m_Stiffness != _copiedDynamicBoneData.currentStiffness)
                    SetDynamicBoneStiffness(db, _copiedDynamicBoneData.currentStiffness);
                if (db.m_Inert != _copiedDynamicBoneData.currentInert)
                    SetDynamicBoneInertia(db, _copiedDynamicBoneData.currentInert);
                if (db.m_Radius != _copiedDynamicBoneData.currentRadius)
                    SetDynamicBoneRadius(db, _copiedDynamicBoneData.currentRadius);
            };
        }

        /// <summary>A press on any UI (kit windows, Studio panels) never starts a gizmo drag.</summary>
        internal static bool PointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        // ---------------------------------------------------------------- kit page access

        /// <summary>Bones with a root, the ones Joan lists.</summary>
        /// <summary>Fills a caller-owned list (asked a few times a second: no new list each time).</summary>
        internal void KitBones(List<DynamicBone> list)
        {
            list.Clear();
            foreach (DynamicBone db in _dynamicBones)
                if (db != null && db.m_Root != null)
                    list.Add(db);
        }

        internal DynamicBone kitTarget
        {
            get { return _dynamicBoneTarget; }
            set { _dynamicBoneTarget = value; }
        }

        internal bool KitIsDirty(DynamicBone db) { return db != null && _dirtyDynamicBones.ContainsKey(db); }

        internal bool KitIsFieldDirty(DynamicBone db, DbField field)
        {
            DynamicBoneData data;
            if (db == null || _dirtyDynamicBones.TryGetValue(db, out data) == false || data == null)
                return false;
            switch (field)
            {
                case DbField.Enabled: return data.originalEnabled.hasValue;
                case DbField.Weight: return data.originalWeight.hasValue;
                case DbField.Gravity: return data.originalGravity.hasValue;
                case DbField.Force: return data.originalForce.hasValue;
                case DbField.FreezeAxis: return data.originalFreezeAxis.hasValue;
                case DbField.Damping: return data.originalDamping.hasValue;
                case DbField.Elasticity: return data.originalElasticity.hasValue;
                case DbField.Stiffness: return data.originalStiffness.hasValue;
                case DbField.Inertia: return data.originalInert.hasValue;
                case DbField.Radius: return data.originalRadius.hasValue;
            }
            return false;
        }

        internal float KitGetFloat(DynamicBone db, DbField field)
        {
            switch (field)
            {
                case DbField.Weight: return db.GetWeight();
                case DbField.Damping: return db.m_Damping;
                case DbField.Elasticity: return db.m_Elasticity;
                case DbField.Stiffness: return db.m_Stiffness;
                case DbField.Inertia: return db.m_Inert;
                case DbField.Radius: return db.m_Radius;
            }
            return 0f;
        }

        internal void KitSetFloat(DynamicBone db, DbField field, float v)
        {
            switch (field)
            {
                case DbField.Weight: SetDynamicBoneWeight(db, v); break;
                case DbField.Damping: SetDynamicBoneDamping(db, v); break;
                case DbField.Elasticity: SetDynamicBoneElasticity(db, v); break;
                case DbField.Stiffness: SetDynamicBoneStiffness(db, v); break;
                case DbField.Inertia: SetDynamicBoneInertia(db, v); break;
                case DbField.Radius: SetDynamicBoneRadius(db, v); break;
            }
        }

        internal void KitSetEnabled(DynamicBone db, bool on) { SetDynamicBoneEnabled(db, on); }
        internal void KitSetGravity(DynamicBone db, Vector3 g) { SetDynamicBoneGravity(db, g); }
        internal void KitSetForce(DynamicBone db, Vector3 f) { SetDynamicBoneForce(db, f); }
        internal void KitSetFreezeAxis(DynamicBone db, DynamicBone.FreezeAxis fa) { SetDynamicBoneFreezeAxis(db, fa); }
        internal void KitReset(DynamicBone db) { SetDynamicBoneNotDirty(db); }
        internal void KitResetAll() { ResetAll(); }
        internal void KitCopyToFK() { CopyToFK(); }
        internal void KitRefreshList() { RefreshDynamicBoneList(); }
        internal bool kitCanPaste { get { return _copiedDynamicBoneData != null; } }

        /// <summary>
        /// Joan's step (10^index, -5..1): the +/- step of gravity / force AND the strength of a gizmo drag in the
        /// viewport. His IMGUI step slider is what sets _inc, so the kit page must set it too (never drawn = 1 = a
        /// thousand times too strong).
        /// </summary>
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

        /// <summary>Joan's "Go to root" / "Go to tail": the Bones module on that bone.</summary>
        internal void KitGoTo(DynamicBone db, bool tail)
        {
            if (db == null || db.m_Root == null)
                return;
            _parent.EnableModule(_parent._bonesEditor);
            _parent._bonesEditor.GoToObject(tail ? db.m_Root.GetDeepestLeaf().gameObject : db.m_Root.gameObject);
        }
    }
}
