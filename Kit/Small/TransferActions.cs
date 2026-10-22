using System.Collections.Generic;
using System.Text;
using HSPE.Kit.Transfer;
using Studio;
using UnityEngine;

namespace HSPE.Kit.Small
{
    using HSUIKit;

    /// <summary>
    /// Copy / paste / preset actions of the small window's header. Copy reads the
    /// first selected object; paste and preset apply go to every selected one, each undoable from the result toast.
    /// </summary>
    internal static class TransferActions
    {
        public static List<PoseController> Selected()
        {
            List<PoseController> list = new List<PoseController>();
            if (Studio.Studio.Instance == null)
                return list;
            TreeNodeObject[] nodes = Studio.Studio.Instance.treeNodeCtrl.selectNodes;
            if (nodes == null)
                return list;
            foreach (TreeNodeObject node in nodes)
            {
                ObjectCtrlInfo info;
                if (node == null || Studio.Studio.Instance.dicInfo.TryGetValue(node, out info) == false)
                    continue;
                PoseController c = info.guideObject.transformTarget.GetComponent<PoseController>();
                if (c != null)
                    list.Add(c);
            }
            return list;
        }

        public static string NameOf(PoseController c)
        {
            if (c == null)
                return "";
            OCIChar chara = c.target.oci as OCIChar;
            if (chara != null)
                return chara.charInfo.customInfo.name;
            return c.target.oci != null && c.target.oci.treeNodeObject != null ? c.target.oci.treeNodeObject.textName : c.name;
        }

        public static string ModulesText(ModuleMask m)
        {
            StringBuilder sb = new StringBuilder();
            foreach (ModuleMask single in ModuleNodes.Singles)
            {
                if ((m & single) == 0)
                    continue;
                if (sb.Length != 0)
                    sb.Append(", ");
                sb.Append(Label(single));
            }
            return sb.Length == 0 ? "nothing" : sb.ToString();
        }

        public static string Label(ModuleMask single)
        {
            switch (single)
            {
                case ModuleMask.DynamicBones: return "Dynamic bones";
                case ModuleMask.BlendShapes: return "Blend shapes";
                case ModuleMask.Pose: return "Pose (Studio IK/FK)";
                default: return single.ToString();
            }
        }

        public static void Copy()
        {
            List<PoseController> sel = Selected();
            if (sel.Count == 0)
            {
                KitToast.Show("Nothing to copy", "Select a character or item first.", ToastKind.Warning, 3f);
                return;
            }
            ModuleMask modules = TransferClipboard.LastModules;
            PresetDocument d = PoseTransfer.Capture(sel[0], modules);
            TransferClipboard.Set(d);
            KitToast.Show("Copied " + NameOf(sel[0]), ModulesText(d.Modules), ToastKind.Success, 3f);
        }

        public static void Paste()
        {
            Apply(TransferClipboard.Get(), TransferClipboard.LastModules, "Pasted", "Nothing to paste", "Copy a character first (or copy HSPE preset text).");
        }

        public static void ApplyPreset(string name)
        {
            ApplyPreset(name, TransferClipboard.LastModules);
        }

        /// <summary>Applies only <paramref name="modules"/> (the Advanced presets page asks which).</summary>
        public static void ApplyPreset(string name, ModuleMask modules)
        {
            Apply(PresetStore.Load(name), modules, "Applied \"" + name + "\"", "Preset not found", name);
        }

        public static void SavePreset(string name)
        {
            SavePreset(name, TransferClipboard.LastModules);
        }

        public static void SavePreset(string name, ModuleMask modules)
        {
            List<PoseController> sel = Selected();
            if (sel.Count == 0)
            {
                KitToast.Show("Nothing to save", "Select a character or item first.", ToastKind.Warning, 3f);
                return;
            }
            name = PresetStore.CleanName(name);
            if (name.Length == 0)
                return;
            PresetStore.Save(name, PoseTransfer.Capture(sel[0], modules), Camera.main);
            KitToast.Show("Saved preset \"" + name + "\"", ModulesText(modules & PoseTransfer.Available(sel[0])), ToastKind.Success, 3f);
        }

        private static void Apply(PresetDocument d, ModuleMask modules, string verb, string emptyTitle, string emptyMessage)
        {
            List<PoseController> targets = Selected();
            if (targets.Count == 0)
            {
                KitToast.Show("Nothing selected", "Select the character(s) to paste onto.", ToastKind.Warning, 3f);
                return;
            }
            if (d == null)
            {
                KitToast.Show(emptyTitle, emptyMessage, ToastKind.Warning, 3f);
                return;
            }
            Dictionary<PoseController, string> undo = new Dictionary<PoseController, string>();
            int found = 0, total = 0, skipped = 0;
            foreach (PoseController c in targets)
            {
                undo[c] = PoseTransfer.FullState(c);
                ApplyResult r = PoseTransfer.Apply(c, d, modules & d.Modules);
                if (r.Error != null)
                {
                    // Apply already restored c; put back the ones done before it too
                    Restore(undo, c);
                    KitToast.Show("Paste failed on " + NameOf(c), r.Error + " - nothing was changed.", ToastKind.Error, 6f);
                    return;
                }
                found += r.Found;
                total += r.Total;
                skipped += r.Skipped.Count;
                foreach (string s in r.Skipped)
                    global::HSPE.Kit.Log.Write("transfer: skipped " + s);
            }
            // one Studio undo step (Ctrl+Z) for the whole paste, next to the toast's own Undo button
            global::HSPE.Kit.Undo.HspeUndo.PushExplicit(verb, targets, undo);
            string who = targets.Count == 1 ? NameOf(targets[0]) : targets.Count + " objects";
            string message = found + " of " + total + " entries" + (skipped > 0 ? ", " + skipped + " skipped (see log)" : "");
            KitToast.Show(verb + " to " + who, message, skipped > 0 ? ToastKind.Warning : ToastKind.Success, 8f, "Undo", delegate
            {
                Restore(undo, null);
                global::HSPE.Kit.Undo.HspeUndo.Refresh(undo.Keys);
                KitToast.Show("Undone", who + " restored.", ToastKind.Info, 2.5f);
            });
        }

        private static void Restore(Dictionary<PoseController, string> undo, PoseController skip)
        {
            foreach (KeyValuePair<PoseController, string> p in undo)
                if (p.Key != null && p.Key != skip)
                    PoseTransfer.Restore(p.Key, p.Value);
        }
    }
}
