using System;
using System.Collections.Generic;
using System.IO;
using ToolBox;
using ToolBox.Extensions;
using UnityEngine;

namespace HSPE.AMModules
{
	public partial class BonesEditor
	{
		private bool? _pendingBonePresetVisibility;
		private bool _createBonePreset;
		private bool _confirmOverwriteBonePreset;
		private string _lastSavedBonePresetName;
		private BonePresetData _lastSavedBonePreset;
		private string _bonePresetSearch = "";
		private string _bonePresetBoneSearch = "";
		private Vector2 _bonePresetContentsScroll;
		private List<KeyValuePair<GameObject, TransformData>> _presetDirtyBones;
		private readonly List<BonePresetSaveRow> _presetSaveRows = new List<BonePresetSaveRow>();
		private readonly List<BonePresetContentRow> _presetContentRows = new List<BonePresetContentRow>();
		private BonePresetPreview _presetPreview;
		private GUISkin _presetSkin;
		private GUIStyle _presetLabel;
		private GUIStyle _presetWrappedLabel;
		private GUIStyle _presetListButton;
		private bool _presetTextFocused;
		private bool _clearPresetTextFocus;
		private const float _presetRowHeight = 24f;

		private sealed class BonePresetSaveRow
		{
			public GameObject bone;
			public TransformData data;
			public string path;
			public string name;
		}

		private sealed class BonePresetContentRow
		{
			public BonePresetEntry entry;
			public string name;
			public bool missing;
			public bool positionDirty;
			public bool rotationDirty;
			public bool scaleDirty;
		}

		internal bool bonePresetsVisible
		{
			get { return _showBonePresetsWindow && _isEnabled && _parent.enabled && PoseController._drawAdvancedMode; }
		}

		internal bool bonePresetTextFocused
		{
			get { return bonePresetsVisible && _presetTextFocused; }
		}

		private void OpenBonePresetsWindow()
		{
			CancelResetAllBonesConfirmation();
			// Change the GUILayout tree only on its next Layout event.
			_pendingBonePresetVisibility = true;
		}

		private void PrepareBonePresetsGUI()
		{
			if (Event.current.type != EventType.Layout)
				return;
			if (_pendingBonePresetVisibility.HasValue)
			{
				CancelBoneBlend("Preview cancelled: left Blend.");
				_blendPage = false;
				_showBonePresetsWindow = _pendingBonePresetVisibility.Value;
				_pendingBonePresetVisibility = null;
				_confirmDeleteBonePreset = false;
				_confirmOverwriteBonePreset = false;
				ClearBonePresetSaveFeedback();
				_presetTextFocused = false;
				GUI.FocusControl(null);
				if (_showBonePresetsWindow)
				{
					RefreshBonePresets();
					_createBonePreset = _bonePresetFiles.Length == 0;
				}
			}
			if (!_showBonePresetsWindow)
				return;

			EnforceBonePresetFilter();
			SynchronizeBonePresetSelections();
			_presetDirtyBones = GetSortedDirtyBones();
			_presetSaveRows.Clear();
			foreach (KeyValuePair<GameObject, TransformData> pair in _presetDirtyBones)
			{
				string path = pair.Key.transform.GetPathFrom(_parent.transform);
				string name = BonePresetDisplayName(path);
				// Match the bone itself, not an ancestor such as Armature and its entire subtree.
				if (pair.Key.name.IndexOf(_bonePresetBoneSearch, StringComparison.OrdinalIgnoreCase) >= 0
					|| name.IndexOf(_bonePresetBoneSearch, StringComparison.OrdinalIgnoreCase) >= 0)
					_presetSaveRows.Add(new BonePresetSaveRow { bone = pair.Key, data = pair.Value, path = path, name = name });
			}
			RefreshBonePresetPreview();
			if (_blendPage)
			{
				RefreshBlendSourcePreviews();
				if (_blendSession == null) _blendReview = BuildBlendSession();
			}
		}

		private static string BonePresetDisplayName(string path)
		{
			string name = path.Substring(path.LastIndexOf('/') + 1);
			string alias;
			return _boneAliases.TryGetValue(name, out alias) ? alias : name;
		}

		private void RefreshBonePresetPreview()
		{
			_presetContentRows.Clear();
			_presetPreview = _selectedBonePresetData == null ? null : PreviewBonePreset(_selectedBonePresetData);
			if (_selectedBonePresetData == null)
				return;
			foreach (BonePresetEntry entry in _selectedBonePresetData.bones)
			{
				Transform target = ResolvePresetBone(entry.path);
				TransformData data = null;
				if (target != null)
					_dirtyBones.TryGetValue(target.gameObject, out data);
				_presetContentRows.Add(new BonePresetContentRow
				{
					entry = entry, name = BonePresetDisplayName(entry.path), missing = target == null,
					positionDirty = data != null && data.position.hasValue,
					rotationDirty = data != null && data.rotation.hasValue,
					scaleDirty = data != null && data.scale.hasValue
				});
			}
		}

		private void DrawBonePresetsPane()
		{
			if (_clearPresetTextFocus)
			{
				GUI.FocusControl(null);
				_clearPresetTextFocus = false;
			}
			if (_presetSkin != GUI.skin)
			{
				_presetSkin = GUI.skin;
				_presetLabel = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
				// Fixed text rectangles already provide vertical spacing. The skin's extra padding
				// clips descenders in 18px rows and the second line in 32/34px summaries.
				_presetLabel.padding = new RectOffset(_presetLabel.padding.left, _presetLabel.padding.right, 0, 0);
				_presetWrappedLabel = new GUIStyle(_presetLabel) { wordWrap = true, alignment = TextAnchor.UpperLeft };
				_presetListButton = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
			}

			// A single layout slot owns all controls. Scroll contents never change the window size.
			Rect area = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
			GUI.BeginGroup(area);
			Color color = GUI.color;
			bool enabled = GUI.enabled;
			try
			{
				if (GUI.Button(new Rect(0f, 0f, 108f, 24f), "< Back to bones"))
				{
					CancelBoneBlend("Preview cancelled: returned to Bones.");
					_pendingBonePresetVisibility = false;
				}
				GUI.Label(new Rect(116f, 0f, 95f, 24f), "Bone presets", _presetLabel);
				float tabWidth = (area.width - 219f) / 3f;
				if (PresetTab(new Rect(219f, 0f, tabWidth - 2f, 24f), "Load preset", !_createBonePreset && !_blendPage))
					SetBonePresetPage(false);
				if (PresetTab(new Rect(219f + tabWidth, 0f, tabWidth - 2f, 24f), "Blend", _blendPage) && !_blendPage)
				{
					SetBonePresetPage(false);
					_blendPage = true;
					_blendReview = BuildBlendSession();
					SetBonePresetStatus("Choose A and B, then Start preview. Missing channels use the captured current state.");
				}
				if (PresetTab(new Rect(219f + tabWidth * 2f, 0f, tabWidth - 2f, 24f), "Create preset", _createBonePreset && !_blendPage))
					SetBonePresetPage(true);

				Rect body = new Rect(0f, 30f, area.width, Mathf.Max(0f, area.height - 68f));
				if (_blendPage)
					DrawBoneBlend(body);
				else if (_createBonePreset)
					DrawBonePresetCreate(body);
				else
					DrawBonePresetLoad(body);

				GUI.color = _bonePresetStatusIsError ? ColorPresets.Tomato : color;
				string targetName = _target.oci != null && _target.oci.treeNodeObject != null ? _target.oci.treeNodeObject.textName : _parent.name;
				string status = _bonePresetStatus.Length != 0 ? _bonePresetStatus
					: "Target: " + targetName + ". Position / Rotation / Scale are separate edits.";
				if (!string.IsNullOrEmpty(GUI.tooltip)) status = GUI.tooltip;
				GUI.Label(new Rect(0f, area.height - 34f, area.width, 34f), new GUIContent(status, status), _presetWrappedLabel);
				_presetTextFocused = GUI.GetNameOfFocusedControl().StartsWith("HSPE.BonePreset.", StringComparison.Ordinal);
			}
			finally
			{
				GUI.color = color;
				GUI.enabled = enabled;
				GUI.EndGroup();
			}
		}

		private bool PresetTab(Rect rect, string label, bool selected)
		{
			Color color = GUI.color;
			if (selected) GUI.color = ColorPresets.Selected;
			bool clicked = GUI.Button(rect, label);
			GUI.color = color;
			return clicked;
		}

		private void SetBonePresetPage(bool create)
		{
			CancelBoneBlend("Preview cancelled: left Blend.");
			_blendPage = false;
			_createBonePreset = create;
			_confirmDeleteBonePreset = false;
			_confirmOverwriteBonePreset = false;
			ClearBonePresetSaveFeedback();
			GUI.FocusControl(null);
			SetBonePresetStatus("");
		}

		private string DrawPresetSearch(Rect rect, string value, string controlName)
		{
			GUI.Label(new Rect(rect.x, rect.y, 45f, rect.height), "Search", _presetLabel);
			GUI.SetNextControlName("HSPE.BonePreset." + controlName);
			value = GUI.TextField(new Rect(rect.x + 45f, rect.y, rect.width - 71f, rect.height), value);
			if (GUI.Button(new Rect(rect.xMax - 23f, rect.y, 23f, rect.height), "X"))
				value = "";
			return value;
		}

		private void DrawBonePresetCreate(Rect body)
		{
			float leftWidth = body.width * 0.62f;
			Rect list = new Rect(body.x, body.y, leftWidth, body.height);
			Rect form = new Rect(body.x + leftWidth + 6f, body.y, body.width - leftWidth - 6f, body.height);
			GUI.Box(list, GUIContent.none);
			GUI.Box(form, GUIContent.none);
			_bonePresetBoneSearch = DrawPresetSearch(new Rect(list.x + 5f, list.y + 5f, list.width - 10f, 22f), _bonePresetBoneSearch, "BoneSearch");
			GUI.Label(new Rect(list.x + 5f, list.y + 30f, list.width - 145f, 22f), "Modified bones (" + _presetDirtyBones.Count + ")", _presetLabel);
			if (GUI.Button(new Rect(list.xMax - 137f, list.y + 30f, 64f, 22f), new GUIContent("All", "Select all modified values shown by the search. Hidden selections stay unchanged."))) SetShownPresetSelections(true);
			if (GUI.Button(new Rect(list.xMax - 70f, list.y + 30f, 65f, 22f), new GUIContent("None", "Clear the values shown by the search. Hidden selections stay unchanged."))) SetShownPresetSelections(false);

			float column = 44f;
			float nameWidth = list.width - 30f - column * 3f;
			DrawPresetChannelHeaders(new Rect(list.x + 5f, list.y + 54f, list.width - 30f, 20f), nameWidth, column);
			Rect viewport = new Rect(list.x + 4f, list.y + 76f, list.width - 8f, Mathf.Max(0f, list.height - 80f));
			Rect contents = new Rect(0f, 0f, viewport.width - 18f, Mathf.Max(viewport.height, _presetSaveRows.Count * _presetRowHeight));
			_bonePresetSaveScroll = GUI.BeginScrollView(viewport, _bonePresetSaveScroll, contents, false, true);
			if (_presetSaveRows.Count == 0)
				GUI.Label(new Rect(4f, 4f, contents.width - 8f, 48f), _presetDirtyBones.Count == 0 ? "No modified bones. Edit bones first, then return here." : "No bones match your search.", _presetWrappedLabel);
			for (int i = 0; i < _presetSaveRows.Count; ++i)
			{
				BonePresetSaveRow row = _presetSaveRows[i];
				if (row.bone == null) continue;
				BonePresetSelection selection = _bonePresetSelections[row.bone];
				float y = i * _presetRowHeight;
				bool selected = selection.HasAllAvailableChannels(row.data);
				bool newSelected = GUI.Toggle(new Rect(2f, y, nameWidth, 22f), selected, new GUIContent(row.name, row.path));
				if (newSelected != selected) { selection.SetAll(row.data, newSelected); _confirmOverwriteBonePreset = false; }
				selection.position = DrawPresetChannelToggle(new Rect(nameWidth, y, column, 22f), selection.position, row.data.position.hasValue, "Position");
				selection.rotation = DrawPresetChannelToggle(new Rect(nameWidth + column, y, column, 22f), selection.rotation, row.data.rotation.hasValue, "Rotation");
				selection.scale = DrawPresetChannelToggle(new Rect(nameWidth + column * 2f, y, column, 22f), selection.scale, row.data.scale.hasValue, "Scale");
			}
			GUI.EndScrollView();

			float x = form.x + 8f;
			float w = form.width - 16f;
			GUI.Label(new Rect(x, form.y + 6f, w, 22f), "Preset name", _presetLabel);
			GUI.SetNextControlName("HSPE.BonePreset.Name");
			string newName = GUI.TextField(new Rect(x, form.y + 30f, w, 24f), _bonePresetName, 120);
			if (newName != _bonePresetName) { _bonePresetName = newName; _confirmOverwriteBonePreset = false; }
			int channels = CountSelectedBonePresetChannels(_presetDirtyBones);
			int bones = 0;
			foreach (KeyValuePair<GameObject, TransformData> pair in _presetDirtyBones)
				if (pair.Key != null && _bonePresetSelections[pair.Key].HasAnySelection) ++bones;
			GUI.Label(new Rect(x, form.y + 64f, w, 24f), bones + " bones / " + channels + " edits selected", _presetLabel);
			GUI.Label(new Rect(x, form.y + 93f, w, 58f), "Choose which modified values to save. Selections outside the search stay included.", _presetWrappedLabel);

			string safeName = SanitizeBonePresetName(_bonePresetName);
			bool exists = BonePresetNameExists(safeName);
			bool saved = MatchesLastBonePresetSave(safeName, _presetDirtyBones);
			if (!saved) ClearBonePresetSaveFeedback();
			string hint = saved ? "Preset saved.\nEnter a new name to save another copy."
				: safeName.Length == 0 ? "Enter a name to save this preset."
				: channels == 0 ? "Select at least one modified value."
				: exists ? "A preset with this name exists. Saving will replace that file."
				: "Only checked edits will be saved.";
			bool enabled = GUI.enabled;
			Color color = GUI.color;
			GUI.color = saved ? ColorPresets.Selected : color;
			GUI.Label(new Rect(x, form.yMax - 84f, w, 52f), hint, _presetWrappedLabel);
			GUI.enabled = enabled && !saved && safeName.Length != 0 && channels > 0;
			GUI.color = exists && !saved ? ColorPresets.Tomato : ColorPresets.Selected;
			float saveWidth = _confirmOverwriteBonePreset ? w - 63f : w;
			if (GUI.Button(new Rect(x, form.yMax - 29f, saveWidth, 24f), saved ? "Saved" : _confirmOverwriteBonePreset ? "Replace file" : "Save preset"))
			{
				if (exists && !_confirmOverwriteBonePreset) _confirmOverwriteBonePreset = true;
				else { SaveBonePreset(); _confirmOverwriteBonePreset = false; }
			}
			GUI.enabled = enabled;
			GUI.color = color;
			if (_confirmOverwriteBonePreset && GUI.Button(new Rect(form.xMax - 66f, form.yMax - 29f, 58f, 24f), "Cancel"))
				_confirmOverwriteBonePreset = false;
		}

		private void ClearBonePresetSaveFeedback()
		{
			if (_lastSavedBonePreset != null) SetBonePresetStatus("");
			_lastSavedBonePresetName = null;
			_lastSavedBonePreset = null;
		}

		private bool MatchesLastBonePresetSave(string name, List<KeyValuePair<GameObject, TransformData>> dirtyBones)
		{
			if (_lastSavedBonePreset == null || !string.Equals(name, _lastSavedBonePresetName, StringComparison.OrdinalIgnoreCase)
				|| !BonePresetSourcesMatch(_lastSavedBonePreset.source, CurrentBonePresetSource()))
				return false;
			// Compare the actual saved channels, not a timer or the number of selected bones.
			// Dirty values can also change outside this form (for example through Timeline).
			int index = 0;
			foreach (KeyValuePair<GameObject, TransformData> pair in dirtyBones)
			{
				if (pair.Key == null) continue;
				BonePresetSelection selection = _bonePresetSelections[pair.Key];
				if (!selection.HasAnySelection) continue;
				if (index >= _lastSavedBonePreset.bones.Count) return false;
				BonePresetEntry entry = _lastSavedBonePreset.bones[index++];
				bool position = selection.position && pair.Value.position.hasValue;
				bool rotation = selection.rotation && pair.Value.rotation.hasValue;
				bool scale = selection.scale && pair.Value.scale.hasValue;
				if (entry.path != pair.Key.transform.GetPathFrom(_parent.transform)
					|| entry.position.HasValue != position || entry.rotation.HasValue != rotation || entry.scale.HasValue != scale
					|| (position && !entry.position.Value.Equals(pair.Value.position.value))
					|| (rotation && !entry.rotation.Value.Equals(pair.Value.rotation.value))
					|| (scale && !entry.scale.Value.Equals(pair.Value.scale.value))) return false;
			}
			return index == _lastSavedBonePreset.bones.Count;
		}

		private bool DrawPresetChannelToggle(Rect rect, bool selected, bool available, string name)
		{
			if (!available) { GUI.Label(rect, "-", _presetLabel); return false; }
			Color color = GUI.color;
			GUI.color = selected ? ColorPresets.Changed : color;
			bool result = GUI.Toggle(rect, selected, new GUIContent("", name));
			GUI.color = color;
			if (result != selected) _confirmOverwriteBonePreset = false;
			return result;
		}

		private void SetShownPresetSelections(bool selected)
		{
			foreach (BonePresetSaveRow row in _presetSaveRows)
				if (row.bone != null) _bonePresetSelections[row.bone].SetAll(row.data, selected);
			_confirmOverwriteBonePreset = false;
		}

		private void DrawPresetChannelHeaders(Rect rect, float nameWidth, float column)
		{
			GUI.Label(new Rect(rect.x, rect.y, nameWidth, rect.height), "Bone", _presetLabel);
			GUI.Label(new Rect(rect.x + nameWidth, rect.y, column, rect.height), new GUIContent("Pos", "Position"), _presetLabel);
			GUI.Label(new Rect(rect.x + nameWidth + column, rect.y, column, rect.height), new GUIContent("Rot", "Rotation"), _presetLabel);
			GUI.Label(new Rect(rect.x + nameWidth + column * 2f, rect.y, column, rect.height), "Scale", _presetLabel);
		}

		private void DrawBonePresetLoad(Rect body)
		{
			float libraryWidth = Mathf.Clamp(body.width * 0.30f, 160f, 250f);
			Rect library = new Rect(body.x, body.y, libraryWidth, body.height);
			Rect detail = new Rect(library.xMax + 6f, body.y, body.width - libraryWidth - 6f, body.height);
			GUI.Box(library, GUIContent.none);
			GUI.Box(detail, GUIContent.none);
			_bonePresetSearch = DrawPresetSearch(new Rect(library.x + 5f, library.y + 5f, library.width - 10f, 22f), _bonePresetSearch, "LibrarySearch");
			DrawShowAllBonePresets(new Rect(library.x + 5f, library.y + 31f, library.width - 10f, 22f));
			List<BonePresetLibraryEntry> files = FilteredBonePresetFiles(_bonePresetSearch);
			Rect viewport = new Rect(library.x + 4f, library.y + 57f, library.width - 8f, Mathf.Max(0f, library.height - 90f));
			Rect contents = new Rect(0f, 0f, viewport.width - 18f, Mathf.Max(viewport.height, files.Count * _presetRowHeight));
			_bonePresetLoadScroll = GUI.BeginScrollView(viewport, _bonePresetLoadScroll, contents, false, true);
			Color color = GUI.color;
			if (files.Count == 0) GUI.Label(new Rect(3f, 3f, contents.width - 6f, 90f), EmptyBonePresetLibraryHint(_bonePresetSearch), _presetWrappedLabel);
			BonePresetSource source = CurrentBonePresetSource();
			for (int i = 0; i < files.Count; ++i)
			{
				BonePresetLibraryEntry file = files[i];
				GUI.color = file.error != null ? ColorPresets.Tomato
					: string.Equals(file.path, _selectedBonePresetPath, StringComparison.OrdinalIgnoreCase) ? ColorPresets.Selected
					: BonePresetSourcesMatch(file.source, source) ? color : ColorPresets.Changed;
				if (GUI.Button(new Rect(0f, i * _presetRowHeight, contents.width, 22f), new GUIContent(Path.GetFileNameWithoutExtension(file.path), BonePresetLibraryHint(file)), _presetListButton))
				{
					SelectBonePreset(file.path);
					RefreshBonePresetPreview();
					_bonePresetContentsScroll = Vector2.zero;
				}
			}
			GUI.color = color;
			GUI.EndScrollView();
			float half = (library.width - 13f) / 2f;
			if (GUI.Button(new Rect(library.x + 5f, library.yMax - 28f, half, 23f), "Refresh")) { RefreshBonePresets(); RefreshBonePresetPreview(); }
			if (GUI.Button(new Rect(library.x + 8f + half, library.yMax - 28f, half, 23f), "Folder")) OpenBonePresetFolder();

			float x = detail.x + 7f;
			float w = detail.width - 14f;
			if (_selectedBonePresetPath == null)
			{
				GUI.Label(new Rect(x, detail.y + 10f, w, 70f), "Select a preset to see its bones and loading options.\n\nBone changes apply only when you click Load selected.", _presetWrappedLabel);
				return;
			}
			GUI.Label(new Rect(x, detail.y + 4f, w, 22f), new GUIContent(Path.GetFileNameWithoutExtension(_selectedBonePresetPath), _selectedBonePresetPath), _presetLabel);
			float modeWidth = (w - 3f) / 2f;
			if (PresetTab(new Rect(x, detail.y + 30f, modeWidth, 24f), "Keep current edits", _bonePresetLoadMode == BonePresetLoadMode.PreserveExistingEdits)) _bonePresetLoadMode = BonePresetLoadMode.PreserveExistingEdits;
			if (PresetTab(new Rect(x + modeWidth + 3f, detail.y + 30f, modeWidth, 24f), "Overwrite edits", _bonePresetLoadMode == BonePresetLoadMode.OverwriteSavedEdits)) _bonePresetLoadMode = BonePresetLoadMode.OverwriteSavedEdits;
			BonePresetSource presetSource = _selectedBonePresetData == null ? null : _selectedBonePresetData.source;
			bool differentSource = presetSource != null && !BonePresetSourcesMatch(presetSource, source);
			string sourceLabel = presetSource == null ? "Unsupported or invalid preset" : (differentSource ? "Different source: " : "Source: ") + BonePresetSourceName(presetSource);
			GUI.color = presetSource == null ? ColorPresets.Tomato : differentSource ? ColorPresets.Changed : color;
			GUI.Label(new Rect(x, detail.y + 56f, w, 18f), new GUIContent(sourceLabel, sourceLabel + ". Bone paths are checked separately; no remapping is attempted."), _presetLabel);
			GUI.color = color;
			GUI.Label(new Rect(x, detail.y + 74f, w, 18f), _bonePresetLoadMode == BonePresetLoadMode.PreserveExistingEdits
				? "Keep dirty values; load only clean values below."
				: "Overwrite listed values; leave other values unchanged.", _presetLabel);
			float column = 57f;
			float nameWidth = w - 20f - column * 3f;
			DrawPresetChannelHeaders(new Rect(x, detail.y + 92f, w, 20f), nameWidth, column);
			viewport = new Rect(x, detail.y + 113f, w, Mathf.Max(0f, detail.height - 178f));
			contents = new Rect(0f, 0f, w - 18f, Mathf.Max(viewport.height, _presetContentRows.Count * _presetRowHeight));
			_bonePresetContentsScroll = GUI.BeginScrollView(viewport, _bonePresetContentsScroll, contents, false, true);
			for (int i = 0; i < _presetContentRows.Count; ++i)
			{
				BonePresetContentRow row = _presetContentRows[i];
				float y = i * _presetRowHeight;
				GUI.Label(new Rect(0f, y, nameWidth, 22f), new GUIContent(row.name, row.entry.path), _presetLabel);
				DrawPresetOutcome(new Rect(nameWidth, y, column, 22f), row.entry.position.HasValue, row.positionDirty, row.missing);
				DrawPresetOutcome(new Rect(nameWidth + column, y, column, 22f), row.entry.rotation.HasValue, row.rotationDirty, row.missing);
				DrawPresetOutcome(new Rect(nameWidth + column * 2f, y, column, 22f), row.entry.scale.HasValue, row.scaleDirty, row.missing);
			}
			GUI.EndScrollView();
			int applyCount = _presetPreview == null ? 0 : _presetPreview.ChannelsToApply(_bonePresetLoadMode);
			string summary = _presetPreview == null ? "Preset could not be read. See the message below."
				: _presetPreview.matchedBones + "/" + _selectedBonePresetData.bones.Count + " bones found; " + applyCount + " edits to apply; "
					+ (_bonePresetLoadMode == BonePresetLoadMode.PreserveExistingEdits ? _presetPreview.conflictingChannels + " kept." : _presetPreview.conflictingChannels + " replaced.");
			if (_presetPreview != null && (_presetPreview.missingBones != 0 || _presetPreview.invalidChannels != 0)) summary += "\nMissing / ambiguous: " + _presetPreview.missingBones + "; invalid values: " + _presetPreview.invalidChannels + ".";
			else if (applyCount == 0 && _presetPreview != null && _presetPreview.conflictingChannels > 0) summary += "\nUse Overwrite edits to apply the dirty values.";
			GUI.Label(new Rect(x, detail.yMax - 62f, w, 32f), new GUIContent(summary, summary), _presetWrappedLabel);
			bool enabled = GUI.enabled;
			GUI.enabled = enabled && BonePresetAllowed(_selectedBonePresetData) && applyCount > 0 && !_confirmDeleteBonePreset;
			GUI.color = _bonePresetLoadMode == BonePresetLoadMode.OverwriteSavedEdits && _presetPreview != null && _presetPreview.conflictingChannels > 0 ? ColorPresets.Changed : ColorPresets.Selected;
			if (GUI.Button(new Rect(x, detail.yMax - 28f, w - 132f, 23f), "Load selected"))
			{
				LoadBonePreset(_selectedBonePresetPath);
				RefreshBonePresetPreview();
			}
			GUI.enabled = enabled;
			GUI.color = ColorPresets.Tomato;
			float deleteWidth = _confirmDeleteBonePreset ? 75f : 126f;
			if (GUI.Button(new Rect(x + w - 126f, detail.yMax - 28f, deleteWidth, 23f), _confirmDeleteBonePreset ? "Delete file" : "Delete..."))
			{
				if (_confirmDeleteBonePreset) { DeleteBonePreset(_selectedBonePresetPath); RefreshBonePresetPreview(); }
				else _confirmDeleteBonePreset = true;
			}
			GUI.color = color;
			if (_confirmDeleteBonePreset && GUI.Button(new Rect(x + w - 48f, detail.yMax - 28f, 48f, 23f), "Cancel")) _confirmDeleteBonePreset = false;
		}

		private void DrawPresetOutcome(Rect rect, bool present, bool dirty, bool missing)
		{
			Color color = GUI.color;
			string text = !present ? "-" : missing ? "Missing" : !dirty ? "Apply" : _bonePresetLoadMode == BonePresetLoadMode.PreserveExistingEdits ? "Keep" : "Replace";
			if (present) GUI.color = missing ? ColorPresets.Tomato : dirty ? ColorPresets.Changed : ColorPresets.Selected;
			GUI.Label(rect, text, _presetLabel);
			GUI.color = color;
		}

		private void OpenBonePresetFolder()
		{
			try { EnsureBonePresetsDirectory(); System.Diagnostics.Process.Start(_bonePresetsPath); }
			catch (Exception e) { SetBonePresetError("Could not open the preset folder: " + e.Message, e); }
		}

		internal void ClearBonePresetTextFocus()
		{
			_presetTextFocused = false;
			_clearPresetTextFocus = true;
		}
	}

	// Capture follows a press that started in the pane, even if a drag leaves its bounds.
	internal sealed class BonePresetPointerCapture
	{
		private bool _captured;
		public bool Update(bool visible, bool inside, bool pressed, bool held)
		{
			if (!visible) { _captured = false; return false; }
			_captured = held && (_captured || (inside && pressed));
			return inside || _captured;
		}
	}
}
