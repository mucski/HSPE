using System.Collections;
using System.IO;
using HSPE.AMModules;
using HSPE.Kit.Undo;
using Studio;
using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Advanced
{
    using HSUIKit;

    // Dev-only functional test of the kit Bone presets page (UserData/HSPE/selftest.txt = "bonepresets"; run by
    // tools/selftest.ps1 -Mode bonepresets). Drives the page's real controls - the sidebar row, tabs, checkboxes,
    // name field, buttons, slider and the confirmation modals - on one added character, and measures bone values.
    // Writes only __selftest_A / __selftest_B into the bone preset folder and deletes them again through the page.
    internal sealed partial class BonePresetsPage
    {
        private const string PresetA = "__selftest_A", PresetB = "__selftest_B";

        private static void Check(bool ok, string what, string detail = null) { UndoSelfTest.Check(ok, "bp " + what, detail); }
        private static void Info(string s) { UndoSelfTest.Info("bp " + s); }

        private static T Find<T>(Transform root, System.Func<T, bool> match) where T : Component
        {
            if (root == null) return null;
            foreach (T c in root.GetComponentsInChildren<T>(false))
                if (match(c)) return c;
            return null;
        }

        private static KitButton ButtonLabelled(Transform root, string label)
        {
            return Find<KitButton>(root, b => b.Label == label);
        }

        private static IEnumerator Click(KitButton b, string what)
        {
            if (b == null) { Check(false, "found button " + what); yield break; }
            if (b.OnClick != null) b.OnClick();
            yield return UndoSelfTest.Frames(3);
        }

        /// <summary>Presses a button of the KitModal that is up (they live in the kit popup layer).</summary>
        private static IEnumerator ConfirmModal(string label)
        {
            yield return UndoSelfTest.Seconds(0.4f);
            KitButton b = ButtonLabelled(Kit.PopupLayer, label);
            Check(b != null, "modal shows \"" + label + "\"");
            if (b != null && b.OnClick != null)
                b.OnClick();
            yield return UndoSelfTest.Seconds(0.4f);
        }

        private static IEnumerator ClickRow(Transform list, string rowName, string what)
        {
            Transform row = list != null ? list.Find(rowName) : null;
            KitHoverClick hc = row != null ? row.GetComponentInChildren<KitHoverClick>() : null;
            Check(hc != null, "row " + what + " is listed");
            if (hc != null && hc.OnClick != null)
                hc.OnClick();
            yield return UndoSelfTest.Frames(3);
        }

        private static IEnumerator SidebarRow(MainWindow main, string label)
        {
            Transform row = null;
            foreach (Transform t in main._kitAdvanced.GetComponentsInChildren<Transform>(false))
                if (t.name == "Row " + label) { row = t; break; }
            if (row == null)
            {
                RectTransform rect = main._kitAdvanced.WindowRect;
                if (rect != null)
                    foreach (Transform t in rect.GetComponentsInChildren<Transform>(false))
                        if (t.name == "Row " + label) { row = t; break; }
            }
            KitHoverClick hc = row != null ? row.GetComponentInChildren<KitHoverClick>() : null;
            Check(hc != null, "sidebar row \"" + label + "\"");
            if (hc != null && hc.OnClick != null)
                hc.OnClick();
            yield return UndoSelfTest.Seconds(0.3f);
        }

        /// <summary>Layout audit (children spilling out of their parents) plus a screenshot of the tab.</summary>
        private static IEnumerator Look(BonePresetsPage p, string tab)
        {
            yield return UndoSelfTest.Seconds(0.5f); // tab fade + tweens settle
            int issues = KitLayoutAudit.Run(p._root, "BonePresets/" + tab);
            Check(issues == 0, "layout audit " + tab, issues + " issue(s)");
            string shot = Path.GetFullPath(Path.Combine(Path.Combine("UserData", "HSPE"), "selftest_" + tab + ".png"));
            Application.CaptureScreenshot(shot);
            yield return UndoSelfTest.Frames(3);
        }

        private static string PresetFile(string name) { return Path.Combine(Path.Combine(Path.Combine("Plugins", "HSPE"), "BonePresets"), name + ".xml"); }

        private static IEnumerator SaveAs(BonePresetsPage p, string name)
        {
            p._tabs.Select((int)BonesEditor.KitPresetTab.Create, true);
            yield return UndoSelfTest.Seconds(0.3f);
            p._saveName.Text = name;
            if (p._bones.KitPresetName != name && p._saveName.OnChanged != null)
                p._saveName.OnChanged(name);
            yield return UndoSelfTest.Frames(2);
            yield return Click(p._saveButton, "Save preset");
            if (File.Exists(PresetFile(name)) == false)
                yield return ConfirmModal("Replace file"); // only when a leftover file of an older run exists
        }

        internal static IEnumerator SelfTest(MainWindow main)
        {
            Info("start");
            while (Studio.Studio.Instance == null || Studio.Studio.Instance.treeNodeCtrl == null) yield return null;
            yield return UndoSelfTest.Seconds(4f);
            yield return UndoSelfTest.HideRecoveryModal();
            foreach (string leftover in new[] { PresetA, PresetB })
                if (File.Exists(PresetFile(leftover))) File.Delete(PresetFile(leftover));

            string[] cards = Directory.GetFiles("UserData/chara/female", "*.png");
            if (cards.Length == 0) { Check(false, "setup: a female card"); UndoSelfTest.Info("done"); yield break; }
            OCICharFemale a = AddObjectFemale.Add(cards[0]);
            yield return UndoSelfTest.Seconds(1.5f);
            yield return UndoSelfTest.Select(main, a);
            PoseController c = main._poseTarget;
            if (c == null || c.target.oci != a) { Check(false, "setup: character selected"); UndoSelfTest.Info("done"); yield break; }
            if (PoseController._drawAdvancedMode == false)
                c.ToggleAdvancedMode();
            yield return UndoSelfTest.Frames(10);
            Check(main._kitAdvanced != null && main._kitAdvanced.WindowRect != null, "setup: kit Advanced window shown");
            if (main._kitAdvanced == null) { UndoSelfTest.Info("done"); yield break; }
            BonesEditor bones = c._bonesEditor;
            // screenshots: NeoUIManager's recovery dialog (IMGUI, after a force-closed run) sits in the screen centre -
            // put the window right of it, at its minimum width (780: also checks the page at its tightest)
            RectTransform win = main._kitAdvanced.WindowRect;
            if (win != null)
            {
                win.sizeDelta = new Vector2(780f, Mathf.Max(win.sizeDelta.y, 600f));
                yield return UndoSelfTest.Frames(2);
                Vector3[] corners = new Vector3[4];
                win.GetWorldCorners(corners);
                win.position += new Vector3(Screen.width - 8f - corners[2].x, Screen.height - 60f - corners[1].y, 0f);
                yield return UndoSelfTest.Frames(2);
            }

            // 1. the sidebar row opens the page; Gru's lifecycle sees his pane as visible
            yield return SidebarRow(main, "Bone presets");
            BonePresetsPage p = Last;
            Check(p != null && p._root.gameObject.activeInHierarchy && p._bones == bones, "1 sidebar row shows the Bone presets page");
            Check(bones.bonePresetsVisible, "1 his preview lifecycle sees the pane as visible");

            Transform spine = UndoSelfTest.FindDeep(c.transform, "cf_J_Spine02_s");
            Transform neck = UndoSelfTest.FindDeep(c.transform, "cf_J_Neck_s");
            if (spine == null || neck == null) { Check(false, "setup: test bones"); UndoSelfTest.Info("done"); yield break; }
            bones.KitSelect(spine);
            Vector3 spine0 = bones.KitGetPosition();
            Vector3 atA = spine0 + new Vector3(0.05f, 0f, 0f), atB = spine0 + new Vector3(0.15f, 0f, 0f), changed = spine0 + new Vector3(0.10f, 0f, 0f);
            bones.KitSetPosition(atA);
            bones.KitSelect(neck);
            Quaternion neckRot = Quaternion.Euler(10f, 0f, 0f);
            bones.KitSetRotation(neckRot, true);
            yield return UndoSelfTest.Seconds(0.4f);

            // 2. Create: both modified bones listed; untick the neck; the file holds only the spine
            p._tabs.Select((int)BonesEditor.KitPresetTab.Create, true);
            yield return UndoSelfTest.Seconds(0.4f);
            Check(bones.KitPresetPage == BonesEditor.KitPresetTab.Create, "2 Create tab is his Create page");
            int rows = 0;
            foreach (Transform t in p._saveRows) if (t.gameObject.activeSelf) rows++;
            Check(rows == 2, "2 Create lists the two modified bones", rows + " rows");
            string neckName = bones.KitSaveRows().Find(r => r.Bone == neck.gameObject) != null ? bones.KitSaveRows().Find(r => r.Bone == neck.gameObject).Name : neck.name;
            KitCheckbox neckBox = Find<KitCheckbox>(p._saveRows, k => k.GetComponentInChildren<Text>() != null && k.GetComponentInChildren<Text>().text == neckName);
            Check(neckBox != null, "2 the neck row has a checkbox", neckName);
            if (neckBox != null) neckBox.SetValue(false, false, true);
            yield return UndoSelfTest.Frames(3);
            int selBones, selChannels;
            bones.KitSaveCounts(out selBones, out selChannels);
            Check(selBones == 1 && selChannels == 1, "2 unticking the neck leaves 1 bone / 1 value", selBones + " / " + selChannels);
            yield return SaveAs(p, PresetA);
            string fileA = File.Exists(PresetFile(PresetA)) ? File.ReadAllText(PresetFile(PresetA)) : "";
            Check(fileA.Contains(spine.name) && fileA.Contains(neck.name) == false, "2 saved file has the spine and not the neck", fileA.Length + " chars");
            Check(p._saveButton.Label == "Saved", "2 Save button shows Saved", p._saveButton.Label);
            yield return Look(p, "Create");

            // 3. Load: Keep leaves a value already changed; Overwrite applies it; the neck stays as it is
            bones.KitSelect(spine);
            bones.KitSetPosition(changed);
            p._tabs.Select((int)BonesEditor.KitPresetTab.Load, true);
            yield return UndoSelfTest.Seconds(0.4f);
            yield return ClickRow(p._loadList, "Preset " + PresetA, PresetA);
            yield return UndoSelfTest.Seconds(0.3f);
            Check(p._loadDetail.gameObject.activeSelf && bones.KitSelectedPresetName == PresetA, "3 clicking the row selects it (nothing applied)");
            bones.KitSelect(spine);
            Check(UndoSelfTest.Near(bones.KitGetPosition(), changed), "3 selecting changed nothing");
            Check(p._loadButton.Interactable == false, "3 Keep current edits: nothing to apply on a changed bone");
            p._loadMode.Select(1, true);
            yield return UndoSelfTest.Seconds(0.3f);
            Check(bones.KitOverwriteMode && p._loadButton.Interactable, "3 Overwrite edits enables Load selected");
            yield return Click(p._loadButton, "Load selected");
            bones.KitSelect(spine);
            Check(UndoSelfTest.Near(bones.KitGetPosition(), atA), "3 Load selected put the saved spine value back", bones.KitGetPosition().ToString("F3"));
            yield return Look(p, "Load");
            Check(Quaternion.Angle(neck.localRotation, neckRot) < 0.01f, "3 the neck (not in the preset) is unchanged");

            // 4. Blend: B = a second preset, A = current state; 50/50 is halfway; Cancel restores; Apply keeps B
            bones.KitSelect(spine);
            bones.KitSetPosition(atB);
            yield return UndoSelfTest.Seconds(0.3f);
            yield return SaveAs(p, PresetB);
            Check(File.Exists(PresetFile(PresetB)), "4 second preset saved");
            bones.KitSelect(spine);
            bones.KitSetPosition(atA);
            p._tabs.Select((int)BonesEditor.KitPresetTab.Blend, true);
            yield return UndoSelfTest.Seconds(0.4f);
            p._blendChoose.Select(1, true);
            yield return UndoSelfTest.Frames(3);
            yield return ClickRow(p._blendList, "Preset " + PresetB, PresetB + " (blend list)");
            yield return UndoSelfTest.Seconds(0.3f);
            Check(p._blendStart.Interactable, "4 Start preview is enabled with B chosen, A = current state");
            yield return Click(p._blendStart, "Start preview");
            Check(bones.KitBlendActive, "4 preview running");
            p._blendSlider.SetValue(0f, false, true);
            yield return UndoSelfTest.Frames(3);
            Vector3 mid = (atA + atB) * 0.5f;
            Check(UndoSelfTest.Near(spine.localPosition, mid), "4 slider at 50/50 shows the halfway value", spine.localPosition.ToString("F3") + " vs " + mid.ToString("F3"));
            Check(p._blendPercent.Value == "50% B", "4 readout says 50% B", p._blendPercent.Value);
            yield return Look(p, "Blend");
            yield return Click(p._blendCancel, "Cancel");
            bones.KitSelect(spine);
            Check(bones.KitBlendActive == false && UndoSelfTest.Near(spine.localPosition, atA), "4 Cancel restores the starting value", spine.localPosition.ToString("F3"));
            yield return Click(p._blendStart, "Start preview (again)");
            yield return Click(p._blendB, "B");
            yield return Click(p._blendApply, "Apply blend");
            bones.KitSelect(spine);
            Check(bones.KitBlendActive == false && UndoSelfTest.Near(bones.KitGetPosition(), atB) && bones.KitIsDirty(spine.gameObject), "4 Apply at B keeps B's value as an edit", bones.KitGetPosition().ToString("F3"));
            Check(p._blendSave.Interactable, "4 Save result... is offered after Apply");

            // 5. leaving the page cancels a running preview (his rule), values back as before it
            p._blendChoose.Select(0, true);
            yield return UndoSelfTest.Frames(3);
            yield return ClickRow(p._blendList, "Preset " + PresetA, PresetA + " as A");
            yield return UndoSelfTest.Seconds(0.3f);
            yield return Click(p._blendStart, "Start preview (A to B)");
            p._blendSlider.SetValue(0f, false, true);
            yield return UndoSelfTest.Frames(3);
            bool previewing = bones.KitBlendActive;
            yield return SidebarRow(main, "Bones");
            Check(previewing && bones.KitBlendActive == false && UndoSelfTest.Near(spine.localPosition, atB), "5 leaving Bone presets cancels the preview", spine.localPosition.ToString("F3"));

            // 6. Bones page: Reset All (modal) and the On card chip
            yield return UndoSelfTest.Seconds(0.5f);
            // the toolbar row holding Reset All + On card (the bone tree's own long names are not this change's)
            KitButton chip = ButtonLabelled(main._kitAdvanced.WindowRect, "Save to card");
            RectTransform toolbar = chip != null ? chip.transform.parent as RectTransform : null;
            int bonesIssues = toolbar != null ? KitLayoutAudit.Run(toolbar, "Advanced/Bones toolbar") : -1;
            Check(bonesIssues == 0, "layout audit Bones toolbar (Reset All + Save to card)", bonesIssues + " issue(s)");
            RectTransform searchBox = toolbar != null && toolbar.childCount != 0 ? toolbar.GetChild(0) as RectTransform : null;
            float searchWidth = searchBox != null ? searchBox.rect.width : 0f;
            Check(searchWidth >= 119f, "search box keeps its room at the minimum window width", searchWidth.ToString("F0") + " px");
            Application.CaptureScreenshot(Path.GetFullPath(Path.Combine(Path.Combine("UserData", "HSPE"), "selftest_Bones.png")));
            yield return UndoSelfTest.Frames(3);
            KitButton resetAll = ButtonLabelled(main._kitAdvanced.WindowRect, "Reset All...");
            Check(resetAll != null && resetAll.Interactable, "6 Reset All... is on the Bones page and enabled");
            int before = bones.KitChangedCount();
            yield return Click(resetAll, "Reset All...");
            yield return ConfirmModal("Reset All");
            Check(before >= 2 && bones.KitChangedCount() == 0, "6 Reset All put every modified bone back", before + " -> " + bones.KitChangedCount());
            bones.KitSelect(spine);
            Check(UndoSelfTest.Near(spine.localPosition, spine0), "6 spine at its original value");
            KitButton card = ButtonLabelled(main._kitAdvanced.WindowRect, "Save to card");
            bool cardBefore = bones.KitSaveOnCard;
            yield return Click(card, "Save to card");
            Check(card != null && bones.KitSaveOnCard != cardBefore, "6 Save to card toggles his Save current settings on card");
            yield return Click(card, "Save to card (back)");

            // 7. Delete (with its modal) removes our two files
            yield return SidebarRow(main, "Bone presets");
            p._tabs.Select((int)BonesEditor.KitPresetTab.Load, true);
            yield return UndoSelfTest.Seconds(0.3f);
            foreach (string name in new[] { PresetA, PresetB })
            {
                yield return ClickRow(p._loadList, "Preset " + name, name);
                yield return UndoSelfTest.Seconds(0.2f);
                yield return Click(p._deleteButton, "Delete...");
                yield return ConfirmModal("Delete file");
                Check(File.Exists(PresetFile(name)) == false, "7 Delete removed " + name);
            }
            foreach (string leftover in new[] { PresetA, PresetB })
                if (File.Exists(PresetFile(leftover))) File.Delete(PresetFile(leftover));

            UndoSelfTest.Info("result " + UndoSelfTest.Passed + " passed, " + UndoSelfTest.Failed + " failed");
            UndoSelfTest.Info("done");
        }
    }
}
