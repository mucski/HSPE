using RootMotion.FinalIK;
using Studio;

namespace HSPE.Kit.Small
{
    using HSUIKit;

    /// <summary>
    /// Joan's copy-limb / swap buttons as a right-click menu, shared by the IK figure and the FK figures.
    /// CopyLimbToTwin(X) copies limb X onto the other side.
    /// </summary>
    internal static class LimbMenu
    {
        public enum Side { None, Right, Left }

        /// <summary>Adds the items for a clicked arm / leg part on one side; side None with neither = the whole menu.</summary>
        public static void Append(KitMenu menu, CharaPoseController chara, Side side, bool arm, bool leg)
        {
            bool all = side == Side.None && !arm && !leg;
            if (all || arm)
            {
                if (side != Side.Left)
                {
                    menu.Item("Copy right arm to left", delegate { chara.CopyLimbToTwin(FullBodyBipedChain.RightArm, OIBoneInfo.BoneGroup.RightArm); });
                    menu.Item("Copy right hand to left", delegate { chara.CopyHandToTwin(OIBoneInfo.BoneGroup.RightHand); });
                }
                if (side != Side.Right)
                {
                    menu.Item("Copy left arm to right", delegate { chara.CopyLimbToTwin(FullBodyBipedChain.LeftArm, OIBoneInfo.BoneGroup.LeftArm); });
                    menu.Item("Copy left hand to right", delegate { chara.CopyHandToTwin(OIBoneInfo.BoneGroup.LeftHand); });
                }
                menu.Separator();
            }
            if (all || leg)
            {
                if (side != Side.Left)
                    menu.Item("Copy right leg to left", delegate { chara.CopyLimbToTwin(FullBodyBipedChain.RightLeg, OIBoneInfo.BoneGroup.RightLeg); });
                if (side != Side.Right)
                    menu.Item("Copy left leg to right", delegate { chara.CopyLimbToTwin(FullBodyBipedChain.LeftLeg, OIBoneInfo.BoneGroup.LeftLeg); });
                menu.Separator();
            }
            menu.Item("Swap pose (mirror left / right)", delegate { chara.SwapPose(); });
        }
    }
}
