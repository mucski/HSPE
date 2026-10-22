#if HONEYSELECT
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Studio;
using ToolBox;
using UnityEngine;

namespace HSPE.AMModules
{
	public partial class BonesEditor
	{
		internal bool saveBonesOnCard;
		internal int boneStateGeneration;
		internal long lastCardOperation;
		private string _boneCardStatus = "";
		private bool _boneCardError;
		private GUIStyle _boneCardLabel;
		private GUISkin _boneCardSkin;

		internal sealed class CardBoneEntry
		{
			internal string path;
			internal SceneBonePart[] scenePath; // Only scene v2 uses structured identity; cards stay portable v1.
			internal Vector3? position, scale, originalPosition, originalScale;
			internal Quaternion? rotation, originalRotation;
		}

		internal sealed class CardBoneState
		{
			internal bool saveOnCard;
			internal readonly List<CardBoneEntry> bones = new List<CardBoneEntry>();
		}

		private void DrawBoneCardBar()
		{
			if (_target.type != GenericOCITarget.Type.Character) return;
			Rect area = GUILayoutUtility.GetRect(0f, 46f, GUILayout.ExpandWidth(true));
			if (_boneCardSkin != GUI.skin)
			{
				_boneCardSkin = GUI.skin;
				_boneCardLabel = new GUIStyle(GUI.skin.label) { clipping = TextClipping.Clip, wordWrap = false, alignment = TextAnchor.MiddleLeft };
			}
			GUI.Box(area, GUIContent.none);
			bool selected = GUI.Toggle(new Rect(area.x + 5f, area.y + 2f, area.width - 10f, 22f), saveBonesOnCard,
				new GUIContent("Save current settings on card", "For this character: save ALL HSPE-modified bone position, rotation and scale channels, even when HSPE is disabled. No native-only FK pose or other HSPE modules. Unchecked: omit bone data on the next card save. During any blend preview, cards save without HSPE bone data; this checkbox stays unchanged."));
			if (selected != saveBonesOnCard) { saveBonesOnCard = selected; _boneCardStatus = ""; _boneCardError = false; }
			Color color = GUI.color;
			if (_boneCardError) GUI.color = ColorPresets.Tomato;
			string status = _boneCardStatus.Length != 0 ? _boneCardStatus
				: saveBonesOnCard ? "All modified bone channels included." : "HSPE bone edits omitted from cards.";
			string label = status;
			if (_boneCardLabel.CalcSize(new GUIContent(label)).x > area.width - 10f)
			{
				while (label.Length > 0 && _boneCardLabel.CalcSize(new GUIContent(label + "...")).x > area.width - 10f) label = label.Substring(0, label.Length - 1);
				label += "...";
			}
			GUI.Label(new Rect(area.x + 5f, area.y + 24f, area.width - 10f, 19f), new GUIContent(label, status), _boneCardLabel);
			GUI.color = color;
		}

		internal void SetBoneCardStatus(string message, bool error)
		{
			_boneCardStatus = message;
			_boneCardError = error;
			if (error) Debug.LogWarning("HSPE cards: " + message);
			else if (VerboseBlendLogs) Debug.Log("HSPE cards: " + message);
		}

		internal CardBoneState CaptureCardBones(bool scene)
		{
			CardBoneState state = new CardBoneState { saveOnCard = saveBonesOnCard };
			HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
			foreach (KeyValuePair<GameObject, TransformData> pair in _dirtyBones)
			{
				if (pair.Key == null) continue;
				TransformData data = pair.Value;
				if (!data.position.hasValue && !data.rotation.hasValue && !data.scale.hasValue) continue;
				string path = CardBonePath(pair.Key.transform);
				if (!paths.Add(path)) throw new InvalidDataException("Ambiguous modified bone path: " + path);
				CardBoneEntry entry = CaptureBoneValues(data, scene);
				entry.path = path;
				ValidateCardEntry(entry);
				state.bones.Add(entry);
				if (state.bones.Count > _maximumPresetBones) throw new InvalidDataException("Too many modified bones for a card.");
			}
			state.bones.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
			return state;
		}

		private static void ValidateCardPath(string path)
		{
			if (string.IsNullOrEmpty(path) || path.Length > 4096) throw new InvalidDataException("Invalid bone path length.");
			string[] parts = path.Split('/');
			if (parts.Length > 256) throw new InvalidDataException("Bone hierarchy is too deep.");
			foreach (string part in parts)
				if (part.Length == 0 || part == "." || part == "..") throw new InvalidDataException("Invalid bone path: " + path);
		}

		private string CardBonePath(Transform bone)
		{
			List<string> parts = new List<string>();
			Transform cursor = bone;
			while (cursor != null && cursor != _parent.transform)
			{
				if (cursor.name.IndexOf('/') >= 0) throw new InvalidDataException("A modified bone name contains '/': " + cursor.name);
				parts.Add(cursor.name);
				cursor = cursor.parent;
			}
			if (cursor == null || parts.Count == 0) throw new InvalidDataException("Only bones inside this character can be exported; the Studio object root cannot be a card bone.");
			parts.Reverse();
			string path = string.Join("/", parts.ToArray());
			ValidateCardPath(path);
			if (ResolveCardBone(path) != bone) throw new InvalidDataException("Modified bone is ambiguous or belongs to a separate Studio object: " + path);
			return path;
		}

		private Transform ResolveCardBone(string path)
		{
			Transform cursor = _parent.transform;
			foreach (string part in path.Split('/'))
			{
				Transform match = null;
				for (int i = 0; i < cursor.childCount; ++i)
				{
					Transform child = cursor.GetChild(i);
					if (child.name != part) continue;
					if (match != null) return null; // Never guess between identically named siblings.
					match = child;
				}
				if (match == null || _parent._childObjects.Contains(match.gameObject)) return null;
				PoseController owner = match.GetComponent<PoseController>();
				if (owner != null && owner != _parent) return null;
				cursor = match;
			}
			return cursor;
		}

		private static void ValidateCardEntry(CardBoneEntry entry)
		{
			ValidateCardPath(entry.path);
			ValidateBoneValues(entry);
		}

		private static CardBoneEntry CaptureBoneValues(TransformData data, bool baselines)
		{
			CardBoneEntry entry = new CardBoneEntry();
			if (data.position.hasValue) entry.position = data.position.value;
			if (data.rotation.hasValue) entry.rotation = data.rotation.value;
			if (data.scale.hasValue) entry.scale = data.scale.value;
			if (baselines)
			{
				if (entry.position.HasValue && data.originalPosition.hasValue) entry.originalPosition = data.originalPosition.value;
				if (entry.rotation.HasValue && data.originalRotation.hasValue) entry.originalRotation = data.originalRotation.value;
				if (entry.scale.HasValue && data.originalScale.hasValue) entry.originalScale = data.originalScale.value;
			}
			return entry;
		}

		private static void WriteBoneChannels(XmlTextWriter writer, CardBoneEntry entry, bool baseline)
		{
			Vector3? position = baseline ? entry.originalPosition : entry.position, scale = baseline ? entry.originalScale : entry.scale;
			Quaternion? rotation = baseline ? entry.originalRotation : entry.rotation;
			if (position.HasValue) WriteVector3(writer, "pos", position.Value);
			if (rotation.HasValue) WriteQuaternion(writer, rotation.Value);
			if (scale.HasValue) WriteVector3(writer, "scale", scale.Value);
		}

		private static void ValidateBoneValues(CardBoneEntry entry)
		{
			if (entry.position.HasValue && !FiniteVector(entry.position.Value) || entry.scale.HasValue && !FiniteVector(entry.scale.Value)
				|| entry.originalPosition.HasValue && !FiniteVector(entry.originalPosition.Value) || entry.originalScale.HasValue && !FiniteVector(entry.originalScale.Value))
				throw new InvalidDataException("Non-finite bone value: " + entry.path);
			if (entry.rotation.HasValue)
			{
				Quaternion value = entry.rotation.Value;
				if (!NormalizeBlendRotation(ref value)) throw new InvalidDataException("Invalid bone rotation: " + entry.path);
				entry.rotation = value;
			}
			if (entry.originalRotation.HasValue)
			{
				Quaternion value = entry.originalRotation.Value;
				if (!NormalizeBlendRotation(ref value)) throw new InvalidDataException("Invalid bone reset rotation: " + entry.path);
				entry.originalRotation = value;
			}
			if (!entry.position.HasValue && !entry.rotation.HasValue && !entry.scale.HasValue) throw new InvalidDataException("Empty bone entry: " + entry.path);
		}

		internal static string EncodeCardBones(CardBoneState state, bool scene)
		{
			using (StringWriter text = new StringWriter())
			using (XmlTextWriter writer = new XmlTextWriter(text))
			{
				writer.WriteStartElement(scene ? "boneSceneState" : "boneCard");
				writer.WriteAttributeString("version", "1");
				writer.WriteAttributeString("saveOnCard", XmlConvert.ToString(state.saveOnCard));
				foreach (CardBoneEntry entry in state.bones)
				{
					ValidateCardEntry(entry);
					writer.WriteStartElement("bone");
					writer.WriteAttributeString("path", entry.path);
					if (entry.position.HasValue) WriteVector3(writer, "pos", entry.position.Value);
					if (entry.rotation.HasValue) WriteQuaternion(writer, entry.rotation.Value);
					if (entry.scale.HasValue) WriteVector3(writer, "scale", entry.scale.Value);
					if (scene)
					{
						writer.WriteStartElement("baseline");
						if (entry.originalPosition.HasValue) WriteVector3(writer, "pos", entry.originalPosition.Value);
						if (entry.originalRotation.HasValue) WriteQuaternion(writer, entry.originalRotation.Value);
						if (entry.originalScale.HasValue) WriteVector3(writer, "scale", entry.originalScale.Value);
						writer.WriteEndElement();
					}
					writer.WriteEndElement();
				}
				writer.WriteEndElement();
				writer.Flush();
				string result = text.ToString();
				if (Encoding.UTF8.GetByteCount(result) > _maximumPresetFileSize) throw new InvalidDataException("HSPE bone data is too large.");
				return result;
			}
		}

		internal static CardBoneState DecodeCardBones(XmlNode node, bool scene)
		{
			if (node == null || node.Name != (scene ? "boneSceneState" : "boneCard") || node.Attributes == null
				|| node.Attributes["version"] == null || node.Attributes["version"].Value != "1") throw new InvalidDataException("Unsupported HSPE bone data version.");
			string xml = node.OuterXml;
			if (xml.Length > _maximumPresetFileSize || Encoding.UTF8.GetByteCount(xml) > _maximumPresetFileSize || node.ChildNodes.Count > _maximumPresetBones)
				throw new InvalidDataException("HSPE bone data exceeds safety limits.");
			if (node.Attributes["saveOnCard"] == null) throw new InvalidDataException("Missing card export preference.");
			CardBoneState state = new CardBoneState { saveOnCard = XmlConvert.ToBoolean(node.Attributes["saveOnCard"].Value) };
			if (!scene && !state.saveOnCard) throw new InvalidDataException("A portable HSPE snapshot must be explicitly enabled.");
			HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
			foreach (XmlNode child in node.ChildNodes)
			{
				if (child.NodeType != XmlNodeType.Element) continue;
				if (child.Name != "bone" || child.Attributes["path"] == null) throw new InvalidDataException("Invalid HSPE bone entry.");
				CardBoneEntry entry = new CardBoneEntry { path = child.Attributes["path"].Value };
				if (!paths.Add(entry.path)) throw new InvalidDataException("Duplicate bone path: " + entry.path);
				ReadCardChannels(child, entry, false);
				bool hadBaseline = false;
				foreach (XmlNode baseline in child.ChildNodes)
				{
					if (baseline.NodeType != XmlNodeType.Element) continue;
					if (!scene || baseline.Name != "baseline" || hadBaseline) throw new InvalidDataException("Invalid bone baseline.");
					hadBaseline = true;
					ReadCardChannels(baseline, entry, true);
				}
				ValidateCardEntry(entry);
				state.bones.Add(entry);
			}
			return state;
		}

		private static void ReadCardChannels(XmlNode node, CardBoneEntry entry, bool baseline)
		{
			Vector3 vector;
			Quaternion rotation;
			PresetChannelParseResult p = TryReadVector3(node, "pos", out vector);
			if (p == PresetChannelParseResult.Valid) { if (baseline) entry.originalPosition = vector; else entry.position = vector; }
			PresetChannelParseResult r = TryReadQuaternion(node, out rotation);
			if (r == PresetChannelParseResult.Valid) { if (baseline) entry.originalRotation = rotation; else entry.rotation = rotation; }
			PresetChannelParseResult s = TryReadVector3(node, "scale", out vector);
			if (s == PresetChannelParseResult.Valid) { if (baseline) entry.originalScale = vector; else entry.scale = vector; }
			if (p == PresetChannelParseResult.Invalid || r == PresetChannelParseResult.Invalid || s == PresetChannelParseResult.Invalid)
				throw new InvalidDataException("Incomplete or invalid bone channels: " + entry.path);
		}

		internal bool ApplyCardBones(CardBoneState state, bool scene)
		{
			// Resolve and capture all affected objects before writing anything. Original references also own rollback.
			List<BoneResetSnapshot> snapshots = new List<BoneResetSnapshot>();
			List<CardBoneEntry> entries = new List<CardBoneEntry>();
			Dictionary<GameObject, TransformData> before = _dirtyBones;
			int missing = 0;
			try
			{
				foreach (CardBoneEntry entry in state.bones)
				{
					bool structured = scene && entry.scenePath != null;
					if (structured) { ScenePathKey(entry.scenePath); ValidateBoneValues(entry); }
					else ValidateCardEntry(entry);
					Transform bone = structured ? ResolveSceneBone(entry.scenePath) : ResolveCardBone(entry.path);
					if (bone == null) { ++missing; continue; }
					BoneResetSnapshot snapshot = new BoneResetSnapshot { bone = bone.gameObject, localPosition = bone.localPosition, localRotation = bone.localRotation, localScale = bone.localScale };
					OCIChar.BoneInfo info;
					if (!scene && entry.rotation.HasValue && _target.fkObjects != null && _target.fkObjects.TryGetValue(bone.gameObject, out info)
						&& info.guideObject != null && info.guideObject.changeAmount != null)
					{
						snapshot.fkChange = info.guideObject.changeAmount;
						snapshot.fkRotation = snapshot.fkChange.rot;
					}
					snapshots.Add(snapshot);
					entries.Add(entry);
				}
				_dirtyBones = new Dictionary<GameObject, TransformData>();
				for (int i = 0; i < entries.Count; ++i)
				{
					CardBoneEntry entry = entries[i];
					BoneResetSnapshot snapshot = snapshots[i];
					Transform bone = snapshot.bone.transform;
					TransformData data = SetBoneDirty(snapshot.bone);
					data.SetData(bone, entry.position, entry.rotation, entry.scale);
					if (scene)
					{
						if (entry.originalPosition.HasValue) data.originalPosition = entry.originalPosition.Value;
						if (entry.originalRotation.HasValue) data.originalRotation = entry.originalRotation.Value;
						if (entry.originalScale.HasValue) data.originalScale = entry.originalScale.Value;
					}
					else if (snapshot.fkChange != null) snapshot.fkChange.rot = entry.rotation.Value.eulerAngles;
				}
				ApplyBoneManualCorrection();
				saveBonesOnCard = state.saveOnCard;
				_bonePresetSelections.Clear();
				_blendResultSelection = null;
				_lastShouldSaveValue = false;
				_oldFKRotationValue = null;
				_oldFKTwinRotationValue = null;
				SetBoneCardStatus((scene ? "Scene" : "Card") + ": restored " + entries.Count + " modified bones." + (missing == 0 ? "" : " Skipped " + missing + " missing / ambiguous bones."), missing != 0);
				return true;
			}
			catch (Exception error)
			{
				_dirtyBones = before;
				foreach (BoneResetSnapshot snapshot in snapshots)
				{
					if (snapshot.bone == null) continue;
					try { if (snapshot.fkChange != null) snapshot.fkChange.rot = snapshot.fkRotation; }
					catch (Exception rollback) { Debug.LogError("HSPE card FK rollback: " + rollback); }
					snapshot.bone.transform.localPosition = snapshot.localPosition;
					snapshot.bone.transform.localRotation = snapshot.localRotation;
					snapshot.bone.transform.localScale = snapshot.localScale;
				}
				SetBoneCardStatus("Bone data could not be applied; no partial bone import was kept. " + error.Message, true);
				return false;
			}
		}

		internal bool RetireBonesForCharacterReplacement()
		{
			++boneStateGeneration;
			if (!ResetAllBoneEdits()) return false;
			saveBonesOnCard = false;
			return true;
		}

		private void ClearBonesForAuthoritativeLoad()
		{
			// Not the user-facing Reset All. Native scene/pose FK guides are already authoritative here.
			foreach (KeyValuePair<GameObject, TransformData> pair in _dirtyBones)
			{
				if (pair.Key == null) continue;
				Transform bone = pair.Key.transform;
				TransformData data = pair.Value;
				if (data.position.hasValue && data.originalPosition.hasValue) bone.localPosition = data.originalPosition.value;
				if (data.scale.hasValue && data.originalScale.hasValue) bone.localScale = data.originalScale.value;
				OCIChar.BoneInfo info;
				if (data.rotation.hasValue && data.originalRotation.hasValue && (!_target.fkEnabled || !_target.fkObjects.TryGetValue(pair.Key, out info) || !info.active))
					bone.localRotation = data.originalRotation.value;
			}
			_dirtyBones.Clear();
			_bonePresetSelections.Clear();
			_blendResultSelection = null;
		}
	}
}
#endif
