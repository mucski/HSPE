using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Studio;
using UnityEngine;

namespace HSPE.Kit.Face
{
    /// <summary>
    /// A face preset: the Face window's input (mood dot, vowel, eyes / mouth open, point drags) plus Studio's tears and
    /// blush, and optionally the eye-look pattern. Stored as INPUT, not as final shape weights, so a preset follows
    /// recipe tuning and works on both sexes (the recipes map it to each head).
    /// </summary>
    internal sealed class FacePreset
    {
        public string Name;
        public bool Default;      // shipped with the plugin: read-only
        public FaceInput Input;
        public int Tears = -1;    // -1 = not stored
        public float Blush = -1f; // -1 = not stored
        public int EyeLook = -1;  // -1 = not stored (the preset didn't tick "eye direction")
    }

    /// <summary>
    /// Defaults: Plugins\HSPE\face_presets\*.xml (shipped, read-only). Yours: UserData\HSPE\Face\Presets\*.xml with a
    /// .png face thumbnail beside each.
    /// </summary>
    internal static class FacePresets
    {
        private const int ThumbSize = 192;

        public static string UserFolder { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../UserData/HSPE/Face/Presets")); } }
        public static string DefaultFolder { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../Plugins/HSPE/face_presets")); } }

        public static string ThumbnailPath(string name) { return Path.Combine(UserFolder, name + ".png"); }

        public static List<FacePreset> List(bool defaults)
        {
            List<FacePreset> list = new List<FacePreset>();
            string folder = defaults ? DefaultFolder : UserFolder;
            if (Directory.Exists(folder) == false)
                return list;
            string[] files = Directory.GetFiles(folder, "*.xml");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                FacePreset p = Load(f);
                if (p == null)
                    continue;
                p.Name = Path.GetFileNameWithoutExtension(f);
                p.Default = defaults;
                list.Add(p);
            }
            return list;
        }

        private static FacePreset Load(string path)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(path);
                XmlElement root = doc.DocumentElement;
                if (root == null || root.Name != "facePreset")
                    return null;
                XmlNode input = root["input"];
                FacePreset p = new FacePreset { Input = input != null ? FaceTimeline.ReadInput(input) : new FaceInput() };
                p.Tears = Int(root, "tears", -1);
                p.Blush = Float(root, "blush", -1f);
                p.EyeLook = Int(root, "eyeLook", -1);
                return p;
            }
            catch (Exception e)
            {
                Log.Write("face presets: can't read " + path + ": " + e.Message);
                return null;
            }
        }

        public static string CleanName(string text)
        {
            string name = (text ?? "").Trim();
            return string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();
        }

        public static bool Exists(string name) { return File.Exists(Path.Combine(UserFolder, name + ".xml")); }

        /// <summary>Saves the session's face (+ Studio tears / blush, + eye look when asked) and a face thumbnail.</summary>
        public static void Save(string name, FaceSession session, bool eyeLook, Camera camera)
        {
            Directory.CreateDirectory(UserFolder);
            OCIChar chara = Chara(session);
            using (XmlTextWriter w = new XmlTextWriter(Path.Combine(UserFolder, name + ".xml"), System.Text.Encoding.UTF8))
            {
                w.Formatting = Formatting.Indented;
                w.WriteStartElement("facePreset");
                w.WriteAttributeString("version", "1");
                if (chara != null)
                {
                    w.WriteAttributeString("tears", chara.GetTearsLv().ToString());
                    w.WriteAttributeString("blush", chara.GetHohoAkaRate().ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    if (eyeLook)
                        w.WriteAttributeString("eyeLook", chara.charInfo.statusInfo.eyesLookPtn.ToString());
                }
                w.WriteStartElement("input");
                FaceTimeline.WriteInput(w, session.Input);
                w.WriteEndElement();
                w.WriteEndElement();
            }
            if (camera != null)
                SaveThumbnail(camera, session, ThumbnailPath(name));
        }

        public static void Delete(string name)
        {
            string xml = Path.Combine(UserFolder, name + ".xml");
            if (File.Exists(xml))
                File.Delete(xml);
            string png = ThumbnailPath(name);
            if (File.Exists(png))
                File.Delete(png);
        }

        public static void Rename(string from, string to)
        {
            File.Move(Path.Combine(UserFolder, from + ".xml"), Path.Combine(UserFolder, to + ".xml"));
            if (File.Exists(ThumbnailPath(from)))
                File.Move(ThumbnailPath(from), ThumbnailPath(to));
        }

        /// <summary>Applies the face (keeps the session's extra points) and Studio's tears / blush / eye look when stored.</summary>
        public static void Apply(FacePreset p, FaceSession session)
        {
            session.Input.CopyFrom(p.Input);
            session.Apply();
            OCIChar chara = Chara(session);
            if (chara == null)
                return;
            if (p.Tears >= 0)
                chara.SetTearsLv((byte)Mathf.Clamp(p.Tears, 0, 255));
            if (p.Blush >= 0f)
                chara.SetHohoAkaRate(Mathf.Clamp01(p.Blush));
            if (p.EyeLook >= 0)
                chara.ChangeLookEyesPtn(p.EyeLook);
        }

        private static OCIChar Chara(FaceSession session)
        {
            return session != null && session.Target != null && session.Target.target != null ? session.Target.target.ociChar : null;
        }

        private static int Int(XmlElement e, string name, int fallback)
        {
            int v;
            return e.HasAttribute(name) && int.TryParse(e.GetAttribute(name), out v) ? v : fallback;
        }

        private static float Float(XmlElement e, string name, float fallback)
        {
            float v;
            return e.HasAttribute(name) && float.TryParse(e.GetAttribute(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        // ---------------------------------------------------------------- thumbnail

        /// <summary>Renders the camera (with its image effects, no UI) and crops a square around the character's face.</summary>
        private static void SaveThumbnail(Camera c, FaceSession session, string path)
        {
            // Zooms the camera's projection onto the face for this one render instead of cropping a full frame: the
            // crop read pixels with the wrong vertical origin, so it only hit the face when the face was at the
            // screen's centre. Viewport maths has no such flip; image effects still run.
            RenderTexture big = null, small = null;
            RenderTexture previousTarget = c.targetTexture, previousActive = RenderTexture.active;
            Texture2D tex = null;
            bool zoomed = false;
            try
            {
                float screenAspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
                // square on screen around the face: centre on the face root, half height = 11 cm projected
                Vector2 centre = new Vector2(0.5f, 0.5f);
                float halfY = 0.5f;
                Transform face = session.Rig != null ? session.Rig.FaceRoot : null;
                if (face != null)
                {
                    Vector3 world = face.position + face.up * 0.03f;
                    Vector3 vp = c.WorldToViewportPoint(world);
                    Vector3 top = c.WorldToViewportPoint(world + c.transform.up * 0.11f);
                    if (vp.z > 0f)
                    {
                        centre = new Vector2(vp.x, vp.y);
                        halfY = Mathf.Clamp(Mathf.Abs(top.y - vp.y), 0.02f, 0.5f);
                    }
                }
                float halfX = halfY / screenAspect; // same size in pixels -> a square image
                // NDC of the square: [2(c-h)-1, 2(c+h)-1] -> scale 1/(2h), offset -(2c-1)/(2h)
                Matrix4x4 zoom = Matrix4x4.identity;
                zoom[0, 0] = 1f / (2f * halfX);
                zoom[0, 3] = -(2f * centre.x - 1f) / (2f * halfX);
                zoom[1, 1] = 1f / (2f * halfY);
                zoom[1, 3] = -(2f * centre.y - 1f) / (2f * halfY);
                Matrix4x4 projection = c.projectionMatrix;
                c.projectionMatrix = zoom * projection;
                zoomed = true;

                big = RenderTexture.GetTemporary(ThumbSize * 2, ThumbSize * 2, 24, RenderTextureFormat.ARGB32);
                c.targetTexture = big;
                c.Render();
                c.targetTexture = previousTarget;
                c.ResetProjectionMatrix();
                zoomed = false;

                small = RenderTexture.GetTemporary(ThumbSize, ThumbSize, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(big, small);
                RenderTexture.active = small;
                tex = new Texture2D(ThumbSize, ThumbSize, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, ThumbSize, ThumbSize), 0, 0);
                tex.Apply(false);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            catch (Exception e)
            {
                Log.Write("face presets: thumbnail failed: " + e);
            }
            finally
            {
                c.targetTexture = previousTarget;
                if (zoomed)
                    c.ResetProjectionMatrix();
                RenderTexture.active = previousActive;
                if (big != null) RenderTexture.ReleaseTemporary(big);
                if (small != null) RenderTexture.ReleaseTemporary(small);
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }
    }
}
