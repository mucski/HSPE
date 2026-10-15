using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace HSPE.Kit.Advanced
{
    using HSUIKit;

    /// <summary>
    /// The one row grid every module page uses:
    ///   [label: fixed width] [control: fills] [value: fixed, right-aligned] [reset slot: fixed]
    /// so labels start, controls end, values and reset icons line up on every page and in both columns.
    /// Rows without a value or reset still reserve those slots (Slot) to keep the right edge.
    /// </summary>
    internal static class FieldGrid
    {
        public const float RowH = 32f, LabelW = 128f, ColumnLabelW = 96f, ValueW = 58f, ResetW = 24f, Gap = 8f;

        public static RectTransform Row(Transform parent, string name)
        {
            RectTransform row = Kit.Rect(parent, name);
            HorizontalLayoutGroup h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = Gap;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            Kit.Layout(row.gameObject, -1f, RowH);
            return row;
        }

        public static Text Label(Transform row, string text, float width)
        {
            Text t = Kit.MakeText(row, "Label", text, Theme.FontSize, Theme.Text, TextAnchor.MiddleLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            Kit.Layout(t.gameObject, width, 22f);
            return t;
        }

        /// <summary>An empty fixed-width cell (keeps the value / reset columns aligned).</summary>
        public static RectTransform Slot(Transform row, float width)
        {
            RectTransform s = Kit.Rect(row, "Slot");
            Kit.Layout(s.gameObject, width, 22f);
            return s;
        }

        public static KitIconButton Reset(Transform row, VoidHandler onReset)
        {
            KitIconButton b = KitIconButton.Create(row, Sprites.Reset, ResetW, false, onReset);
            Kit.Layout(b.gameObject, ResetW, ResetW);
            KitTooltip.Attach(b.gameObject, "Back to the original value");
            b.SetShown(false, false);
            return b;
        }

        /// <summary>Makes a child fill the row's free width.</summary>
        public static void Fill(GameObject go, float height)
        {
            LayoutElement le = Kit.Layout(go, -1f, height);
            le.flexibleWidth = 1f;
            le.minWidth = 40f;
        }
    }

    /// <summary>[label] [slider] [value] [reset]; the reset icon shows only while the value is changed.</summary>
    internal sealed class FloatRow
    {
        public RectTransform Root;
        private KitSlider _slider;
        private Text _label, _value;
        private KitIconButton _reset;
        private bool _shownDirty;
        private string _format;
        private float _shownValue = float.NaN;

        public static FloatRow Create(Transform parent, string label, float min, float max, FloatHandler onChanged, VoidHandler onReset)
        {
            return Create(parent, label, min, max, onChanged, onReset, false, FieldGrid.LabelW);
        }

        public static FloatRow Create(Transform parent, string label, float min, float max, FloatHandler onChanged, VoidHandler onReset, bool whole)
        {
            return Create(parent, label, min, max, onChanged, onReset, whole, FieldGrid.LabelW);
        }

        public static FloatRow Create(Transform parent, string label, float min, float max, FloatHandler onChanged, VoidHandler onReset, bool whole, float labelWidth)
        {
            FloatRow r = new FloatRow();
            r._format = whole ? "0" : "0.000";
            r.Root = FieldGrid.Row(parent, "Float " + label);
            r._label = FieldGrid.Label(r.Root, label, labelWidth);
            r._slider = KitSlider.Create(r.Root, null, min, max, min, whole, v =>
            {
                r.ShowValue(v);
                if (onChanged != null) onChanged(v);
            });
            FieldGrid.Fill(r._slider.transform.parent.gameObject, 28f);
            r._value = Kit.MakeText(r.Root, "Value", "", Theme.FontSize, Theme.TextDim, TextAnchor.MiddleRight);
            Kit.Layout(r._value.gameObject, FieldGrid.ValueW, 22f);
            r._reset = FieldGrid.Reset(r.Root, onReset);
            return r;
        }

        public void SetRange(float min, float max)
        {
            if (!_slider.Dragging)
                _slider.SetRange(min, max);
        }

        public void Show(float value, bool dirty)
        {
            if (!_slider.Dragging && !Mathf.Approximately(_slider.Value, value))
                _slider.SetValue(value, false, false);
            ShowValue(_slider.Dragging ? _slider.Value : value);
            if (dirty != _shownDirty)
            {
                _shownDirty = dirty;
                _reset.SetShown(dirty, true);
                _label.color = dirty ? Theme.Warning : Theme.Text;
            }
        }

        // formatting only on change: a ToString per row per frame was HSPE's main garbage source
        private void ShowValue(float v)
        {
            if (v == _shownValue)
                return;
            _shownValue = v;
            _value.text = v.ToString(_format, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>[label] [X] [Y] [Z] [reset] - drag, type or wheel each axis.</summary>
    internal sealed class VectorRow
    {
        private static readonly Color[] Colors =
        {
            new Color32(0xF0, 0x61, 0x6D, 0xFF), new Color32(0x3D, 0xD6, 0x8C, 0xFF), new Color32(0x5B, 0x8C, 0xFF, 0xFF)
        };

        public RectTransform Root;
        private readonly AxisCell[] _cells = new AxisCell[3];
        private Text _label;
        private KitIconButton _reset;
        private bool _shownDirty;

        public static VectorRow Create(Transform parent, string label, Vector3Getter get, Vector3Handler set, VoidHandler onReset)
        {
            return Create(parent, label, get, set, onReset, FieldGrid.LabelW);
        }

        /// <param name="get">current value (read when a delta arrives)</param>
        public static VectorRow Create(Transform parent, string label, Vector3Getter get, Vector3Handler set, VoidHandler onReset, float labelWidth)
        {
            VectorRow r = new VectorRow();
            r.Root = FieldGrid.Row(parent, "Vector " + label);
            r.Root.GetComponent<HorizontalLayoutGroup>().spacing = 6f;
            r._label = FieldGrid.Label(r.Root, label, labelWidth);
            string[] axes = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                int axis = i;
                AxisCell c = AxisCell.Create(r.Root, axes[i], Colors[i], "Drag (Shift x10, Ctrl x0.1) · click to type · wheel = one step");
                c.OnDelta = d =>
                {
                    Vector3 value = get();
                    value[axis] += d;
                    set(value);
                };
                c.OnTyped = t =>
                {
                    Vector3 value = get();
                    value[axis] = t;
                    set(value);
                };
                r._cells[i] = c;
            }
            r._reset = FieldGrid.Reset(r.Root, onReset);
            return r;
        }

        public void SetStep(float step)
        {
            for (int i = 0; i < 3; i++)
            {
                _cells[i].Step = step;
                _cells[i].Format = step < 0.001f ? "0.00000" : step < 0.01f ? "0.0000" : "0.000";
            }
        }

        public void Show(Vector3 value, bool dirty)
        {
            _cells[0].Show(value.x);
            _cells[1].Show(value.y);
            _cells[2].Show(value.z);
            if (dirty != _shownDirty)
            {
                _shownDirty = dirty;
                _reset.SetShown(dirty, true);
                _label.color = dirty ? Theme.Warning : Theme.Text;
            }
        }
    }

    /// <summary>[label] [......] [switch] [reset] - the switch ends where the values end.</summary>
    internal sealed class BoolRow
    {
        public RectTransform Root;
        public KitSwitch Switch;
        private Text _label;
        private KitIconButton _reset;
        private bool _shownDirty;

        public static BoolRow Create(Transform parent, string label, BoolHandler onChanged, VoidHandler onReset, string tooltip)
        {
            BoolRow r = new BoolRow();
            r.Root = FieldGrid.Row(parent, "Bool " + label);
            r._label = Kit.MakeText(r.Root, "Label", label, Theme.FontSize, Theme.Text, TextAnchor.MiddleLeft);
            FieldGrid.Fill(r._label.gameObject, 22f);
            r.Switch = KitSwitch.Create(r.Root, "", false, onChanged);
            Kit.Layout(r.Switch.gameObject, 52f, 28f);
            // no reset = no empty reset slot either: the switch goes to the edge
            r._reset = onReset != null ? FieldGrid.Reset(r.Root, onReset) : null;
            if (tooltip != null)
                KitTooltip.Attach(r.Root.gameObject, tooltip);
            return r;
        }

        public void Show(bool value, bool dirty)
        {
            if (Switch.Value != value)
                Switch.SetValue(value, true, false);
            if (_reset != null && dirty != _shownDirty)
            {
                _shownDirty = dirty;
                _reset.SetShown(dirty, true);
                _label.color = dirty ? Theme.Warning : Theme.Text;
            }
        }
    }

    /// <summary>[label] [segmented: fills] [reset].</summary>
    internal sealed class ChoiceRow
    {
        public RectTransform Root;
        public KitSegmented Segmented;
        private Text _label;
        private KitIconButton _reset;
        private bool _shownDirty;

        public static ChoiceRow Create(Transform parent, string label, string[] options, IntHandler onChanged, VoidHandler onReset)
        {
            ChoiceRow r = new ChoiceRow();
            r.Root = FieldGrid.Row(parent, "Choice " + label);
            r._label = FieldGrid.Label(r.Root, label, FieldGrid.LabelW);
            r.Segmented = KitSegmented.Create(r.Root, options, 0, onChanged);
            FieldGrid.Fill(r.Segmented.gameObject, 30f);
            r._reset = FieldGrid.Reset(r.Root, onReset);
            return r;
        }

        public void Show(int index, bool dirty)
        {
            if (Segmented.Selected != index)
                Segmented.Select(index, false);
            if (dirty != _shownDirty)
            {
                _shownDirty = dirty;
                _reset.SetShown(dirty, true);
                _label.color = dirty ? Theme.Warning : Theme.Text;
            }
        }
    }

    internal delegate Vector3 Vector3Getter();

    /// <summary>Joan's step (10^-5 .. 10^1): the X / Y / Z step and the viewport gizmo drag strength.</summary>
    internal static class StepChoice
    {
        public static KitDropdown Create(Transform parent, int index, IntHandler onIndex)
        {
            List<string> options = new List<string>();
            for (int i = -5; i <= 1; i++)
                options.Add(Mathf.Pow(10, i).ToString("0.#####", CultureInfo.InvariantCulture));
            KitDropdown d = KitDropdown.Create(parent, options, Mathf.Clamp(index, -5, 1) + 5, i => onIndex(i - 5));
            KitTooltip.Attach(d.gameObject, "Step of the X / Y / Z values, and how strongly dragging a gizmo in the viewport pulls");
            return d;
        }

        /// <summary>The step as a grid row: [label] [dropdown: fills] [reset slot].</summary>
        public static KitDropdown Row(Transform parent, string label, int index, IntHandler onIndex)
        {
            RectTransform row = FieldGrid.Row(parent, "Step");
            FieldGrid.Label(row, label, FieldGrid.LabelW);
            KitDropdown d = Create(row, index, onIndex);
            FieldGrid.Fill(d.gameObject, 30f);
            FieldGrid.Slot(row, FieldGrid.ResetW);
            return d;
        }
    }
}
