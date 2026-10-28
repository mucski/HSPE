using System.Collections.Generic;
using System.Linq;
using RootMotion.FinalIK;
using Studio;
using UnityEngine;

namespace HSPE.Kit.Small
{
    /// <summary>
    /// The small window's pose input, moved out of Joan's MainWindow.GUILogic so his canvas and the kit window
    /// drive the same code: IK effector / bend-goal selection, the X/Y/Z move and rotate drags (with his StartDrag /
    /// StopDrag undo), intensity and World/Local. The math is Joan's, unchanged.
    /// </summary>
    internal sealed class PoseInput
    {
        public readonly List<FullBodyBipedEffector> ikBoneTargets = new List<FullBodyBipedEffector>();
        public readonly List<FullBodyBipedChain> ikBendGoalTargets = new List<FullBodyBipedChain>();
        private readonly Vector3[] _lastIKBonesPositions = new Vector3[14];
        private readonly Quaternion[] _lastIKBonesRotations = new Quaternion[14];
        private readonly Vector3[] _lastIKBendGoalsPositions = new Vector3[4];
        private Quaternion _lastFKBonesRotation;
        private Vector2 _delta;

        public bool xMove, yMove, zMove, xRot, yRot, zRot;
        public float intensity = 1f;
        public bool positionWorld;
        public bool modeIK = true;

        public bool dragging { get { return xMove || yMove || zMove || xRot || yRot || zRot; } }

        // On this Mono (.NET 3.5) List<enum>.Contains, HashSet<enum> and LINQ box the enum on every compare / hash, so
        // anything asked every frame uses these plain loops and a bool table (no per-frame garbage).
        private static bool[] _nonRotatable;

        private static bool NonRotatable(FullBodyBipedEffector e)
        {
            if (_nonRotatable == null)
            {
                _nonRotatable = new bool[32];
                foreach (FullBodyBipedEffector n in CharaPoseController.nonRotatableEffectors) // once
                    _nonRotatable[(int)n] = true;
            }
            int i = (int)e;
            return i >= 0 && i < _nonRotatable.Length && _nonRotatable[i];
        }

        public bool IsEffectorSelected(FullBodyBipedEffector e)
        {
            for (int i = 0; i < ikBoneTargets.Count; i++)
                if (ikBoneTargets[i] == e)
                    return true;
            return false;
        }

        public bool IsBendGoalSelected(FullBodyBipedChain c)
        {
            for (int i = 0; i < ikBendGoalTargets.Count; i++)
                if (ikBendGoalTargets[i] == c)
                    return true;
            return false;
        }

        /// <summary>Joan's intensity slider: value 7 = x1, each step doubles / halves.</summary>
        public static float IntensityFromSlider(float value)
        {
            return Mathf.Pow(2, value - 7);
        }

        public static string IntensityText(float value)
        {
            return value >= 1f ? "x" + value.ToString("0.##") : "/" + (1f / value).ToString("0.##");
        }

        public static bool CtrlHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        /// <summary>Plain click replaces the selection, Ctrl-click toggles one entry (Joan's SetBoneTarget).</summary>
        public void SelectEffector(FullBodyBipedEffector bone, bool additive)
        {
            if (!additive)
            {
                ikBoneTargets.Clear();
                ikBendGoalTargets.Clear();
                ikBoneTargets.Add(bone);
            }
            else if (ikBoneTargets.Contains(bone))
                ikBoneTargets.Remove(bone);
            else
                ikBoneTargets.Add(bone);
        }

        public void SelectBendGoal(FullBodyBipedChain bendGoal, bool additive)
        {
            if (!additive)
            {
                ikBoneTargets.Clear();
                ikBendGoalTargets.Clear();
                ikBendGoalTargets.Add(bendGoal);
            }
            else if (ikBendGoalTargets.Contains(bendGoal))
                ikBendGoalTargets.Remove(bendGoal);
            else
                ikBendGoalTargets.Add(bendGoal);
        }

        // ---------------------------------------------------------------- FK multi-select
        // Studio marks FK guides single-select, so extra bones live here: the primary stays Studio's selected guide
        // (its gizmo, Joan's rules), the extras turn with it. Picking any other bone elsewhere drops the extras.
        public readonly List<GuideObject> fkExtras = new List<GuideObject>();
        private GuideObject _fkPrimary;
        private readonly List<Quaternion> _lastFKExtraRotations = new List<Quaternion>();

        /// <summary>Once per frame: forgets the extras when Studio's selection moved, drops destroyed ones.</summary>
        public void SyncFkSelection()
        {
            GuideObject sel = FkBones.SelectedGuide();
            if (sel != _fkPrimary)
            {
                _fkPrimary = sel;
                fkExtras.Clear();
            }
            for (int i = fkExtras.Count - 1; i >= 0; i--)
                if (fkExtras[i] == null)
                    fkExtras.RemoveAt(i);
        }

        public bool IsFkSelected(GuideObject g)
        {
            return g != null && (g == _fkPrimary || fkExtras.Contains(g));
        }

        /// <summary>Plain click selects one bone; Ctrl-click adds / removes it.</summary>
        public void ClickFk(GuideObject g, bool additive)
        {
            SyncFkSelection();
            if (!additive || _fkPrimary == null)
            {
                SelectFkPrimary(g);
                return;
            }
            if (g == _fkPrimary)
            {
                if (fkExtras.Count == 0)
                    return;
                GuideObject next = fkExtras[0];
                fkExtras.RemoveAt(0);
                List<GuideObject> keep = new List<GuideObject>(fkExtras);
                SelectFkPrimary(next);
                fkExtras.AddRange(keep);
                return;
            }
            if (fkExtras.Contains(g))
                fkExtras.Remove(g);
            else
                fkExtras.Add(g);
        }

        /// <summary>Selects a group (a whole finger...): the first one is Studio's selection.</summary>
        public void SelectFkGroup(List<GuideObject> group)
        {
            if (group.Count == 0)
                return;
            SelectFkPrimary(group[0]);
            for (int i = 1; i < group.Count; i++)
                if (group[i] != null && !fkExtras.Contains(group[i]))
                    fkExtras.Add(group[i]);
        }

        private void SelectFkPrimary(GuideObject g)
        {
            FkBones.SelectGuide(g);
            _fkPrimary = FkBones.SelectedGuide();
            fkExtras.Clear();
        }

        public void ReleaseDrags()
        {
            xMove = yMove = zMove = false;
            xRot = yRot = zRot = false;
        }

        /// <summary>Once per frame while the window is shown and a target is selected.</summary>
        public void Tick(PoseController poseTarget)
        {
            CharaPoseController charaPoseTarget = poseTarget as CharaPoseController;
            bool isCharacter = charaPoseTarget != null;
            if (dragging)
            {
                _delta += (new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * (Input.GetKey(KeyCode.LeftShift) ? 4f : 1f) / (Input.GetKey(KeyCode.LeftControl) ? 6f : 1f)) / 10f;

                if (poseTarget._currentDragType == PoseController.DragType.None)
                    poseTarget.StartDrag(xMove || yMove || zMove ? PoseController.DragType.Position : PoseController.DragType.Rotation);
                if (modeIK)
                {
                    if (isCharacter)
                    {
                        for (int i = 0; i < ikBoneTargets.Count; ++i)
                        {
                            bool changePosition = false;
                            bool changeRotation = false;
                            Vector3 newPosition = _lastIKBonesPositions[i];
                            Quaternion newRotation = _lastIKBonesRotations[i];
                            if (xMove)
                            {
                                newPosition.x += _delta.y * intensity;
                                changePosition = true;
                            }
                            if (yMove)
                            {
                                newPosition.y += _delta.y * intensity;
                                changePosition = true;
                            }
                            if (zMove)
                            {
                                newPosition.z += _delta.y * intensity;
                                changePosition = true;
                            }
                            if (xRot)
                            {
                                newRotation *= Quaternion.AngleAxis(_delta.x * 20f * intensity, Vector3.right);
                                changeRotation = true;
                            }
                            if (yRot)
                            {
                                newRotation *= Quaternion.AngleAxis(_delta.x * 20f * intensity, Vector3.up);
                                changeRotation = true;
                            }
                            if (zRot)
                            {
                                newRotation *= Quaternion.AngleAxis(_delta.x * 20f * intensity, Vector3.forward);
                                changeRotation = true;
                            }
                            FullBodyBipedEffector bone = ikBoneTargets[i];
                            if (changePosition && charaPoseTarget.IsPartEnabled(bone))
                                charaPoseTarget.SetBoneTargetPosition(bone, newPosition, positionWorld);
                            if (changeRotation && charaPoseTarget.IsPartEnabled(bone))
                                charaPoseTarget.SetBoneTargetRotation(ikBoneTargets[i], newRotation);
                        }
                        for (int i = 0; i < ikBendGoalTargets.Count; ++i)
                        {
                            Vector3 newPosition = _lastIKBendGoalsPositions[i];
                            if (xMove)
                                newPosition.x += _delta.y * intensity;
                            if (yMove)
                                newPosition.y += _delta.y * intensity;
                            if (zMove)
                                newPosition.z += _delta.y * intensity;
                            FullBodyBipedChain bendGoal = ikBendGoalTargets[i];
                            if (charaPoseTarget.IsPartEnabled(bendGoal))
                                charaPoseTarget.SetBendGoalPosition(bendGoal, newPosition, positionWorld);
                        }
                    }
                }
                else
                {
                    bool changeRotation = false;
                    Quaternion turn = Quaternion.identity; // the same local turn for the bone and every extra
                    if (xRot)
                    {
                        turn *= Quaternion.AngleAxis(_delta.x * 20f * intensity, Vector3.right);
                        changeRotation = true;
                    }
                    if (yRot)
                    {
                        turn *= Quaternion.AngleAxis(_delta.x * 20f * intensity, Vector3.up);
                        changeRotation = true;
                    }
                    if (zRot)
                    {
                        turn *= Quaternion.AngleAxis(_delta.x * 20f * intensity, Vector3.forward);
                        changeRotation = true;
                    }
                    if (changeRotation)
                    {
                        poseTarget.SeFKBoneTargetRotation(FkBones.SelectedGuide(), _lastFKBonesRotation * turn);
                        for (int i = 0; i < fkExtras.Count && i < _lastFKExtraRotations.Count; i++)
                            if (fkExtras[i] != null)
                                poseTarget.SeFKBoneTargetRotation(fkExtras[i], _lastFKExtraRotations[i] * turn);
                    }
                }
            }
            else
            {
                _delta = Vector2.zero;
                if (poseTarget._currentDragType != PoseController.DragType.None)
                    poseTarget.StopDrag();
                if (modeIK)
                {
                    if (isCharacter)
                    {
                        for (int i = 0; i < ikBoneTargets.Count; ++i)
                        {
                            _lastIKBonesPositions[i] = charaPoseTarget.GetBoneTargetPosition(ikBoneTargets[i], positionWorld);
                            _lastIKBonesRotations[i] = charaPoseTarget.GetBoneTargetRotation(ikBoneTargets[i]);
                        }
                        for (int i = 0; i < ikBendGoalTargets.Count; ++i)
                            _lastIKBendGoalsPositions[i] = charaPoseTarget.GetBendGoalPosition(ikBendGoalTargets[i], positionWorld);
                    }
                }
                else
                {
                    if (FkBones.SelectedGuide() != null)
                        _lastFKBonesRotation = poseTarget.GetFKBoneTargetRotation(FkBones.SelectedGuide());
                    _lastFKExtraRotations.Clear();
                    for (int i = 0; i < fkExtras.Count; i++)
                        _lastFKExtraRotations.Add(fkExtras[i] != null ? poseTarget.GetFKBoneTargetRotation(fkExtras[i]) : Quaternion.identity);
                }
            }
        }

        /// <summary>Joan's rule for the move buttons (IK tab only).</summary>
        public bool CanMove(PoseController poseTarget)
        {
            CharaPoseController chara = poseTarget as CharaPoseController;
            return chara != null && chara.target.ikEnabled;
        }

        /// <summary>Joan's rule for the rotate buttons.</summary>
        public bool CanRotate(PoseController poseTarget)
        {
            if (modeIK)
            {
                CharaPoseController chara = poseTarget as CharaPoseController;
                if (chara == null || chara.target.ikEnabled == false || ikBendGoalTargets.Count != 0)
                    return false;
                for (int i = 0; i < ikBoneTargets.Count; i++) // = Joan's Intersect(nonRotatableEffectors).Any() == false
                    if (NonRotatable(ikBoneTargets[i]))
                        return false;
                return true;
            }
            OCIChar.BoneInfo bone;
            return poseTarget.target.fkEnabled
                   && FkBones.SelectedGuide() != null
                   && poseTarget.target.fkObjects.TryGetValue(FkBones.SelectedGuide().transformTarget.gameObject, out bone)
                   && bone.active;
        }
    }
}
