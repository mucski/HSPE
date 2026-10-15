using System.Collections.Generic;
using System.Xml;
using Studio;
using ToolBox.Extensions;
using UnityEngine;

namespace HSPE.AMModules
{
	public partial class BlendShapesEditor
	{
		internal static class TimelineCompatibility
		{
			private class GroupParameter
			{
				public readonly BlendShapesEditor editor;
				public readonly string rendererPath;

				public SkinnedMeshRenderer renderer
				{
					get
					{
						if (_renderer == null)
							editor._skinnedMeshRenderersByPath.TryGetValue(rendererPath, out _renderer);
						return _renderer;
					}
				}
				private SkinnedMeshRenderer _renderer;
				private readonly int _hashCode;

				public GroupParameter(BlendShapesEditor editor, SkinnedMeshRenderer renderer)
				{
					this.editor = editor;
					_renderer = renderer;
					rendererPath = _renderer.transform.GetPathFrom(this.editor._parent.transform);

					unchecked
					{
						int hash = 17;
						hash = hash * 31 + (this.editor != null ? this.editor.GetHashCode() : 0);
						_hashCode = hash * 31 + (rendererPath != null ? rendererPath.GetHashCode() : 0);
					}
				}

				public GroupParameter(BlendShapesEditor editor, string rendererPath)
				{
					this.editor = editor;
					this.rendererPath = rendererPath;

					unchecked
					{
						int hash = 17;
						hash = hash * 31 + (this.editor != null ? this.editor.GetHashCode() : 0);
						_hashCode = hash * 31 + (this.rendererPath != null ? this.rendererPath.GetHashCode() : 0);
					}
				}

				public override int GetHashCode()
				{
					return _hashCode;
				}

				public override string ToString()
				{
					return $"editor: {editor}, rendererPath: {rendererPath}, renderer: {_renderer}";
				}
			}

			private class IndividualParameter : GroupParameter
			{
				public readonly int index;
				private readonly int _hashCode;

				public IndividualParameter(BlendShapesEditor editor, SkinnedMeshRenderer renderer, int index) : base(editor, renderer)
				{
					this.index = index;

					unchecked
					{
						_hashCode = base.GetHashCode() * 31 + this.index.GetHashCode();
					}
				}

				public IndividualParameter(BlendShapesEditor editor, string rendererPath, int index) : base(editor, rendererPath)
				{
					this.index = index;

					unchecked
					{
						_hashCode = base.GetHashCode() * 31 + this.index.GetHashCode();
					}
				}

				public override int GetHashCode()
				{
					return _hashCode;
				}

				public override string ToString()
				{
					return $"{base.ToString()}, index: {index}";
				}
			}

			public static void Populate()
			{
				ToolBox.TimelineCompatibility.AddInterpolableModelDynamic(
						owner: HSPE._name,
						id: "lastBlendShape",
						name: "BlendShape (Last Modified)",
						interpolateBefore: (oci, parameter, leftValue, rightValue, factor) =>
						{
							IndividualParameter p = (IndividualParameter)parameter;
							if (p.editor._isBusy == false)
								p.editor.SetBlendShapeWeight(p.renderer, p.index, Mathf.LerpUnclamped((float)leftValue, (float)rightValue, factor));
						},
						interpolateAfter: null,
						isCompatibleWithTarget: oci => oci != null && oci.guideObject != null && oci.guideObject.transformTarget != null && oci.guideObject.transformTarget.GetComponent<PoseController>() != null,
						getValue: (oci, parameter) =>
						{
							IndividualParameter p = (IndividualParameter)parameter;
							return p.renderer.GetBlendShapeWeight(p.index);
						},
						readValueFromXml: (parameter, node) => node.ReadFloat("value"),
						writeValueToXml: (parameter, writer, o) => writer.WriteValue("value", (float)o),
						getParameter: oci =>
						{
							PoseController controller = oci.guideObject.transformTarget.GetComponent<PoseController>();
							return new IndividualParameter(controller._blendShapesEditor, controller._blendShapesEditor._skinnedMeshTarget, controller._blendShapesEditor._lastEditedBlendShape);
						},
						readParameterFromXml: (oci, node) => new IndividualParameter(oci.guideObject.transformTarget.GetComponent<PoseController>()._blendShapesEditor, node.Attributes["parameter1"].Value, node.ReadInt("parameter2")),
						writeParameterToXml: (oci, writer, o) =>
						{
							IndividualParameter p = (IndividualParameter)o;
							writer.WriteAttributeString("parameter1", p.rendererPath);
							writer.WriteValue("parameter2", p.index);
						},
						checkIntegrity: (oci, parameter, leftValue, rightValue) =>
						{
							if (parameter == null)
								return false;
							IndividualParameter p = (IndividualParameter)parameter;
							if (p.editor == null)
								return false;
							if (p.editor._isBusy)
								return true;
							if (p.renderer == null || p.index >= p.renderer.sharedMesh.blendShapeCount)
								return false;
							return true;
						},
						getFinalName: (name, oci, parameter) =>
						{
							IndividualParameter p = (IndividualParameter)parameter;
							if (_skinnedMeshAliases.TryGetValue(p.renderer.name, out string skinnedMeshName) == false)
								skinnedMeshName = p.renderer.name;
							return $"BS ({skinnedMeshName} {p.index})";
						});

				ToolBox.TimelineCompatibility.AddInterpolableModelDynamic(
						owner: HSPE._name,
						id: "groupBlendShape",
						name: "BlendShape (Group)",
						interpolateBefore: (oci, parameter, leftValue, rightValue, factor) =>
						{
							GroupParameter p = (GroupParameter)parameter;

							if (p.editor._isBusy == false)
							{
								SkinnedMeshRenderer renderer = p.renderer;
								float[] left = (float[])leftValue;
								float[] right = (float[])rightValue;
								for (int i = 0; i < left.Length; i++)
									p.editor.SetBlendShapeWeight(renderer, i, Mathf.LerpUnclamped(left[i], right[i], factor));
							}
						},
						interpolateAfter: null,
						isCompatibleWithTarget: oci => oci != null && oci.guideObject != null && oci.guideObject.transformTarget != null && oci.guideObject.transformTarget.GetComponent<PoseController>() != null,
						getValue: GetGroupValue,
						readValueFromXml: ReadGroupValueFromXml,
						writeValueToXml: WriteGroupValueToXml,
						getParameter: GetGroupParameter,
						readParameterFromXml: ReadGroupParameterFromXml,
						writeParameterToXml: WriteGroupParameterToXml,
						checkIntegrity: CheckGroupIntegrity,
						getFinalName: (name, oci, parameter) =>
						{
							GroupParameter p = (GroupParameter)parameter;
							if (_skinnedMeshAliases.TryGetValue(p.renderer.name, out string skinnedMeshName) == false)
								skinnedMeshName = p.renderer.name;
							return $"BS ({skinnedMeshName})";
						});
				ToolBox.TimelineCompatibility.AddInterpolableModelDynamic(
						owner: HSPE._name,
						id: "groupBlendShapeLink",
						name: "BlendShape (Group, Link)",
						interpolateBefore: (oci, parameter, leftValue, rightValue, factor) =>
						{
							GroupParameter p = (GroupParameter)parameter;

							if (p.editor._isBusy == false)
							{
								SkinnedMeshRenderer renderer = p.renderer;
								float[] left = (float[])leftValue;
								float[] right = (float[])rightValue;
								int targetLinkMaxCount = p.editor._target.isFemale ? _femaleEyesComponentsCount : _maleEyesComponentsCount;
								for (int i = 0; i < left.Length; i++)
								{
									float newBlendShapeWeight = Mathf.LerpUnclamped(left[i], right[i], factor);
									p.editor.SetBlendShapeWeight(renderer, i, newBlendShapeWeight);
									if (i < targetLinkMaxCount)
									{
										if (p.editor._links.TryGetValue(renderer, out SkinnedMeshRendererWrapper wrapper))
										{
											foreach (SkinnedMeshRendererWrapper link in wrapper.links)
											{
												if (i < link.renderer.sharedMesh.blendShapeCount)
													p.editor.SetBlendShapeWeight(link.renderer, i, newBlendShapeWeight);
											}
										}
									}
								}
							}
						},
						interpolateAfter: null,
						isCompatibleWithTarget: oci => oci != null && oci.guideObject != null && oci.guideObject.transformTarget != null && oci.guideObject.transformTarget.GetComponent<CharaPoseController>() != null,
						getValue: GetGroupValue,
						readValueFromXml: ReadGroupValueFromXml,
						writeValueToXml: WriteGroupValueToXml,
						getParameter: GetGroupParameter,
						readParameterFromXml: ReadGroupParameterFromXml,
						writeParameterToXml: WriteGroupParameterToXml,
						checkIntegrity: CheckGroupIntegrity,
						getFinalName: (name, oci, parameter) =>
						{
							GroupParameter p = (GroupParameter)parameter;
							if (_skinnedMeshAliases.TryGetValue(p.renderer.name, out string skinnedMeshName) == false)
								skinnedMeshName = p.renderer.name;
							return $"BS ({skinnedMeshName}, L)";
						});

				//TODO maybe do that, or maybe not, idk
				//ToolBox.TimelineCompatibility.AddInterpolableModelDynamic(
				//        owner: HSPE._name,
				//        id: "everythingBlendShape",
				//        name: "BlendShape (Everything)",
				//        interpolateBefore: (oci, parameter, leftValue, rightValue, factor) =>
				//        {
				//            HashedPair<BlendShapesEditor, SkinnedMeshRenderer> pair = (HashedPair<BlendShapesEditor, SkinnedMeshRenderer>)parameter;
				//            float[] left = (float[])leftValue;
				//            float[] right = (float[])rightValue;
				//            for (int i = 0; i < left.Length; i++)
				//                pair.key.SetBlendShapeWeight(pair.value, i, Mathf.LerpUnclamped(left[i], right[i], factor));
				//        },
				//        interpolateAfter: null,
				//        isCompatibleWithTarget: oci => oci != null && oci.guideObject.transformTarget.GetComponent<PoseController>() != null,
				//        getValue: (oci, parameter) =>
				//        {
				//            HashedPair<BlendShapesEditor, SkinnedMeshRenderer> pair = (HashedPair<BlendShapesEditor, SkinnedMeshRenderer>)parameter;
				//            float[] value = new float[pair.value.sharedMesh.blendShapeCount];
				//            for (int i = 0; i < value.Length; ++i)
				//                value[i] = pair.value.GetBlendShapeWeight(i);
				//            return value;
				//        },
				//        readValueFromXml: node =>
				//        {
				//            float[] value = new float[node.ReadInt("valueCount")];
				//            for (int i = 0; i < value.Length; i++)
				//                value[i] = node.ReadFloat($"value{i}");
				//            return value;
				//        },
				//        writeValueToXml: (writer, o) =>
				//        {
				//            float[] value = (float[])o;
				//            writer.WriteValue("valueCount", value.Length);
				//            for (int i = 0; i < value.Length; i++)
				//                writer.WriteValue($"value{i}", value[i]);
				//        },
				//        getParameter: oci => oci.guideObject.transformTarget.GetComponent<PoseController>()._blendShapesEditor,
				//        checkIntegrity: (oci, parameter) => parameter != null,
				//        getFinalName: (name, oci, parameter) => "BS (Everything)");
			}

			private static object GetGroupParameter(ObjectCtrlInfo oci)
			{
				PoseController controller = oci.guideObject.transformTarget.GetComponent<PoseController>();
				return new GroupParameter(controller._blendShapesEditor, controller._blendShapesEditor._skinnedMeshTarget);
			}

			private static object GetGroupValue(ObjectCtrlInfo oci, object parameter)
			{
				GroupParameter p = (GroupParameter)parameter;
				SkinnedMeshRenderer renderer = p.renderer;
				float[] value = new float[renderer.sharedMesh.blendShapeCount];
				for (int i = 0; i < value.Length; ++i)
					value[i] = renderer.GetBlendShapeWeight(i);
				return value;
			}
			private static object ReadGroupValueFromXml(object parameter, XmlNode node)
			{
				GroupParameter p = (GroupParameter)parameter;
				SkinnedMeshRenderer renderer = p.renderer;
				int count = node.ReadInt("valueCount");
				if (renderer.sharedMesh.blendShapeCount != count)
				{
					Dictionary<int, float> tempValue = new Dictionary<int, float>();
					for (int i = 0; i < count; i++)
					{
						string blendShapeName = node.Attributes[$"name{i}"]?.Value ?? string.Empty;
						if (string.IsNullOrEmpty(blendShapeName))
						{
							tempValue.Add(i, node.ReadFloat($"value{i}"));
						}
						else
						{
							int index = renderer.sharedMesh.GetBlendShapeIndex(blendShapeName);
							if (index != -1)
							{
								tempValue[index] = node.ReadFloat($"value{i}");
							}
						}
					}
					float[] value = new float[renderer.sharedMesh.blendShapeCount];
					for (int i = 0; i < value.Length; i++)
					{
						value[i] = tempValue.TryGetValue(i, out float v) ? v : 0;
					}
					return value;
				}
				else
				{
					float[] value = new float[count];
					for (int i = 0; i < value.Length; i++)
						value[i] = node.ReadFloat($"value{i}");
					return value;
				}
			}
			private static void WriteGroupValueToXml(object parameter, XmlTextWriter writer, object o)
			{
				GroupParameter p = (GroupParameter)parameter;
				SkinnedMeshRenderer renderer = p.renderer;
				float[] value = (float[])o;
				writer.WriteValue("valueCount", value.Length);
				for (int i = 0; i < value.Length; i++)
				{
					writer.WriteValue($"value{i}", value[i]);
					writer.WriteAttributeString($"name{i}", renderer.sharedMesh.GetBlendShapeName(i));
				}
			}
			private static object ReadGroupParameterFromXml(ObjectCtrlInfo oci, XmlNode node)
			{
				return new GroupParameter(oci.guideObject.transformTarget.GetComponent<PoseController>()._blendShapesEditor, node.Attributes["parameter"].Value);
			}
			private static void WriteGroupParameterToXml(ObjectCtrlInfo oci, XmlTextWriter writer, object o)
			{
				writer.WriteAttributeString("parameter", ((GroupParameter)o).rendererPath);
			}
			private static bool CheckGroupIntegrity(ObjectCtrlInfo oci, object parameter, object leftValue, object rightValue)
			{
				if (parameter == null || leftValue == null || rightValue == null)
					return false;
				GroupParameter p = (GroupParameter)parameter;
				if (p.editor == null)
					return false;
				if (p.editor._isBusy)
					return true;
				SkinnedMeshRenderer renderer = p.renderer;
				if (renderer == null || renderer.sharedMesh.blendShapeCount != ((float[])leftValue).Length || renderer.sharedMesh.blendShapeCount != ((float[])rightValue).Length)
					return false;
				return true;
			}

		}
	}
}