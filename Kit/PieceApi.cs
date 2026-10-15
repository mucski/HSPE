using HSPE.AMModules;
using Studio;
using UnityEngine;

namespace HSPE
{
    // Public entry point for other plugins (HSPieceEdit) to edit pieces of Studio items through HSPE's own data.
    public static class PieceApi
    {
        private static PoseController Controller(ObjectCtrlInfo item)
        {
            if (item == null || item.guideObject == null || item.guideObject.transformTarget == null) return null;
            return item.guideObject.transformTarget.GetComponent<PoseController>();
        }

        public static bool IsAvailable(ObjectCtrlInfo item)
        {
            PoseController pc = Controller(item);
            return pc != null && pc._bonesEditor != null;
        }

        // Items start with their controller disabled (nothing applied); same as pressing HSPE's Enable.
        public static void EnsureEnabled(ObjectCtrlInfo item)
        {
            PoseController pc = Controller(item);
            if (pc != null && !pc.enabled) pc.enabled = true;
        }

        public static void SetPiece(ObjectCtrlInfo item, Transform piece, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            PoseController pc = Controller(item);
            if (pc == null || pc._bonesEditor == null || piece == null) return;
            pc._bonesEditor.PieceSet(piece, localPosition, localRotation, localScale);
        }

        public static void ResetPiece(ObjectCtrlInfo item, Transform piece)
        {
            PoseController pc = Controller(item);
            if (pc == null || pc._bonesEditor == null || piece == null) return;
            pc._bonesEditor.PieceReset(piece);
        }

        // Pieces of this item that HSPE currently holds an edit for.
        public static void EditedPieces(ObjectCtrlInfo item, System.Collections.Generic.List<Transform> into)
        {
            PoseController pc = Controller(item);
            if (pc != null && pc._bonesEditor != null) pc._bonesEditor.PieceEdited(into);
        }

        // Every piece edit HSPE holds for this item back to the prefab's values; returns how many pieces were edited.
        public static int ResetAll(ObjectCtrlInfo item)
        {
            PoseController pc = Controller(item);
            if (pc == null || pc._bonesEditor == null) return 0;
            return pc._bonesEditor.PieceResetAll();
        }

        // Selects the item and opens HSPE's advanced bones page on the piece.
        public static void GoToPiece(ObjectCtrlInfo item, Transform piece)
        {
            PoseController pc = Controller(item);
            if (pc == null || pc._bonesEditor == null || piece == null) return;
            global::Studio.Studio studio = Singleton<global::Studio.Studio>.Instance;
            if (studio != null && item.treeNodeObject != null) studio.treeNodeCtrl.SelectSingle(item.treeNodeObject);
            pc.EnableModule(pc._bonesEditor);
            if (!PoseController._drawAdvancedMode) pc.ToggleAdvancedMode();
            // Not GoToObject: it also scrolls the IMGUI list (GUI.skin), which throws outside OnGUI.
            pc._bonesEditor.KitSelect(piece);
            pc._bonesEditor.KitReveal(piece.gameObject);
        }
    }
}
