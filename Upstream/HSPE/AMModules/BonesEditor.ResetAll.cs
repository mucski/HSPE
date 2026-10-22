using System;
using System.Collections.Generic;
using Studio;
using ToolBox;
using UnityEngine;

namespace HSPE.AMModules
{
	public partial class BonesEditor
	{
		private static BonesEditor _resetAllConfirmationOwner;
		private bool _confirmResetAllBones;
		private bool _resetAllBonesFailed;
		private string _resetAllBonesStatus = "";
		private GUISkin _resetAllBonesSkin;
		private GUIStyle _resetAllBonesLabel;

		private sealed class BoneResetSnapshot
		{
			public GameObject bone;
			public TransformData data;
			public Vector3 localPosition, localScale;
			public Quaternion localRotation;
			public ChangeAmount fkChange;
			public Vector3 fkRotation;
		}

		private void CancelResetAllBonesConfirmation()
		{
			_confirmResetAllBones = false;
			if (_resetAllConfirmationOwner == this) _resetAllConfirmationOwner = null;
		}

		private void DrawResetAllBonesBar()
		{
			// Reserve the same slot before and during confirmation; never change the GUILayout tree on a click.
			Rect area = GUILayoutUtility.GetRect(0f, 27f, GUILayout.ExpandWidth(true));
			if (_resetAllBonesSkin != GUI.skin)
			{
				_resetAllBonesSkin = GUI.skin;
				_resetAllBonesLabel = new GUIStyle(GUI.skin.label) { clipping = TextClipping.Clip, wordWrap = false, alignment = TextAnchor.MiddleLeft };
			}
			Color color = GUI.color;
			bool enabled = GUI.enabled;
			int count = _dirtyBones.Count;
			if (count == 0) CancelResetAllBonesConfirmation();
			string explanation = "Restore recorded pre-edit position, rotation and scale for ALL modified bones on this object, including hidden search results and affected FK rotations. Clean channels and preset files are untouched. Pause Timeline first.";
			try
			{
				GUI.Box(area, GUIContent.none);
				if (_confirmResetAllBones)
				{
					GUI.color = ColorPresets.Tomato;
					GUI.Label(new Rect(area.x + 5f, area.y + 2f, area.width - 148f, 23f), new GUIContent("Reset " + count + " bones?", explanation), _resetAllBonesLabel);
					if (GUI.Button(new Rect(area.xMax - 141f, area.y + 2f, 83f, 23f), new GUIContent("Reset All", explanation))) ResetAllBoneEdits();
					GUI.color = color;
					if (GUI.Button(new Rect(area.xMax - 55f, area.y + 2f, 52f, 23f), "Cancel")) CancelResetAllBonesConfirmation();
				}
				else
				{
					if (_resetAllBonesFailed) GUI.color = ColorPresets.Tomato;
					string label = _resetAllBonesFailed ? "Reset failed (see log)" : count + " modified bones";
					GUI.Label(new Rect(area.x + 5f, area.y + 2f, area.width - 108f, 23f), new GUIContent(label, _resetAllBonesStatus.Length == 0 ? explanation : _resetAllBonesStatus), _resetAllBonesLabel);
					GUI.enabled = enabled && count > 0;
					GUI.color = ColorPresets.Tomato;
					if (GUI.Button(new Rect(area.xMax - 101f, area.y + 2f, 98f, 23f), new GUIContent("Reset All...", explanation)))
					{
						_confirmResetAllBones = true;
						_resetAllConfirmationOwner = this;
						_resetAllBonesFailed = false;
					}
				}
			}
			finally { GUI.color = color; GUI.enabled = enabled; }
		}

		private bool ResetAllBoneEdits()
		{
			CancelResetAllBonesConfirmation();
			CancelBoneBlend("Preview cancelled before resetting bone edits.");
			List<BoneResetSnapshot> snapshots = new List<BoneResetSnapshot>();
			int missingBaselines = 0, resetBones = 0;
			try
			{
				// Snapshot every entry before the first mutation. Do not recurse through the tree or depend on search/selection.
				foreach (KeyValuePair<GameObject, TransformData> pair in _dirtyBones)
				{
					BoneResetSnapshot snapshot = new BoneResetSnapshot { bone = pair.Key, data = new TransformData(pair.Value) };
					if (pair.Key != null)
					{
						Transform bone = pair.Key.transform;
						snapshot.localPosition = bone.localPosition;
						snapshot.localRotation = bone.localRotation;
						snapshot.localScale = bone.localScale;
						OCIChar.BoneInfo info;
						// Also restore a mapped but currently inactive FK guide: enabling it later must not bring the edit back.
						if (pair.Value.rotation.hasValue && pair.Value.originalRotation.hasValue && _target.fkObjects != null
							&& _target.fkObjects.TryGetValue(pair.Key, out info) && info.guideObject != null && info.guideObject.changeAmount != null)
						{
							snapshot.fkChange = info.guideObject.changeAmount;
							snapshot.fkRotation = snapshot.fkChange.rot;
						}
					}
					snapshots.Add(snapshot);
				}
			}
			catch (Exception e)
			{
				SetResetAllBonesError(e);
				return false; // No transform or dirty metadata has changed during preparation.
			}

			try
			{
				foreach (BoneResetSnapshot snapshot in snapshots)
				{
					if (snapshot.bone == null) continue; // Destroyed dirty entries are removed without resolving replacement objects.
					Transform bone = snapshot.bone.transform;
					TransformData data = snapshot.data;
					if (data.position.hasValue)
					{
						if (data.originalPosition.hasValue) bone.localPosition = data.originalPosition.value;
						else ++missingBaselines;
					}
					if (data.rotation.hasValue)
					{
						if (data.originalRotation.hasValue)
						{
							if (snapshot.fkChange != null) snapshot.fkChange.rot = data.originalRotation.value.eulerAngles;
							bone.localRotation = data.originalRotation.value;
						}
						else ++missingBaselines;
					}
					if (data.scale.hasValue)
					{
						if (data.originalScale.hasValue) bone.localScale = data.originalScale.value;
						else ++missingBaselines;
					}
					++resetBones;
				}
				_dirtyBones.Clear();
				_bonePresetSelections.Clear();
				_blendResultSelection = null;
				_lastShouldSaveValue = false;
				_oldFKRotationValue = null;
				_oldFKTwinRotationValue = null;
				_resetAllBonesFailed = false;
				_resetAllBonesStatus = "Reset " + resetBones + " modified bones on this object. Preset files were not changed.";
				if (missingBaselines > 0)
				{
					_resetAllBonesStatus += " " + missingBaselines + " channels had no recorded baseline; their overrides were removed without inventing default values.";
					Debug.LogWarning("HSPE: " + _resetAllBonesStatus);
				}
				if (VerboseBlendLogs) Debug.Log("HSPE ResetAll: tracked=" + snapshots.Count + " restoredBones=" + resetBones + " missingBaselines=" + missingBaselines);
				return true;
			}
			catch (Exception e)
			{
				// Restore the transaction if a game/third-party FK callback throws partway through.
				_dirtyBones.Clear();
				foreach (BoneResetSnapshot snapshot in snapshots)
				{
					if (snapshot.bone == null) continue;
					_dirtyBones[snapshot.bone] = new TransformData(snapshot.data);
					try { if (snapshot.fkChange != null) snapshot.fkChange.rot = snapshot.fkRotation; }
					catch (Exception restoreError) { Debug.LogError("HSPE: FK callback threw while restoring a failed Reset All: " + restoreError); }
					if (snapshot.data.position.hasValue) snapshot.bone.transform.localPosition = snapshot.localPosition;
					if (snapshot.data.rotation.hasValue) snapshot.bone.transform.localRotation = snapshot.localRotation;
					if (snapshot.data.scale.hasValue) snapshot.bone.transform.localScale = snapshot.localScale;
				}
				SetResetAllBonesError(e);
				return false;
			}
		}

		private void SetResetAllBonesError(Exception e)
		{
			_resetAllBonesFailed = true;
			_resetAllBonesStatus = "Reset All failed; the previous edits were retained. " + e.Message;
			Debug.LogError("HSPE: " + _resetAllBonesStatus + "\n" + e);
		}
	}
}
