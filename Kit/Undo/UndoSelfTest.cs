using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HSPE.AMModules;
using HSPE.Kit.Small;
using Studio;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HSPE.Kit.Undo
{
    /// <summary>
    /// Dev-only functional test of HspeUndo (UserData/HSPE/selftest.txt = "undo"; run by tools/selftest.ps1).
    /// Adds two female characters (first card) to the open scene, then drives real code: kit bone edits, Gru's Reset All,
    /// copy / paste, Joan's StartDrag / StopDrag, and Studio's own UndoRedoManager.Undo / Redo (what Ctrl+Z calls).
    /// Only the pointer / buttons / wheel are fed through HspeUndo.FakeInput - hit-testing against the real windows,
    /// SaveXml / LoadXml and the undo stack are the real ones. Saves nothing; the clipboard is put back.
    /// Logs "undotest: PASS / FAIL ..." lines and "undotest: done".
    /// </summary>
    internal static class UndoSelfTest
    {
        private const string TriggerPath = "UserData/HSPE/selftest.txt";
        private static bool _checked;
        private static int _pass, _fail;

        internal static void MaybeStart(MainWindow main)
        {
            if (_checked)
                return;
            _checked = true;
            string mode = "";
            try { mode = File.Exists(TriggerPath) ? File.ReadAllText(TriggerPath).Trim() : ""; }
            catch (Exception) { }
            if (mode == "undo")
                main.StartCoroutine(Run(main));
            else if (mode == "bonepresets")
                main.StartCoroutine(global::HSPE.Kit.Advanced.BonePresetsPage.SelfTest(main));
        }

        /// <summary>
        /// NeoUIManager asks "Recover last session?" after every force-closed run: wait for it (up to 15 s), then hide
        /// its blocker and window (a user would click Not now), so clicks and screenshots reach HSPE.
        /// </summary>
        internal static IEnumerator HideRecoveryModal()
        {
            float until = Time.unscaledTime + 15f;
            GameObject recovery = null;
            while (recovery == null && Time.unscaledTime < until)
            {
                recovery = GameObject.Find("NeoUIManager Recovery Input Blocker");
                yield return null;
            }
            int hidden = 0;
            foreach (RectTransform r in Resources.FindObjectsOfTypeAll<RectTransform>())
                if (r != null && r.gameObject.activeInHierarchy && r.name.StartsWith("NeoUIManager Recovery", StringComparison.Ordinal))
                {
                    r.gameObject.SetActive(false);
                    hidden++;
                }
            // the dialog window itself: found by its title, hidden below its canvas
            foreach (UnityEngine.UI.Text t in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>())
            {
                if (t == null || t.gameObject.activeInHierarchy == false || t.text == null || t.text.IndexOf("Recover last session", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Transform w = t.transform;
                while (w.parent != null && w.parent.GetComponent<Canvas>() == null)
                    w = w.parent;
                Info("recovery dialog: hiding " + w.name);
                w.gameObject.SetActive(false);
                hidden++;
            }
            // drawn with IMGUI: switch off the component that draws it (a user would click Not now)
            foreach (MonoBehaviour m in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (m == null || m.enabled == false || m.GetType().Name.IndexOf("Recovery", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Info("recovery dialog: disabling " + m.GetType().FullName);
                m.enabled = false;
                hidden++;
            }
            Info(recovery != null ? "hid NeoUIManager's recovery modal (" + hidden + " objects)" : "no NeoUIManager recovery modal within 15 s");
        }

        internal static int Passed { get { return _pass; } }
        internal static int Failed { get { return _fail; } }

        internal static void Check(bool ok, string what, string detail = null)
        {
            if (ok) _pass++; else _fail++;
            global::HSPE.Kit.Log.Write("undotest: " + (ok ? "PASS " : "FAIL ") + what + (detail != null ? "  (" + detail + ")" : ""));
        }

        internal static void Info(string s) { global::HSPE.Kit.Log.Write("undotest: " + s); }

        private static int Steps()
        {
            FieldInfo f = typeof(UndoRedoManager).GetField("undo", BindingFlags.Instance | BindingFlags.NonPublic);
            return ((Stack<ICommand>)f.GetValue(UndoRedoManager.Instance)).Count;
        }

        private static string TopStep()
        {
            FieldInfo f = typeof(UndoRedoManager).GetField("undo", BindingFlags.Instance | BindingFlags.NonPublic);
            Stack<ICommand> s = (Stack<ICommand>)f.GetValue(UndoRedoManager.Instance);
            return s.Count == 0 ? "none" : s.Peek().GetType().Name;
        }

        internal static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        internal static IEnumerator Seconds(float s)
        {
            float end = Time.unscaledTime + s;
            while (Time.unscaledTime < end) yield return null;
        }

        private static Vector2 Center(RectTransform r)
        {
            // kit windows are on an overlay canvas, Studio's own UI on a camera canvas
            Canvas canvas = null;
            foreach (Canvas c in r.GetComponentsInParent<Canvas>(true))
                canvas = c; // outermost last (Unity 5.3 has no Canvas.rootCanvas)
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(cam, r.TransformPoint(r.rect.center));
        }

        internal static GameObject TopUi(Vector2 p)
        {
            EventSystem es = EventSystem.current;
            if (es == null) return null;
            PointerEventData e = new PointerEventData(es) { position = p };
            List<RaycastResult> hits = new List<RaycastResult>();
            es.RaycastAll(e, hits);
            return hits.Count != 0 ? hits[0].gameObject : null;
        }

        /// <summary>
        /// Logs what the pointer point hits. Another plugin's full-screen modal (NeoUIManager's crash-recovery window
        /// after a force-closed run) would swallow every click; a user dismisses it, the test hides it and says so.
        /// </summary>
        private static void Aim(Vector2 at, string what)
        {
            for (int i = 0; i < 5; i++)
            {
                GameObject top = TopUi(at);
                if (top == null || top.name.IndexOf("Raycast Blocker", StringComparison.OrdinalIgnoreCase) < 0 || top.transform.parent == null)
                {
                    Info(what + ": pointer hits " + (top != null ? top.name : "no UI"));
                    return;
                }
                Info(what + ": hiding another plugin's modal " + top.transform.parent.name + " / " + top.name);
                top.transform.parent.gameObject.SetActive(false);
                top.SetActive(false);
            }
        }

        /// <summary>Hover (before-state), press, change, hold, release, wait for the gesture to end.</summary>
        private static IEnumerator Click(Vector2 at, Action change)
        {
            Aim(at, "click");
            HspeUndo.FakeInput.Active = true;
            HspeUndo.FakeInput.Mouse = at;
            HspeUndo.FakeInput.Pressed = HspeUndo.FakeInput.Held = HspeUndo.FakeInput.Wheel = HspeUndo.FakeInput.Key = false;
            yield return Frames(3);
            HspeUndo.FakeInput.Pressed = true;
            HspeUndo.FakeInput.Held = true;
            yield return null;
            HspeUndo.FakeInput.Pressed = false;
            if (change != null) change();
            yield return Frames(3);
            HspeUndo.FakeInput.Held = false;
            yield return Seconds(0.7f);
        }

        private static IEnumerator Wheel(Vector2 at, int ticks, Action change)
        {
            Aim(at, "wheel");
            HspeUndo.FakeInput.Active = true;
            HspeUndo.FakeInput.Mouse = at;
            HspeUndo.FakeInput.Pressed = HspeUndo.FakeInput.Held = HspeUndo.FakeInput.Wheel = false;
            yield return Frames(3);
            for (int i = 0; i < ticks; i++)
            {
                HspeUndo.FakeInput.Wheel = true;
                change(); // the same frame as the tick, as a field nudged by the EventSystem would be
                yield return null;
                HspeUndo.FakeInput.Wheel = false;
                yield return Seconds(0.1f);
            }
            yield return Seconds(0.7f);
        }

        internal static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        internal static bool Near(Vector3 a, Vector3 b) { return (a - b).sqrMagnitude < 1e-8f; }

        internal static IEnumerator Select(MainWindow main, OCIChar oci)
        {
            Studio.Studio.Instance.treeNodeCtrl.SelectSingle(oci.treeNodeObject);
            for (int i = 0; i < 60 && (main._poseTarget == null || main._poseTarget.target.oci != oci); i++)
                yield return null;
        }

        private static IEnumerator Run(MainWindow main)
        {
            string clipboard = GUIUtility.systemCopyBuffer;
            Info("start");
            while (Studio.Studio.Instance == null || Studio.Studio.Instance.treeNodeCtrl == null) yield return null;
            yield return Seconds(4f);
            // NeoUIManager shows a crash-recovery modal a few seconds in after a force-closed run: wait for it, hide it
            yield return HideRecoveryModal();

            string[] cards = Directory.GetFiles("UserData/chara/female", "*.png");
            if (cards.Length == 0) { Check(false, "setup: a female card in UserData/chara/female"); Info("done"); yield break; }
            OCICharFemale a = AddObjectFemale.Add(cards[0]);
            OCICharFemale b = AddObjectFemale.Add(cards[0]);
            yield return Seconds(1.5f);
            yield return Select(main, a);
            PoseController ca = main._poseTarget;
            Check(ca != null && ca.target.oci == a, "setup: character A selected, HSPE target");
            if (ca == null) { Info("done"); yield break; }
            if (PoseController._drawAdvancedMode == false)
                ca.ToggleAdvancedMode();
            if (main._kitSmall != null && main._kitSmall.visible == false)
                main._kitSmall.Toggle();
            yield return Frames(10);
            RectTransform adv = main._kitAdvanced != null ? main._kitAdvanced.WindowRect : null;
            RectTransform small = main._kitSmall != null ? main._kitSmall.WindowRect : null;
            Check(adv != null && small != null, "setup: kit Advanced + small windows shown");
            if (adv == null || small == null) { Info("done"); yield break; }
            Vector2 advAt = Center(adv);
            Vector2 smallAt = Center(small);
            GameObject advHit = TopUi(advAt), smallHit = TopUi(smallAt);
            Check(advHit != null && advHit.transform.IsChildOf(adv), "setup: pointer point is on the Advanced window", advHit != null ? advHit.name : "nothing");
            Check(smallHit != null && smallHit.transform.IsChildOf(small), "setup: pointer point is on the small window", smallHit != null ? smallHit.name : "nothing");

            // a 3D-view point (no uGUI, not Joan's IMGUI rect) and a Studio-UI point (the object list)
            Vector2 view = Vector2.zero;
            bool viewFound = false;
            for (int y = 2; y < 9 && viewFound == false; y++)
                for (int x = 2; x < 9 && viewFound == false; x++)
                {
                    Vector2 p = new Vector2(Screen.width * x / 10f, Screen.height * y / 10f);
                    if (TopUi(p) == null) { view = p; viewFound = true; }
                }
            Check(viewFound, "setup: a 3D-view point without UI", view.ToString());
            // any UI that is not HSPE's (Studio's own panels, another plugin): scanned at click time, after modals
            RectTransform faceRectNow = null;

            BonesEditor bones = ca._bonesEditor;
            Transform spine = FindDeep(ca.transform, "cf_J_Spine02_s") ?? FindDeep(ca.transform, "cf_J_Spine02");
            Transform neck = FindDeep(ca.transform, "cf_J_Neck_s") ?? FindDeep(ca.transform, "cf_J_Neck");
            Check(spine != null && neck != null, "setup: test bones found", (spine != null ? spine.name : "-") + ", " + (neck != null ? neck.name : "-"));
            if (spine == null || neck == null) { Info("done"); yield break; }
            bones.KitSelect(spine);
            yield return Frames(2);
            Vector3 spine0 = bones.KitGetPosition();
            Vector3 moved = spine0 + new Vector3(0.05f, 0f, 0f);

            // 1. one kit edit = one step; Ctrl+Z (Undo) restores it, Ctrl+Shift+Z (Redo) re-applies it
            int s0 = Steps();
            yield return Click(advAt, delegate { bones.KitSetPosition(moved); });
            Check(Steps() == s0 + 1, "1 bone edit on the Advanced window = 1 step", (Steps() - s0) + " step(s), top " + TopStep());
            UndoRedoManager.Instance.Undo();
            yield return Frames(3);
            bones.KitSelect(spine);
            Check(bones.KitIsDirty(spine.gameObject) == false && Near(spine.localPosition, spine0), "1 Undo: bone back, not modified", spine.localPosition.ToString("F4"));
            UndoRedoManager.Instance.Redo();
            yield return Frames(3);
            bones.KitSelect(spine);
            Check(bones.KitIsDirty(spine.gameObject) && Near(bones.KitGetPosition(), moved), "1 Redo: edit back", bones.KitGetPosition().ToString("F4"));

            // 2. a click in the 3D view that changes nothing (camera) = no step
            s0 = Steps();
            if (viewFound)
            {
                yield return Click(view, null);
                Check(Steps() == s0, "2 3D-view click without HSPE change = no step", (Steps() - s0) + " step(s)");
            }

            // 3. a change during a click on Studio's own UI is not HSPE's step; the next hover takes it as the new before
            s0 = Steps();
            Vector2 studioAt = Vector2.zero;
            GameObject studioHit = null;
            for (int y = 1; y < 20 && studioHit == null; y++)
                for (int x = 1; x < 20 && studioHit == null; x++)
                {
                    Vector2 p = new Vector2(Screen.width * x / 20f, Screen.height * y / 20f);
                    GameObject h = TopUi(p);
                    if (h != null && h.transform.IsChildOf(adv) == false && h.transform.IsChildOf(small) == false
                        && (faceRectNow == null || h.transform.IsChildOf(faceRectNow) == false))
                    {
                        studioHit = h;
                        studioAt = p;
                    }
                }
            bool studioOk = studioHit != null;
            Check(studioOk, "3 setup: a point on non-HSPE UI", studioHit != null ? studioHit.transform.root.name + " / " + studioHit.name + " at " + studioAt : "nothing");
            if (studioOk)
            {
                yield return Click(studioAt, delegate { bones.KitSelect(neck); bones.KitSetPosition(bones.KitGetPosition() + new Vector3(0f, 0.03f, 0f)); });
                Check(Steps() == s0, "3 change during a Studio-UI click = no HSPE step", (Steps() - s0) + " step(s)");
                yield return Seconds(0.1f);
                yield return Click(advAt, null);
                Check(Steps() == s0, "3 next Advanced click without change = no step (before-state refreshed on hover)", (Steps() - s0) + " step(s)");
            }

            // 4. a small-window drag: Joan's StopDrag pushes its own step, HSPE does not add a second one
            s0 = Steps();
            yield return Click(smallAt, delegate
            {
                ca.StartDrag(PoseController.DragType.Rotation);
                bones.KitSelect(spine);
                bones.KitSetPosition(bones.KitGetPosition() + new Vector3(0f, 0f, 0.02f));
                ca.StopDrag();
            });
            Check(Steps() == s0 + 1 && TopStep() == "MoveRotateEqualsCommand", "4 small-window drag = only Joan's step", (Steps() - s0) + " step(s), top " + TopStep());

            // 5. three wheel nudges in a row = one step; Undo goes back past all three
            bones.KitSelect(spine);
            yield return Frames(2);
            Vector3 beforeWheel = bones.KitGetPosition();
            s0 = Steps();
            yield return Wheel(advAt, 3, delegate { bones.KitSetPosition(bones.KitGetPosition() + new Vector3(0.01f, 0f, 0f)); });
            Check(Steps() == s0 + 1, "5 three wheel nudges = 1 step", (Steps() - s0) + " step(s)");
            UndoRedoManager.Instance.Undo();
            yield return Frames(3);
            bones.KitSelect(spine);
            Check(Near(bones.KitGetPosition(), beforeWheel), "5 Undo: back before all three nudges", bones.KitGetPosition().ToString("F4") + " vs " + beforeWheel.ToString("F4"));

            // 6. Gru's Reset All (the method his button calls) = one step; Undo brings every edit back
            List<Transform> dirtyBefore = bones.KitDirtyBones();
            MethodInfo resetAll = typeof(BonesEditor).GetMethod("ResetAllBoneEdits", BindingFlags.Instance | BindingFlags.NonPublic);
            s0 = Steps();
            yield return Click(advAt, delegate { resetAll.Invoke(bones, null); });
            int dirtyAfterReset = bones.KitChangedCount();
            Check(dirtyBefore.Count >= 2 && dirtyAfterReset == 0 && Steps() == s0 + 1, "6 Reset All = 1 step", dirtyBefore.Count + " modified -> " + dirtyAfterReset + ", " + (Steps() - s0) + " step(s)");
            UndoRedoManager.Instance.Undo();
            yield return Frames(3);
            Check(bones.KitChangedCount() == dirtyBefore.Count, "6 Undo: all modified bones back", bones.KitChangedCount() + " of " + dirtyBefore.Count);

            // 7. paste onto B (Copy / Paste are the small window's header actions) = exactly one step, undo / redo exact
            TransferActions.Copy();
            yield return Select(main, b);
            PoseController cb = main._poseTarget;
            Check(cb != null && cb.target.oci == b, "7 setup: character B selected");
            if (cb != null)
            {
                yield return Frames(5);
                small = main._kitSmall.WindowRect;
                smallAt = small != null ? Center(small) : smallAt;
                string bBefore = HspeUndo.State(cb);
                s0 = Steps();
                yield return Click(smallAt, delegate { TransferActions.Paste(); });
                string bAfter = HspeUndo.State(cb);
                Check(Steps() == s0 + 1 && bAfter != bBefore, "7 paste = 1 step and B changed", (Steps() - s0) + " step(s)");
                UndoRedoManager.Instance.Undo();
                yield return Frames(3);
                Check(HspeUndo.State(cb) == bBefore, "7 Undo: B exactly as before the paste");
                UndoRedoManager.Instance.Redo();
                yield return Frames(3);
                Check(HspeUndo.State(cb) == bAfter, "7 Redo: B exactly as after the paste");
            }

            // 8. a Face window gesture is left to the Face window's own undo (no Studio step, no stale before-state)
            yield return Select(main, a);
            global::HSPE.Kit.Face.FaceWindow.Toggle();
            yield return Frames(10);
            RectTransform face = global::HSPE.Kit.Face.FaceWindow.WindowRect;
            if (face != null)
            {
                Vector2 faceAt = Center(face);
                GameObject faceHit = TopUi(faceAt);
                if (faceHit != null && faceHit.transform.IsChildOf(face))
                {
                    s0 = Steps();
                    yield return Click(faceAt, delegate { bones.KitSelect(neck); bones.KitSetPosition(bones.KitGetPosition() + new Vector3(0f, 0.01f, 0f)); });
                    Check(Steps() == s0, "8 change during a Face window click = no Studio step", (Steps() - s0) + " step(s)");
                    global::HSPE.Kit.Face.FaceWindow.Toggle();
                    yield return Frames(5);
                    yield return Click(Center(main._kitAdvanced.WindowRect), null);
                    Check(Steps() == s0, "8 next Advanced click without change = no step", (Steps() - s0) + " step(s)");
                }
                else
                    Info("8 skipped: Face window centre covered by " + (faceHit != null ? faceHit.name : "nothing"));
            }
            else
                Info("8 skipped: Face window not shown");

            HspeUndo.FakeInput.Active = false;
            GUIUtility.systemCopyBuffer = clipboard;
            Info("result " + _pass + " passed, " + _fail + " failed");
            Info("done");
        }
    }
}
