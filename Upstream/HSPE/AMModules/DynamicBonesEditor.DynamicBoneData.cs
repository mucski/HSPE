using UnityEngine;
#if HONEYSELECT || PLAYHOME || KOIKATSU
#endif

namespace HSPE.AMModules
{
	public partial class DynamicBonesEditor
	{
		private class DynamicBoneData
		{
			public EditableValue<bool> originalEnabled;
			public bool currentEnabled;
			public EditableValue<Vector3> originalGravity;
			public Vector3 currentGravity;
			public EditableValue<Vector3> originalForce;
			public Vector3 currentForce;
#if AISHOUJO || HONEYSELECT2
			public EditableValue<DynamicBone.UpdateMode> originalUpdateMode;
			public DynamicBone.UpdateMode currentUpdateMode;
#endif
			public EditableValue<DynamicBone.FreezeAxis> originalFreezeAxis;
			public DynamicBone.FreezeAxis currentFreezeAxis;
			public EditableValue<float> originalWeight;
			public float currentWeight;
			public EditableValue<float> originalDamping;
			public float currentDamping;
			public EditableValue<float> originalElasticity;
			public float currentElasticity;
			public EditableValue<float> originalStiffness;
			public float currentStiffness;
			public EditableValue<float> originalInert;
			public float currentInert;
			public EditableValue<float> originalRadius;
			public float currentRadius;

			public DynamicBoneData()
			{
			}

			public DynamicBoneData(DynamicBoneData other)
			{
				originalEnabled = other.originalEnabled;
				originalWeight = other.originalWeight;
				originalGravity = other.originalGravity;
				originalForce = other.originalForce;
#if AISHOUJO || HONEYSELECT2
				this.originalUpdateMode = other.originalUpdateMode;
#endif
				originalFreezeAxis = other.originalFreezeAxis;
				originalDamping = other.originalDamping;
				originalElasticity = other.originalElasticity;
				originalStiffness = other.originalStiffness;
				originalInert = other.originalInert;
				originalRadius = other.originalRadius;

				currentEnabled = other.currentEnabled;
				currentWeight = other.currentWeight;
				currentGravity = other.currentGravity;
				currentForce = other.currentForce;
#if AISHOUJO || HONEYSELECT2
				this.currentUpdateMode= other.currentUpdateMode;
#endif
				currentFreezeAxis = other.currentFreezeAxis;
				currentDamping = other.currentDamping;
				currentElasticity = other.currentElasticity;
				currentStiffness = other.currentStiffness;
				currentInert = other.currentInert;
				currentRadius = other.currentRadius;
			}
		}
	}
}
