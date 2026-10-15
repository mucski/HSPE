using System.Xml;
using UnityEngine;

namespace HSPE.Kit.Face
{
    /// <summary>
    /// The Face window's settings inside HSPE's own per-character data: a &lt;face&gt; child next to the module nodes,
    /// written only while the character has a non-neutral face (other scenes stay byte-identical). Everything that
    /// saves HSPE through PoseController.SaveXml / LoadXml carries it: scenes, HSSceneStates, VNGE. Joan's HSPE and
    /// the modules read their own nodes by name and ignore it.
    /// </summary>
    internal static class FaceState
    {
        public static void Save(PoseController pc, XmlTextWriter w)
        {
            FaceSession s = FaceSessions.Get(pc, false);
            if (s == null || s.Input.IsNeutral)
                return;
            w.WriteStartElement("face");
            w.WriteAttributeString("v", "1");
            FaceTimeline.WriteInput(w, s.Input);
            w.WriteEndElement();
        }

        /// <summary>
        /// After HSPE's modules loaded (they reset and restored every shape / bone): take the saved face as the
        /// window's state, or - when the data has none - forget the old one (its values are already gone).
        /// </summary>
        public static void Load(PoseController pc, XmlNode node)
        {
            FaceSessions.Prune(); // a scene load is when characters disappear
            XmlNode face = null;
            foreach (XmlNode child in node.ChildNodes)
                if (child.Name == "face")
                {
                    face = child;
                    break;
                }
            if (face == null)
            {
                FaceSession old = FaceSessions.Get(pc, false);
                if (old != null)
                    old.Forget();
                return;
            }
            FaceSession s = FaceSessions.Get(pc, true);
            if (s == null)
                return;
            s.Forget();
            s.Input.CopyFrom(FaceTimeline.ReadInput(face));
            s.Apply(); // writes the same values HSPE just loaded, and takes ownership of them again
            s.Version++;
        }

        /// <summary>
        /// For state loaders (HSSceneStates, by reflection) whose state has no HSPE data at all: hand the Face
        /// window's face back to the game so the state's own Studio expression shows (only the face; other HSPE
        /// edits stay). Returns true when there was a face to release.
        /// </summary>
        public static bool ReleaseFor(Component character)
        {
            PoseController pc = character != null ? character.GetComponent<PoseController>() : null;
            FaceSession s = FaceSessions.Get(pc, false);
            if (s == null || (s.Input.IsNeutral && s.Applier.Owns == false))
                return false;
            s.Reset();
            s.Version++;
            return true;
        }
    }
}
