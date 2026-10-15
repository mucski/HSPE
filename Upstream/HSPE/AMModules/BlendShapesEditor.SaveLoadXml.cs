using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using ToolBox.Extensions;
using UnityEngine;

namespace HSPE.AMModules
{
	public partial class BlendShapesEditor
	{
		public override int SaveXml(XmlTextWriter xmlWriter)
		{
			int written = 0;
			if (_dirtySkinnedMeshRenderers.Count != 0)
			{
				xmlWriter.WriteStartElement("skinnedMeshes");
				if (_target.type == GenericOCITarget.Type.Character)
				{
					xmlWriter.WriteAttributeString("eyesPtn", XmlConvert.ToString(_target.ociChar.charInfo.GetEyesPtn()));
#if HONEYSELECT || KOIKATSU || AISHOUJO || HONEYSELECT2
					xmlWriter.WriteAttributeString("eyesOpen", XmlConvert.ToString(_target.ociChar.charInfo.GetEyesOpenMax()));
#elif PLAYHOME
					xmlWriter.WriteAttributeString("eyesOpen", XmlConvert.ToString(this._target.ociChar.charInfo.fileStatus.eyesOpenMax));
#endif
					xmlWriter.WriteAttributeString("mouthPtn", XmlConvert.ToString(_target.ociChar.charInfo.GetMouthPtn()));
					xmlWriter.WriteAttributeString("mouthOpen", XmlConvert.ToString(_target.ociChar.oiCharInfo.mouthOpen));
#if KOIKATSU || AISHOUJO || HONEYSELECT2
					xmlWriter.WriteAttributeString("eyebrowsPtn", XmlConvert.ToString(this._target.ociChar.charInfo.GetEyebrowPtn()));
					xmlWriter.WriteAttributeString("eyebrowsOpen", XmlConvert.ToString(this._target.ociChar.charInfo.GetEyebrowOpenMax()));
#endif
					++written;
				}
				foreach (KeyValuePair<SkinnedMeshRenderer, SkinnedMeshRendererData> kvp in _dirtySkinnedMeshRenderers)
				{
					// note: null check for key, because sometimes this isn't valid (e.g. changing face resets blendshapes)
					if (kvp.Key == null)
						continue;

					xmlWriter.WriteStartElement("skinnedMesh");
					xmlWriter.WriteAttributeString("name", kvp.Key.transform.GetPathFrom(_parent.transform));

					foreach (KeyValuePair<int, BlendShapeData> weight in kvp.Value.dirtyBlendShapes)
					{
						xmlWriter.WriteStartElement("blendShape");
						xmlWriter.WriteAttributeString("index", XmlConvert.ToString(weight.Key));
						xmlWriter.WriteAttributeString("weight", XmlConvert.ToString(weight.Value.weight));
						xmlWriter.WriteEndElement();
					}

					xmlWriter.WriteEndElement();
					++written;
				}
				xmlWriter.WriteEndElement();
			}
			return written;
		}

		public override bool LoadXml(XmlNode xmlNode)
		{
			ResetAll();
			RefreshSkinnedMeshRendererList();

			bool changed = false;
			XmlNode skinnedMeshesNode = xmlNode.FindChildNode("skinnedMeshes");
			Dictionary<XmlNode, SkinnedMeshRenderer> potentialChildrenNodes = new Dictionary<XmlNode, SkinnedMeshRenderer>();
			if (skinnedMeshesNode != null)
			{
				if (_target.type == GenericOCITarget.Type.Character)
				{
					if (skinnedMeshesNode.Attributes["eyesPtn"] != null)
					{
#if HONEYSELECT || KOIKATSU || AISHOUJO || HONEYSELECT2
						_target.ociChar.charInfo.ChangeEyesPtn(XmlConvert.ToInt32(skinnedMeshesNode.Attributes["eyesPtn"].Value), false);
#elif PLAYHOME
						this._target.ociChar.charInfo.ChangeEyesPtn(XmlConvert.ToInt32(skinnedMeshesNode.Attributes["eyesPtn"].Value));
#endif
					}
					if (skinnedMeshesNode.Attributes["eyesOpen"] != null)
						_target.ociChar.ChangeEyesOpen(XmlConvert.ToSingle(skinnedMeshesNode.Attributes["eyesOpen"].Value));
					if (skinnedMeshesNode.Attributes["mouthPtn"] != null)
					{
#if HONEYSELECT || KOIKATSU || AISHOUJO || HONEYSELECT2
						_target.ociChar.charInfo.ChangeMouthPtn(XmlConvert.ToInt32(skinnedMeshesNode.Attributes["mouthPtn"].Value), false);
#elif PLAYHOME
						this._target.ociChar.charInfo.ChangeMouthPtn(XmlConvert.ToInt32(skinnedMeshesNode.Attributes["mouthPtn"].Value));
#endif
					}
					if (skinnedMeshesNode.Attributes["mouthOpen"] != null)
						_target.ociChar.ChangeMouthOpen(XmlConvert.ToSingle(skinnedMeshesNode.Attributes["mouthOpen"].Value));
#if KOIKATSU || AISHOUJO || HONEYSELECT2
					if (skinnedMeshesNode.Attributes["eyebrowsPtn"] != null)
						this._target.ociChar.charInfo.ChangeEyebrowPtn(XmlConvert.ToInt32(skinnedMeshesNode.Attributes["eyebrowsPtn"].Value), false);
					if (skinnedMeshesNode.Attributes["eyebrowsOpen"] != null)
						this._target.ociChar.charInfo.ChangeEyebrowOpenMax(XmlConvert.ToSingle(skinnedMeshesNode.Attributes["eyebrowsOpen"].Value));
#endif

				}
				foreach (XmlNode node in skinnedMeshesNode.ChildNodes)
				{
					try
					{
						Transform t = _parent.transform.Find(node.Attributes["name"].Value);
						if (!t)
							continue;
						SkinnedMeshRenderer renderer = t.GetComponent<SkinnedMeshRenderer>();
						if (!renderer)
							continue;
						if (!_skinnedMeshRenderers.Contains(renderer))
						{
							potentialChildrenNodes.Add(node, renderer);
							continue;
						}
						if (LoadSingleSkinnedMeshRenderer(node, renderer))
							changed = true;
					}
					catch (Exception e)
					{
						UnityEngine.Debug.LogError($"HSPE: Couldn't load blendshape for object {_parent.name} {node.OuterXml}\r\n{e}");
					}
				}
			}
			if (potentialChildrenNodes.Count > 0)
			{
				foreach (KeyValuePair<XmlNode, SkinnedMeshRenderer> pair in potentialChildrenNodes)
				{
					PoseController childController = pair.Value.GetComponentInParent<PoseController>();
					if (childController != _parent)
					{
						childController.enabled = true;
						if (!childController._blendShapesEditor._secondPassLoadingNodes.ContainsKey(pair.Key))
							childController._blendShapesEditor._secondPassLoadingNodes.Add(pair.Key, pair.Value);
					}
				}
			}

			_parent.ExecuteDelayed(() =>
			{
				foreach (KeyValuePair<XmlNode, SkinnedMeshRenderer> pair in _secondPassLoadingNodes)
				{
					try
					{
						if (_skinnedMeshRenderers.Contains(pair.Value))
							LoadSingleSkinnedMeshRenderer(pair.Key, pair.Value);
					}
					catch (Exception e)
					{
						UnityEngine.Debug.LogError($"HSPE: Couldn't load blendshape for object {_parent.name} {pair.Key.OuterXml}\r\n{e}");
					}
				}
				_secondPassLoadingNodes.Clear();
			}, 2);
			return changed || _secondPassLoadingNodes.Count > 0;
		}
		private bool LoadSingleSkinnedMeshRenderer(XmlNode node, SkinnedMeshRenderer renderer)
		{
			bool loaded = false;
			SkinnedMeshRendererData data = new SkinnedMeshRendererData();
			foreach (XmlNode childNode in node.ChildNodes)
			{
				int index = XmlConvert.ToInt32(childNode.Attributes["index"].Value);
				if (index >= renderer.sharedMesh.blendShapeCount || index < 0)
					continue;
				loaded = true;
				BlendShapeData bsData = SetBlendShapeDirty(data, index);
				bsData.originalWeight = renderer.GetBlendShapeWeight(index);
				bsData.weight = XmlConvert.ToSingle(childNode.Attributes["weight"].Value);
			}
			data.path = renderer.transform.GetPathFrom(_parent.transform);
			_dirtySkinnedMeshRenderers.Add(renderer, data);
			return loaded;
		}
	}
}
