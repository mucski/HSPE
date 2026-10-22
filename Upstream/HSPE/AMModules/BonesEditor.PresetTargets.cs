using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using Studio;
using UnityEngine;

namespace HSPE.AMModules
{
	public partial class BonesEditor
	{
		private sealed class BonePresetSource
		{
			public GenericOCITarget.Type type;
			public bool isFemale;
			public string bundle, asset, manifest, name;
		}

		private sealed class BonePresetLibraryEntry
		{
			public string path, error;
			public BonePresetSource source;
		}

		private bool _showAllBonePresets;
		private readonly Dictionary<string, BonePresetLibraryEntry> _bonePresetLibrary = new Dictionary<string, BonePresetLibraryEntry>(StringComparer.OrdinalIgnoreCase);

		private BonePresetSource CurrentBonePresetSource()
		{
			if (_target.type == GenericOCITarget.Type.Character)
			{
				bool female = _target.isFemale;
#if HONEYSELECT
				if (_target.ociChar != null && _target.ociChar.charInfo != null) female = _target.ociChar.charInfo.Sex == 1;
#endif
				return new BonePresetSource { type = GenericOCITarget.Type.Character, isFemale = female };
			}
#if HONEYSELECT
			if (_target.type == GenericOCITarget.Type.Item && _target.ociItem != null && _target.ociItem.itemInfo != null && Singleton<Studio.Info>.IsInstance())
			{
				Studio.Info.ItemLoadInfo info;
				if (Singleton<Studio.Info>.Instance.dicItemLoadInfo.TryGetValue(_target.ociItem.itemInfo.no, out info)
					&& info != null && ValidPresetSourceText(info.bundlePath, false) && ValidPresetSourceText(info.fileName, false)
					&& ValidPresetSourceText(info.manifest ?? "", true))
					return new BonePresetSource { type = GenericOCITarget.Type.Item, bundle = info.bundlePath.Replace('\\', '/'),
						asset = info.fileName, manifest = info.manifest ?? "", name = string.IsNullOrEmpty(info.name) || info.name.Trim().Length == 0 ? info.fileName : info.name.Trim() };
			}
#endif
			// A missing item record is not item zero, a scene name, or a guessed hierarchy identity.
			return null;
		}

		private static bool ValidPresetSourceText(string value, bool allowEmpty)
		{
			return value != null && value.Length <= 2048 && (allowEmpty || value.Trim().Length != 0);
		}

		private static bool BonePresetSourcesMatch(BonePresetSource a, BonePresetSource b)
		{
			if (a == null || b == null || a.type != b.type) return false;
			if (a.type == GenericOCITarget.Type.Character) return a.isFemale == b.isFemale;
			return a.type == GenericOCITarget.Type.Item
				&& string.Equals(a.bundle, b.bundle, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(a.asset, b.asset, StringComparison.Ordinal)
				&& string.Equals(a.manifest, b.manifest, StringComparison.Ordinal);
		}

		private static string BonePresetSourceName(BonePresetSource source)
		{
			if (source == null) return "Source unavailable";
			return source.type == GenericOCITarget.Type.Character ? (source.isFemale ? "Female character" : "Male character")
				: "Item: " + source.name;
		}

		private static XmlReader OpenBonePresetReader(string path)
		{
			if (!File.Exists(path)) throw new FileNotFoundException("Preset file was not found.", path);
			if (new FileInfo(path).Length > _maximumPresetFileSize) throw new InvalidDataException("The preset file is too large.");
			XmlReaderSettings settings = new XmlReaderSettings { ProhibitDtd = true, XmlResolver = null };
			return XmlReader.Create(path, settings);
		}

		private static BonePresetSource ReadBonePresetSource(XmlReader reader)
		{
			if (reader.MoveToContent() != XmlNodeType.Element || reader.Name != _bonePresetRootName)
				throw new InvalidDataException("This is not an HSPE bone preset.");
			if (reader.GetAttribute("version") != _bonePresetVersion)
				throw new InvalidDataException("Unsupported preset format. Recreate presets from earlier development builds.");
			string type = reader.GetAttribute("targetType");
			if (type == "Character")
			{
				string sex = reader.GetAttribute("sex");
				if (sex != "female" && sex != "male") throw new InvalidDataException("The character preset has no valid sex metadata.");
				return new BonePresetSource { type = GenericOCITarget.Type.Character, isFemale = sex == "female" };
			}
			if (type != "Item") throw new InvalidDataException("The preset has no valid target type.");
			string bundle = reader.GetAttribute("sourceBundle"), asset = reader.GetAttribute("sourceAsset"), manifest = reader.GetAttribute("sourceManifest");
			string name = reader.GetAttribute("sourceName") ?? asset;
			if (!ValidPresetSourceText(bundle, false) || !ValidPresetSourceText(asset, false)
				|| !ValidPresetSourceText(manifest, true) || !ValidPresetSourceText(name, false))
				throw new InvalidDataException("The item preset has incomplete or invalid model metadata.");
			return new BonePresetSource { type = GenericOCITarget.Type.Item, bundle = bundle.Replace('\\', '/'), asset = asset, manifest = manifest, name = name };
		}

		private static void WriteBonePresetSource(XmlWriter writer, BonePresetSource source)
		{
			writer.WriteAttributeString("targetType", source.type.ToString());
			if (source.type == GenericOCITarget.Type.Character) writer.WriteAttributeString("sex", source.isFemale ? "female" : "male");
			else
			{
				writer.WriteAttributeString("sourceBundle", source.bundle);
				writer.WriteAttributeString("sourceAsset", source.asset);
				writer.WriteAttributeString("sourceManifest", source.manifest);
				writer.WriteAttributeString("sourceName", source.name.Length > 2048 ? source.name.Substring(0, 2048) : source.name);
			}
		}

		private void RefreshBonePresetLibrary()
		{
			_bonePresetLibrary.Clear();
			foreach (string path in _bonePresetFiles)
			{
				BonePresetLibraryEntry entry = new BonePresetLibraryEntry { path = path };
				try { using (XmlReader reader = OpenBonePresetReader(path)) entry.source = ReadBonePresetSource(reader); }
				catch (Exception error) { entry.error = error.Message; }
				_bonePresetLibrary.Add(path, entry);
			}
		}

		private void UpdateBonePresetLibraryEntry(string path, BonePresetSource source, string error = null)
		{
			if (path == null) return;
			BonePresetLibraryEntry entry;
			if (!_bonePresetLibrary.TryGetValue(path, out entry)) return;
			entry.source = source;
			entry.error = error;
		}

		private List<BonePresetLibraryEntry> FilteredBonePresetFiles(string search)
		{
			BonePresetSource target = CurrentBonePresetSource();
			List<BonePresetLibraryEntry> result = new List<BonePresetLibraryEntry>();
			foreach (string path in _bonePresetFiles)
			{
				BonePresetLibraryEntry entry;
				if (!_bonePresetLibrary.TryGetValue(path, out entry)) continue;
				if ((_showAllBonePresets || BonePresetSourcesMatch(entry.source, target))
					&& Path.GetFileNameWithoutExtension(path).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) result.Add(entry);
			}
			return result;
		}

		private bool BonePresetAllowed(BonePresetData preset)
		{
			return preset != null && (_showAllBonePresets || BonePresetSourcesMatch(preset.source, CurrentBonePresetSource()));
		}

		private void EnforceBonePresetFilter()
		{
			if (_showAllBonePresets) return;
			bool cleared = false, blendChanged = false;
			if (_selectedBonePresetPath != null && !BonePresetAllowed(_selectedBonePresetData)) { ClearSelectedBonePreset(); cleared = true; }
			if (_blendA != null && !BonePresetAllowed(_blendA)) { _blendA = null; _blendAPath = null; blendChanged = true; }
			if (_blendB != null && !BonePresetAllowed(_blendB)) { _blendB = null; _blendBPath = null; blendChanged = true; }
			if (blendChanged) { CancelBoneBlend("Preview cancelled: preset sources no longer match the target."); RebuildBlendRows(); }
			if ((cleared || blendChanged) && !_bonePresetStatusIsError) SetBonePresetStatus("Selection cleared: its source does not match this target. Show all presets allows other sources.");
		}

		private void SetShowAllBonePresets(bool value)
		{
			if (_blendSession != null || value == _showAllBonePresets) return;
			_showAllBonePresets = value;
			_bonePresetLoadScroll = _blendLibraryScroll = Vector2.zero;
			EnforceBonePresetFilter();
			RefreshBonePresetPreview();
		}

		private void DrawShowAllBonePresets(Rect rect)
		{
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && _blendSession == null;
			bool show = GUI.Toggle(rect, _showAllBonePresets, new GUIContent("Show all presets",
				"Off: presets for " + BonePresetSourceName(CurrentBonePresetSource()) + ". On: inspect and intentionally reuse other sources. Locked during preview."));
			GUI.enabled = enabled;
			SetShowAllBonePresets(show);
		}

		private string BonePresetLibraryHint(BonePresetLibraryEntry entry)
		{
			if (entry.error != null) return entry.error;
			return "Source: " + BonePresetSourceName(entry.source)
				+ (BonePresetSourcesMatch(entry.source, CurrentBonePresetSource()) ? "." : ". Different source from this target.")
				+ " Selecting does not apply edits.";
		}

		private string EmptyBonePresetLibraryHint(string search)
		{
			if (_bonePresetFiles.Length == 0) return "No presets yet. Use Create preset to save your edits.";
			if (search.Length != 0) return "No matching presets. Clear the search or enable Show all presets.";
			return _showAllBonePresets ? "No presets available." : "No presets for this target. Create one or enable Show all presets.";
		}

		private static bool ValidPresetBonePath(string path)
		{
			if (string.IsNullOrEmpty(path) || path.Length > 4096) return false;
			string[] parts = path.Split('/');
			if (parts.Length > 256) return false;
			foreach (string part in parts) if (part.Length == 0 || part == "." || part == "..") return false;
			return true;
		}

		private Transform ResolvePresetBone(string path)
		{
			if (!ValidPresetBonePath(path)) return null;
			string[] parts = path.Split('/');
			Transform cursor = _parent.transform;
			foreach (string part in parts)
			{
				Transform match = null;
				for (int i = 0; i < cursor.childCount; ++i)
				{
					Transform child = cursor.GetChild(i);
					if (child.name != part) continue;
					if (match != null) return null;
					match = child;
				}
				if (match == null || _parent._childObjects.Contains(match.gameObject)) return null;
				PoseController owner = match.GetComponent<PoseController>();
				if (owner != null && owner != _parent) return null;
				cursor = match;
			}
			return cursor;
		}
	}
}
