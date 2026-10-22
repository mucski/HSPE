using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using Studio;
using ToolBox;
using ToolBox.Extensions;
using UnityEngine;

namespace HSPE.AMModules
{
	public partial class BonesEditor
	{
		private const string _bonePresetRootName = "hspeBonePreset";
		private const string _bonePresetVersion = "2";
		private const int _maximumPresetBones = 20000;
		private const long _maximumPresetFileSize = 16L * 1024L * 1024L;

		private static readonly string _bonePresetsPath = Path.Combine(
			Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), HSPE._name),
			"BonePresets");

		private readonly Dictionary<GameObject, BonePresetSelection> _bonePresetSelections = new Dictionary<GameObject, BonePresetSelection>();
		private bool _showBonePresetsWindow;
		private bool _confirmDeleteBonePreset;
		private string _bonePresetName = "";
		private string[] _bonePresetFiles = new string[0];
		private string _selectedBonePresetPath;
		private BonePresetData _selectedBonePresetData;
		private Vector2 _bonePresetSaveScroll;
		private Vector2 _bonePresetLoadScroll;
		private BonePresetLoadMode _bonePresetLoadMode = BonePresetLoadMode.PreserveExistingEdits;
		private string _bonePresetStatus = "";
		private bool _bonePresetStatusIsError;

		private enum BonePresetLoadMode
		{
			PreserveExistingEdits,
			OverwriteSavedEdits
		}

		private enum PresetChannelParseResult
		{
			Absent,
			Valid,
			Invalid
		}

		private sealed class BonePresetSelection
		{
			public bool position;
			public bool rotation;
			public bool scale;
			private bool _hadPosition;
			private bool _hadRotation;
			private bool _hadScale;

			public void Synchronize(TransformData data)
			{
				if (data.position.hasValue && !_hadPosition)
					position = true;
				if (data.rotation.hasValue && !_hadRotation)
					rotation = true;
				if (data.scale.hasValue && !_hadScale)
					scale = true;

				if (!data.position.hasValue)
					position = false;
				if (!data.rotation.hasValue)
					rotation = false;
				if (!data.scale.hasValue)
					scale = false;

				_hadPosition = data.position.hasValue;
				_hadRotation = data.rotation.hasValue;
				_hadScale = data.scale.hasValue;
			}

			public void SetAll(TransformData data, bool value)
			{
				position = data.position.hasValue && value;
				rotation = data.rotation.hasValue && value;
				scale = data.scale.hasValue && value;
			}

			public bool HasAnySelection
			{
				get { return position || rotation || scale; }
			}

			public bool HasAllAvailableChannels(TransformData data)
			{
				return (!data.position.hasValue || position)
					&& (!data.rotation.hasValue || rotation)
					&& (!data.scale.hasValue || scale);
			}
		}

		private sealed class BonePresetData
		{
			public BonePresetSource source;
			public readonly List<BonePresetEntry> bones = new List<BonePresetEntry>();
			public int invalidChannels;
		}

		private sealed class BonePresetEntry
		{
			public string path;
			public Vector3? position;
			public Quaternion? rotation;
			public Vector3? scale;
		}

		private sealed class ResolvedBonePresetEntry
		{
			public BonePresetEntry entry;
			public Transform target;
			public BlendChannels channels, written;
			public BoneResetSnapshot before;
		}

		private sealed class BonePresetApplyResult
		{
			public int appliedBones;
			public int appliedChannels;
			public int skippedDirtyChannels;
			public int missingBones;
			public int invalidChannels;
		}

		private sealed class BonePresetPreview
		{
			public int matchedBones;
			public int totalChannels;
			public int matchedChannels;
			public int conflictingChannels;
			public int missingBones;
			public int invalidChannels;

			public int ChannelsToApply(BonePresetLoadMode mode)
			{
				return mode == BonePresetLoadMode.PreserveExistingEdits
					? matchedChannels - conflictingChannels
					: matchedChannels;
			}
		}

		private void SynchronizeBonePresetSelections()
		{
			List<GameObject> staleBones = new List<GameObject>();
			foreach (KeyValuePair<GameObject, BonePresetSelection> pair in _bonePresetSelections)
			{
				if (pair.Key == null || !_dirtyBones.ContainsKey(pair.Key))
					staleBones.Add(pair.Key);
			}
			foreach (GameObject staleBone in staleBones)
				_bonePresetSelections.Remove(staleBone);

			foreach (KeyValuePair<GameObject, TransformData> pair in _dirtyBones)
			{
				if (pair.Key == null)
					continue;
				if (!_bonePresetSelections.TryGetValue(pair.Key, out BonePresetSelection selection))
				{
					selection = new BonePresetSelection();
					_bonePresetSelections.Add(pair.Key, selection);
				}
				selection.Synchronize(pair.Value);
			}
		}

		private List<KeyValuePair<GameObject, TransformData>> GetSortedDirtyBones()
		{
			List<KeyValuePair<GameObject, TransformData>> dirtyBones = new List<KeyValuePair<GameObject, TransformData>>();
			foreach (KeyValuePair<GameObject, TransformData> pair in _dirtyBones)
			{
				if (pair.Key != null && (pair.Value.position.hasValue || pair.Value.rotation.hasValue || pair.Value.scale.hasValue))
					dirtyBones.Add(pair);
			}
			dirtyBones.Sort(delegate(KeyValuePair<GameObject, TransformData> left, KeyValuePair<GameObject, TransformData> right)
			{
				string leftPath = left.Key.transform.GetPathFrom(_parent.transform);
				string rightPath = right.Key.transform.GetPathFrom(_parent.transform);
				return StringComparer.Ordinal.Compare(leftPath, rightPath);
			});
			return dirtyBones;
		}

		private void SetAllBonePresetSelections(List<KeyValuePair<GameObject, TransformData>> dirtyBones, bool value)
		{
			foreach (KeyValuePair<GameObject, TransformData> pair in dirtyBones)
				_bonePresetSelections[pair.Key].SetAll(pair.Value, value);
		}

		private int CountSelectedBonePresetChannels(List<KeyValuePair<GameObject, TransformData>> dirtyBones)
		{
			int count = 0;
			foreach (KeyValuePair<GameObject, TransformData> pair in dirtyBones)
			{
				BonePresetSelection selection = _bonePresetSelections[pair.Key];
				if (pair.Value.position.hasValue && selection.position)
					++count;
				if (pair.Value.rotation.hasValue && selection.rotation)
					++count;
				if (pair.Value.scale.hasValue && selection.scale)
					++count;
			}
			return count;
		}

		private void SaveBonePreset()
		{
			ClearBonePresetSaveFeedback();
			try
			{
				SynchronizeBonePresetSelections();
				List<KeyValuePair<GameObject, TransformData>> dirtyBones = GetSortedDirtyBones();
				int selectedChannels = CountSelectedBonePresetChannels(dirtyBones);
				if (selectedChannels == 0)
				{
					SetBonePresetError("Select at least one modified channel.");
					return;
				}

				string safeName = SanitizeBonePresetName(_bonePresetName);
				if (safeName.Length == 0)
				{
					SetBonePresetError("Enter a valid preset name.");
					return;
				}

				BonePresetSource source = CurrentBonePresetSource();
				if (source == null)
				{
					SetBonePresetError("Cannot identify this target's source model. No preset was saved.");
					return;
				}

				EnsureBonePresetsDirectory();
				string path = Path.Combine(_bonePresetsPath, safeName + ".xml");
				string temporaryPath = path + ".tmp";
				HashSet<string> writtenPaths = new HashSet<string>(StringComparer.Ordinal);
				try
				{
					XmlWriterSettings settings = new XmlWriterSettings();
					settings.Encoding = new UTF8Encoding(false);
					settings.Indent = true;
					using (XmlWriter writer = XmlWriter.Create(temporaryPath, settings))
					{
						writer.WriteStartDocument();
						writer.WriteStartElement(_bonePresetRootName);
						writer.WriteAttributeString("version", _bonePresetVersion);
						WriteBonePresetSource(writer, source);
						writer.WriteAttributeString("createdUtc", DateTime.UtcNow.ToString("o"));

						foreach (KeyValuePair<GameObject, TransformData> pair in dirtyBones)
						{
							BonePresetSelection selection = _bonePresetSelections[pair.Key];
							if (!selection.HasAnySelection)
								continue;

							string bonePath = pair.Key.transform.GetPathFrom(_parent.transform);
							if (bonePath.Length == 0)
								throw new InvalidOperationException("The edited object root cannot be stored as a bone preset entry.");
							if (!writtenPaths.Add(bonePath))
								throw new InvalidOperationException("Two modified bones resolve to the same path: " + bonePath);
							if (writtenPaths.Count > _maximumPresetBones)
								throw new InvalidDataException("Too many selected bones for one preset.");
							if (ResolvePresetBone(bonePath) != pair.Key.transform)
								throw new InvalidOperationException("This bone path is ambiguous or belongs to a separate Studio object: " + bonePath);
							writer.WriteStartElement("bone");
							writer.WriteAttributeString("path", bonePath);
							if (selection.position && pair.Value.position.hasValue)
								WriteVector3(writer, "pos", pair.Value.position.value);
							if (selection.rotation && pair.Value.rotation.hasValue)
								WriteQuaternion(writer, pair.Value.rotation.value);
							if (selection.scale && pair.Value.scale.hasValue)
								WriteVector3(writer, "scale", pair.Value.scale.value);
							writer.WriteEndElement();
						}

						writer.WriteEndElement();
						writer.WriteEndDocument();
					}

					if (new FileInfo(temporaryPath).Length > _maximumPresetFileSize)
						throw new InvalidDataException("The selected preset data is too large.");
					if (File.Exists(path))
						File.Replace(temporaryPath, path, null);
					else
						File.Move(temporaryPath, path);
				}
				finally
				{
					if (File.Exists(temporaryPath))
						File.Delete(temporaryPath);
				}

				_bonePresetName = safeName;
				RefreshBonePresets();
				SelectBonePreset(path, false);
				_lastSavedBonePresetName = safeName;
				_lastSavedBonePreset = _selectedBonePresetData;
				SetBonePresetStatus("Preset saved: \"" + safeName + "\" (" + selectedChannels + " selected value" + (selectedChannels == 1 ? ")." : "s)."));
			}
			catch (Exception e)
			{
				SetBonePresetError("Could not save the preset: " + e.Message, e);
			}
		}

		private static void WriteVector3(XmlWriter writer, string prefix, Vector3 value)
		{
			if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z))
				throw new InvalidDataException("A selected " + prefix + " channel contains a non-finite value.");
			writer.WriteAttributeString(prefix + "X", XmlConvert.ToString(value.x));
			writer.WriteAttributeString(prefix + "Y", XmlConvert.ToString(value.y));
			writer.WriteAttributeString(prefix + "Z", XmlConvert.ToString(value.z));
		}

		private static void WriteQuaternion(XmlWriter writer, Quaternion value)
		{
			if (!IsFinite(value.w) || !IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z))
				throw new InvalidDataException("A selected rotation channel contains a non-finite value.");
			float magnitudeSquared = value.w * value.w + value.x * value.x + value.y * value.y + value.z * value.z;
			if (magnitudeSquared <= 0.000001f)
				throw new InvalidDataException("A selected rotation channel contains an invalid zero quaternion.");
			writer.WriteAttributeString("rotW", XmlConvert.ToString(value.w));
			writer.WriteAttributeString("rotX", XmlConvert.ToString(value.x));
			writer.WriteAttributeString("rotY", XmlConvert.ToString(value.y));
			writer.WriteAttributeString("rotZ", XmlConvert.ToString(value.z));
		}

		private void SelectBonePreset(string path, bool updateStatus = true)
		{
			_selectedBonePresetPath = path;
			_selectedBonePresetData = null;
			_confirmDeleteBonePreset = false;
			try
			{
				_selectedBonePresetData = ReadBonePreset(path);
				UpdateBonePresetLibraryEntry(path, _selectedBonePresetData.source);
				if (!BonePresetAllowed(_selectedBonePresetData))
				{
					ClearSelectedBonePreset();
					SetBonePresetError("This preset is for a different target. Enable Show all presets to inspect and reuse it.");
					return;
				}
				if (updateStatus)
					SetBonePresetStatus("Selected \"" + Path.GetFileNameWithoutExtension(path) + "\". No changes have been applied.");
			}
			catch (Exception e)
			{
				UpdateBonePresetLibraryEntry(path, null, e.Message);
				SetBonePresetError("Could not inspect the preset: " + e.Message, e);
			}
		}

		private BonePresetPreview PreviewBonePreset(BonePresetData preset)
		{
			BonePresetPreview preview = new BonePresetPreview();
			preview.invalidChannels = preset.invalidChannels;
			foreach (BonePresetEntry entry in preset.bones)
			{
				int entryChannels = (entry.position.HasValue ? 1 : 0)
					+ (entry.rotation.HasValue ? 1 : 0)
					+ (entry.scale.HasValue ? 1 : 0);
				preview.totalChannels += entryChannels;

				Transform target = ResolvePresetBone(entry.path);
				if (target == null)
				{
					++preview.missingBones;
					continue;
				}

				++preview.matchedBones;
				TransformData currentData;
				_dirtyBones.TryGetValue(target.gameObject, out currentData);
				if (entry.position.HasValue)
				{
					++preview.matchedChannels;
					if (currentData != null && currentData.position.hasValue)
						++preview.conflictingChannels;
				}
				if (entry.rotation.HasValue)
				{
					++preview.matchedChannels;
					if (currentData != null && currentData.rotation.hasValue)
						++preview.conflictingChannels;
				}
				if (entry.scale.HasValue)
				{
					++preview.matchedChannels;
					if (currentData != null && currentData.scale.hasValue)
						++preview.conflictingChannels;
				}
			}
			return preview;
		}

		private void LoadBonePreset(string path)
		{
			try
			{
				// Apply the inspected snapshot; an external file edit needs an explicit Refresh.
				BonePresetData preset = _selectedBonePresetData != null && string.Equals(path, _selectedBonePresetPath, StringComparison.OrdinalIgnoreCase)
					? _selectedBonePresetData : ReadBonePreset(path);
				if (!BonePresetAllowed(preset))
				{
					SetBonePresetError("No edits applied: this preset is for a different target. Enable Show all presets to reuse it.");
					return;
				}
				BonePresetApplyResult result = ApplyBonePreset(preset, _bonePresetLoadMode);
				string status = "Applied " + result.appliedChannels + " channel" + (result.appliedChannels == 1 ? "" : "s")
					+ " on " + result.appliedBones + " bone" + (result.appliedBones == 1 ? "" : "s") + ".";
				if (result.skippedDirtyChannels != 0)
					status += " Skipped " + result.skippedDirtyChannels + " existing dirty channel" + (result.skippedDirtyChannels == 1 ? "." : "s.");
				if (result.missingBones != 0)
					status += " Missing bones: " + result.missingBones + ".";
				if (result.invalidChannels != 0)
					status += " Invalid channels ignored: " + result.invalidChannels + ".";
				SetBonePresetStatus(status);
				_confirmDeleteBonePreset = false;
				SynchronizeBonePresetSelections();
			}
			catch (Exception e)
			{
				SetBonePresetError("Could not load the preset: " + e.Message, e);
			}
		}

		private BonePresetData ReadBonePreset(string path)
		{
			BonePresetData preset = new BonePresetData();
			XmlDocument document = new XmlDocument();
			document.XmlResolver = null;
			using (XmlReader reader = OpenBonePresetReader(path))
			{
				preset.source = ReadBonePresetSource(reader);
				document.Load(reader);
			}

			XmlElement root = document.DocumentElement;
			HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
			int processedBoneNodes = 0;
			foreach (XmlNode node in root.ChildNodes)
			{
				if (node.NodeType != XmlNodeType.Element || node.Name != "bone")
					continue;
				++processedBoneNodes;
				if (processedBoneNodes > _maximumPresetBones)
					throw new InvalidDataException("The preset contains too many bones.");

				XmlAttribute pathAttribute = node.Attributes["path"];
				if (pathAttribute == null || !ValidPresetBonePath(pathAttribute.Value))
				{
					++preset.invalidChannels;
					continue;
				}
				if (!paths.Add(pathAttribute.Value))
					throw new InvalidDataException("The preset contains a duplicate bone path: " + pathAttribute.Value);

				BonePresetEntry entry = new BonePresetEntry();
				entry.path = pathAttribute.Value;
				Vector3 vector;
				Quaternion quaternion;
				PresetChannelParseResult channelResult = TryReadVector3(node, "pos", out vector);
				if (channelResult == PresetChannelParseResult.Valid)
					entry.position = vector;
				else if (channelResult == PresetChannelParseResult.Invalid)
					++preset.invalidChannels;

				channelResult = TryReadQuaternion(node, out quaternion);
				if (channelResult == PresetChannelParseResult.Valid)
					entry.rotation = quaternion;
				else if (channelResult == PresetChannelParseResult.Invalid)
					++preset.invalidChannels;

				channelResult = TryReadVector3(node, "scale", out vector);
				if (channelResult == PresetChannelParseResult.Valid)
					entry.scale = vector;
				else if (channelResult == PresetChannelParseResult.Invalid)
					++preset.invalidChannels;

				if (entry.position.HasValue || entry.rotation.HasValue || entry.scale.HasValue)
					preset.bones.Add(entry);
			}
			return preset;
		}

		private static PresetChannelParseResult TryReadVector3(XmlNode node, string prefix, out Vector3 value)
		{
			value = Vector3.zero;
			XmlAttribute x = node.Attributes[prefix + "X"];
			XmlAttribute y = node.Attributes[prefix + "Y"];
			XmlAttribute z = node.Attributes[prefix + "Z"];
			if (x == null && y == null && z == null)
				return PresetChannelParseResult.Absent;
			if (x == null || y == null || z == null)
				return PresetChannelParseResult.Invalid;

			try
			{
				value.x = XmlConvert.ToSingle(x.Value);
				value.y = XmlConvert.ToSingle(y.Value);
				value.z = XmlConvert.ToSingle(z.Value);
				return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z)
					? PresetChannelParseResult.Valid
					: PresetChannelParseResult.Invalid;
			}
			catch (FormatException)
			{
				return PresetChannelParseResult.Invalid;
			}
			catch (OverflowException)
			{
				return PresetChannelParseResult.Invalid;
			}
		}

		private static PresetChannelParseResult TryReadQuaternion(XmlNode node, out Quaternion value)
		{
			value = Quaternion.identity;
			XmlAttribute w = node.Attributes["rotW"];
			XmlAttribute x = node.Attributes["rotX"];
			XmlAttribute y = node.Attributes["rotY"];
			XmlAttribute z = node.Attributes["rotZ"];
			if (w == null && x == null && y == null && z == null)
				return PresetChannelParseResult.Absent;
			if (w == null || x == null || y == null || z == null)
				return PresetChannelParseResult.Invalid;

			try
			{
				value.w = XmlConvert.ToSingle(w.Value);
				value.x = XmlConvert.ToSingle(x.Value);
				value.y = XmlConvert.ToSingle(y.Value);
				value.z = XmlConvert.ToSingle(z.Value);
				if (!IsFinite(value.w) || !IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z))
					return PresetChannelParseResult.Invalid;
				float magnitudeSquared = value.w * value.w + value.x * value.x + value.y * value.y + value.z * value.z;
				return magnitudeSquared > 0.000001f ? PresetChannelParseResult.Valid : PresetChannelParseResult.Invalid;
			}
			catch (FormatException)
			{
				return PresetChannelParseResult.Invalid;
			}
			catch (OverflowException)
			{
				return PresetChannelParseResult.Invalid;
			}
		}

		private BonePresetApplyResult ApplyBonePreset(BonePresetData preset, BonePresetLoadMode mode)
		{
			CancelBoneBlend("Preview cancelled: a preset was loaded.");
			BonePresetApplyResult result = new BonePresetApplyResult();
			result.invalidChannels = preset.invalidChannels;
			List<ResolvedBonePresetEntry> resolvedEntries = new List<ResolvedBonePresetEntry>();
			foreach (BonePresetEntry entry in preset.bones)
			{
				Transform target = ResolvePresetBone(entry.path);
				if (target == null)
				{
					++result.missingBones;
					continue;
				}
				ResolvedBonePresetEntry resolved = new ResolvedBonePresetEntry();
				resolved.entry = entry;
				resolved.target = target;
				TransformData currentData;
				_dirtyBones.TryGetValue(target.gameObject, out currentData);
				BlendChannels dirty = currentData == null ? BlendChannels.None : DirtyChannels(currentData);
				if (entry.position.HasValue)
				{
					if (ShouldApplyPresetChannel(mode, (dirty & BlendChannels.Position) != 0)) resolved.channels |= BlendChannels.Position;
					else ++result.skippedDirtyChannels;
				}
				if (entry.rotation.HasValue)
				{
					if (ShouldApplyPresetChannel(mode, (dirty & BlendChannels.Rotation) != 0)) resolved.channels |= BlendChannels.Rotation;
					else ++result.skippedDirtyChannels;
				}
				if (entry.scale.HasValue)
				{
					if (ShouldApplyPresetChannel(mode, (dirty & BlendChannels.Scale) != 0)) resolved.channels |= BlendChannels.Scale;
					else ++result.skippedDirtyChannels;
				}
				if (resolved.channels == BlendChannels.None) continue;
				// Capture every affected channel and FK guide before the first write. A failing
				// callback must not leave earlier bones applied, nor invent new reset baselines.
				resolved.before = new BoneResetSnapshot {
					bone = target.gameObject, data = currentData == null ? new TransformData() : new TransformData(currentData),
					localPosition = target.localPosition, localRotation = target.localRotation, localScale = target.localScale
				};
				OCIChar.BoneInfo info;
				if ((resolved.channels & BlendChannels.Rotation) != 0 && _target.fkEnabled && _target.fkObjects != null
					&& _target.fkObjects.TryGetValue(target.gameObject, out info) && info.active && info.guideObject != null)
				{
					resolved.before.fkChange = info.guideObject.changeAmount;
					if (resolved.before.fkChange != null) resolved.before.fkRotation = resolved.before.fkChange.rot;
				}
				resolvedEntries.Add(resolved);
			}

			try
			{
				foreach (ResolvedBonePresetEntry resolved in resolvedEntries)
				{
					if ((resolved.channels & BlendChannels.Position) != 0)
					{
						resolved.written |= BlendChannels.Position;
						SetBoneDirty(resolved.target.gameObject).SetPosition(resolved.entry.position.Value, resolved.target);
						++result.appliedChannels;
					}
					if ((resolved.channels & BlendChannels.Rotation) != 0)
					{
						resolved.written |= BlendChannels.Rotation;
						SetBoneDirty(resolved.target.gameObject).SetRotation(resolved.entry.rotation.Value, resolved.target);
						if (resolved.before.fkChange != null) resolved.before.fkChange.rot = resolved.entry.rotation.Value.eulerAngles;
						++result.appliedChannels;
					}
					if ((resolved.channels & BlendChannels.Scale) != 0)
					{
						resolved.written |= BlendChannels.Scale;
						SetBoneDirty(resolved.target.gameObject).SetScale(resolved.entry.scale.Value, resolved.target);
						++result.appliedChannels;
					}
					++result.appliedBones;
				}
				if (result.appliedChannels != 0) ApplyBoneManualCorrection();
				return result;
			}
			catch (Exception error)
			{
				foreach (ResolvedBonePresetEntry resolved in resolvedEntries)
				{
					if (resolved.written == BlendChannels.None || resolved.target == null) continue;
					BoneResetSnapshot before = resolved.before;
					TransformData data = SetBoneDirty(before.bone);
					bool p = (resolved.written & BlendChannels.Position) != 0, r = (resolved.written & BlendChannels.Rotation) != 0, s = (resolved.written & BlendChannels.Scale) != 0;
					// Restore only channels owned by this transaction; unrelated callback edits survive.
					if (p) { data.position = before.data.position; data.originalPosition = before.data.originalPosition; }
					if (r) { data.rotation = before.data.rotation; data.originalRotation = before.data.originalRotation; }
					if (s) { data.scale = before.data.scale; data.originalScale = before.data.originalScale; }
					if (DirtyChannels(data) == BlendChannels.None) _dirtyBones.Remove(before.bone);
					try { if (r && before.fkChange != null) before.fkChange.rot = before.fkRotation; }
					catch (Exception rollback) { Debug.LogError("HSPE: FK callback threw while rolling back a preset load: " + rollback); }
					if (p) resolved.target.localPosition = before.localPosition;
					if (r) resolved.target.localRotation = before.localRotation;
					if (s) resolved.target.localScale = before.localScale;
				}
				throw new InvalidOperationException("Preset changes were rolled back; previous bone edits were retained. " + error.Message, error);
			}
		}

		private static bool ShouldApplyPresetChannel(BonePresetLoadMode mode, bool destinationIsDirty)
		{
			return mode == BonePresetLoadMode.OverwriteSavedEdits || !destinationIsDirty;
		}

		private void ApplyPresetRotationToActiveFK(Transform bone, Quaternion rotation)
		{
			if (!_target.fkEnabled)
				return;
			if (_target.fkObjects.TryGetValue(bone.gameObject, out OCIChar.BoneInfo info)
				&& info.active && info.guideObject != null)
				info.guideObject.changeAmount.rot = rotation.eulerAngles;
		}

		private void RefreshBonePresets()
		{
			ClearBonePresetSaveFeedback();
			try
			{
				if (!Directory.Exists(_bonePresetsPath))
				{
					_bonePresetFiles = new string[0];
					_bonePresetLibrary.Clear();
					ClearSelectedBonePreset();
					ReloadBlendSources();
					return;
				}
				_bonePresetFiles = Directory.GetFiles(_bonePresetsPath, "*.xml", SearchOption.TopDirectoryOnly);
				Array.Sort(_bonePresetFiles, StringComparer.OrdinalIgnoreCase);
				RefreshBonePresetLibrary();
				if (_selectedBonePresetPath != null)
				{
					string selectedPath = null;
					foreach (string presetPath in _bonePresetFiles)
					{
						if (string.Equals(presetPath, _selectedBonePresetPath, StringComparison.OrdinalIgnoreCase))
						{
							selectedPath = presetPath;
							break;
						}
					}
					if (selectedPath == null)
						ClearSelectedBonePreset();
					else
						SelectBonePreset(selectedPath, false);
				}
				ReloadBlendSources();
			}
			catch (Exception e)
			{
				_bonePresetFiles = new string[0];
				_bonePresetLibrary.Clear();
				ClearSelectedBonePreset();
				_blendA = _blendB = null; _blendAPath = _blendBPath = null;
				RebuildBlendRows();
				SetBonePresetError("Could not read the preset folder: " + e.Message, e);
			}
		}

		private void ClearSelectedBonePreset()
		{
			_selectedBonePresetPath = null;
			_selectedBonePresetData = null;
			_confirmDeleteBonePreset = false;
		}

		private bool BonePresetNameExists(string name)
		{
			if (name.Length == 0)
				return false;
			foreach (string path in _bonePresetFiles)
			{
				if (string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase))
					return true;
			}
			return false;
		}

		private void DeleteBonePreset(string path)
		{
			try
			{
				string displayName = Path.GetFileNameWithoutExtension(path);
				if (File.Exists(path))
					File.Delete(path);
				ClearSelectedBonePreset();
				RefreshBonePresets();
				SetBonePresetStatus("Deleted \"" + displayName + "\".");
			}
			catch (Exception e)
			{
				SetBonePresetError("Could not delete the preset: " + e.Message, e);
			}
		}

		private static string SanitizeBonePresetName(string name)
		{
			string result = name.Trim();
			result = string.Join("_", result.Split(Path.GetInvalidFileNameChars()));
			if (result.EndsWith(".", StringComparison.Ordinal))
				result = result.TrimEnd('.');
			if (result.Length > 120)
				result = result.Substring(0, 120).TrimEnd();
			return result;
		}

		private static bool IsFinite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}

		private static void EnsureBonePresetsDirectory()
		{
			if (!Directory.Exists(_bonePresetsPath))
				Directory.CreateDirectory(_bonePresetsPath);
		}

		private void SetBonePresetStatus(string status)
		{
			_bonePresetStatus = status;
			_bonePresetStatusIsError = false;
		}

		private void SetBonePresetError(string status, Exception exception = null)
		{
			_bonePresetStatus = status;
			_bonePresetStatusIsError = true;
			if (exception != null)
				UnityEngine.Debug.LogError("HSPE: " + status + "\n" + exception);
		}
	}
}
