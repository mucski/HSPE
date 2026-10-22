using System;
using System.Collections.Generic;
using System.IO;
using ToolBox.Extensions;
using UnityEngine;

namespace HSPE.AMModules
{
    // Kit access to Gru's bone presets / Blend / Reset All / card setting (HSPE 2.15.0, merged in 2.16.0).
    // His logic and state are used as they are - the kit Bone presets page reads and sets the same fields his IMGUI
    // pane (BonesEditor.PresetsUI.cs / BlendUI.cs) uses and calls the same methods, so his tested rules hold in both
    // windows. Only the few lines of his UI code that call GUI.FocusControl (illegal outside OnGUI) are mirrored here.
    public partial class BonesEditor
    {
        internal enum KitPresetTab { Load = 0, Blend = 1, Create = 2 }
        internal enum KitOutcome { None, Apply, Keep, Replace, Missing }
        internal enum KitBlendCell { None, Missing, Keep, Toggle }

        internal sealed class KitPresetFile
        {
            public string Path, Name, Hint;
            public bool Error, OtherSource, Selected;
        }

        internal sealed class KitLoadRow
        {
            public string Name, Path;
            public KitOutcome Position, Rotation, Scale;
        }

        internal sealed class KitSaveRow
        {
            public GameObject Bone;
            public string Name, Path;
            public bool HasPosition, HasRotation, HasScale, Position, Rotation, Scale, All;
        }

        internal sealed class KitBlendRow
        {
            public int Index;
            public string Name, Tip;
            public bool Missing, All;
            public KitBlendCell Position, Rotation, Scale;
            public bool OnPosition, OnRotation, OnScale;
            public string TipPosition, TipRotation, TipScale;
        }

        private bool _kitPresetsShown;

        // ---------------------------------------------------------------- page lifecycle

        /// <summary>The kit page opened / closed. His preview lifecycle needs the pane "visible" (bonePresetsVisible).</summary>
        internal void KitPresetsShown(bool shown)
        {
            if (shown == _kitPresetsShown)
                return;
            _kitPresetsShown = shown;
            CancelResetAllBonesConfirmation();
            CancelBoneBlend(shown ? "Preview cancelled: left Blend." : "Preview cancelled: left Bone presets.");
            _showBonePresetsWindow = shown;
            _blendPage = false;
            _confirmDeleteBonePreset = false;
            _confirmOverwriteBonePreset = false;
            ClearBonePresetSaveFeedback();
            _presetTextFocused = false;
            if (shown)
            {
                RefreshBonePresets();
                _createBonePreset = _bonePresetFiles.Length == 0;
                KitPresetsPrepare();
            }
        }

        internal KitPresetTab KitPresetPage
        {
            get { return _blendPage ? KitPresetTab.Blend : _createBonePreset ? KitPresetTab.Create : KitPresetTab.Load; }
        }

        /// <summary>His SetBonePresetPage (+ the Blend tab's entry), without GUI.FocusControl.</summary>
        internal void KitSetPresetPage(KitPresetTab tab)
        {
            if (tab == KitPresetPage)
                return;
            CancelBoneBlend("Preview cancelled: left Blend.");
            _blendPage = false;
            _createBonePreset = tab == KitPresetTab.Create;
            _confirmDeleteBonePreset = false;
            _confirmOverwriteBonePreset = false;
            ClearBonePresetSaveFeedback();
            SetBonePresetStatus("");
            if (tab == KitPresetTab.Blend)
            {
                _blendPage = true;
                _blendReview = BuildBlendSession();
                SetBonePresetStatus("Choose A and B, then Start preview. Missing channels use the captured current state.");
            }
            KitPresetsPrepare();
        }

        /// <summary>His PrepareBonePresetsGUI (the per-Layout part), run by the kit page a few times a second.</summary>
        internal void KitPresetsPrepare()
        {
            EnforceBonePresetFilter();
            SynchronizeBonePresetSelections();
            _presetDirtyBones = GetSortedDirtyBones();
            _presetSaveRows.Clear();
            foreach (KeyValuePair<GameObject, TransformData> pair in _presetDirtyBones)
            {
                string path = pair.Key.transform.GetPathFrom(_parent.transform);
                string name = BonePresetDisplayName(path);
                if (pair.Key.name.IndexOf(_bonePresetBoneSearch, StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf(_bonePresetBoneSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    _presetSaveRows.Add(new BonePresetSaveRow { bone = pair.Key, data = pair.Value, path = path, name = name });
            }
            RefreshBonePresetPreview();
            if (_blendPage)
            {
                RefreshBlendSourcePreviews();
                if (_blendSession == null)
                    _blendReview = BuildBlendSession();
            }
        }

        /// <summary>Changes whenever something a list shows changes (the page rebuilds its rows only then).</summary>
        internal int KitPresetsStamp()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + _bonePresetFiles.Length;
                h = h * 31 + (_showAllBonePresets ? 1 : 0);
                h = h * 31 + (int)_bonePresetLoadMode;
                h = h * 31 + (_selectedBonePresetPath != null ? _selectedBonePresetPath.GetHashCode() : 0);
                h = h * 31 + (_selectedBonePresetData != null ? _selectedBonePresetData.GetHashCode() : 0);
                h = h * 31 + _presetSaveRows.Count;
                foreach (BonePresetSaveRow row in _presetSaveRows)
                {
                    BonePresetSelection s;
                    if (row.bone == null || _bonePresetSelections.TryGetValue(row.bone, out s) == false)
                        continue;
                    h = h * 31 + row.bone.GetInstanceID();
                    h = h * 31 + (row.data.position.hasValue ? 1 : 0) + (row.data.rotation.hasValue ? 2 : 0) + (row.data.scale.hasValue ? 4 : 0);
                    h = h * 31 + (s.position ? 1 : 0) + (s.rotation ? 2 : 0) + (s.scale ? 4 : 0);
                }
                foreach (BonePresetContentRow row in _presetContentRows)
                    h = h * 31 + (row.missing ? 1 : 0) + (row.positionDirty ? 2 : 0) + (row.rotationDirty ? 4 : 0) + (row.scaleDirty ? 8 : 0);
                h = h * 31 + (_blendPage ? 1 : 0) + (_blendChooseA ? 2 : 0) + (_blendSession != null ? 4 : 0) + (_blendProtectEdits ? 8 : 0)
                    + (_blendAcceptScaleRisk ? 16 : 0) + (_blendResultSelection != null ? 32 : 0);
                h = h * 31 + (_blendAPath != null ? _blendAPath.GetHashCode() : 0);
                h = h * 31 + (_blendBPath != null ? _blendBPath.GetHashCode() : 0);
                foreach (BlendRow row in _blendRows)
                    h = h * 31 + (int)row.selected;
                BlendSession review = _blendSession ?? _blendReview;
                if (review != null)
                    h = h * 31 + review.channels + review.protectedChannels * 7 + review.riskyScales * 13 + review.missingBones * 17;
                h = h * 31 + _dirtyBones.Count;
                return h;
            }
        }

        // ---------------------------------------------------------------- status

        internal string KitPresetStatus
        {
            get
            {
                if (_bonePresetStatus.Length != 0)
                    return _bonePresetStatus;
                return "Target: " + KitTargetName + ". Position / Rotation / Scale are separate edits.";
            }
        }
        internal bool KitPresetStatusError { get { return _bonePresetStatus.Length != 0 && _bonePresetStatusIsError; } }
        internal string KitTargetName { get { return _target.oci != null && _target.oci.treeNodeObject != null ? _target.oci.treeNodeObject.textName : _parent.name; } }

        // ---------------------------------------------------------------- library (Load and Blend)

        internal bool KitShowAll
        {
            get { return _showAllBonePresets; }
            set { SetShowAllBonePresets(value); }
        }
        internal bool KitShowAllLocked { get { return _blendSession != null; } }
        internal string KitShowAllHint
        {
            get { return "Off: presets for " + BonePresetSourceName(CurrentBonePresetSource()) + ". On: inspect and intentionally reuse other sources. Locked during preview."; }
        }

        internal List<KitPresetFile> KitLibrary(string search, bool forBlend)
        {
            List<KitPresetFile> list = new List<KitPresetFile>();
            BonePresetSource source = CurrentBonePresetSource();
            string selected = forBlend ? (_blendChooseA ? _blendAPath : _blendBPath) : _selectedBonePresetPath;
            foreach (BonePresetLibraryEntry e in FilteredBonePresetFiles(search ?? ""))
                list.Add(new KitPresetFile
                {
                    Path = e.path,
                    Name = Path.GetFileNameWithoutExtension(e.path),
                    Hint = BonePresetLibraryHint(e),
                    Error = e.error != null,
                    OtherSource = e.error == null && BonePresetSourcesMatch(e.source, source) == false,
                    Selected = string.Equals(e.path, selected, StringComparison.OrdinalIgnoreCase)
                });
            return list;
        }

        internal string KitEmptyLibraryHint(string search) { return EmptyBonePresetLibraryHint(search ?? ""); }

        internal void KitRefreshLibrary()
        {
            if (_blendPage)
                RefreshBlendSources();
            else
            {
                RefreshBonePresets();
                RefreshBonePresetPreview();
            }
            KitPresetsPrepare();
        }

        internal void KitOpenPresetFolder() { OpenBonePresetFolder(); }

        // ---------------------------------------------------------------- Load

        internal void KitSelectPreset(string path)
        {
            SelectBonePreset(path);
            RefreshBonePresetPreview();
        }

        internal string KitSelectedPresetName { get { return _selectedBonePresetPath != null ? Path.GetFileNameWithoutExtension(_selectedBonePresetPath) : null; } }
        internal string KitSelectedPresetPath { get { return _selectedBonePresetPath; } }

        internal bool KitOverwriteMode
        {
            get { return _bonePresetLoadMode == BonePresetLoadMode.OverwriteSavedEdits; }
            set { _bonePresetLoadMode = value ? BonePresetLoadMode.OverwriteSavedEdits : BonePresetLoadMode.PreserveExistingEdits; }
        }

        /// <summary>0 = same source, 1 = different source, 2 = unreadable.</summary>
        internal int KitSelectedSource(out string label)
        {
            BonePresetSource presetSource = _selectedBonePresetData == null ? null : _selectedBonePresetData.source;
            bool different = presetSource != null && BonePresetSourcesMatch(presetSource, CurrentBonePresetSource()) == false;
            label = presetSource == null ? "Unsupported or invalid preset" : (different ? "Different source: " : "Source: ") + BonePresetSourceName(presetSource);
            return presetSource == null ? 2 : different ? 1 : 0;
        }

        internal string KitLoadModeHint
        {
            get
            {
                return _bonePresetLoadMode == BonePresetLoadMode.PreserveExistingEdits
                    ? "Keep values you already changed; load only the clean ones."
                    : "Overwrite the listed values; other values stay as they are.";
            }
        }

        internal List<KitLoadRow> KitLoadRows()
        {
            List<KitLoadRow> list = new List<KitLoadRow>();
            foreach (BonePresetContentRow row in _presetContentRows)
                list.Add(new KitLoadRow
                {
                    Name = row.name,
                    Path = row.entry.path,
                    Position = Outcome(row.entry.position.HasValue, row.positionDirty, row.missing),
                    Rotation = Outcome(row.entry.rotation.HasValue, row.rotationDirty, row.missing),
                    Scale = Outcome(row.entry.scale.HasValue, row.scaleDirty, row.missing)
                });
            return list;
        }

        private KitOutcome Outcome(bool present, bool dirty, bool missing)
        {
            if (!present) return KitOutcome.None;
            if (missing) return KitOutcome.Missing;
            if (!dirty) return KitOutcome.Apply;
            return _bonePresetLoadMode == BonePresetLoadMode.PreserveExistingEdits ? KitOutcome.Keep : KitOutcome.Replace;
        }

        internal string KitLoadSummary
        {
            get
            {
                if (_selectedBonePresetPath == null)
                    return "";
                int applyCount = _presetPreview == null ? 0 : _presetPreview.ChannelsToApply(_bonePresetLoadMode);
                string summary = _presetPreview == null ? "The preset could not be read. See the message below."
                    : _presetPreview.matchedBones + "/" + _selectedBonePresetData.bones.Count + " bones found; " + applyCount + " edits to apply; "
                        + _presetPreview.conflictingChannels + (_bonePresetLoadMode == BonePresetLoadMode.PreserveExistingEdits ? " kept." : " replaced.");
                if (_presetPreview != null && (_presetPreview.missingBones != 0 || _presetPreview.invalidChannels != 0))
                    summary += "\nMissing / ambiguous: " + _presetPreview.missingBones + "; invalid values: " + _presetPreview.invalidChannels + ".";
                else if (applyCount == 0 && _presetPreview != null && _presetPreview.conflictingChannels > 0)
                    summary += "\nUse Overwrite edits to apply the values you already changed.";
                return summary;
            }
        }

        internal bool KitCanLoad
        {
            get { return _selectedBonePresetPath != null && BonePresetAllowed(_selectedBonePresetData) && _presetPreview != null && _presetPreview.ChannelsToApply(_bonePresetLoadMode) > 0; }
        }

        internal bool KitLoadReplaces { get { return KitOverwriteMode && _presetPreview != null && _presetPreview.conflictingChannels > 0; } }

        internal void KitLoadSelected()
        {
            if (_selectedBonePresetPath == null)
                return;
            LoadBonePreset(_selectedBonePresetPath);
            RefreshBonePresetPreview();
        }

        internal void KitDeleteSelected()
        {
            if (_selectedBonePresetPath == null)
                return;
            DeleteBonePreset(_selectedBonePresetPath);
            RefreshBonePresetPreview();
        }

        // ---------------------------------------------------------------- Create

        internal string KitBoneSearch
        {
            get { return _bonePresetBoneSearch; }
            set { _bonePresetBoneSearch = value ?? ""; KitPresetsPrepare(); }
        }

        internal int KitModifiedBoneCount { get { return _presetDirtyBones != null ? _presetDirtyBones.Count : 0; } }

        internal List<KitSaveRow> KitSaveRows()
        {
            List<KitSaveRow> list = new List<KitSaveRow>();
            foreach (BonePresetSaveRow row in _presetSaveRows)
            {
                BonePresetSelection s;
                if (row.bone == null || _bonePresetSelections.TryGetValue(row.bone, out s) == false)
                    continue;
                list.Add(new KitSaveRow
                {
                    Bone = row.bone, Name = row.name, Path = row.path,
                    HasPosition = row.data.position.hasValue, HasRotation = row.data.rotation.hasValue, HasScale = row.data.scale.hasValue,
                    Position = s.position, Rotation = s.rotation, Scale = s.scale, All = s.HasAllAvailableChannels(row.data)
                });
            }
            return list;
        }

        /// <summary>channel 0 = the whole bone (his name toggle), 1 / 2 / 3 = position / rotation / scale.</summary>
        internal void KitSetSaveChannel(GameObject bone, int channel, bool on)
        {
            BonePresetSelection s;
            TransformData data;
            if (bone == null || _bonePresetSelections.TryGetValue(bone, out s) == false || _dirtyBones.TryGetValue(bone, out data) == false)
                return;
            if (channel == 0) s.SetAll(data, on);
            else if (channel == 1 && data.position.hasValue) s.position = on;
            else if (channel == 2 && data.rotation.hasValue) s.rotation = on;
            else if (channel == 3 && data.scale.hasValue) s.scale = on;
            _confirmOverwriteBonePreset = false;
        }

        internal void KitSelectShownForSave(bool on) { SetShownPresetSelections(on); }

        internal string KitPresetName
        {
            get { return _bonePresetName; }
            set
            {
                value = value ?? "";
                if (value.Length > 120) value = value.Substring(0, 120);
                if (value != _bonePresetName) { _bonePresetName = value; _confirmOverwriteBonePreset = false; }
            }
        }

        internal void KitSaveCounts(out int bones, out int channels)
        {
            channels = _presetDirtyBones != null ? CountSelectedBonePresetChannels(_presetDirtyBones) : 0;
            bones = 0;
            if (_presetDirtyBones == null)
                return;
            foreach (KeyValuePair<GameObject, TransformData> pair in _presetDirtyBones)
            {
                BonePresetSelection s;
                if (pair.Key != null && _bonePresetSelections.TryGetValue(pair.Key, out s) && s.HasAnySelection)
                    ++bones;
            }
        }

        /// <summary>0 = can save, 1 = just saved, 2 = needs a name, 3 = nothing selected. exists = would replace a file.</summary>
        internal int KitSaveState(out bool exists, out string hint)
        {
            string safeName = SanitizeBonePresetName(_bonePresetName);
            exists = BonePresetNameExists(safeName);
            int bones, channels;
            KitSaveCounts(out bones, out channels);
            bool saved = _presetDirtyBones != null && MatchesLastBonePresetSave(safeName, _presetDirtyBones);
            if (!saved && _lastSavedBonePreset != null)
                ClearBonePresetSaveFeedback();
            hint = saved ? "Preset saved. Enter a new name to save another copy."
                : safeName.Length == 0 ? "Enter a name to save this preset."
                : channels == 0 ? "Select at least one modified value."
                : exists ? "A preset with this name exists. Saving will replace that file."
                : "Only the checked values are saved.";
            return saved ? 1 : safeName.Length == 0 ? 2 : channels == 0 ? 3 : 0;
        }

        internal string KitSanitizedPresetName { get { return SanitizeBonePresetName(_bonePresetName); } }

        internal void KitSavePreset() { SaveBonePreset(); _confirmOverwriteBonePreset = false; }

        // ---------------------------------------------------------------- Blend

        internal bool KitChooseA
        {
            get { return _blendChooseA; }
            set { if (_blendSession == null) _blendChooseA = value; }
        }

        internal bool KitBlendActive { get { return _blendSession != null; } }

        internal void KitUseCurrentForA() { SetBlendSource(true, null); KitPresetsPrepare(); }

        internal void KitSetBlendSource(string path) { SetBlendSource(_blendChooseA, path); KitPresetsPrepare(); }

        internal string KitEndpoint(bool endpointA, out string hint, out bool otherSource)
        {
            BonePresetData preset = endpointA ? _blendA : _blendB;
            BonePresetPreview preview = endpointA ? _blendAPreview : _blendBPreview;
            string path = endpointA ? _blendAPath : _blendBPath;
            string label = endpointA ? "A: " : "B: ";
            otherSource = preset != null && !BonePresetSourcesMatch(preset.source, CurrentBonePresetSource());
            if (preset == null)
            {
                label += endpointA ? (_blendSession != null ? "Current state (captured)" : "Current state") : "Choose a preset from the list";
                hint = endpointA ? "Current state is captured once when Start preview is clicked." : "Select a preset for B. Selecting does not apply edits.";
                return label;
            }
            string matches = (preview == null ? 0 : preview.matchedBones) + "/" + preset.bones.Count + " bones found";
            label += Path.GetFileNameWithoutExtension(path) + "   ·   " + matches + (otherSource ? "   ·   other source" : "");
            hint = Path.GetFileNameWithoutExtension(path) + ". Source: " + BonePresetSourceName(preset.source) + ". " + matches
                + (otherSource ? ". Different source from this target. Review the matched paths before preview." : ". Missing or ambiguous paths are skipped; extra target bones do not matter.");
            return label;
        }

        internal bool KitProtectEdits
        {
            get { return _blendProtectEdits; }
            set
            {
                if (_blendSession != null || value == _blendProtectEdits) return;
                _blendProtectEdits = value;
                _blendAcceptScaleRisk = false;
                _blendReview = BuildBlendSession();
            }
        }

        internal int KitRiskyScales { get { BlendSession r = _blendSession ?? _blendReview; return r != null ? r.riskyScales : 0; } }

        internal bool KitAcceptScaleRisk
        {
            get { return _blendAcceptScaleRisk; }
            set { if (_blendSession == null) _blendAcceptScaleRisk = value; }
        }

        internal List<KitBlendRow> KitBlendRows()
        {
            List<KitBlendRow> list = new List<KitBlendRow>();
            for (int i = 0; i < _blendRows.Count; i++)
            {
                BlendRow row = _blendRows[i];
                Transform bone = ResolvePresetBone(row.path);
                TransformData data = null;
                if (bone != null) _dirtyBones.TryGetValue(bone.gameObject, out data);
                KitBlendRow k = new KitBlendRow
                {
                    Index = i,
                    Name = BonePresetDisplayName(row.path),
                    Tip = row.path + (bone == null ? " (missing)" : ". Missing source channels use the captured current state."),
                    Missing = bone == null,
                    All = row.selected == row.available
                };
                k.Position = BlendCell(row, BlendChannels.Position, data, bone == null, out k.OnPosition, out k.TipPosition);
                k.Rotation = BlendCell(row, BlendChannels.Rotation, data, bone == null, out k.OnRotation, out k.TipRotation);
                k.Scale = BlendCell(row, BlendChannels.Scale, data, bone == null, out k.OnScale, out k.TipScale);
                list.Add(k);
            }
            return list;
        }

        private KitBlendCell BlendCell(BlendRow row, BlendChannels channel, TransformData data, bool missing, out bool on, out string tip)
        {
            on = Includes(row.selected, channel);
            tip = null;
            if (!Includes(row.available, channel)) return KitBlendCell.None;
            if (missing) return KitBlendCell.Missing;
            if (_blendProtectEdits && Includes(DirtyChannels(data), channel)) return KitBlendCell.Keep;
            tip = channel + ": " + (Includes(EntryChannels(row.a), channel) ? "A" : "Current") + " -> " + (Includes(EntryChannels(row.b), channel) ? "B" : "Current");
            return KitBlendCell.Toggle;
        }

        /// <summary>channel 0 = the whole bone, 1 / 2 / 3 = position / rotation / scale.</summary>
        internal void KitSetBlendChannel(int index, int channel, bool on)
        {
            if (_blendSession != null || index < 0 || index >= _blendRows.Count) return;
            BlendRow row = _blendRows[index];
            if (channel == 0) row.selected = on ? row.available : BlendChannels.None;
            else
            {
                BlendChannels c = channel == 1 ? BlendChannels.Position : channel == 2 ? BlendChannels.Rotation : BlendChannels.Scale;
                if (on) row.selected |= c & row.available; else row.selected &= ~c;
            }
            _blendAcceptScaleRisk = false;
        }

        internal void KitBlendScope(bool on) { SetBlendScope(on); }

        internal string KitBlendSummary
        {
            get
            {
                BlendSession review = _blendSession ?? _blendReview;
                if (review == null) return "Choose B to review the blend.";
                string summary = review.channels + " edits / " + review.bones.Count + " bones; " + review.protectedChannels + " preserved";
                bool disjoint = _blendA != null && review.sharedChannels == 0 && review.channels > 0;
                if (review.missingBones > 0 || review.invalidChannels > 0)
                    summary += ". Skipped: " + review.missingBones + " missing bones, " + review.invalidChannels + " invalid values.";
                else if (disjoint)
                    summary += ". No shared channels: the gaps use the captured current state.";
                return summary;
            }
        }

        internal float KitBlendWeight { get { return _blendSession != null ? _blendSession.weight : -1f; } }
        internal void KitSetBlendWeight(float w) { SetBoneBlendWeight(w); }

        internal bool KitCanStartBlend
        {
            get
            {
                BlendSession review = _blendReview;
                return _blendSession == null && BonePresetAllowed(_blendB) && (_blendA == null || BonePresetAllowed(_blendA))
                    && review != null && review.channels > 0 && (review.riskyScales == 0 || _blendAcceptScaleRisk);
            }
        }

        internal void KitStartBlend() { BeginBoneBlend(); }
        internal void KitApplyBlend() { CommitBoneBlend(); KitPresetsPrepare(); }
        internal void KitCancelBlend() { CancelBoneBlend("Preview cancelled. Starting edits restored."); KitPresetsPrepare(); }
        internal bool KitCanSaveBlendResult { get { return _blendSession == null && _blendResultSelection != null; } }

        /// <summary>His SelectBlendResultForSave, without GUI.FocusControl: opens Create with only the blended values ticked.</summary>
        internal void KitSaveBlendResult()
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
            KitSetPresetPage(KitPresetTab.Create);
            SetBonePresetStatus("Only blended modified channels are selected. Name the result to save it.");
        }

        // ---------------------------------------------------------------- Reset All and card setting (Bones page)

        internal bool KitResetAllBones()
        {
            CancelResetAllBonesConfirmation();
            _resetAllBonesFailed = false;
            return ResetAllBoneEdits();
        }

        internal bool KitResetAllFailed { get { return _resetAllBonesFailed; } }

        internal bool KitIsCharacter { get { return _target.type == GenericOCITarget.Type.Character; } }

        internal bool KitSaveOnCard
        {
            get { return saveBonesOnCard; }
            set { if (value != saveBonesOnCard) { saveBonesOnCard = value; _boneCardStatus = ""; _boneCardError = false; } }
        }

        internal string KitCardStatus
        {
            get
            {
                return _boneCardStatus.Length != 0 ? _boneCardStatus
                    : saveBonesOnCard ? "All modified bone channels included." : "HSPE bone edits omitted from cards.";
            }
        }
        internal bool KitCardError { get { return _boneCardError; } }
    }
}
