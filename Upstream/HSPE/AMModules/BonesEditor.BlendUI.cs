using System;
using System.Collections.Generic;
using System.IO;
using ToolBox;
using UnityEngine;

namespace HSPE.AMModules
{
	public partial class BonesEditor
	{
		private bool _blendPage, _blendChooseA;
		private string _blendSearch = "";
		private Vector2 _blendLibraryScroll, _blendChannelsScroll;
		private BonePresetPreview _blendAPreview, _blendBPreview;

		private void RefreshBlendSourcePreviews()
		{
			_blendAPreview = _blendA == null ? null : PreviewBonePreset(_blendA);
			_blendBPreview = _blendB == null ? null : PreviewBonePreset(_blendB);
		}

		private bool DrawBlendSourceButton(Rect rect, bool endpointA, bool active)
		{
			BonePresetData preset = endpointA ? _blendA : _blendB;
			BonePresetPreview preview = endpointA ? _blendAPreview : _blendBPreview;
			string path = endpointA ? _blendAPath : _blendBPath;
			string label = endpointA ? "A: " : "B: ";
			string hint;
			bool different = preset != null && !BonePresetSourcesMatch(preset.source, CurrentBonePresetSource());
			if (preset == null)
			{
				label += endpointA ? (active ? "Current state (captured)" : "Current state") : "Choose a preset from the list";
				hint = endpointA ? "Current state is captured once when Start preview is clicked." : "Select a preset for B. Selecting does not apply edits.";
			}
			else
			{
				string matches = (preview == null ? 0 : preview.matchedBones) + "/" + preset.bones.Count + " bones found";
				label += (different ? "Other source | " : "") + matches + " | " + Path.GetFileNameWithoutExtension(path);
				hint = Path.GetFileNameWithoutExtension(path) + ". Source: " + BonePresetSourceName(preset.source) + ". " + matches
					+ (different ? ". Different source from this target. Review the matched paths before preview." : ". Missing or ambiguous paths are skipped; extra target bones do not matter.");
			}
			Color color = GUI.color;
			GUI.color = different ? ColorPresets.Changed : endpointA == _blendChooseA ? ColorPresets.Selected : color;
			bool clicked = GUI.Button(rect, new GUIContent(label, hint), _presetListButton);
			GUI.color = color;
			return clicked;
		}

		private void DrawBoneBlend(Rect body)
		{
			bool enabled = GUI.enabled;
			Color color = GUI.color;
			bool active = _blendSession != null;
			BlendSession review = active ? _blendSession : _blendReview;
			float libraryWidth = Mathf.Clamp(body.width * .30f, 160f, 250f);
			Rect library = new Rect(body.x, body.y, libraryWidth, body.height);
			Rect detail = new Rect(library.xMax + 6f, body.y, body.width - libraryWidth - 6f, body.height);
			GUI.Box(library, GUIContent.none);
			GUI.Box(detail, GUIContent.none);
			GUI.enabled = enabled && !active;
			float half = (library.width - 13f) / 2f;
			if (PresetTab(new Rect(library.x + 5f, library.y + 5f, half, 23f), "Choose A", _blendChooseA)) _blendChooseA = true;
			if (PresetTab(new Rect(library.x + 8f + half, library.y + 5f, half, 23f), "Choose B", !_blendChooseA)) _blendChooseA = false;
			GUI.enabled = enabled && !active && _blendChooseA;
			if (GUI.Button(new Rect(library.x + 5f, library.y + 33f, library.width - 10f, 23f), new GUIContent("Use current state for A", "Captured once when Start preview is clicked."))) SetBlendSource(true, null);
			GUI.enabled = enabled && !active;
			_blendSearch = DrawPresetSearch(new Rect(library.x + 5f, library.y + 62f, library.width - 10f, 22f), _blendSearch, "BlendSearch");
			DrawShowAllBonePresets(new Rect(library.x + 5f, library.y + 90f, library.width - 10f, 22f));
			List<BonePresetLibraryEntry> files = FilteredBonePresetFiles(_blendSearch);
			Rect viewport = new Rect(library.x + 4f, library.y + 114f, library.width - 8f, Mathf.Max(0f, library.height - 146f));
			Rect contents = new Rect(0f, 0f, viewport.width - 18f, Mathf.Max(viewport.height, files.Count * _presetRowHeight));
			_blendLibraryScroll = GUI.BeginScrollView(viewport, _blendLibraryScroll, contents, false, true);
			int index = 0;
			BonePresetSource source = CurrentBonePresetSource();
			foreach (BonePresetLibraryEntry file in files)
			{
				GUI.enabled = enabled && !active && file.error == null;
				GUI.color = file.error != null ? ColorPresets.Tomato
					: string.Equals(file.path, _blendChooseA ? _blendAPath : _blendBPath, StringComparison.OrdinalIgnoreCase) ? ColorPresets.Selected
					: BonePresetSourcesMatch(file.source, source) ? color : ColorPresets.Changed;
				if (GUI.Button(new Rect(0f, index++ * _presetRowHeight, contents.width, 22f), new GUIContent(Path.GetFileNameWithoutExtension(file.path), BonePresetLibraryHint(file)), _presetListButton)) SetBlendSource(_blendChooseA, file.path);
			}
			GUI.enabled = enabled && !active;
			if (files.Count == 0) GUI.Label(new Rect(3f, 3f, contents.width - 6f, 90f), EmptyBonePresetLibraryHint(_blendSearch), _presetWrappedLabel);
			GUI.color = color;
			GUI.EndScrollView();
			if (GUI.Button(new Rect(library.x + 5f, library.yMax - 28f, half, 23f), "Refresh")) RefreshBlendSources();
			if (GUI.Button(new Rect(library.x + 8f + half, library.yMax - 28f, half, 23f), "Folder")) OpenBonePresetFolder();

			float x = detail.x + 7f, w = detail.width - 14f;
			if (DrawBlendSourceButton(new Rect(x, detail.y + 5f, w, 23f), true, active)) _blendChooseA = true;
			if (DrawBlendSourceButton(new Rect(x, detail.y + 32f, w, 23f), false, active)) _blendChooseA = false;
			bool protect = GUI.Toggle(new Rect(x, detail.y + 61f, w - 123f, 22f), _blendProtectEdits,
				new GUIContent("Preserve dirty", "Keep position, rotation or scale values already marked dirty by HSPE before preview. Leave off to blend your current edits."));
			if (protect != _blendProtectEdits) { _blendProtectEdits = protect; _blendAcceptScaleRisk = false; _blendReview = BuildBlendSession(); review = _blendReview; }
			if (GUI.Button(new Rect(x + w - 117f, detail.y + 61f, 56f, 22f), "All")) SetBlendScope(true);
			if (GUI.Button(new Rect(x + w - 58f, detail.y + 61f, 58f, 22f), "None")) SetBlendScope(false);
			float extra = review != null && review.riskyScales > 0 ? 24f : 0f;
			if (extra > 0f)
			{
				GUI.color = ColorPresets.Tomato;
				_blendAcceptScaleRisk = GUI.Toggle(new Rect(x, detail.y + 85f, w, 22f), _blendAcceptScaleRisk,
					new GUIContent("Allow scale through zero (collapse / invert)", review.riskyScales + " scale channels reach or cross zero. Values are not clamped or silently changed."));
				GUI.color = color;
			}
			float column = 48f, nameWidth = w - 20f - column * 3f;
			DrawPresetChannelHeaders(new Rect(x, detail.y + 85f + extra, w, 20f), nameWidth, column);
			viewport = new Rect(x, detail.y + 106f + extra, w, Mathf.Max(0f, detail.height - 206f - extra));
			contents = new Rect(0f, 0f, w - 18f, Mathf.Max(viewport.height, _blendRows.Count * _presetRowHeight));
			GUI.enabled = enabled; // Reading and scrolling the scope remains possible during preview.
			_blendChannelsScroll = GUI.BeginScrollView(viewport, _blendChannelsScroll, contents, false, true);
			for (int i = 0; i < _blendRows.Count; ++i)
			{
				BlendRow row = _blendRows[i];
				float y = i * _presetRowHeight;
				Transform bone = ResolvePresetBone(row.path);
				TransformData data = null;
				if (bone != null) _dirtyBones.TryGetValue(bone.gameObject, out data);
				GUI.enabled = enabled && !active && bone != null;
				GUI.color = bone == null ? ColorPresets.Tomato : color;
				string tooltip = row.path + (bone == null ? " (missing)" : ". Missing source channels use the captured current state.");
				bool all = row.selected == row.available;
				bool select = GUI.Toggle(new Rect(0f, y, nameWidth, 22f), all, new GUIContent(BonePresetDisplayName(row.path), tooltip));
				if (all != select) { row.selected = select ? row.available : BlendChannels.None; _blendAcceptScaleRisk = false; }
				GUI.color = color;
				DrawBlendChannel(new Rect(nameWidth, y, column, 22f), row, BlendChannels.Position, data, bone == null);
				DrawBlendChannel(new Rect(nameWidth + column, y, column, 22f), row, BlendChannels.Rotation, data, bone == null);
				DrawBlendChannel(new Rect(nameWidth + column * 2f, y, column, 22f), row, BlendChannels.Scale, data, bone == null);
			}
			GUI.enabled = enabled;
			GUI.EndScrollView();
			string summary = review == null ? "Choose B to review the blend." : review.channels + " edits / " + review.bones.Count + " bones; " + review.protectedChannels + " preserved";
			bool disjoint = review != null && _blendA != null && review.sharedChannels == 0 && review.channels > 0;
			string explanation = disjoint ? "No shared channels: edits fade via the captured current state." : "Missing source channels use the captured current state.";
			if (review != null && (review.missingBones > 0 || review.invalidChannels > 0)) summary += "\nSkipped: " + review.missingBones + " missing bones, " + review.invalidChannels + " invalid values.";
			else if (disjoint) summary += "\nDifferent channels: gaps use current state.";
			GUI.Label(new Rect(x, detail.yMax - 97f, w, 38f), new GUIContent(summary, summary + " " + explanation), _presetWrappedLabel);

			GUI.enabled = enabled && active;
			float weight = active ? _blendSession.weight : -1f;
			float next = GUI.HorizontalSlider(new Rect(x, detail.yMax - 53f, w - 182f, 20f), weight, -1f, 1f);
			if (GUI.Button(new Rect(x + w - 177f, detail.yMax - 57f, 31f, 23f), new GUIContent("A", "-1: entirely A"))) next = -1f;
			if (GUI.Button(new Rect(x + w - 143f, detail.yMax - 57f, 46f, 23f), new GUIContent("50/50", "0: halfway between A and B"))) next = 0f;
			if (GUI.Button(new Rect(x + w - 94f, detail.yMax - 57f, 31f, 23f), new GUIContent("B", "+1: entirely B"))) next = 1f;
			GUI.Label(new Rect(x + w - 60f, detail.yMax - 57f, 60f, 23f), ((next + 1f) * 50f).ToString("0") + "% B", _presetLabel);
			if (next != weight) SetBoneBlendWeight(next);
			GUI.enabled = enabled;
			if (active)
			{
				GUI.color = ColorPresets.Selected;
				if (GUI.Button(new Rect(x, detail.yMax - 28f, w - 85f, 23f), "Apply blend")) CommitBoneBlend();
				GUI.color = color;
				if (GUI.Button(new Rect(x + w - 80f, detail.yMax - 28f, 80f, 23f), "Cancel")) CancelBoneBlend("Preview cancelled. Starting edits restored.");
			}
			else
			{
				GUI.enabled = enabled && BonePresetAllowed(_blendB) && (_blendA == null || BonePresetAllowed(_blendA)) && review != null && review.channels > 0 && (review.riskyScales == 0 || _blendAcceptScaleRisk);
				GUI.color = ColorPresets.Selected;
				if (GUI.Button(new Rect(x, detail.yMax - 28f, w - 115f, 23f), "Start preview")) BeginBoneBlend();
				GUI.color = color;
				GUI.enabled = enabled && _blendResultSelection != null;
				if (GUI.Button(new Rect(x + w - 110f, detail.yMax - 28f, 110f, 23f), "Save result...")) SelectBlendResultForSave();
			}
			GUI.enabled = enabled;
			GUI.color = color;
		}

		private void SetBlendScope(bool selected)
		{
			if (_blendSession != null) return;
			foreach (BlendRow row in _blendRows) row.selected = selected ? row.available : BlendChannels.None;
			_blendAcceptScaleRisk = false;
		}

		private void DrawBlendChannel(Rect rect, BlendRow row, BlendChannels channel, TransformData data, bool missing)
		{
			if (!Includes(row.available, channel)) { GUI.Label(rect, "-", _presetLabel); return; }
			if (missing) { GUI.Label(rect, "Missing", _presetLabel); return; }
			if (_blendProtectEdits && Includes(DirtyChannels(data), channel)) { GUI.Label(rect, "Keep", _presetLabel); return; }
			string source = (Includes(EntryChannels(row.a), channel) ? "A" : "Current") + " -> " + (Includes(EntryChannels(row.b), channel) ? "B" : "Current");
			bool selected = Includes(row.selected, channel);
			bool next = GUI.Toggle(rect, selected, new GUIContent("", channel + ": " + source));
			if (next != selected)
			{
				if (next) row.selected |= channel; else row.selected &= ~channel;
				_blendAcceptScaleRisk = false;
			}
		}

		private void RefreshBlendSources()
		{
			if (_blendSession != null) return;
			RefreshBonePresets();
		}

		private void ReloadBlendSources()
		{
			if (_blendSession != null) return;
			try
			{
				BonePresetData a = _blendAPath == null ? null : ReadBonePreset(_blendAPath);
				BonePresetData b = _blendBPath == null ? null : ReadBonePreset(_blendBPath);
				_blendA = a; _blendB = b;
				if (a != null) UpdateBonePresetLibraryEntry(_blendAPath, a.source);
				if (b != null) UpdateBonePresetLibraryEntry(_blendBPath, b.source);
				EnforceBonePresetFilter();
				RebuildBlendRows();
			}
			catch (Exception e)
			{
				_blendA = _blendB = null; _blendAPath = _blendBPath = null;
				RebuildBlendRows();
				SetBonePresetError("Blend sources could not be refreshed. Select them again. " + e.Message, e);
			}
		}
	}
}
