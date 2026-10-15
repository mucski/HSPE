using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using HSPE.AMModules;
using ToolBox.Extensions; // GetPathFrom
using UnityEngine;

namespace HSPE.Kit.Transfer
{
    public sealed class ApplyResult
    {
        public int Found;
        public int Total;
        public List<string> Skipped = new List<string>();
        /// <summary>Set when applying failed and the object was restored.</summary>
        public string Error;
    }

    /// <summary>Copy / paste / presets on a live PoseController, through each module's own SaveXml / LoadXml.</summary>
    public static class PoseTransfer
    {
        private static ModuleMask MaskOf(AdvancedModeModule module)
        {
            switch (module.type)
            {
                case AdvancedModeModuleType.BonesEditor: return ModuleMask.Bones;
                case AdvancedModeModuleType.CollidersEditor: return ModuleMask.Colliders;
                case AdvancedModeModuleType.DynamicBonesEditor: return ModuleMask.DynamicBones;
                case AdvancedModeModuleType.BlendShapes: return ModuleMask.BlendShapes;
                case AdvancedModeModuleType.IK: return ModuleMask.IK;
                case AdvancedModeModuleType.BoobsEditor: return ModuleMask.Boobs;
                default: return ModuleMask.None;
            }
        }

        /// <summary>The modules this controller has (items and males: no Boobs; only characters have a Studio pose).</summary>
        public static ModuleMask Available(PoseController c)
        {
            ModuleMask m = ModuleMask.None;
            foreach (AdvancedModeModule module in c.kitModules)
                m |= MaskOf(module);
            if (c.target.ociChar != null)
                m |= ModuleMask.Pose;
            return m;
        }

        /// <summary>
        /// Everything HSPE holds for this object, as a &lt;characterInfo&gt; string, plus the Studio pose of a
        /// character as a &lt;studioPose&gt; child (used for capture and for undo).
        /// </summary>
        public static string FullState(PoseController c)
        {
            using (StringWriter sw = new StringWriter())
            {
                using (XmlTextWriter w = new XmlTextWriter(sw))
                {
                    w.WriteStartElement("characterInfo");
                    c.SaveXml(w);
                    if (c.target.ociChar != null)
                    {
                        w.WriteStartElement(ModuleNodes.NodeName(ModuleMask.Pose));
                        w.WriteAttributeString("version", XmlConvert.ToString(PoseVersion));
                        w.WriteAttributeString("data", SavePose(c.target.ociChar));
                        w.WriteEndElement();
                    }
                    w.WriteEndElement();
                }
                return sw.ToString();
            }
        }

        // Studio's own pose format (PauseCtrl.FileInfo, the same data as the pose files), as base64
        private const int PoseVersion = 101;

        private static string SavePose(Studio.OCIChar ociChar)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(ms))
                    new Studio.PauseCtrl.FileInfo(ociChar).Save(writer);
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        /// <summary>
        /// Applies a Studio pose exactly like Studio's original PauseCtrl.FileInfo.Apply, step by step. Deliberately NOT
        /// calling FileInfo.Apply: PoseViewer and BetterSceneLoader replace it with Harmony prefixes that keep the target's
        /// current IK/FK mode (their pose-file options), so a pasted IK pose on an FK character changed nothing visible.
        /// </summary>
        private static void ApplyPose(Studio.OCIChar ociChar, XmlElement pose)
        {
            Studio.PauseCtrl.FileInfo info = new Studio.PauseCtrl.FileInfo();
            using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(pose.GetAttribute("data"))))
            using (BinaryReader reader = new BinaryReader(ms))
                info.Load(reader, XmlConvert.ToInt32(pose.GetAttribute("version")));

            ociChar.LoadAnime(info.group, info.category, info.no, info.normalizedTime);
            for (int i = 0; i < info.activeIK.Length; i++)
                ociChar.ActiveIK((Studio.OIBoneInfo.BoneGroup)(1 << i), info.activeIK[i]);
            ociChar.ActiveKinematicMode(Studio.OICharInfo.KinematicMode.IK, info.enableIK, true);
            foreach (KeyValuePair<int, Studio.ChangeAmount> p in info.dicIK)
                ociChar.oiCharInfo.ikTarget[p.Key].changeAmount.Copy(p.Value);
            for (int j = 0; j < info.activeFK.Length; j++)
                ociChar.ActiveFK(Studio.FKCtrl.parts[j], info.activeFK[j]);
            ociChar.ActiveKinematicMode(Studio.OICharInfo.KinematicMode.FK, info.enableFK, true);
            foreach (KeyValuePair<int, Studio.ChangeAmount> p in info.dicFK)
                ociChar.oiCharInfo.bones[p.Key].changeAmount.Copy(p.Value);
            for (int k = 0; k < info.expression.Length; k++)
                ociChar.EnableExpressionCategory(k, info.expression[k]);
        }

        public static PresetDocument Capture(PoseController c, ModuleMask modules)
        {
            modules &= Available(c);
            bool isCharacter = c.target.type == GenericOCITarget.Type.Character;
            List<MeshInfo> meshes = (modules & ModuleMask.BlendShapes) != 0 ? MeshesOf(c) : null;
            return PresetDocument.FromCharacterXml(FullState(c), isCharacter ? "character" : "item", isCharacter && c.target.isFemale, modules, meshes);
        }

        private static List<MeshInfo> MeshesOf(PoseController c)
        {
            List<MeshInfo> list = new List<MeshInfo>();
            foreach (SkinnedMeshRenderer r in c.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                MeshInfo info = Describe(c.transform, r);
                if (info != null)
                    list.Add(info);
            }
            return list;
        }

        private static MeshInfo Describe(Transform root, SkinnedMeshRenderer r)
        {
            if (r == null || r.sharedMesh == null)
                return null;
            Mesh mesh = r.sharedMesh;
            StringBuilder names = new StringBuilder();
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                if (i != 0)
                    names.Append('|');
                names.Append(mesh.GetBlendShapeName(i));
            }
            return new MeshInfo { Path = r.transform.GetPathFrom(root), Count = mesh.blendShapeCount, Names = names.ToString() };
        }

        /// <summary>
        /// Applies the chosen modules. Only those modules' LoadXml run (the others aren't touched, not even reset).
        /// On any exception the object is restored to how it was and Error is set.
        /// </summary>
        public static ApplyResult Apply(PoseController c, PresetDocument d, ModuleMask modules)
        {
            ApplyResult result = new ApplyResult();
            modules &= Available(c);
            string before = FullState(c);
            try
            {
                Transform root = c.transform;
                XmlElement node = d.BuildApplyNode(modules, path =>
                {
                    Transform t = root.Find(path);
                    return t != null ? Describe(root, t.GetComponent<SkinnedMeshRenderer>()) : null;
                }, result.Skipped);
                List<string> missing = result.Skipped;
                result.Found = PresetDocument.CountEntries(node, modules, (module, key) =>
                {
                    bool exists = Exists(c, module, key);
                    if (exists == false)
                        missing.Add(module + " entry not on target: " + key);
                    return exists;
                }, out result.Total);

                // the Studio pose first (it switches FK / IK on or off), then HSPE's modules on top of it
                XmlElement pose = node[ModuleNodes.NodeName(ModuleMask.Pose)];
                if (pose != null && c.target.ociChar != null)
                {
                    if (d.IsFemale != c.target.isFemale)
                        result.Skipped.Add("pose (Studio poses don't transfer between sexes)");
                    else
                        ApplyPose(c.target.ociChar, pose);
                }

                CharaPoseController chara = c as CharaPoseController;
                if (chara != null && (modules & ModuleMask.Bones) != 0)
                    ApplySwitches(chara, node);
                foreach (AdvancedModeModule module in c.kitModules)
                    if ((modules & MaskOf(module)) != 0)
                        module.LoadXml(node);
                // the pasted shapes / bones replaced whatever the Face window had written: it lets go of that face
                if ((modules & (ModuleMask.BlendShapes | ModuleMask.Bones)) != 0)
                    global::HSPE.Kit.Face.FaceState.Load(c, node);
            }
            catch (Exception e)
            {
                Debug.LogError("HSPE: applying settings failed, restoring the previous state.\n" + e);
                global::HSPE.Kit.Log.Write("applying settings failed, restored: " + e);
                Restore(c, before);
                result.Error = e.Message;
            }
            return result;
        }

        private static void ApplySwitches(CharaPoseController chara, XmlElement node)
        {
            // Same defaults as CharaPoseController.LoadXml, but only reached when Bones is applied
            chara.optimizeIK = node.HasAttribute("optimizeIK") == false || XmlConvert.ToBoolean(node.GetAttribute("optimizeIK"));
            chara.crotchJointCorrection = node.HasAttribute("crotchCorrection") && XmlConvert.ToBoolean(node.GetAttribute("crotchCorrection"));
            chara.leftFootJointCorrection = node.HasAttribute("leftAnkleCorrection") && XmlConvert.ToBoolean(node.GetAttribute("leftAnkleCorrection"));
            chara.rightFootJointCorrection = node.HasAttribute("rightAnkleCorrection") && XmlConvert.ToBoolean(node.GetAttribute("rightAnkleCorrection"));
        }

        private static bool Exists(PoseController c, ModuleMask module, string key)
        {
            switch (module)
            {
                case ModuleMask.Boobs:
                    return c.target.isFemale && (key == "left" || key == "right");
                case ModuleMask.DynamicBones:
                    foreach (DynamicBone db in c.GetComponentsInChildren<DynamicBone>(true))
                    {
                        Transform t = db.m_Root != null ? db.m_Root : db.transform;
                        if (t.GetPathFrom(c.transform) == key || t.name == key)
                            return true;
                    }
                    return false;
                default:
                    return c.transform.Find(key) != null;
            }
        }

        /// <summary>Puts back a FullState snapshot (Studio pose, all modules and switches), as a scene load would.</summary>
        public static void Restore(PoseController c, string fullState)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(fullState);
            XmlElement pose = doc.DocumentElement[ModuleNodes.NodeName(ModuleMask.Pose)];
            if (pose != null && c.target.ociChar != null)
                ApplyPose(c.target.ociChar, pose);
            c.LoadXml(doc.DocumentElement);
        }
    }
}
