using System.Xml;
using Studio;
using ToolBox.Extensions;
using UnityEngine;

namespace HSPE.AMModules
{
	public partial class BonesEditor
	{
		internal static class TimelineCompatibility
		{
			public static void Populate()
			{
				ToolBox.TimelineCompatibility.AddInterpolableModelDynamic(
						owner: HSPE._name,
						id: "bonePos",
						name: "Bone Position",
						interpolateBefore: (oci, parameter, leftValue, rightValue, factor) =>
						{
							HashedPair<BonesEditor, Transform> pair = (HashedPair<BonesEditor, Transform>)parameter;
							pair.key.SetBonePosition(pair.value, Vector3.LerpUnclamped((Vector3)leftValue, (Vector3)rightValue, factor));
						},
						interpolateAfter: null,
						isCompatibleWithTarget: IsCompatibleWithTarget,
						getValue: (oci, parameter) => ((HashedPair<BonesEditor, Transform>)parameter).value.localPosition,
						readValueFromXml: (parameter, node) => node.ReadVector3("value"),
						writeValueToXml: (parameter, writer, o) => writer.WriteValue("value", (Vector3)o),
						getParameter: GetParameter,
						readParameterFromXml: ReadParameterFromXml,
						writeParameterToXml: WriteParameterToXml,
						checkIntegrity: CheckIntegrity,
						getFinalName: (name, oci, parameter) => $"B Position ({((HashedPair<BonesEditor, Transform>)parameter).value.name})"
				);
				ToolBox.TimelineCompatibility.AddInterpolableModelDynamic(
						owner: HSPE._name,
						id: "boneRot",
						name: "Bone Rotation",
						interpolateBefore: (oci, parameter, leftValue, rightValue, factor) =>
						{
							HashedPair<BonesEditor, Transform> pair = (HashedPair<BonesEditor, Transform>)parameter;
							pair.key.SetBoneRotation(pair.value, Quaternion.SlerpUnclamped((Quaternion)leftValue, (Quaternion)rightValue, factor));
						},
						interpolateAfter: null,
						isCompatibleWithTarget: IsCompatibleWithTarget,
						getValue: (oci, parameter) => ((HashedPair<BonesEditor, Transform>)parameter).value.localRotation,
						readValueFromXml: (parameter, node) => node.ReadQuaternion("value"),
						writeValueToXml: (parameter, writer, o) => writer.WriteValue("value", (Quaternion)o),
						getParameter: GetParameter,
						readParameterFromXml: ReadParameterFromXml,
						writeParameterToXml: WriteParameterToXml,
						checkIntegrity: CheckIntegrity,
						getFinalName: (name, oci, parameter) => $"B Rotation ({((HashedPair<BonesEditor, Transform>)parameter).value.name})"
				);
				ToolBox.TimelineCompatibility.AddInterpolableModelDynamic(
						owner: HSPE._name,
						id: "boneScale",
						name: "Bone Scale",
						interpolateBefore: (oci, parameter, leftValue, rightValue, factor) =>
						{
							HashedPair<BonesEditor, Transform> pair = (HashedPair<BonesEditor, Transform>)parameter;
							pair.key.SetBoneScale(pair.value, Vector3.LerpUnclamped((Vector3)leftValue, (Vector3)rightValue, factor));
						},
						interpolateAfter: null,
						isCompatibleWithTarget: IsCompatibleWithTarget,
						getValue: (oci, parameter) => ((HashedPair<BonesEditor, Transform>)parameter).value.localScale,
						readValueFromXml: (parameter, node) => node.ReadVector3("value"),
						writeValueToXml: (parameter, writer, o) => writer.WriteValue("value", (Vector3)o),
						getParameter: GetParameter,
						readParameterFromXml: ReadParameterFromXml,
						writeParameterToXml: WriteParameterToXml,
						checkIntegrity: CheckIntegrity,
						getFinalName: (name, oci, parameter) => $"B Scale ({((HashedPair<BonesEditor, Transform>)parameter).value.name})");
			}

			private static bool CheckIntegrity(ObjectCtrlInfo oci, object parameter, object leftValue, object rightValue)
			{
				if (parameter == null)
					return false;
				HashedPair<BonesEditor, Transform> pair = (HashedPair<BonesEditor, Transform>)parameter;
				return pair.key != null && pair.value;
			}

			private static bool IsCompatibleWithTarget(ObjectCtrlInfo oci)
			{
				return oci != null && oci.guideObject != null && oci.guideObject.transformTarget && oci.guideObject.transformTarget.GetComponent<PoseController>();
			}

			private static object GetParameter(ObjectCtrlInfo oci)
			{
				PoseController controller = oci.guideObject.transformTarget.GetComponent<PoseController>();
				return new HashedPair<BonesEditor, Transform>(controller._bonesEditor, controller._bonesEditor._boneTarget);
			}

			private static object ReadParameterFromXml(ObjectCtrlInfo oci, XmlNode node)
			{
				PoseController controller = oci.guideObject.transformTarget.GetComponent<PoseController>();
				return new HashedPair<BonesEditor, Transform>(controller._bonesEditor, controller.transform.Find(node.Attributes["parameter"].Value));
			}

			private static void WriteParameterToXml(ObjectCtrlInfo oci, XmlTextWriter writer, object parameter)
			{
				writer.WriteAttributeString("parameter", ((HashedPair<BonesEditor, Transform>)parameter).value.GetPathFrom(oci.guideObject.transformTarget));
			}
		}
	}
}