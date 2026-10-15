using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace HSPE.Kit.Transfer
{
    public delegate MeshInfo MeshLookup(string path);

    public delegate bool PathExists(ModuleMask module, string key);

    /// <summary>
    /// A character's (or item's) HSPE settings for some modules: Joan's own module XML inside a small header.
    /// Unity-free so it can be unit tested.
    /// &lt;hspePreset version kind sex modules&gt;&lt;characterInfo switches...&gt;module nodes&lt;/characterInfo&gt;&lt;meshInfo .../&gt;&lt;/hspePreset&gt;
    /// </summary>
    public sealed class PresetDocument
    {
        public const string RootName = "hspePreset";
        public const int FormatVersion = 1;
        private static readonly string[] _switches = { "optimizeIK", "crotchCorrection", "leftAnkleCorrection", "rightAnkleCorrection" };

        public string Kind;
        public bool IsFemale;
        public ModuleMask Modules;
        public XmlElement Character;
        public List<MeshInfo> Meshes = new List<MeshInfo>();

        private XmlDocument _doc;

        /// <summary>Builds a snapshot from a full &lt;characterInfo&gt; (PoseController.SaveXml output).</summary>
        public static PresetDocument FromCharacterXml(string characterInfoXml, string kind, bool isFemale, ModuleMask modules, List<MeshInfo> meshes)
        {
            XmlDocument source = new XmlDocument();
            source.LoadXml(characterInfoXml);
            PresetDocument d = new PresetDocument { Kind = kind, IsFemale = isFemale, Modules = modules, _doc = new XmlDocument() };
            XmlElement ci = d._doc.CreateElement("characterInfo");
            d._doc.AppendChild(ci);
            // the character switches are pose settings: they travel with Bones only
            if ((modules & ModuleMask.Bones) != 0)
                foreach (string s in _switches)
                    if (source.DocumentElement.HasAttribute(s))
                        ci.SetAttribute(s, source.DocumentElement.GetAttribute(s));
            foreach (XmlNode child in source.DocumentElement.ChildNodes)
            {
                ModuleMask m = ModuleNodes.FromNodeName(child.Name);
                if (m == ModuleMask.None || (modules & m) == 0)
                    continue;
                XmlElement copy = (XmlElement)d._doc.ImportNode(child, true);
                if (m == ModuleMask.Colliders)
                    StripColliderLinks(copy);
                ci.AppendChild(copy);
            }
            d.Character = ci;
            if ((modules & ModuleMask.BlendShapes) != 0 && meshes != null)
                d.Meshes.AddRange(meshes);
            return d;
        }

        /// <summary>Collider ignore-links point at the original objects' instance ids, which mean nothing elsewhere.</summary>
        private static void StripColliderLinks(XmlElement colliders)
        {
            foreach (XmlNode collider in colliders.ChildNodes)
            {
                for (int i = collider.ChildNodes.Count - 1; i >= 0; i--)
                {
                    string n = collider.ChildNodes[i].Name;
                    if (n == "ignoredDynamicBone" || n == "ignoredDynamicBone2")
                        collider.RemoveChild(collider.ChildNodes[i]);
                }
            }
        }

        public string ToXml()
        {
            XmlDocument doc = new XmlDocument();
            XmlElement root = doc.CreateElement(RootName);
            root.SetAttribute("version", XmlConvert.ToString(FormatVersion));
            root.SetAttribute("kind", this.Kind);
            root.SetAttribute("sex", this.IsFemale ? "female" : "male");
            root.SetAttribute("modules", XmlConvert.ToString((int)this.Modules));
            doc.AppendChild(root);
            root.AppendChild(doc.ImportNode(this.Character, true));
            foreach (MeshInfo m in this.Meshes)
            {
                XmlElement e = doc.CreateElement("meshInfo");
                e.SetAttribute("path", m.Path);
                e.SetAttribute("count", XmlConvert.ToString(m.Count));
                e.SetAttribute("names", m.Names ?? "");
                root.AppendChild(e);
            }
            using (StringWriter sw = new StringWriter())
            {
                doc.Save(sw);
                return sw.ToString();
            }
        }

        /// <summary>Null when the text isn't an HSPE preset (the clipboard may hold anything).</summary>
        public static PresetDocument Parse(string xml)
        {
            if (string.IsNullOrEmpty(xml) || xml.IndexOf("<" + RootName, StringComparison.Ordinal) < 0)
                return null;
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);
                XmlElement root = doc.DocumentElement;
                if (root == null || root.Name != RootName || root["characterInfo"] == null)
                    return null;
                PresetDocument d = new PresetDocument
                {
                    _doc = doc,
                    Kind = root.GetAttribute("kind"),
                    IsFemale = root.GetAttribute("sex") == "female",
                    Modules = (ModuleMask)XmlConvert.ToInt32(root.GetAttribute("modules")),
                    Character = root["characterInfo"]
                };
                foreach (XmlNode n in root.ChildNodes)
                {
                    XmlElement e = n as XmlElement;
                    if (e == null || e.Name != "meshInfo")
                        continue;
                    d.Meshes.Add(new MeshInfo { Path = e.GetAttribute("path"), Count = XmlConvert.ToInt32(e.GetAttribute("count")), Names = e.GetAttribute("names") });
                }
                return d;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The &lt;characterInfo&gt; to hand to the chosen modules' LoadXml: only those modules' nodes, the switches
        /// only when Bones is applied, and blend-shape renderers whose target shapes differ dropped (listed in skipped).
        /// The preset itself is not modified.
        /// </summary>
        public XmlElement BuildApplyNode(ModuleMask modules, MeshLookup targetMesh, List<string> skipped)
        {
            XmlDocument doc = new XmlDocument();
            XmlElement ci = doc.CreateElement("characterInfo");
            doc.AppendChild(ci);
            if ((modules & ModuleMask.Bones) != 0)
                foreach (string s in _switches)
                    if (this.Character.HasAttribute(s))
                        ci.SetAttribute(s, this.Character.GetAttribute(s));
            foreach (XmlNode child in this.Character.ChildNodes)
            {
                ModuleMask m = ModuleNodes.FromNodeName(child.Name);
                if (m == ModuleMask.None || (modules & m) == 0)
                    continue;
                XmlElement copy = (XmlElement)doc.ImportNode(child, true);
                if (m == ModuleMask.BlendShapes)
                    this.FilterMeshes(copy, targetMesh, skipped);
                ci.AppendChild(copy);
            }
            return ci;
        }

        private void FilterMeshes(XmlElement skinnedMeshes, MeshLookup targetMesh, List<string> skipped)
        {
            for (int i = skinnedMeshes.ChildNodes.Count - 1; i >= 0; i--)
            {
                XmlElement mesh = skinnedMeshes.ChildNodes[i] as XmlElement;
                if (mesh == null)
                    continue;
                string path = mesh.GetAttribute("name");
                MeshInfo source = this.Meshes.Find(m => m.Path == path);
                MeshInfo target = targetMesh(path);
                if (source == null || source.SameShapes(target) == false)
                {
                    skinnedMeshes.RemoveChild(mesh);
                    skipped.Add("blend shapes of " + path + (target == null ? " (not on target)" : " (different shapes)"));
                }
            }
        }

        /// <summary>How many entries of the apply node exist on the target (found) out of all entries (total).</summary>
        public static int CountEntries(XmlElement applyNode, ModuleMask modules, PathExists exists, out int total)
        {
            int found = 0;
            total = 0;
            foreach (XmlNode child in applyNode.ChildNodes)
            {
                ModuleMask m = ModuleNodes.FromNodeName(child.Name);
                if (m == ModuleMask.None || (modules & m) == 0)
                    continue;
                foreach (XmlNode entry in child.ChildNodes)
                {
                    XmlElement e = entry as XmlElement;
                    if (e == null)
                        continue;
                    string key = e.HasAttribute("name") ? e.GetAttribute("name") : e.HasAttribute("root") ? e.GetAttribute("root") : e.Name;
                    total++;
                    if (exists(m, key))
                        found++;
                }
            }
            return found;
        }
    }
}
