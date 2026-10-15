using IllusionPlugin;
using UnityEngine;

namespace HSPE.Kit.Transfer
{
    /// <summary>
    /// The copy / paste buffer. Also written to the Windows clipboard as text, so a copy can be pasted in another
    /// Studio session or shared; Get prefers the Windows clipboard when it holds an HSPE preset.
    /// </summary>
    public static class TransferClipboard
    {
        private static PresetDocument _last;

        /// <summary>The module checkboxes' last state (all ticked by default).</summary>
        public static ModuleMask LastModules
        {
            get { return (ModuleMask)ModPrefs.GetInt("HSPE", "TransferModules", (int)ModuleMask.All, true); }
            set { ModPrefs.SetInt("HSPE", "TransferModules", (int)value); }
        }

        public static void Set(PresetDocument d)
        {
            _last = d;
            try
            {
                GUIUtility.systemCopyBuffer = d.ToXml();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("HSPE: could not write the Windows clipboard: " + e.Message);
            }
        }

        public static PresetDocument Get()
        {
            try
            {
                PresetDocument fromSystem = PresetDocument.Parse(GUIUtility.systemCopyBuffer);
                if (fromSystem != null)
                    return fromSystem;
            }
            catch (System.Exception)
            {
            }
            return _last;
        }
    }
}
