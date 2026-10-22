using System.Collections.Generic;
using System.IO;
using System.Xml;
using HSPE.Kit.Small;
using HSPE.Kit.Transfer;
using Studio;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HSPE.Kit.Undo
{
    /// <summary>
    /// Turns HSPE edits (Advanced window: kit pages, Joan's IMGUI window and the 2.15 bone-preset panes inside it;
    /// HSPE gizmo drags; copy / paste / presets) into Studio undo steps, so Ctrl+Z / Ctrl+Shift+Z cover them.
    ///
    /// No per-control wiring: a "gesture" (mouse press, wheel nudges or typing that starts on HSPE's own UI, or a press
    /// in the viewport while the Advanced window is open) ends when the mouse is up and nothing happened for a moment;
    /// then the HSPE state of the selected objects (PoseController.SaveXml, the scene format) is compared with the
    /// state from before, and a change becomes one step. The before-state is taken when the pointer comes over an HSPE
    /// window (and refreshed while it rests there), so it does not matter whether Unity's EventSystem handled the
    /// press before or after this Tick.
    /// Not recorded (only the before-states are refreshed): small-window drags (StopDrag pushes Joan's own step), Face
    /// window gestures (own undo), Timeline playback.
    /// </summary>
    internal static class HspeUndo
    {
        private const float QuietTime = 0.4f;      // wheel / typing gestures end this long after the last tick / key
        private const float BaselineMaxAge = 2f;   // a resting pointer refreshes the before-states this often

        private sealed class Baseline
        {
            public string State;
            public float Time;
        }

        private static readonly Dictionary<PoseController, Baseline> _baselines = new Dictionary<PoseController, Baseline>();
        private static readonly List<PoseController> _watched = new List<PoseController>();
        private static readonly List<RaycastResult> _hits = new List<RaycastResult>();
        private static PointerEventData _pointer;
        private static EventSystem _pointerSystem;

        private static bool _active;          // a gesture is running
        private static bool _recordThis;      // false = only refresh before-states at the end (Face, small-window drag)
        private static bool _viewport;        // the gesture started in the 3D view (before-state taken at the press)
        private static float _lastTick;       // last wheel tick / key press of the running gesture
        private static bool _hovering;
        private static bool _popupOwned;      // the open kit popup (menu, dropdown) was opened from an HSPE window

        /// <summary>True while one of our own commands restores a state (its LoadXml must not look like an edit).</summary>
        private static bool _restoring;

        /// <summary>Self-test seam: when Active, Tick reads these instead of UnityEngine.Input (everything else is real).</summary>
        internal static class FakeInput
        {
            public static bool Active;
            public static Vector2 Mouse;
            public static bool Pressed, Held, Wheel, Key;
        }

        private static Vector2 MousePos() { return FakeInput.Active ? FakeInput.Mouse : (Vector2)Input.mousePosition; }

        // ---------------------------------------------------------------- hooks

        /// <summary>A small-window / IK drag started: Joan's StopDrag pushes the step, this gesture must not.</summary>
        internal static void NoteStudioDrag()
        {
            if (_active)
                _recordThis = false;
        }

        /// <summary>Scene load / import: every saved before-state belongs to the old objects.</summary>
        internal static void Forget()
        {
            _baselines.Clear();
            _watched.Clear();
            _active = false;
        }

        /// <summary>Somebody else changed these objects on purpose (paste undo toast...): take their state as the new before.</summary>
        internal static void Refresh(IEnumerable<PoseController> controllers)
        {
            foreach (PoseController c in controllers)
                TakeBaseline(c);
        }

        /// <summary>Pushes an explicit step (paste / preset), states from PoseTransfer.FullState (with the Studio pose).</summary>
        internal static void PushExplicit(string label, List<PoseController> targets, Dictionary<PoseController, string> before)
        {
            List<StateCommand.Entry> entries = new List<StateCommand.Entry>();
            foreach (PoseController c in targets)
            {
                string b;
                if (c == null || before.TryGetValue(c, out b) == false)
                    continue;
                entries.Add(new StateCommand.Entry { Target = c, Before = b, After = PoseTransfer.FullState(c) });
            }
            if (entries.Count != 0)
                Push(new StateCommand(label, entries));
            Refresh(targets);
        }

        // ---------------------------------------------------------------- per frame

        internal static void Tick(MainWindow main)
        {
            if (Studio.Studio.Instance == null || main == null)
                return;
            UndoSelfTest.MaybeStart(main);
            if (ToolBox.TimelineCompatibility.IsPlayingIfAvailable)
            {
                // Timeline writes HSPE values every frame: nothing here is a user edit, and every before-state is stale
                if (_active || _baselines.Count != 0)
                    Forget();
                return;
            }

            RectTransform advanced = main._kitAdvanced != null ? main._kitAdvanced.WindowRect : null;
            RectTransform small = main._kitSmall != null ? main._kitSmall.WindowRect : null;
            bool imguiAdvanced = main._poseTarget != null && PoseController._drawAdvancedMode && main._kitAdvanced == null;
            bool advancedOpen = advanced != null || imguiAdvanced;
            Canvas joan = main._kitSmall == null ? main.joanCanvas : null;
            bool joanOpen = joan != null && joan.gameObject.activeInHierarchy;

            if (advancedOpen == false && small == null && joanOpen == false && _active == false)
            {
                if (_baselines.Count != 0)
                    _baselines.Clear();
                return;
            }

            Vector2 mouse = MousePos();
            RectTransform layer = global::HSUIKit.Kit.PopupLayer;
            bool popupOpen = layer != null && layer.childCount != 0;
            if (popupOpen == false)
                _popupOwned = false;

            // before-states: taken when the pointer comes over an HSPE window, refreshed while it rests there
            bool hovering = Contains(advanced, mouse) || Contains(small, mouse) || (imguiAdvanced && ImguiContains(main, mouse)) || (_popupOwned && popupOpen);
            if (_active == false && hovering && (_hovering == false || BaselinesOlderThan(BaselineMaxAge)))
                TakeBaselines(main);
            _hovering = hovering;

            bool pressed = FakeInput.Active ? FakeInput.Pressed : Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
            bool held = FakeInput.Active ? FakeInput.Held : Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
            bool wheel = FakeInput.Active ? FakeInput.Wheel : Input.mouseScrollDelta.y != 0f;
            bool typing = TypingInHspe(main, advanced, small, imguiAdvanced);
            bool key = (FakeInput.Active ? FakeInput.Key : Input.anyKeyDown) && pressed == false && typing;

            if (_active == false)
            {
                if (pressed || wheel)
                {
                    bool face;
                    bool viewport;
                    if (IsHspeGesture(main, mouse, advanced, small, imguiAdvanced, joanOpen ? joan : null, pressed, popupOpen, out face, out viewport))
                        Begin(main, true, viewport);
                    else if (face)
                        Begin(main, false, false);
                }
                else if (key)
                    Begin(main, true, false);
            }
            else if (wheel || key)
                _lastTick = Time.unscaledTime;

            if (_active && held == false && TypingInKitField(advanced, small) == false && Time.unscaledTime - _lastTick > QuietTime)
                End(main, popupOpen);
        }

        private static void Begin(MainWindow main, bool record, bool viewport)
        {
            _active = true;
            _recordThis = record;
            _viewport = viewport;
            _lastTick = Time.unscaledTime;
            _watched.Clear();
            foreach (PoseController c in TransferActions.Selected())
                _watched.Add(c);
            if (main._poseTarget != null && _watched.Contains(main._poseTarget) == false)
                _watched.Add(main._poseTarget);
            foreach (PoseController c in _watched)
            {
                Baseline b;
                // viewport gizmo drags change values only after the press frame: a fresh before-state is exact there
                if (viewport || _baselines.TryGetValue(c, out b) == false || Time.unscaledTime - b.Time > BaselineMaxAge + 0.5f)
                    TakeBaseline(c);
            }
            if (FakeInput.Active)
                global::HSPE.Kit.Log.Write("undo: begin record=" + record + " viewport=" + viewport + " objects=" + _watched.Count);
        }

        private static void End(MainWindow main, bool popupOpen)
        {
            _active = false;
            if (popupOpen && _recordThis && _viewport == false)
                _popupOwned = true; // this gesture (on an HSPE window) opened the menu / dropdown that is now up
            List<StateCommand.Entry> entries = null;
            foreach (PoseController c in _watched)
            {
                if (c == null)
                    continue;
                string after = State(c);
                Baseline b;
                if (_recordThis && _baselines.TryGetValue(c, out b) && b.State != after)
                {
                    if (entries == null)
                        entries = new List<StateCommand.Entry>();
                    entries.Add(new StateCommand.Entry { Target = c, Before = b.State, After = after });
                }
                _baselines[c] = new Baseline { State = after, Time = Time.unscaledTime };
            }
            if (FakeInput.Active)
                global::HSPE.Kit.Log.Write("undo: end record=" + _recordThis + " objects=" + _watched.Count + " changed=" + (entries != null ? entries.Count : 0));
            _watched.Clear();
            if (entries != null)
                Push(new StateCommand("HSPE edit", entries));
        }

        // ---------------------------------------------------------------- states

        /// <summary>The object's HSPE state in the scene format, without the Studio pose (that has Studio's own undo).</summary>
        internal static string State(PoseController c)
        {
            using (StringWriter sw = new StringWriter())
            {
                using (XmlTextWriter w = new XmlTextWriter(sw))
                {
                    w.WriteStartElement("characterInfo");
                    c.SaveXml(w);
                    w.WriteEndElement();
                }
                return sw.ToString();
            }
        }

        private static void TakeBaseline(PoseController c)
        {
            if (c == null)
                return;
            _baselines[c] = new Baseline { State = State(c), Time = Time.unscaledTime };
        }

        private static void TakeBaselines(MainWindow main)
        {
            foreach (PoseController c in TransferActions.Selected())
                TakeBaseline(c);
            if (main._poseTarget != null)
                TakeBaseline(main._poseTarget);
        }

        private static bool BaselinesOlderThan(float age)
        {
            foreach (KeyValuePair<PoseController, Baseline> p in _baselines)
                if (Time.unscaledTime - p.Value.Time > age)
                    return true;
            return _baselines.Count == 0;
        }

        private static void Push(StateCommand command)
        {
            UndoRedoManager.Instance.Push(command);
            global::HSPE.Kit.Log.Write("undo: recorded " + command.Label + " on " + command.Count + " object(s)");
        }

        // ---------------------------------------------------------------- where is the pointer

        private static bool Contains(RectTransform rect, Vector2 screen)
        {
            return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, screen, null);
        }

        private static bool ImguiContains(MainWindow main, Vector2 screen)
        {
            return main._advancedModeRect.Contains(new Vector2(screen.x, Screen.height - screen.y));
        }

        /// <summary>
        /// Does this press / wheel tick start an HSPE gesture? Joan's IMGUI window draws over everything, then the
        /// topmost uGUI object decides; a press that hits no UI counts while the Advanced window is open (HSPE gizmos).
        /// </summary>
        private static bool IsHspeGesture(MainWindow main, Vector2 mouse, RectTransform advanced, RectTransform small, bool imguiAdvanced,
            Canvas joan, bool pressed, bool popupOpen, out bool face, out bool viewport)
        {
            face = false;
            viewport = false;
            if (imguiAdvanced && ImguiContains(main, mouse))
                return true;
            GameObject top = TopUiObject(mouse);
            if (top == null)
            {
                viewport = pressed && (advanced != null || imguiAdvanced); // a wheel in the view is the camera zoom
                return viewport;
            }
            Transform t = top.transform;
            RectTransform faceRect = global::HSPE.Kit.Face.FaceWindow.WindowRect;
            if (faceRect != null && t.IsChildOf(faceRect))
            {
                face = true;
                return false;
            }
            if ((advanced != null && t.IsChildOf(advanced)) || (small != null && t.IsChildOf(small)) || (joan != null && t.IsChildOf(joan.transform)))
                return true;
            RectTransform layer = global::HSUIKit.Kit.PopupLayer;
            return popupOpen && _popupOwned && layer != null && t.IsChildOf(layer);
        }

        private static GameObject TopUiObject(Vector2 mouse)
        {
            // a raycast at this point, not IsPointerOverGameObject (that reports the last pointer event, not this point)
            EventSystem es = EventSystem.current;
            if (es == null)
                return null;
            if (_pointer == null || _pointerSystem != es)
            {
                _pointer = new PointerEventData(es);
                _pointerSystem = es;
            }
            _pointer.position = mouse;
            _hits.Clear();
            es.RaycastAll(_pointer, _hits);
            GameObject top = _hits.Count != 0 ? _hits[0].gameObject : null;
            _hits.Clear();
            return top;
        }

        private static bool TypingInKitField(RectTransform advanced, RectTransform small)
        {
            if (KitSmallWindow.TypingInField() == false)
                return false;
            Transform t = EventSystem.current.currentSelectedGameObject.transform;
            RectTransform layer = global::HSUIKit.Kit.PopupLayer;
            return (advanced != null && t.IsChildOf(advanced)) || (small != null && t.IsChildOf(small)) || (_popupOwned && layer != null && t.IsChildOf(layer));
        }

        private static bool TypingInHspe(MainWindow main, RectTransform advanced, RectTransform small, bool imguiAdvanced)
        {
            if (TypingInKitField(advanced, small))
                return true;
            // Joan's IMGUI fields: keyboard focus is held while the pointer is over his window
            return imguiAdvanced && GUIUtility.keyboardControl != 0 && ImguiContains(main, MousePos());
        }

        // ---------------------------------------------------------------- the step

        /// <summary>One Studio undo step: whole HSPE states of one or more objects, before and after.</summary>
        private sealed class StateCommand : ICommand
        {
            internal sealed class Entry
            {
                public PoseController Target;
                public string Before;
                public string After;
            }

            private readonly List<Entry> _entries;
            internal readonly string Label;
            internal int Count { get { return _entries.Count; } }

            public StateCommand(string label, List<Entry> entries)
            {
                Label = label;
                _entries = entries;
            }

            public void Do() { Apply(false); }
            public void Redo() { Apply(false); }
            public void Undo() { Apply(true); }

            private void Apply(bool undo)
            {
                if (_restoring)
                    return;
                _restoring = true;
                try
                {
                    for (int i = undo ? _entries.Count - 1 : 0; undo ? i >= 0 : i < _entries.Count; i += undo ? -1 : 1)
                    {
                        Entry e = _entries[i];
                        if (e.Target == null) // object deleted since
                            continue;
                        try
                        {
                            PoseTransfer.Restore(e.Target, undo ? e.Before : e.After);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError("HSPE: undo / redo failed on " + e.Target.name + "\n" + ex);
                            global::HSPE.Kit.Log.Write("undo: failed: " + ex);
                        }
                        TakeBaseline(e.Target);
                    }
                }
                finally
                {
                    _restoring = false;
                }
            }
        }
    }
}
