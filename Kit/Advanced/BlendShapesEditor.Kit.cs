using System.Collections.Generic;
using System.IO;
using ToolBox.Extensions;
using UnityEngine;

namespace HSPE.AMModules
{
    // addition (HSPE fork): the kit Blend Shapes page's access to Joan's BlendShapesEditor. The logic that
    // lived inside his IMGUI code moved here and his GUILogic calls it: setting a weight (with "link eyes components"),
    // resetting one shape (linked), resetting a renderer (linked), saving an alias.
    public partial class BlendShapesEditor
    {

        /// <summary>Changed entries, for the kit sidebar badge (a count, instead of serializing the module 1/s).</summary>
        internal int KitChangedCount()
        {
            int n = 0;
            foreach (KeyValuePair<SkinnedMeshRenderer, SkinnedMeshRendererData> p in _dirtySkinnedMeshRenderers)
                n += p.Value.dirtyBlendShapes.Count;
            return n;
        }
        // ---------------------------------------------------------------- moved from GUILogic (shared)

        private bool IsEyeComponent(int index)
        {
            return index < (_target.isFemale ? _femaleEyesComponentsCount : _maleEyesComponentsCount);
        }

        /// <summary>A shape's new weight; with "link eyes components" the eye shapes follow on the linked meshes.</summary>
        internal void SetWeightLinked(SkinnedMeshRenderer renderer, int index, float weight)
        {
            _lastEditedBlendShape = index;
            SetBlendShapeWeight(renderer, index, weight);
            SkinnedMeshRendererWrapper wrapper;
            if (_linkEyesComponents && IsEyeComponent(index) && _links.TryGetValue(renderer, out wrapper))
                foreach (SkinnedMeshRendererWrapper link in wrapper.links)
                    if (index < link.renderer.sharedMesh.blendShapeCount)
                        SetBlendShapeWeight(link.renderer, index, weight);
        }

        /// <summary>One shape back to its original weight (linked meshes too).</summary>
        internal void ResetShapeLinked(SkinnedMeshRenderer renderer, int index)
        {
            SkinnedMeshRendererData data;
            BlendShapeData bsData;
            if (_dirtySkinnedMeshRenderers.TryGetValue(renderer, out data) == false || data.dirtyBlendShapes.TryGetValue(index, out bsData) == false)
                return;
            renderer.TrySetBlendShapeWeight(index, bsData.originalWeight);
            data.dirtyBlendShapes.Remove(index);
            if (data.dirtyBlendShapes.Count == 0)
                SetMeshRendererNotDirty(renderer);

            SkinnedMeshRendererWrapper wrapper;
            if (_linkEyesComponents && IsEyeComponent(index) && _links.TryGetValue(renderer, out wrapper))
                foreach (SkinnedMeshRendererWrapper link in wrapper.links)
                    if (_dirtySkinnedMeshRenderers.TryGetValue(link.renderer, out data) && data.dirtyBlendShapes.TryGetValue(index, out bsData))
                    {
                        link.renderer.TrySetBlendShapeWeight(index, bsData.originalWeight);
                        data.dirtyBlendShapes.Remove(index);
                        if (data.dirtyBlendShapes.Count == 0)
                            SetMeshRendererNotDirty(link.renderer);
                    }
        }

        /// <summary>Every shape of a renderer back (and of its linked meshes when "link eyes" is on).</summary>
        internal void ResetRendererLinked(SkinnedMeshRenderer renderer)
        {
            SetMeshRendererNotDirty(renderer);
            SkinnedMeshRendererWrapper wrapper;
            if (_linkEyesComponents && _links.TryGetValue(renderer, out wrapper))
                foreach (SkinnedMeshRendererWrapper link in wrapper.links)
                    SetMeshRendererNotDirty(link.renderer);
        }

        /// <summary>Joan's Rename / Save: empty or the real name removes the alias.</summary>
        internal static void SetBlendShapeAlias(string blendShapeName, string alias)
        {
            alias = (alias ?? "").Trim();
            if (alias.Length == 0 || alias == blendShapeName)
                _blendShapeAliases.Remove(blendShapeName);
            else
                _blendShapeAliases[blendShapeName] = alias;
        }

        // ---------------------------------------------------------------- kit page access

        /// <summary>Renderers that have blend shapes (Joan's list).</summary>
        /// <summary>Fills a caller-owned list (asked a few times a second: no new list each time).</summary>
        internal void KitRenderers(List<SkinnedMeshRenderer> list)
        {
            list.Clear();
            foreach (SkinnedMeshRenderer r in _skinnedMeshRenderers)
                if (r && r.sharedMesh && r.sharedMesh.blendShapeCount > 0)
                    list.Add(r);
        }

        internal static string KitRendererName(SkinnedMeshRenderer r)
        {
            string alias;
            return _skinnedMeshAliases.TryGetValue(r.name, out alias) ? alias : r.name;
        }

        internal static string KitShapeAlias(string blendShapeName)
        {
            string alias;
            return _blendShapeAliases.TryGetValue(blendShapeName, out alias) ? alias : null;
        }

        internal SkinnedMeshRenderer kitTarget
        {
            get { return _skinnedMeshTarget; }
            set
            {
                _skinnedMeshTarget = value;
                _lastEditedBlendShape = -1;
            }
        }

        internal bool kitIsCharacter { get { return _target.type == GenericOCITarget.Type.Character; } }

        internal bool kitLinkEyes
        {
            get { return _linkEyesComponents; }
            set { _linkEyesComponents = value; }
        }

        /// <summary>"Eyes" / "Mouth" separator before this shape of the head mesh, or null.</summary>
        internal string KitSeparator(SkinnedMeshRenderer r, int index)
        {
            if (r != _headRenderer)
                return null;
            string s;
            return (_target.isFemale ? _femaleSeparators : _maleSeparators).TryGetValue(index, out s) ? s : null;
        }

        internal bool KitIsDirty(SkinnedMeshRenderer r) { return r != null && _dirtySkinnedMeshRenderers.ContainsKey(r); }

        internal int KitDirtyCount(SkinnedMeshRenderer r)
        {
            SkinnedMeshRendererData data;
            return r != null && _dirtySkinnedMeshRenderers.TryGetValue(r, out data) ? data.dirtyBlendShapes.Count : 0;
        }

        /// <summary>The shown weight (HSPE's value when it holds one, like Joan's slider) and whether HSPE holds it.</summary>
        internal float KitWeight(SkinnedMeshRenderer r, int index, out bool dirty)
        {
            SkinnedMeshRendererData data;
            BlendShapeData bs;
            if (_dirtySkinnedMeshRenderers.TryGetValue(r, out data) && data.dirtyBlendShapes.TryGetValue(index, out bs))
            {
                dirty = true;
                return bs.weight;
            }
            dirty = false;
            return r.GetBlendShapeWeight(index);
        }

        // ---------------------------------------------------------------- face window (no eye linking: it writes every mesh itself)

        internal bool kitIsFemale { get { return _target.isFemale; } }

        internal void KitFaceSetWeight(SkinnedMeshRenderer r, int index, float weight) { SetBlendShapeWeight(r, index, weight); }

        /// <summary>One shape back to its original weight, this mesh only.</summary>
        internal void KitFaceRelease(SkinnedMeshRenderer r, int index)
        {
            SkinnedMeshRendererData data;
            BlendShapeData bs;
            if (_dirtySkinnedMeshRenderers.TryGetValue(r, out data) == false || data.dirtyBlendShapes.TryGetValue(index, out bs) == false)
                return;
            r.TrySetBlendShapeWeight(index, bs.originalWeight);
            data.dirtyBlendShapes.Remove(index);
            if (data.dirtyBlendShapes.Count == 0)
                SetMeshRendererNotDirty(r);
        }

        /// <summary>HSPE's held weight, or false when it holds none (to spot edits made elsewhere).</summary>
        internal bool KitFaceHolds(SkinnedMeshRenderer r, int index, out float weight)
        {
            SkinnedMeshRendererData data;
            BlendShapeData bs;
            weight = 0f;
            if (_dirtySkinnedMeshRenderers.TryGetValue(r, out data) == false || data.dirtyBlendShapes.TryGetValue(index, out bs) == false)
                return false;
            weight = bs.weight;
            return true;
        }

        internal void KitRefresh() { RefreshSkinnedMeshRendererList(); }
        internal void KitResetAll() { ResetAll(); }

        // Joan's blend-shape presets (his files in Plugins\HSPE\BlendShapesPresets, whole-module save / load)
        internal static string kitPresetsFolder { get { return _presetsPath; } }

        internal static string[] KitPresets()
        {
            if (Directory.Exists(_presetsPath) == false)
                return new string[0];
            string[] files = Directory.GetFiles(_presetsPath, "*.xml");
            for (int i = 0; i < files.Length; i++)
                files[i] = Path.GetFileNameWithoutExtension(files[i]);
            System.Array.Sort(files, System.StringComparer.OrdinalIgnoreCase);
            return files;
        }

        internal void KitSavePreset(string name) { SavePreset(name + ".xml"); RefreshPresets(); }
        internal void KitLoadPreset(string name) { LoadPreset(name + ".xml"); }
        internal void KitDeletePreset(string name) { DeletePreset(name + ".xml"); }
    }
}
