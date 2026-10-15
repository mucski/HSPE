using System;

namespace HSPE.Kit.Transfer
{
    /// <summary>The HSPE modules a copy / paste / preset carries. Values are stored in preset files - never renumber.</summary>
    [Flags]
    public enum ModuleMask
    {
        None = 0,
        Bones = 1,
        Colliders = 2,
        DynamicBones = 4,
        BlendShapes = 8,
        IK = 16,
        Boobs = 32,
        /// <summary>Studio's own IK/FK pose (not HSPE data). Opt-in: not part of All.</summary>
        Pose = 64,
        /// <summary>The six HSPE modules - the default selection.</summary>
        All = Bones | Colliders | DynamicBones | BlendShapes | IK | Boobs
    }

    /// <summary>Joan's XML node name for each module (the scene format).</summary>
    internal static class ModuleNodes
    {
        internal static readonly ModuleMask[] Singles =
        {
            ModuleMask.Bones, ModuleMask.Colliders, ModuleMask.DynamicBones, ModuleMask.BlendShapes, ModuleMask.IK, ModuleMask.Boobs, ModuleMask.Pose
        };

        internal static string NodeName(ModuleMask single)
        {
            switch (single)
            {
                case ModuleMask.Bones: return "advancedObjects";
                case ModuleMask.Colliders: return "colliders";
                case ModuleMask.DynamicBones: return "dynamicBones";
                case ModuleMask.BlendShapes: return "skinnedMeshes";
                case ModuleMask.IK: return "iks";
                case ModuleMask.Boobs: return "boobs";
                case ModuleMask.Pose: return "studioPose"; // our element (PoseTransfer), not written by HSPE's SaveXml
                default: return null;
            }
        }

        internal static ModuleMask FromNodeName(string name)
        {
            foreach (ModuleMask m in Singles)
                if (NodeName(m) == name)
                    return m;
            return ModuleMask.None;
        }
    }
}
