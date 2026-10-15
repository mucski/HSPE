using System.Collections.Generic;
using RootMotion.FinalIK;
using UnityEngine;

namespace HSPE.AMModules
{
    // addition (HSPE fork): the kit IK page's access to Joan's IKEditor. Values go through his setters
    // (which record the originals). His ~20 per-field resets are inline lambdas in GUILogic; rewriting all of them in
    // his IMGUI code would risk more than it saves, so they are reproduced here field by field (same assignments) and
    // his GUILogic is left untouched.
    public partial class IKEditor
    {

        /// <summary>Changed entries, for the kit sidebar badge (a count, instead of serializing the module 1/s).</summary>
        internal int KitChangedCount() { return _dirtyIks.Count; }
        internal enum IKField
        {
            Enabled, FixTransforms, PositionWeight,
            Tolerance, MaxIterations, RootPin, Iterations,
            Axis, ClampWeight, ClampSmoothing, PoleAxis, PolePosition, PoleWeight,
            LimbRotationWeight, LimbBendModifierWeight
        }

        internal enum KitIKType { Unknown, FABRIK, FABRIKRoot, AimIK, CCDIK, FullBodyBipedIK, LimbIK }

        internal enum FbbEffector { Body, RightShoulder, LeftShoulder, RightHand, LeftHand, RightThigh, LeftThigh, RightFoot, LeftFoot }

        internal enum FbbBend { RightArm, LeftArm, RightLeg, LeftLeg }

        // ---------------------------------------------------------------- list / target

        /// <summary>Fills a caller-owned list (asked a few times a second: no new list each time).</summary>
        internal void KitIKs(List<IK> list)
        {
            list.Clear();
            foreach (KeyValuePair<IK, IKWrapper> pair in _iks)
                if (pair.Key != null)
                    list.Add(pair.Key);
        }

        private IKWrapper W(IK ik)
        {
            IKWrapper w;
            return ik != null && _iks.TryGetValue(ik, out w) ? w : null;
        }

        internal KitIKType KitType(IK ik)
        {
            IKWrapper w = W(ik);
            return w == null ? KitIKType.Unknown : (KitIKType)(int)w.type;
        }

        internal Transform KitRoot(IK ik)
        {
            IKWrapper w = W(ik);
            return w != null ? w.solver.GetRoot() : null;
        }

        internal IK kitTarget
        {
            get { return _ikTarget != null ? _ikTarget.ik : null; }
            set
            {
                IKWrapper w = W(value);
                if (w == null || w == _ikTarget)
                    return;
                _ikTarget = w;
                ToolBox.TimelineCompatibility.RefreshInterpolablesList(); // as Joan's list does
            }
        }

        internal bool kitIsCharacter { get { return _isCharacter; } }
        internal bool KitIsDirty(IK ik) { IKWrapper w = W(ik); return w != null && _dirtyIks.ContainsKey(w); }
        internal void KitReset(IK ik) { IKWrapper w = W(ik); if (w != null) SetIKNotDirty(w); }
        internal void KitResetAll() { ResetAll(); }
        internal void KitCopyToFK() { CopyToFK(); }
        internal void KitRefresh() { RefreshIKList(); }

        /// <summary>Aim IK draws Pole Position only without a pole target (Joan's rule).</summary>
        internal bool KitHasPoleTarget(IK ik)
        {
            IKWrapper w = W(ik);
            IKSolverAim aim = w != null ? w.solver as IKSolverAim : null;
            return aim != null && aim.poleTarget != null;
        }

        /// <summary>Joan's step for the Aim IK vectors (his IMGUI slider sets _inc; the kit page must too).</summary>
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

        // ---------------------------------------------------------------- solver fields

        internal bool KitGetBool(IK ik, IKField f)
        {
            IKWrapper w = W(ik);
            if (w == null) return false;
            return f == IKField.Enabled ? GetIKEnabled(w) : GetIKFixTransforms(w);
        }

        internal void KitSetBool(IK ik, IKField f, bool v)
        {
            IKWrapper w = W(ik);
            if (w == null) return;
            if (f == IKField.Enabled) SetIKEnabled(w, v);
            else SetIKFixTransforms(w, v);
        }

        internal float KitGetFloat(IK ik, IKField f)
        {
            IKWrapper w = W(ik);
            if (w == null) return 0f;
            switch (f)
            {
                case IKField.PositionWeight: return GetIKPositionWeight(w);
                case IKField.Tolerance:
                    if (w.type == IKType.FABRIK) return GetFABRIKTolerance(w);
                    if (w.type == IKType.CCDIK) return GetCCDIKTolerance(w);
                    return GetAimIKTolerance(w);
                case IKField.MaxIterations:
                    if (w.type == IKType.FABRIK) return GetFABRIKMaxIterations(w);
                    if (w.type == IKType.CCDIK) return GetCCDIKMaxIterations(w);
                    return GetAimIKMaxIterations(w);
                case IKField.RootPin: return GetFABRIKRootRootPin(w);
                case IKField.Iterations: return GetFABRIKRootIterations(w);
                case IKField.ClampWeight: return GetAimIKClampWeight(w);
                case IKField.ClampSmoothing: return GetAimIKClampSmoothing(w);
                case IKField.PoleWeight: return GetAimIKPoleWeight(w);
                case IKField.LimbRotationWeight: return GetLimbIKRotationWeight(w);
                case IKField.LimbBendModifierWeight: return GetLimbIKBendModifierWeight(w);
            }
            return 0f;
        }

        internal void KitSetFloat(IK ik, IKField f, float v)
        {
            IKWrapper w = W(ik);
            if (w == null) return;
            switch (f)
            {
                case IKField.PositionWeight: SetIKPositionWeight(w, v); break;
                case IKField.Tolerance:
                    if (w.type == IKType.FABRIK) SetFABRIKTolerance(w, v);
                    else if (w.type == IKType.CCDIK) SetCCDIKTolerance(w, v);
                    else SetAimIKTolerance(w, v);
                    break;
                case IKField.MaxIterations:
                    int it = Mathf.RoundToInt(v);
                    if (w.type == IKType.FABRIK) SetFABRIKMaxIterations(w, it);
                    else if (w.type == IKType.CCDIK) SetCCDIKMaxIterations(w, it);
                    else SetAimIKMaxIterations(w, it);
                    break;
                case IKField.RootPin: SetFABRIKRootRootPin(w, v); break;
                case IKField.Iterations: SetFABRIKRootIterations(w, Mathf.RoundToInt(v)); break;
                case IKField.ClampWeight: SetAimIKClampWeight(w, v); break;
                case IKField.ClampSmoothing: SetAimIKClampSmoothing(w, Mathf.RoundToInt(v)); break;
                case IKField.PoleWeight: SetAimIKPoleWeight(w, v); break;
                case IKField.LimbRotationWeight: SetLimbIKRotationWeight(w, v); break;
                case IKField.LimbBendModifierWeight: SetLimbIKBendModifierWeight(w, v); break;
            }
        }

        internal Vector3 KitGetVector(IK ik, IKField f)
        {
            IKWrapper w = W(ik);
            if (w == null) return Vector3.zero;
            switch (f)
            {
                case IKField.Axis: return GetAimIKAxis(w);
                case IKField.PoleAxis: return GetAimIKPoleAxis(w);
                case IKField.PolePosition: return GetAimIKPolePosition(w);
            }
            return Vector3.zero;
        }

        internal void KitSetVector(IK ik, IKField f, Vector3 v)
        {
            IKWrapper w = W(ik);
            if (w == null) return;
            switch (f)
            {
                case IKField.Axis: SetAimIKAxis(w, v); break;
                case IKField.PoleAxis: SetAimIKPoleAxis(w, v); break;
                case IKField.PolePosition: SetAimIKPolePosition(w, v); break;
            }
        }

        internal bool KitIsFieldDirty(IK ik, IKField f)
        {
            IKWrapper w = W(ik);
            IKData d;
            if (w == null || _dirtyIks.TryGetValue(w, out d) == false || d == null)
                return false;
            switch (f)
            {
                case IKField.Enabled: return d.originalEnabled.hasValue;
                case IKField.FixTransforms: return d.originalFixTransforms.hasValue;
                case IKField.PositionWeight: return d.originalWeight.hasValue;
            }
            FABRIKData fab = d as FABRIKData;
            CCDIKData ccd = d as CCDIKData;
            AimIKData aim = d as AimIKData;
            FABRIKRootData root = d as FABRIKRootData;
            LimbIKData limb = d as LimbIKData;
            switch (f)
            {
                case IKField.Tolerance:
                    return (fab != null && fab.originalTolerance.hasValue) || (ccd != null && ccd.originalTolerance.hasValue) || (aim != null && aim.originalTolerance.hasValue);
                case IKField.MaxIterations:
                    return (fab != null && fab.originalMaxIterations.hasValue) || (ccd != null && ccd.originalMaxIterations.hasValue) || (aim != null && aim.originalMaxIterations.hasValue);
                case IKField.RootPin: return root != null && root.originalRootPin.hasValue;
                case IKField.Iterations: return root != null && root.originalIterations.hasValue;
                case IKField.Axis: return aim != null && aim.originalAxis.hasValue;
                case IKField.ClampWeight: return aim != null && aim.originalClampWeight.hasValue;
                case IKField.ClampSmoothing: return aim != null && aim.originalClampSmoothing.hasValue;
                case IKField.PoleAxis: return aim != null && aim.originalPoleAxis.hasValue;
                case IKField.PolePosition: return aim != null && aim.originalPolePosition.hasValue;
                case IKField.PoleWeight: return aim != null && aim.originalPoleWeight.hasValue;
                case IKField.LimbRotationWeight: return limb != null && limb.originalRotationWeight.hasValue;
                case IKField.LimbBendModifierWeight: return limb != null && limb.originalBendModifierWeight.hasValue;
            }
            return false;
        }

        /// <summary>The same assignments as Joan's inline Reset buttons / onReset lambdas for that field.</summary>
        internal void KitResetField(IK ik, IKField f)
        {
            IKWrapper w = W(ik);
            IKData d;
            if (w == null || _dirtyIks.TryGetValue(w, out d) == false || d == null)
                return;
            switch (f)
            {
                case IKField.Enabled:
                    if (d.originalEnabled.hasValue)
                    {
                        w.ik.enabled = d.originalEnabled;
                        d.originalEnabled.Reset();
                        if (w.ik.enabled == false)
                            w.solver.FixTransforms();
                    }
                    return;
                case IKField.FixTransforms:
                    if (d.originalFixTransforms.hasValue)
                    {
                        w.ik.fixTransforms = d.originalFixTransforms;
                        d.originalFixTransforms.Reset();
                    }
                    return;
                case IKField.PositionWeight:
                    if (d.originalWeight.hasValue)
                    {
                        w.solver.SetIKPositionWeight(d.originalWeight);
                        d.originalWeight.Reset();
                    }
                    return;
            }
            FABRIKData fab = d as FABRIKData;
            CCDIKData ccd = d as CCDIKData;
            AimIKData aim = d as AimIKData;
            FABRIKRootData root = d as FABRIKRootData;
            LimbIKData limb = d as LimbIKData;
            switch (f)
            {
                case IKField.Tolerance:
                    if (fab != null && fab.originalTolerance.hasValue) { ((IKSolverFABRIK)w.solver).tolerance = fab.originalTolerance; fab.originalTolerance.Reset(); }
                    if (ccd != null && ccd.originalTolerance.hasValue) { ((IKSolverCCD)w.solver).tolerance = ccd.originalTolerance; ccd.originalTolerance.Reset(); }
                    if (aim != null && aim.originalTolerance.hasValue) { ((IKSolverAim)w.solver).tolerance = aim.originalTolerance; aim.originalTolerance.Reset(); }
                    break;
                case IKField.MaxIterations:
                    if (fab != null && fab.originalMaxIterations.hasValue) { ((IKSolverFABRIK)w.solver).maxIterations = fab.originalMaxIterations; fab.originalMaxIterations.Reset(); }
                    if (ccd != null && ccd.originalMaxIterations.hasValue) { ((IKSolverCCD)w.solver).maxIterations = ccd.originalMaxIterations; ccd.originalMaxIterations.Reset(); }
                    if (aim != null && aim.originalMaxIterations.hasValue) { ((IKSolverAim)w.solver).maxIterations = aim.originalMaxIterations; aim.originalMaxIterations.Reset(); }
                    break;
                case IKField.RootPin:
                    if (root != null && root.originalRootPin.hasValue) { ((IKSolverFABRIKRoot)w.solver).rootPin = root.originalRootPin; root.originalRootPin.Reset(); }
                    break;
                case IKField.Iterations:
                    if (root != null && root.originalIterations.hasValue) { ((IKSolverFABRIKRoot)w.solver).iterations = root.originalIterations; root.originalIterations.Reset(); }
                    break;
                case IKField.Axis:
                    if (aim != null && aim.originalAxis.hasValue) { ((IKSolverAim)w.solver).axis = aim.originalAxis; aim.originalAxis.Reset(); }
                    break;
                case IKField.ClampWeight:
                    if (aim != null && aim.originalClampWeight.hasValue) { ((IKSolverAim)w.solver).clampWeight = aim.originalClampWeight; aim.originalClampWeight.Reset(); }
                    break;
                case IKField.ClampSmoothing:
                    if (aim != null && aim.originalClampSmoothing.hasValue) { ((IKSolverAim)w.solver).clampSmoothing = aim.originalClampSmoothing; aim.originalClampSmoothing.Reset(); }
                    break;
                case IKField.PoleAxis:
                    if (aim != null && aim.originalPoleAxis.hasValue) { ((IKSolverAim)w.solver).poleAxis = aim.originalPoleAxis; aim.originalPoleAxis.Reset(); }
                    break;
                case IKField.PolePosition:
                    if (aim != null && aim.originalPolePosition.hasValue) { ((IKSolverAim)w.solver).polePosition = aim.originalPolePosition; aim.originalPolePosition.Reset(); }
                    break;
                case IKField.PoleWeight:
                    if (aim != null && aim.originalPoleWeight.hasValue) { ((IKSolverAim)w.solver).poleWeight = aim.originalPoleWeight; aim.originalPoleWeight.Reset(); }
                    break;
                case IKField.LimbRotationWeight:
                    if (limb != null && limb.originalRotationWeight.hasValue) { ((IKSolverLimb)w.solver).SetIKRotationWeight(limb.originalRotationWeight); limb.originalRotationWeight.Reset(); }
                    break;
                case IKField.LimbBendModifierWeight:
                    if (limb != null && limb.originalBendModifierWeight.hasValue) { ((IKSolverLimb)w.solver).bendModifierWeight = limb.originalBendModifierWeight; limb.originalBendModifierWeight.Reset(); }
                    break;
            }
        }

        // ---------------------------------------------------------------- full body biped (characters)

        private IKEffector Effector(IKSolverFullBodyBiped s, FbbEffector e)
        {
            switch (e)
            {
                case FbbEffector.Body: return s.bodyEffector;
                case FbbEffector.RightShoulder: return s.rightShoulderEffector;
                case FbbEffector.LeftShoulder: return s.leftShoulderEffector;
                case FbbEffector.RightHand: return s.rightHandEffector;
                case FbbEffector.LeftHand: return s.leftHandEffector;
                case FbbEffector.RightThigh: return s.rightThighEffector;
                case FbbEffector.LeftThigh: return s.leftThighEffector;
                case FbbEffector.RightFoot: return s.rightFootEffector;
                default: return s.leftFootEffector;
            }
        }

        private static FullBodyBipedIKData.EffectorData EffectorData(FullBodyBipedIKData d, FbbEffector e)
        {
            switch (e)
            {
                case FbbEffector.Body: return d.body;
                case FbbEffector.RightShoulder: return d.rightShoulder;
                case FbbEffector.LeftShoulder: return d.leftShoulder;
                case FbbEffector.RightHand: return d.rightHand;
                case FbbEffector.LeftHand: return d.leftHand;
                case FbbEffector.RightThigh: return d.rightThigh;
                case FbbEffector.LeftThigh: return d.leftThigh;
                case FbbEffector.RightFoot: return d.rightFoot;
                default: return d.leftFoot;
            }
        }

        private static IKConstraintBend Bend(IKSolverFullBodyBiped s, FbbBend b)
        {
            switch (b)
            {
                case FbbBend.RightArm: return s.rightArmChain.bendConstraint;
                case FbbBend.LeftArm: return s.leftArmChain.bendConstraint;
                case FbbBend.RightLeg: return s.rightLegChain.bendConstraint;
                default: return s.leftLegChain.bendConstraint;
            }
        }

        private static FullBodyBipedIKData.ConstraintBendData BendData(FullBodyBipedIKData d, FbbBend b)
        {
            switch (b)
            {
                case FbbBend.RightArm: return d.rightArm;
                case FbbBend.LeftArm: return d.leftArm;
                case FbbBend.RightLeg: return d.rightLeg;
                default: return d.leftLeg;
            }
        }

        private IKSolverFullBodyBiped Fbb(IK ik)
        {
            IKWrapper w = W(ik);
            return w != null ? w.solver as IKSolverFullBodyBiped : null;
        }

        private FullBodyBipedIKData FbbData(IK ik, bool create)
        {
            IKWrapper w = W(ik);
            if (w == null)
                return null;
            IKData d;
            if (_dirtyIks.TryGetValue(w, out d))
                return d as FullBodyBipedIKData;
            // Joan's setters do nothing without this data, so on a character whose IK wasn't changed yet his sliders
            // silently didn't apply; the kit creates it first
            return create ? SetIKDirty(w) as FullBodyBipedIKData : null;
        }

        /// <summary>
        /// HSPE's value when it holds one: Studio sets the effector weights back every frame and Joan's LateUpdate
        /// re-applies his, so reading the live solver from a UI Update showed Studio's 1.0 (the
        /// slider snapped back while the character still moved). Joan's IMGUI drew after his LateUpdate, so it didn't.
        /// </summary>
        internal float KitGetEffector(IK ik, FbbEffector e, bool rotation)
        {
            IKSolverFullBodyBiped s = Fbb(ik);
            if (s == null) return 0f;
            FullBodyBipedIKData d = FbbData(ik, false);
            if (d != null)
            {
                FullBodyBipedIKData.EffectorData ed = EffectorData(d, e);
                if (rotation && ed.originalRotationWeight.hasValue) return ed.currentRotationWeight;
                if (!rotation && ed.originalPositionWeight.hasValue) return ed.currentPositionWeight;
            }
            IKEffector eff = Effector(s, e);
            return rotation ? GetEffectorRotationWeight(eff) : GetEffectorPositionWeight(eff);
        }

        internal void KitSetEffector(IK ik, FbbEffector e, bool rotation, float v)
        {
            IKSolverFullBodyBiped s = Fbb(ik);
            FullBodyBipedIKData d = FbbData(ik, true);
            if (s == null || d == null) return;
            IKEffector eff = Effector(s, e);
            if (rotation)
                SetEffectorRotationWeight(v, eff, EffectorData(d, e));
            else
                SetEffectorPositionWeight(v, eff, EffectorData(d, e));
        }

        internal bool KitEffectorDirty(IK ik, FbbEffector e, bool rotation)
        {
            FullBodyBipedIKData d = FbbData(ik, false);
            if (d == null) return false;
            FullBodyBipedIKData.EffectorData ed = EffectorData(d, e);
            return rotation ? ed.originalRotationWeight.hasValue : ed.originalPositionWeight.hasValue;
        }

        internal void KitResetEffector(IK ik, FbbEffector e, bool rotation)
        {
            IKSolverFullBodyBiped s = Fbb(ik);
            FullBodyBipedIKData d = FbbData(ik, false);
            if (s == null || d == null) return;
            IKEffector eff = Effector(s, e);
            FullBodyBipedIKData.EffectorData ed = EffectorData(d, e);
            if (rotation && ed.originalRotationWeight.hasValue)
            {
                eff.rotationWeight = ed.originalRotationWeight;
                ed.originalRotationWeight.Reset();
            }
            else if (!rotation && ed.originalPositionWeight.hasValue)
            {
                eff.positionWeight = ed.originalPositionWeight;
                ed.originalPositionWeight.Reset();
            }
        }

        internal float KitGetBend(IK ik, FbbBend b)
        {
            IKSolverFullBodyBiped s = Fbb(ik);
            if (s == null) return 0f;
            FullBodyBipedIKData d = FbbData(ik, false);
            if (d != null && BendData(d, b).originalWeight.hasValue)
                return BendData(d, b).currentWeight; // same reason as KitGetEffector
            return GetConstraintBendWeight(Bend(s, b));
        }

        internal void KitSetBend(IK ik, FbbBend b, float v)
        {
            IKSolverFullBodyBiped s = Fbb(ik);
            FullBodyBipedIKData d = FbbData(ik, true);
            if (s != null && d != null)
                SetConstraintBendWeight(v, Bend(s, b), BendData(d, b));
        }

        internal bool KitBendDirty(IK ik, FbbBend b)
        {
            FullBodyBipedIKData d = FbbData(ik, false);
            return d != null && BendData(d, b).originalWeight.hasValue;
        }

        internal void KitResetBend(IK ik, FbbBend b)
        {
            IKSolverFullBodyBiped s = Fbb(ik);
            FullBodyBipedIKData d = FbbData(ik, false);
            if (s == null || d == null) return;
            FullBodyBipedIKData.ConstraintBendData bd = BendData(d, b);
            if (bd.originalWeight.hasValue)
            {
                Bend(s, b).weight = bd.originalWeight;
                bd.originalWeight.Reset();
            }
        }
    }
}
