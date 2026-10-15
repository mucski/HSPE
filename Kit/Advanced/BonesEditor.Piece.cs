using UnityEngine;

namespace HSPE.AMModules
{
    public partial class BonesEditor
    {
        // Entry points for HSPE.PieceApi: write a piece's local transform into the normal per-bone data
        // (applied in LateUpdate, saved with the scene), or drop it back to the prefab's original values.
        internal void PieceSet(Transform piece, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            SetBonePosition(piece, localPosition);
            SetBoneRotation(piece, localRotation);
            SetBoneScale(piece, localScale);
        }

        internal void PieceReset(Transform piece)
        {
            ResetBonePos(piece);
            ResetBoneRot(piece);
            ResetBoneScale(piece);
        }

        internal void PieceEdited(System.Collections.Generic.List<Transform> into)
        {
            foreach (GameObject go in _dirtyBones.Keys)
                if (go != null) into.Add(go.transform);
        }

        // Every edited transform of this controller back to its original values; returns how many were edited.
        internal int PieceResetAll()
        {
            var edited = new System.Collections.Generic.List<GameObject>(_dirtyBones.Keys);
            foreach (GameObject go in edited)
                if (go != null) PieceReset(go.transform);
            return edited.Count;
        }
    }
}
