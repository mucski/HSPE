#if HONEYSELECT
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using UnityEngine;

namespace HSPE.AMModules
{
	public partial class BonesEditor
	{
		// Scene addresses are not portable card/preset paths. Asset hierarchies can contain
		// duplicate, empty or slash-containing names. Occurrence is among same-named siblings;
		// unrelated sibling insertions do not shift it. A changed name count fails closed.
		internal sealed class SceneBonePart
		{
			internal string name;
			internal int occurrence, count;
		}

		private bool IsForeignBoneRoot(Transform bone)
		{
			if (_parent._childObjects.Contains(bone.gameObject)) return true;
			PoseController owner = bone.GetComponent<PoseController>();
			return owner != null && owner != _parent;
		}

		private bool IsOwnedSceneBone(Transform bone)
		{
			if (bone == null || bone == _parent.transform) return false;
			for (Transform cursor = bone; cursor != null; cursor = cursor.parent)
			{
				if (cursor == _parent.transform) return true;
				if (IsForeignBoneRoot(cursor)) return false;
			}
			return false;
		}

		private SceneBonePart[] CaptureScenePath(Transform bone)
		{
			List<SceneBonePart> parts = new List<SceneBonePart>();
			Transform cursor = bone;
			while (cursor != null && cursor != _parent.transform)
			{
				if (IsForeignBoneRoot(cursor)) throw new InvalidDataException("Modified transform belongs to a separately attached Studio object: " + bone.name + ". Reset that old parent edit and edit the attached object directly.");
				Transform parent = cursor.parent;
				if (parent == null) break;
				SceneBonePart part = new SceneBonePart { name = cursor.name };
				for (int i = 0; i < parent.childCount; ++i)
				{
					Transform sibling = parent.GetChild(i);
					if (sibling.name != part.name) continue;
					if (sibling == cursor) part.occurrence = part.count;
					++part.count;
				}
				parts.Add(part);
				if (parts.Count > 256) throw new InvalidDataException("Modified bone hierarchy is too deep: " + bone.name);
				cursor = parent;
			}
			if (cursor != _parent.transform || parts.Count == 0) throw new InvalidDataException("Modified transform is outside its Studio object's bone hierarchy: " + bone.name);
			parts.Reverse();
			SceneBonePart[] result = parts.ToArray();
			ScenePathKey(result); // Validate the same address limits used on read.
			return result;
		}

		private static string ScenePathKey(SceneBonePart[] parts)
		{
			if (parts == null || parts.Length == 0 || parts.Length > 256) throw new InvalidDataException("Invalid scene bone address.");
			StringBuilder key = new StringBuilder();
			int length = 0;
			foreach (SceneBonePart part in parts)
			{
				if (part.name == null || part.occurrence < 0 || part.count <= part.occurrence || part.count > _maximumPresetBones)
					throw new InvalidDataException("Invalid scene bone occurrence.");
				length += part.name.Length + 1;
				if (length > 4096) throw new InvalidDataException("Scene bone address is too long.");
				key.Append(part.name.Length).Append(':').Append(part.name).Append(':').Append(part.occurrence).Append(';');
			}
			return key.ToString();
		}

		private Transform ResolveSceneBone(SceneBonePart[] parts)
		{
			Transform cursor = _parent.transform;
			foreach (SceneBonePart part in parts)
			{
				Transform match = null;
				int count = 0;
				for (int i = 0; i < cursor.childCount; ++i)
				{
					Transform child = cursor.GetChild(i);
					if (child.name != part.name) continue;
					if (count == part.occurrence) match = child;
					++count;
				}
				if (count != part.count || match == null || IsForeignBoneRoot(match)) return null;
				cursor = match;
			}
			return cursor;
		}

		private CardBoneState CaptureSceneBones()
		{
			CardBoneState state = new CardBoneState { saveOnCard = _target.type == GenericOCITarget.Type.Character && saveBonesOnCard };
			foreach (KeyValuePair<GameObject, TransformData> pair in _dirtyBones)
			{
				if (pair.Key == null || DirtyChannels(pair.Value) == BlendChannels.None) continue;
				SceneBonePart[] parts = CaptureScenePath(pair.Key.transform);
				CardBoneEntry entry = CaptureBoneValues(pair.Value, true);
				entry.scenePath = parts;
				entry.path = string.Join("/", Array.ConvertAll(parts, p => p.name));
				ValidateBoneValues(entry);
				state.bones.Add(entry);
				if (state.bones.Count > _maximumPresetBones) throw new InvalidDataException("Too many modified bones for this scene object.");
			}
			state.bones.Sort((a, b) => string.CompareOrdinal(ScenePathKey(a.scenePath), ScenePathKey(b.scenePath)));
			return state;
		}

		private static string EncodeSceneBones(CardBoneState state)
		{
			using (StringWriter text = new StringWriter())
			using (XmlTextWriter writer = new XmlTextWriter(text))
			{
				writer.WriteStartElement("boneSceneState");
				writer.WriteAttributeString("version", "2");
				writer.WriteAttributeString("saveOnCard", XmlConvert.ToString(state.saveOnCard));
				foreach (CardBoneEntry entry in state.bones)
				{
					ScenePathKey(entry.scenePath);
					ValidateBoneValues(entry);
					writer.WriteStartElement("bone");
					writer.WriteAttributeString("path", entry.path); // Readable label only, never used for v2 resolution.
					WriteBoneChannels(writer, entry, false);
					writer.WriteStartElement("address");
					foreach (SceneBonePart part in entry.scenePath)
					{
						writer.WriteStartElement("part");
						writer.WriteAttributeString("name", part.name);
						writer.WriteAttributeString("occurrence", XmlConvert.ToString(part.occurrence));
						writer.WriteAttributeString("count", XmlConvert.ToString(part.count));
						writer.WriteEndElement();
					}
					writer.WriteEndElement();
					writer.WriteStartElement("baseline"); WriteBoneChannels(writer, entry, true); writer.WriteEndElement();
					writer.WriteEndElement();
				}
				writer.WriteEndElement(); writer.Flush();
				string result = text.ToString();
				if (Encoding.UTF8.GetByteCount(result) > _maximumPresetFileSize) throw new InvalidDataException("HSPE scene bone data is too large.");
				return result;
			}
		}

		private static CardBoneState DecodeSceneBones(XmlNode node)
		{
			if (node.Attributes?["version"]?.Value == "1") return DecodeCardBones(node, true);
			if (node.Name != "boneSceneState" || node.Attributes?["version"]?.Value != "2" || node.Attributes?["saveOnCard"] == null)
				throw new InvalidDataException("Unsupported HSPE scene bone data.");
			string xml = node.OuterXml;
			if (xml.Length > _maximumPresetFileSize || Encoding.UTF8.GetByteCount(xml) > _maximumPresetFileSize || node.ChildNodes.Count > _maximumPresetBones)
				throw new InvalidDataException("HSPE scene bone data exceeds safety limits.");
			CardBoneState state = new CardBoneState { saveOnCard = XmlConvert.ToBoolean(node.Attributes["saveOnCard"].Value) };
			HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
			foreach (XmlNode bone in node.ChildNodes)
			{
				if (bone.NodeType != XmlNodeType.Element) continue;
				if (bone.Name != "bone") throw new InvalidDataException("Invalid scene bone entry.");
				CardBoneEntry entry = new CardBoneEntry();
				XmlNode address = null, baseline = null;
				foreach (XmlNode child in bone.ChildNodes)
				{
					if (child.NodeType != XmlNodeType.Element) continue;
					if (child.Name == "address" && address == null) address = child;
					else if (child.Name == "baseline" && baseline == null) baseline = child;
					else throw new InvalidDataException("Invalid scene bone metadata.");
				}
				if (address == null) throw new InvalidDataException("Missing scene bone address.");
				List<SceneBonePart> parts = new List<SceneBonePart>();
				foreach (XmlNode part in address.ChildNodes)
				{
					if (part.NodeType != XmlNodeType.Element) continue;
					if (part.Name != "part" || part.Attributes["name"] == null || part.Attributes["occurrence"] == null || part.Attributes["count"] == null || parts.Count >= 256)
						throw new InvalidDataException("Invalid scene bone address part.");
					parts.Add(new SceneBonePart { name = part.Attributes["name"].Value, occurrence = XmlConvert.ToInt32(part.Attributes["occurrence"].Value), count = XmlConvert.ToInt32(part.Attributes["count"].Value) });
				}
				entry.scenePath = parts.ToArray();
				if (!paths.Add(ScenePathKey(entry.scenePath))) throw new InvalidDataException("Duplicate scene bone address.");
				entry.path = string.Join("/", Array.ConvertAll(entry.scenePath, p => p.name));
				ReadCardChannels(bone, entry, false);
				if (baseline != null) ReadCardChannels(baseline, entry, true);
				ValidateBoneValues(entry);
				state.bones.Add(entry);
			}
			return state;
		}
	}
}
#endif
