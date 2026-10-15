using UnityEngine;

namespace HSPE.AMModules
{
	public partial class BonesEditor
	{
		private class TransformData
		{
			public EditableValue<Vector3> position;
			public EditableValue<Quaternion> rotation;
			public EditableValue<Vector3> scale;
			public EditableValue<Vector3> originalPosition;
			public EditableValue<Quaternion> originalRotation;
			public EditableValue<Vector3> originalScale;

			public TransformData()
			{
			}

			public TransformData(TransformData other)
			{
				position = other.position;
				rotation = other.rotation;
				scale = other.scale;
				originalPosition = other.originalPosition;
				originalRotation = other.originalRotation;
				originalScale = other.originalScale;
			}

			public void SetPosition(Vector3 position, Transform target)
			{
				this.position = position;
				if (!originalPosition.hasValue)
					originalPosition = target.localPosition;
			}

			public void SetRotation(Quaternion rotation, Transform target)
			{
				this.rotation = rotation;
				if (!originalRotation.hasValue)
					originalRotation = target.localRotation;
			}

			public void SetScale(Vector3 scale, Transform target)
			{
				this.scale = scale;
				if (!originalScale.hasValue)
					originalScale = target.localScale;
			}

			public void SetData(Transform target, Vector3? position = null, Quaternion? rotation = null, Vector3? scale = null)
			{
				if (position.HasValue)
					SetPosition(position.Value, target);
				if (rotation.HasValue)
					SetRotation(rotation.Value, target);
				if (scale.HasValue)
					SetScale(scale.Value, target);
			}

		}
	}
}