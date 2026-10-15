using System;
using System.Collections.Generic;
using System.IO;
using HSPE.Kit.Transfer;
using UnityEngine;

namespace HSPE.Kit.Advanced
{
    /// <summary>
    /// Preset thumbnails, reloaded when the file changes (same cache as HSLRE's). Each is cropped to the
    /// shown box's aspect and gets rounded corners with a one-pixel soft edge baked into its alpha (a stencil Mask
    /// clips hard and looked pixelated), plus mipmaps so the small display stays smooth.
    /// </summary>
    internal sealed class ThumbnailCache
    {
        private sealed class Entry
        {
            public DateTime written;
            public Texture2D texture;
        }

        private readonly Dictionary<string, Entry> _loaded = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly float _boxAspect, _boxHeight, _radius;

        public ThumbnailCache(float boxAspect, float boxHeight, float radius)
        {
            _boxAspect = boxAspect;
            _boxHeight = boxHeight;
            _radius = radius;
        }

        /// <summary>The preset's thumbnail, or null when it has none.</summary>
        public Texture2D Get(string presetName)
        {
            string path = PresetStore.ThumbnailPath(presetName);
            Entry entry;
            bool cached = _loaded.TryGetValue(presetName, out entry);
            if (File.Exists(path) == false)
            {
                if (cached)
                    Forget(presetName);
                return null;
            }
            DateTime written = File.GetLastWriteTimeUtc(path);
            if (cached && entry.written == written)
                return entry.texture;
            if (cached)
                Forget(presetName);
            Texture2D file = new Texture2D(2, 2, TextureFormat.RGB24, false);
            Texture2D tex;
            try
            {
                if (file.LoadImage(File.ReadAllBytes(path)) == false)
                    return null;
                tex = Bake(file);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                UnityEngine.Object.Destroy(file);
            }
            _loaded[presetName] = new Entry { written = written, texture = tex };
            return tex;
        }

        private Texture2D Bake(Texture2D file)
        {
            int sw = file.width, sh = file.height;
            int w = sw, h = sh;
            if ((float)sw / sh > _boxAspect)
                w = Mathf.Max(1, Mathf.RoundToInt(sh * _boxAspect));
            else
                h = Mathf.Max(1, Mathf.RoundToInt(sw / _boxAspect));
            int x0 = (sw - w) / 2, y0 = (sh - h) / 2;
            Color32[] src = file.GetPixels32();
            Color32[] dst = new Color32[w * h];
            float texelsPerUnit = h / _boxHeight;
            float r = _radius * texelsPerUnit;
            float edge = Mathf.Max(1f, texelsPerUnit);
            float hx = w * 0.5f, hy = h * 0.5f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color32 c = src[(y + y0) * sw + x + x0];
                    float qx = Mathf.Abs(x + 0.5f - hx) - (hx - r), qy = Mathf.Abs(y + 0.5f - hy) - (hy - r);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                    c.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - outside / edge) * 255f);
                    dst[y * w + x] = c;
                }
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.SetPixels32(dst);
            tex.Apply(true, true);
            return tex;
        }

        private void Forget(string presetName)
        {
            Entry entry;
            if (_loaded.TryGetValue(presetName, out entry))
            {
                if (entry.texture != null)
                    UnityEngine.Object.Destroy(entry.texture);
                _loaded.Remove(presetName);
            }
        }

        private readonly List<string> _drop = new List<string>();

        /// <summary>Frees thumbnails of presets that are no longer listed (deleted / renamed) so they don't pile up.</summary>
        public void Retain(string[] names)
        {
            _drop.Clear();
            foreach (KeyValuePair<string, Entry> p in _loaded)
                if (Array.IndexOf(names, p.Key) < 0)
                    _drop.Add(p.Key);
            foreach (string n in _drop)
                Forget(n);
        }

        public void Clear()
        {
            foreach (Entry entry in _loaded.Values)
                if (entry.texture != null)
                    UnityEngine.Object.Destroy(entry.texture);
            _loaded.Clear();
        }
    }
}
