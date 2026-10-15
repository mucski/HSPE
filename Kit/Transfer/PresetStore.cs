using System;
using System.IO;
using UnityEngine;

namespace HSPE.Kit.Transfer
{
    /// <summary>Preset files: UserData\HSPE\Presets\&lt;name&gt;.xml with a &lt;name&gt;.png thumbnail beside it.</summary>
    public static class PresetStore
    {
        private const int ThumbnailWidth = 320;

        public static string Folder
        {
            get { return Path.GetFullPath(Path.Combine(Application.dataPath, "../UserData/HSPE/Presets")); }
        }

        public static string[] List()
        {
            if (Directory.Exists(Folder) == false)
                return new string[0];
            string[] files = Directory.GetFiles(Folder, "*.xml");
            for (int i = 0; i < files.Length; i++)
                files[i] = Path.GetFileNameWithoutExtension(files[i]);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            return files;
        }

        public static string ThumbnailPath(string name)
        {
            return Path.Combine(Folder, name + ".png");
        }

        /// <summary>Trimmed; invalid file name characters become "_".</summary>
        public static string CleanName(string text)
        {
            string name = (text ?? "").Trim();
            return string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();
        }

        public static void Save(string name, PresetDocument d, Camera thumbnailCamera)
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Combine(Folder, name + ".xml"), d.ToXml());
            if (thumbnailCamera != null)
                SaveThumbnail(thumbnailCamera, ThumbnailPath(name));
        }

        public static PresetDocument Load(string name)
        {
            string path = Path.Combine(Folder, name + ".xml");
            return File.Exists(path) ? PresetDocument.Parse(File.ReadAllText(path)) : null;
        }

        public static void Delete(string name)
        {
            string xml = Path.Combine(Folder, name + ".xml");
            if (File.Exists(xml))
                File.Delete(xml);
            string png = ThumbnailPath(name);
            if (File.Exists(png))
                File.Delete(png);
        }

        /// <summary>Renders the camera into a render texture (every image effect, no UI) and saves a small PNG.</summary>
        private static void SaveThumbnail(Camera c, string path)
        {
            RenderTexture big = null, small = null;
            RenderTexture previousTarget = c.targetTexture, previousActive = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
                int w = ThumbnailWidth * 2, h = Mathf.Max(1, Mathf.RoundToInt(w / aspect));
                big = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                c.targetTexture = big;
                c.Render();
                c.targetTexture = previousTarget;
                int tw = ThumbnailWidth, th = Mathf.Max(1, Mathf.RoundToInt(tw / aspect));
                small = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(big, small);
                RenderTexture.active = small;
                tex = new Texture2D(tw, th, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
                tex.Apply(false);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            catch (Exception e)
            {
                Debug.LogError("HSPE: could not save the preset thumbnail.\n" + e);
            }
            finally
            {
                c.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                if (big != null) RenderTexture.ReleaseTemporary(big);
                if (small != null) RenderTexture.ReleaseTemporary(small);
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }
    }
}
