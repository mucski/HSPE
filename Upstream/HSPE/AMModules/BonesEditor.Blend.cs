using System;
using System.Collections.Generic;
using Studio;
using UnityEngine;

namespace HSPE.AMModules
{
	public partial class BonesEditor
	{
		[Flags]
		private enum BlendChannels { None = 0, Position = 1, Rotation = 2, Scale = 4 }

		private sealed class BlendRow
		{
			public string path;
			public BonePresetEntry a, b;
			public BlendChannels available, selected;
		}

		private sealed class BlendBone
		{
			public Transform target;
			public string path;
			public BlendChannels channels;
			public TransformData before;
			public Vector3 currentPosition, currentScale, aPosition, bPosition, aScale, bScale;
			public Quaternion currentRotation, aRotation, bRotation;
			public OCIChar.BoneInfo fk;
			public Vector3 fkRotation;
		}

		private sealed class BlendSession
		{
			public BonePresetSource source;
			public readonly List<BlendBone> bones = new List<BlendBone>();
			public int channels, protectedChannels, missingBones, invalidChannels, riskyScales, sharedChannels;
			public float weight = -1f;
		}

		// The preview is an overlay, never a write to _dirtyBones or Studio's FK guides.
		// This keeps saves, reset baselines and dirty flags isolated until explicit Apply.
		private static BonesEditor _activeBlendEditor;
		private BlendSession _blendSession, _blendReview;
		private readonly List<BlendRow> _blendRows = new List<BlendRow>();
		private BonePresetData _blendA, _blendB;
		private string _blendAPath, _blendBPath;
		private bool _blendProtectEdits, _blendAcceptScaleRisk;
		private Dictionary<GameObject, BlendChannels> _blendResultSelection;
		internal static bool VerboseBlendLogs = false;

		private static bool Includes(BlendChannels mask, BlendChannels channel) { return (mask & channel) != 0; }
		private static int ChannelCount(BlendChannels mask)
		{
			return (Includes(mask, BlendChannels.Position) ? 1 : 0) + (Includes(mask, BlendChannels.Rotation) ? 1 : 0) + (Includes(mask, BlendChannels.Scale) ? 1 : 0);
		}
		private static BlendChannels EntryChannels(BonePresetEntry entry)
		{
			return entry == null ? BlendChannels.None : (entry.position.HasValue ? BlendChannels.Position : 0)
				| (entry.rotation.HasValue ? BlendChannels.Rotation : 0) | (entry.scale.HasValue ? BlendChannels.Scale : 0);
		}
		private static BlendChannels DirtyChannels(TransformData data)
		{
			return data == null ? BlendChannels.None : (data.position.hasValue ? BlendChannels.Position : 0)
				| (data.rotation.hasValue ? BlendChannels.Rotation : 0) | (data.scale.hasValue ? BlendChannels.Scale : 0);
		}

		private void SetBlendSource(bool endpointA, string path)
		{
			if (_blendSession != null) return;
			try
			{
				BonePresetData data = path == null ? null : ReadBonePreset(path);
				if (data != null)
				{
					UpdateBonePresetLibraryEntry(path, data.source);
					if (!BonePresetAllowed(data))
					{
						SetBonePresetError("This preset is for a different target. Enable Show all presets to use it as a blend source.");
						return;
					}
				}
				if (endpointA) { _blendAPath = path; _blendA = data; }
				else { _blendBPath = path; _blendB = data; }
				RebuildBlendRows();
				SetBonePresetStatus("Sources selected. Nothing changes until Start preview.");
			}
			catch (Exception e) { UpdateBonePresetLibraryEntry(path, null, e.Message); SetBonePresetError("Could not select blend source: " + e.Message, e); }
		}

		private void RebuildBlendRows()
		{
			_blendRows.Clear();
			Dictionary<string, BlendRow> rows = new Dictionary<string, BlendRow>(StringComparer.Ordinal);
			for (int side = 0; side < 2; ++side)
			{
				BonePresetData preset = side == 0 ? _blendA : _blendB;
				if (preset == null) continue;
				foreach (BonePresetEntry entry in preset.bones)
				{
					BlendRow row;
					if (!rows.TryGetValue(entry.path, out row)) { row = new BlendRow { path = entry.path }; rows.Add(entry.path, row); }
					if (side == 0) row.a = entry; else row.b = entry;
					row.available |= EntryChannels(entry);
					row.selected = row.available;
				}
			}
			_blendRows.AddRange(rows.Values);
			_blendRows.Sort(delegate(BlendRow a, BlendRow b) { return StringComparer.Ordinal.Compare(a.path, b.path); });
			_blendAcceptScaleRisk = false;
			_blendResultSelection = null;
			RefreshBlendSourcePreviews();
			_blendReview = BuildBlendSession();
		}

		private BlendSession BuildBlendSession()
		{
			BlendSession session = new BlendSession { source = CurrentBonePresetSource() };
			session.invalidChannels = (_blendA == null ? 0 : _blendA.invalidChannels) + (_blendB == null ? 0 : _blendB.invalidChannels);
			foreach (BlendRow row in _blendRows)
			{
				BlendChannels channels = row.selected & row.available;
				if (channels == BlendChannels.None) continue;
				Transform bone = ResolvePresetBone(row.path);
				if (bone == null) { ++session.missingBones; continue; }
				TransformData data;
				_dirtyBones.TryGetValue(bone.gameObject, out data);
				if (_blendProtectEdits)
				{
					session.protectedChannels += ChannelCount(channels & DirtyChannels(data));
					channels &= ~DirtyChannels(data);
				}
				if (channels == BlendChannels.None) continue;
				BlendBone item = new BlendBone { target = bone, path = row.path, channels = channels,
					before = data == null ? new TransformData() : new TransformData(data),
					currentPosition = data != null && data.position.hasValue ? data.position.value : bone.localPosition,
					currentRotation = data != null && data.rotation.hasValue ? data.rotation.value : bone.localRotation,
					currentScale = data != null && data.scale.hasValue ? data.scale.value : bone.localScale };
				item.aPosition = row.a != null && row.a.position.HasValue ? row.a.position.Value : item.currentPosition;
				item.bPosition = row.b != null && row.b.position.HasValue ? row.b.position.Value : item.currentPosition;
				item.aRotation = row.a != null && row.a.rotation.HasValue ? row.a.rotation.Value : item.currentRotation;
				item.bRotation = row.b != null && row.b.rotation.HasValue ? row.b.rotation.Value : item.currentRotation;
				item.aScale = row.a != null && row.a.scale.HasValue ? row.a.scale.Value : item.currentScale;
				item.bScale = row.b != null && row.b.scale.HasValue ? row.b.scale.Value : item.currentScale;
				if (Includes(channels, BlendChannels.Position) && (!FiniteVector(item.currentPosition) || !FiniteVector(item.aPosition) || !FiniteVector(item.bPosition)))
				{ item.channels &= ~BlendChannels.Position; ++session.invalidChannels; }
				Quaternion checkedCurrentRotation = item.currentRotation;
				if (Includes(channels, BlendChannels.Rotation) && (!NormalizeBlendRotation(ref checkedCurrentRotation) || !NormalizeBlendRotation(ref item.aRotation) || !NormalizeBlendRotation(ref item.bRotation)))
				{ item.channels &= ~BlendChannels.Rotation; ++session.invalidChannels; }
				if (Includes(channels, BlendChannels.Scale))
				{
					if (!FiniteVector(item.currentScale) || !FiniteVector(item.aScale) || !FiniteVector(item.bScale)) { item.channels &= ~BlendChannels.Scale; ++session.invalidChannels; }
					else if (RiskyBlendScale(item.aScale, item.bScale)) ++session.riskyScales;
				}
				if (Includes(item.channels, BlendChannels.Rotation))
				{
					item.fk = ActiveBlendFK(bone);
					if (item.fk != null) item.fkRotation = item.fk.guideObject.changeAmount.rot;
				}
				if (item.channels == BlendChannels.None) continue;
				session.bones.Add(item);
				session.channels += ChannelCount(item.channels);
				session.sharedChannels += ChannelCount(item.channels & EntryChannels(row.a) & EntryChannels(row.b));
			}
			return session;
		}

		private OCIChar.BoneInfo ActiveBlendFK(Transform bone)
		{
			OCIChar.BoneInfo info;
			return _target.fkEnabled && _target.fkObjects.TryGetValue(bone.gameObject, out info) && info.active && info.guideObject != null ? info : null;
		}

		private bool BeginBoneBlend()
		{
			if (_blendSession != null || _blendB == null) return false;
			if (!BonePresetAllowed(_blendB) || (_blendA != null && !BonePresetAllowed(_blendA)))
			{ SetBonePresetError("Blend sources no longer match this target. Review them or enable Show all presets."); return false; }
			if (ToolBox.TimelineCompatibility.IsPlayingIfAvailable) { SetBonePresetError("Pause Timeline before starting a blend preview."); return false; }
			BlendSession session = BuildBlendSession(); // Fresh, pre-preview snapshot, not the preceding GUI frame.
			if (session.channels == 0) { SetBonePresetError("No eligible edits. Check the selected channels and Preserve dirty."); return false; }
			if (session.riskyScales > 0 && !_blendAcceptScaleRisk) { SetBonePresetError("Scale crosses or reaches zero. Review and accept the scale warning first."); return false; }
			if (_activeBlendEditor != null && _activeBlendEditor != this) _activeBlendEditor.CancelBoneBlend("Preview cancelled: target changed.");
			_blendSession = session;
			_activeBlendEditor = this;
			_blendResultSelection = null;
			SetBonePresetStatus("Preview only. Apply to keep; Cancel to restore. Pause animation/Timeline while blending.");
			TraceBlend("begin", session);
			ApplyBlendOverlay();
			return true;
		}

		private void SetBoneBlendWeight(float weight)
		{
			if (_blendSession == null || !IsFinite(weight)) return;
			_blendSession.weight = Mathf.Clamp(weight, -1f, 1f);
			ApplyBlendOverlay();
		}

		private bool ValidateBlendSession()
		{
			if (_blendSession == null) return false;
			if (ToolBox.TimelineCompatibility.IsPlayingIfAvailable)
			{ CancelBoneBlend("Preview cancelled: Timeline playback started."); return false; }
			BonePresetSource source = CurrentBonePresetSource();
			if ((_blendSession.source != null || source != null) && !BonePresetSourcesMatch(_blendSession.source, source))
			{ CancelBoneBlend("Preview cancelled: the target's source changed."); return false; }
			foreach (BlendBone item in _blendSession.bones)
			{
				if (item.target == null || _parent == null || ResolvePresetBone(item.path) != item.target)
				{ CancelBoneBlend("Preview cancelled: the bone hierarchy changed."); return false; }
				if (Includes(item.channels, BlendChannels.Rotation) && (ActiveBlendFK(item.target) != item.fk
					|| (item.fk != null && item.fk.guideObject.changeAmount.rot != item.fkRotation)))
				{ CancelBoneBlend("Preview cancelled: FK state changed. Pause other editing tools before blending."); return false; }
			}
			return true;
		}

		private void ApplyBlendOverlay()
		{
			if (!ValidateBlendSession()) return;
			float t = (_blendSession.weight + 1f) * .5f;
			foreach (BlendBone item in _blendSession.bones)
			{
				if (Includes(item.channels, BlendChannels.Position)) item.target.localPosition = BlendVector(item.aPosition, item.bPosition, t);
				if (Includes(item.channels, BlendChannels.Rotation)) item.target.localRotation = BlendRotation(item.aRotation, item.bRotation, t);
				if (Includes(item.channels, BlendChannels.Scale)) item.target.localScale = BlendVector(item.aScale, item.bScale, t);
			}
		}

		private void RestoreBlendTransforms(BlendSession session)
		{
			foreach (BlendBone item in session.bones)
			{
				if (item.target == null) continue; // Never resolve a replacement object by its old path.
				if (Includes(item.channels, BlendChannels.Position)) item.target.localPosition = item.currentPosition;
				if (Includes(item.channels, BlendChannels.Rotation)) item.target.localRotation = item.currentRotation;
				if (Includes(item.channels, BlendChannels.Scale)) item.target.localScale = item.currentScale;
			}
		}

		internal void CancelBoneBlend(string reason)
		{
			if (_blendSession == null) return;
			BlendSession session = _blendSession;
			_blendSession = null;
			if (_activeBlendEditor == this) _activeBlendEditor = null;
			RestoreBlendTransforms(session);
			ApplyBoneManualCorrection();
			SetBonePresetStatus(reason);
			TraceBlend("cancel", session);
		}

		private void CommitBoneBlend()
		{
			if (!ValidateBlendSession()) return;
			BlendSession session = _blendSession;
			float t = (session.weight + 1f) * .5f;
			_blendSession = null;
			_activeBlendEditor = null;
			RestoreBlendTransforms(session); // Setters must capture the true reset baseline, not a preview value.
			Dictionary<GameObject, BlendChannels> result = new Dictionary<GameObject, BlendChannels>();
			try
			{
				foreach (BlendBone item in session.bones)
				{
					Vector3 pos = BlendVector(item.aPosition, item.bPosition, t), scale = BlendVector(item.aScale, item.bScale, t);
					Quaternion rot = BlendRotation(item.aRotation, item.bRotation, t);
					// Returning to the captured state must not turn a clean channel into a permanent override.
					if (Includes(item.channels, BlendChannels.Position) && !pos.Equals(item.currentPosition)) SetBoneDirty(item.target.gameObject).SetPosition(pos, item.target);
					if (Includes(item.channels, BlendChannels.Scale) && !scale.Equals(item.currentScale)) SetBoneDirty(item.target.gameObject).SetScale(scale, item.target);
					if (Includes(item.channels, BlendChannels.Rotation) && !SameBlendRotation(rot, item.currentRotation))
					{
						SetBoneDirty(item.target.gameObject).SetRotation(rot, item.target);
						ApplyPresetRotationToActiveFK(item.target, rot);
					}
					result.Add(item.target.gameObject, item.channels);
				}
				ApplyBoneManualCorrection();
				_blendResultSelection = result;
				SetBonePresetStatus("Blend applied. Save result opens Create with only the blended modified channels selected.");
				TraceBlend("commit", session);
			}
			catch (Exception e)
			{
				// A failed commit is atomic for its own channels, without overwriting unrelated edits.
				foreach (BlendBone item in session.bones)
				{
					if (item.target == null) continue;
					TransformData data = SetBoneDirty(item.target.gameObject);
					if (Includes(item.channels, BlendChannels.Position)) { data.position = item.before.position; data.originalPosition = item.before.originalPosition; }
					if (Includes(item.channels, BlendChannels.Rotation)) { data.rotation = item.before.rotation; data.originalRotation = item.before.originalRotation; }
					if (Includes(item.channels, BlendChannels.Scale)) { data.scale = item.before.scale; data.originalScale = item.before.originalScale; }
					if (DirtyChannels(data) == BlendChannels.None) _dirtyBones.Remove(item.target.gameObject);
					try { if (item.fk != null && item.fk.guideObject != null) item.fk.guideObject.changeAmount.rot = item.fkRotation; }
					catch (Exception restoreError) { Debug.LogError("HSPE: FK callback threw while restoring a failed blend: " + restoreError); }
				}
				RestoreBlendTransforms(session);
				ApplyBoneManualCorrection();
				SetBonePresetError("Blend was not applied; starting edits restored. " + e.Message, e);
			}
		}

		private void SelectBlendResultForSave()
		{
			if (_blendResultSelection == null) return;
			SynchronizeBonePresetSelections();
			foreach (KeyValuePair<GameObject, BonePresetSelection> pair in _bonePresetSelections)
			{
				BlendChannels channels;
				_blendResultSelection.TryGetValue(pair.Key, out channels);
				TransformData data = _dirtyBones[pair.Key];
				pair.Value.position = Includes(channels, BlendChannels.Position) && data.position.hasValue;
				pair.Value.rotation = Includes(channels, BlendChannels.Rotation) && data.rotation.hasValue;
				pair.Value.scale = Includes(channels, BlendChannels.Scale) && data.scale.hasValue;
			}
			_bonePresetBoneSearch = "";
			_bonePresetName = "";
			SetBonePresetPage(true);
			SetBonePresetStatus("Only blended modified channels are selected. Name the result to save it.");
		}

		private void CancelBlendForExternalEdit(Transform bone, BlendChannels channel)
		{
			if (_blendSession == null) return;
			foreach (BlendBone item in _blendSession.bones)
				if (item.target == bone && Includes(item.channels, channel))
				{ CancelBoneBlend("Preview cancelled: another editor or Timeline wrote a blended channel."); return; }
		}

		private void UpdateBoneBlendLifecycle()
		{
			if (_blendSession == null) return;
			if (!bonePresetsVisible || !_blendPage || MainWindow._self == null || !MainWindow._self.enabled
				|| !MainWindow._self.gameObject.activeInHierarchy || MainWindow._self._poseTarget != _parent)
				CancelBoneBlend("Preview cancelled: Blend is no longer the active editor.");
			else ValidateBlendSession();
		}

		internal static bool AllowSaveDuringBoneBlend()
		{
			if (_activeBlendEditor == null || _activeBlendEditor._blendSession == null) return true;
			_activeBlendEditor.SetBonePresetError("Save blocked: Apply blend or Cancel the preview, then save again.");
			Debug.LogWarning("HSPE: Save blocked while a bone blend preview is active. Apply blend or Cancel, then save again.");
			return false;
		}

#if HONEYSELECT
		internal static bool OmitCardForBlendPreview()
		{
			if (_activeBlendEditor == null || _activeBlendEditor._blendSession == null) return false;
			_activeBlendEditor.SetBonePresetStatus("Card save continues without HSPE bone data because preview is active. Preview and checkbox are unchanged.");
			return true;
		}
#endif

		internal static void CancelActiveBoneBlend(string reason)
		{
			if (_activeBlendEditor != null) _activeBlendEditor.CancelBoneBlend(reason);
		}

		internal static void CancelBoneBlendForTarget(ObjectCtrlInfo target)
		{
			if (_activeBlendEditor != null && _activeBlendEditor._target.oci == target)
				_activeBlendEditor.CancelBoneBlend("Preview cancelled: character or pose replaced.");
		}

		public override void OnCharacterReplaced() { CancelBoneBlend("Preview cancelled: character replaced."); }
		public override void OnLoadClothesFile() { CancelBoneBlend("Preview cancelled: clothing replaced."); }

		private static bool FiniteVector(Vector3 v) { return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z); }
		private static bool NormalizeBlendRotation(ref Quaternion q)
		{
			if (!IsFinite(q.x) || !IsFinite(q.y) || !IsFinite(q.z) || !IsFinite(q.w)) return false;
			double length = Math.Sqrt((double)q.x * q.x + (double)q.y * q.y + (double)q.z * q.z + (double)q.w * q.w);
			if (length < .001) return false; // Same minimum magnitude as the preset parser, computed without float overflow.
			q = new Quaternion((float)(q.x / length), (float)(q.y / length), (float)(q.z / length), (float)(q.w / length));
			return true;
		}
		private static Quaternion BlendRotation(Quaternion a, Quaternion b, float t)
		{
			if (t <= 0f) return a;
			if (t >= 1f) return b;
			if (Quaternion.Dot(a, b) < 0f) b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
			if (a.Equals(b)) return a;
			return Quaternion.SlerpUnclamped(a, b, t);
		}
		private static bool SameBlendRotation(Quaternion a, Quaternion b)
		{
			// a is already normalized by the endpoint/SLERP path. Normalize the captured value once,
			// exactly as when building the endpoints. Exact components retain HSPE's 0.00001-degree edits.
			NormalizeBlendRotation(ref b);
			if (Quaternion.Dot(a, b) < 0f) b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
			return a.Equals(b);
		}
		private static Vector3 BlendVector(Vector3 a, Vector3 b, float t)
		{
			if (t <= 0f) return a;
			if (t >= 1f) return b;
			// Double intermediates prevent finite large endpoints overflowing during subtraction.
			return new Vector3((float)((1.0 - t) * a.x + (double)t * b.x), (float)((1.0 - t) * a.y + (double)t * b.y), (float)((1.0 - t) * a.z + (double)t * b.z));
		}
		private static bool RiskyBlendScale(Vector3 a, Vector3 b)
		{
			return RiskyScaleAxis(a.x, b.x) || RiskyScaleAxis(a.y, b.y) || RiskyScaleAxis(a.z, b.z);
		}
		private static bool RiskyScaleAxis(float a, float b) { return a == 0f || b == 0f || (a < 0f) != (b < 0f); }
		private static void TraceBlend(string action, BlendSession session)
		{
			if (VerboseBlendLogs) Debug.Log("HSPE BoneBlend " + action + ": bones=" + session.bones.Count + " channels=" + session.channels
				+ " protected=" + session.protectedChannels + " missing=" + session.missingBones + " invalid=" + session.invalidChannels
				+ " riskyScale=" + session.riskyScales + " weight=" + session.weight);
		}
	}
}
