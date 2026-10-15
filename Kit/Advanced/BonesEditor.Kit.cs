using System.Collections.Generic;
using Studio;
using ToolBox.Extensions;
using UnityEngine;

namespace HSPE.AMModules
{
    // addition (HSPE fork): the kit Bones page's access to Joan's BonesEditor. Everything goes through his
    // own private methods and state (_boneTarget, _dirtyBones, _openedBones, clipboards, shortcuts, aliases), so both
    // windows edit the same data the same way. The two pieces of logic that lived inside his IMGUI code (Default,
    // one Rotate Around step) moved here and his GUILogic calls them too.
    public partial class BonesEditor
    {

        /// <summary>Changed entries, for the kit sidebar badge (a count, instead of serializing the module 1/s).</summary>
        internal int KitChangedCount() { return _dirtyBones.Count; }
        internal enum KitCoord { Position, Rotation, Scale, RotateAround }

        // ---------------------------------------------------------------- moved from GUILogic (shared)

        /// <summary>Joan's "Default" button: position 0, rotation identity, scale 1 (twin mirrored when symmetrical).</summary>
        internal void SetTargetToDefault(OCIChar.BoneInfo fkBoneInfo, OCIChar.BoneInfo fkTwinBoneInfo)
        {
            TransformData td = SetBoneDirty(_boneTarget.gameObject);
            td.SetPosition(Vector3.zero, _boneTarget);

            Quaternion symmetricalRotation = Quaternion.identity;
            if (fkBoneInfo != null)
            {
                if (!_lastShouldSaveValue)
                    _oldFKRotationValue = fkBoneInfo.guideObject.changeAmount.rot;
                fkBoneInfo.guideObject.changeAmount.rot = Vector3.zero;

                if (_symmetricalEdition && fkTwinBoneInfo != null && fkTwinBoneInfo.active)
                {
                    if (!_lastShouldSaveValue)
                        _oldFKTwinRotationValue = fkTwinBoneInfo.guideObject.changeAmount.rot;
                    fkTwinBoneInfo.guideObject.changeAmount.rot = symmetricalRotation.eulerAngles;
                }
            }

            td.SetRotation(Quaternion.identity, _boneTarget);
            td.SetScale(Vector3.one, _boneTarget);

            if (_symmetricalEdition && _twinBoneTarget)
                SetBoneDirty(_twinBoneTarget.gameObject).SetData(_twinBoneTarget, Vector3.zero, symmetricalRotation, Vector3.one);
        }

        /// <summary>
        /// Joan's Rotate Around, once per step: rotating = true turns the bone around the camera's target by angle
        /// degrees on axis (world); rotating = false after a rotating step pushes the FK undo command.
        /// </summary>
        internal void RotateAroundStep(bool rotating, Vector3 axis, float angle, OCIChar.BoneInfo fkBoneInfo)
        {
            if (!_boneTarget)
                return;
            if (rotating)
            {
                Quaternion currentRotation = GetBoneTargetRotation(fkBoneInfo);
                Vector3 currentPosition = GetBoneTargetPosition();

                _boneTarget.RotateAround(Studio.Studio.Instance.cameraCtrl.targetPos, axis, angle);

                if (fkBoneInfo != null)
                {
                    if (_oldFKRotationValue == null)
                        _oldFKRotationValue = fkBoneInfo.guideObject.changeAmount.rot;
                    fkBoneInfo.guideObject.changeAmount.rot = _boneTarget.localEulerAngles;
                }

                TransformData td = SetBoneDirty(_boneTarget.gameObject);

                td.rotation = _boneTarget.localRotation;
                if (!td.originalRotation.hasValue)
                    td.originalRotation = currentRotation;

                td.position = _boneTarget.localPosition;
                if (!td.originalPosition.hasValue)
                    td.originalPosition = currentPosition;
            }
            else if (fkBoneInfo != null && _lastShouldSaveValue)
            {
                GuideCommand.EqualsInfo[] infos = new GuideCommand.EqualsInfo[1];
                infos[0] = new GuideCommand.EqualsInfo()
                {
                    dicKey = fkBoneInfo.guideObject.dicKey,
                    oldValue = _oldFKRotationValue.Value,
                    newValue = fkBoneInfo.guideObject.changeAmount.rot
                };
                UndoRedoManager.Instance.Push(new GuideCommand.RotationEqualsCommand(infos));
                _oldFKRotationValue = null;
            }
        }

        // ---------------------------------------------------------------- tree

        internal Transform kitRoot { get { return _parent.transform; } }
        internal Transform kitTarget { get { return _boneTarget; } }
        internal Transform kitTwin { get { return _twinBoneTarget; } }

        internal bool KitIsHidden(GameObject go)
        {
            return _parent._childObjects.Contains(go); // other Studio objects parented under this one
        }

        internal bool KitIsOpen(GameObject go) { return _openedBones.Contains(go); }

        internal void KitSetOpen(GameObject go, bool open)
        {
            if (open)
                _openedBones.Add(go);
            else
                _openedBones.Remove(go);
        }

        internal void KitSelect(Transform t)
        {
            if (t == null || ReferenceEquals(t, _parent.transform))
                return;
            ChangeBoneTarget(t);
        }

        /// <summary>Opens every parent so the bone is visible in the tree (Joan's OpenParents).</summary>
        internal void KitReveal(GameObject go)
        {
            if (go != null)
                OpenParents(go);
        }

        internal void KitOpenChanged()
        {
            foreach (KeyValuePair<GameObject, TransformData> pair in _dirtyBones)
                if (pair.Key != null)
                    OpenParents(pair.Key);
        }

        internal bool KitIsDirty(GameObject go) { return go != null && _dirtyBones.ContainsKey(go); }

        internal List<Transform> KitDirtyBones()
        {
            List<Transform> list = new List<Transform>();
            foreach (KeyValuePair<GameObject, TransformData> pair in _dirtyBones)
                if (pair.Key != null)
                    list.Add(pair.Key.transform);
            return list;
        }

        /// <summary>Which parts of the bone HSPE holds: x = position, y = rotation, z = scale.</summary>
        internal void KitDirtyParts(GameObject go, out bool position, out bool rotation, out bool scale)
        {
            TransformData data;
            if (go != null && _dirtyBones.TryGetValue(go, out data))
            {
                position = data.position.hasValue;
                rotation = data.rotation.hasValue;
                scale = data.scale.hasValue;
            }
            else
                position = rotation = scale = false;
        }

        internal bool KitIsCollider(Transform t) { return _parent._collidersEditor._colliders.ContainsKey(t); }

        // ---------------------------------------------------------------- aliases, gizmos, symmetry

        internal static string KitAlias(string boneName)
        {
            string alias;
            return _boneAliases.TryGetValue(boneName, out alias) ? alias : null;
        }

        /// <summary>Bumped on every alias change, so kit lists know to refresh their names.</summary>
        internal static int kitAliasVersion;

        /// <summary>Joan's alias Save button: empty removes it.</summary>
        internal static void KitSetAlias(string boneName, string alias)
        {
            kitAliasVersion++;
            alias = (alias ?? "").Trim();
            if (alias.Length == 0)
                _boneAliases.Remove(boneName);
            else
                _boneAliases[boneName] = alias;
        }

        internal bool kitDrawGizmos
        {
            get { return _drawGizmos; }
            set
            {
                _drawGizmos = value;
                UpdateDebugLinesState(this);
            }
        }

        internal bool kitSymmetrical
        {
            get { return _symmetricalEdition; }
            set { _symmetricalEdition = value; }
        }

        // ---------------------------------------------------------------- favourites (Joan's shortcuts)

        /// <summary>Joan's fixed shortcuts (hands, feet, face) - shown first, can't be removed.</summary>
        internal Dictionary<Transform, string> kitBuiltInFavourites { get { return _boneEditionShortcuts; } }

        /// <summary>Joan's custom shortcuts for this kind of target (path -> name), saved in configNEO.xml.</summary>
        internal Dictionary<string, string> kitFavourites
        {
            get { return _target.type == GenericOCITarget.Type.Character ? (_target.isFemale ? _femaleShortcuts : _maleShortcuts) : _itemShortcuts; }
        }

        private readonly Dictionary<Transform, string> _kitPaths = new Dictionary<Transform, string>();

        /// <summary>Cached: the Bones page asks every frame (favourite star) and GetPathFrom builds a new string each time.</summary>
        internal string KitPath(Transform t)
        {
            string path;
            if (_kitPaths.TryGetValue(t, out path) == false)
            {
                if (_kitPaths.Count > 4096)
                    _kitPaths.Clear();
                path = t.GetPathFrom(_parent.transform);
                _kitPaths.Add(t, path);
            }
            return path;
        }

        internal bool KitIsFavourite(Transform t)
        {
            if (t == null)
                return false;
            return _boneEditionShortcuts.ContainsKey(t) || kitFavourites.ContainsKey(KitPath(t));
        }

        internal bool KitIsBuiltInFavourite(Transform t) { return t != null && _boneEditionShortcuts.ContainsKey(t); }

        /// <summary>Joan's "+ Add Shortcut" / remove.</summary>
        internal void KitToggleFavourite(Transform t)
        {
            if (t == null || _boneEditionShortcuts.ContainsKey(t))
                return;
            string path = KitPath(t);
            if (path.Length == 0)
                return;
            if (kitFavourites.ContainsKey(path))
                kitFavourites.Remove(path);
            else
                kitFavourites.Add(path, t.name);
        }

        // ---------------------------------------------------------------- face window (writes like the drawer does)

        /// <summary>The bone's value before HSPE changed it (its rest pose for face offsets).</summary>
        internal void KitFaceRest(Transform t, out Vector3 position, out Quaternion rotation)
        {
            TransformData td;
            if (_dirtyBones.TryGetValue(t.gameObject, out td))
            {
                position = td.originalPosition.hasValue ? td.originalPosition.value : t.localPosition;
                rotation = td.originalRotation.hasValue ? td.originalRotation.value : t.localRotation;
            }
            else
            {
                position = t.localPosition;
                rotation = t.localRotation;
            }
        }

        internal void KitFaceSetPosition(Transform t, Vector3 position) { SetBoneDirty(t.gameObject).SetPosition(position, t); }
        internal void KitFaceSetRotation(Transform t, Quaternion rotation) { SetBoneDirty(t.gameObject).SetRotation(rotation, t); }

        /// <summary>Hands one coordinate of the bone back (Joan's per-coordinate reset, no twin, no children).</summary>
        internal void KitFaceRelease(Transform t, bool rotation)
        {
            if (rotation)
                ResetBoneRot(t, null, false);
            else
                ResetBonePos(t, null, false);
        }

        /// <summary>Whether HSPE currently holds this coordinate (to spot edits made elsewhere).</summary>
        internal bool KitFaceHolds(Transform t, bool rotation, out Vector3 position, out Quaternion rot)
        {
            TransformData td;
            position = Vector3.zero;
            rot = Quaternion.identity;
            if (_dirtyBones.TryGetValue(t.gameObject, out td) == false)
                return false;
            if (rotation)
            {
                rot = td.rotation.value;
                return td.rotation.hasValue;
            }
            position = td.position.value;
            return td.position.hasValue;
        }

        // ---------------------------------------------------------------- values

        private OCIChar.BoneInfo KitFk()
        {
            OCIChar.BoneInfo info = null;
            if (_boneTarget && _target.fkEnabled)
                _target.fkObjects.TryGetValue(_boneTarget.gameObject, out info);
            return info;
        }

        private OCIChar.BoneInfo KitFkTwin()
        {
            OCIChar.BoneInfo info = null;
            if (_symmetricalEdition && _twinBoneTarget && _target.fkEnabled)
                _target.fkObjects.TryGetValue(_twinBoneTarget.gameObject, out info);
            return info;
        }

        /// <summary>True when Studio's FK drives the rotation (Joan hides Reset / Lock rotation then).</summary>
        internal bool kitRotationIsFK
        {
            get
            {
                OCIChar.BoneInfo fk = KitFk();
                return fk != null && fk.active;
            }
        }

        internal Vector3 KitGetPosition() { return GetBoneTargetPosition(); }
        internal void KitSetPosition(Vector3 v) { SetBoneTargetPosition(v); }
        internal Vector3 KitGetScale() { return GetBoneTargetScale(); }
        internal void KitSetScale(Vector3 v) { SetBoneTargetScale(v); }
        internal Quaternion KitGetRotation() { return GetBoneTargetRotation(KitFk()); }

        /// <summary>
        /// Like Joan's rotation editor: while a change is going on (final = false) the FK node follows without undo;
        /// the final call pushes Studio's undo for FK bones.
        /// </summary>
        internal void KitSetRotation(Quaternion q, bool final)
        {
            SetBoneTargetRotation(q);
            SetBoneTargetRotationFKNode(q, final, KitFk(), KitFkTwin());
        }

        /// <summary>End of a rotation drag: push the FK undo for the current value (Joan's "stopped changing" frame).</summary>
        internal void KitEndRotation()
        {
            OCIChar.BoneInfo fk = KitFk();
            if (fk != null && _oldFKRotationValue != null)
                SetBoneTargetRotationFKNode(KitGetRotation(), true, fk, KitFkTwin());
        }

        /// <summary>One Rotate Around step; axis 0/1/2 = the bone's right / up / forward, like Joan's buttons.</summary>
        internal void KitRotateAround(int axis, float angle)
        {
            if (!_boneTarget)
                return;
            Vector3 a = axis == 0 ? _boneTarget.right : axis == 1 ? _boneTarget.up : _boneTarget.forward;
            RotateAroundStep(true, a, angle, KitFk());
            _lastShouldSaveValue = true;
        }

        internal void KitEndRotateAround()
        {
            RotateAroundStep(false, Vector3.zero, 0f, KitFk());
            _lastShouldSaveValue = false;
        }

        internal void KitDefault() { if (_boneTarget) SetTargetToDefault(KitFk(), KitFkTwin()); }

        internal void KitReset(KitCoord coord, bool withChildren)
        {
            if (!_boneTarget)
                return;
            switch (coord)
            {
                case KitCoord.Position: ResetBonePos(_boneTarget, _twinBoneTarget, withChildren); break;
                case KitCoord.Rotation: ResetBoneRot(_boneTarget, _twinBoneTarget, withChildren); break;
                case KitCoord.Scale: ResetBoneScale(_boneTarget, _twinBoneTarget, withChildren); break;
            }
        }

        /// <summary>Joan's "Dirty Pos. / Rot. / Scale": keep the current value as an HSPE value (locks it).</summary>
        internal void KitLock(KitCoord coord, bool withChildren)
        {
            if (!_boneTarget)
                return;
            switch (coord)
            {
                case KitCoord.Position: MakeDirtyBonePos(_boneTarget, _twinBoneTarget, withChildren); break;
                case KitCoord.Rotation: MakeDirtyBoneRot(_boneTarget, _twinBoneTarget, withChildren); break;
                case KitCoord.Scale: MakeDirtyBoneScale(_boneTarget, _twinBoneTarget, withChildren); break;
            }
        }

        // Joan's static clipboards (shared with his window)
        internal bool KitCanPaste(KitCoord coord)
        {
            switch (coord)
            {
                case KitCoord.Position: return _clipboardPosition != null;
                case KitCoord.Rotation: return _clipboardRotation != null;
                case KitCoord.Scale: return _clipboardScale != null;
                default: return false;
            }
        }

        internal void KitCopy(KitCoord coord)
        {
            if (!_boneTarget)
                return;
            switch (coord)
            {
                case KitCoord.Position: _clipboardPosition = GetBoneTargetPosition(); break;
                case KitCoord.Rotation: _clipboardRotation = KitGetRotation(); break;
                case KitCoord.Scale: _clipboardScale = GetBoneTargetScale(); break;
            }
        }

        internal void KitPaste(KitCoord coord)
        {
            if (!_boneTarget)
                return;
            switch (coord)
            {
                case KitCoord.Position:
                    if (_clipboardPosition != null)
                        SetBoneTargetPosition(_clipboardPosition.Value);
                    break;
                case KitCoord.Rotation:
                    if (_clipboardRotation != null)
                    {
                        SetBoneTargetRotation(_clipboardRotation.Value);
                        SetBoneTargetRotationFKNode(_clipboardRotation.Value, true, KitFk(), KitFkTwin());
                    }
                    break;
                case KitCoord.Scale:
                    if (_clipboardScale != null)
                        SetBoneTargetScale(_clipboardScale.Value);
                    break;
            }
        }
    }
}
