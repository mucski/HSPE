using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HSPE.Kit.Advanced
{
    using HSUIKit;

    /// <summary>
    /// One X / Y / Z value of the Bones drawer: drag on it to scrub (Shift x10, Ctrl x0.1 - the kit convention), click
    /// to type, mouse wheel to nudge by one step. Reports deltas (scrub / wheel) and typed absolute values separately so
    /// rotations can be applied incrementally, like Joan's +/- buttons. A cell without a value (Rotate Around) only
    /// scrubs and nudges.
    /// </summary>
    internal sealed class AxisCell : MonoBehaviour, IPointerClickHandler, IScrollHandler
    {
        public FloatHandler OnDelta;
        public FloatHandler OnTyped;
        public BoolHandler OnActive;
        public float Step = 0.01f;
        public string Format = "0.000";

        private Image _bg;
        private KitInput _input;
        private Text _hint;
        private bool _hasValue = true, _hover, _dragging;
        private int _typingSince;
        private float _shownValue = float.NaN;
        private string _shownFormat;

        public static AxisCell Create(Transform parent, string axis, Color color, string tooltip)
        {
            RectTransform rt = Kit.Rect(parent, "Axis " + axis);
            LayoutElement le = Kit.Layout(rt.gameObject, -1f, 32f);
            le.flexibleWidth = 1f;
            le.minWidth = 60f;
            AxisCell c = rt.gameObject.AddComponent<AxisCell>();

            c._bg = Kit.Image(rt, "Bg", Sprites.Rounded(Theme.RadiusSmall), Theme.Surface);
            KitThemeColor.Bind(c._bg, () => Theme.Surface);
            Kit.Fill(c._bg.rectTransform);
            Image tag = Kit.Image(rt, "Tag", Sprites.Rounded(Mathf.Min(Theme.RadiusSmall, 8)), Kit.Alpha(color, 0.85f));
            tag.rectTransform.anchorMin = new Vector2(0f, 0f);
            tag.rectTransform.anchorMax = new Vector2(0f, 1f);
            tag.rectTransform.pivot = new Vector2(0f, 0.5f);
            tag.rectTransform.sizeDelta = new Vector2(20f, -6f);
            tag.rectTransform.anchoredPosition = new Vector2(3f, 0f);
            Text t = Kit.MakeText(tag.rectTransform, "Axis", axis, Theme.SmallSize, Color.white, TextAnchor.MiddleCenter);
            t.fontStyle = FontStyle.Bold;
            Kit.Fill(t.rectTransform);

            c._input = KitInput.Create(rt, "", "", null);
            Kit.Fill((RectTransform)c._input.transform, 24f, 0f, 0f, 0f);
            c._input.Field.contentType = InputField.ContentType.DecimalNumber;
            c._input.OnSubmit = c.Submit;
            c._input.gameObject.SetActive(false); // shown only while typing

            c._hint = Kit.MakeText(rt, "Value", "", Theme.FontSize, Theme.Text, TextAnchor.MiddleCenter);
            Kit.Fill(c._hint.rectTransform, 24f, 0f, 4f, 0f);

            // the whole cell scrubs; with the drag threshold kept, a plain click still arrives as a click (= type)
            Kit.HitArea(c._bg.gameObject);
            c._bg.raycastTarget = true;
            KitScrub s = rt.gameObject.AddComponent<KitScrub>();
            s.Immediate = false;
            s.OnHover = on => { c._hover = on; c.Tint(); };
            s.OnActive = on =>
            {
                c._dragging = on;
                c.Tint();
                if (c.OnActive != null) c.OnActive(on);
            };
            s.OnScrub = px =>
            {
                if (c.OnDelta != null) c.OnDelta(px * c.Step * 0.1f);
            };
            if (tooltip != null)
                KitTooltip.Attach(rt.gameObject, tooltip);
            return c;
        }

        /// <summary>A cell without a value (Rotate Around): shows a hint instead, no typing.</summary>
        public void SetNoValue(string hint)
        {
            _hasValue = false;
            _hint.text = hint;
            _hint.color = Theme.TextDim;
        }

        public void SetHasValue()
        {
            _hasValue = true;
            _shownValue = float.NaN;
            _hint.color = Theme.Text;
        }

        public void Show(float value)
        {
            if (!_hasValue || _input.gameObject.activeSelf)
                return;
            // formatting only on change (per-frame ToString = garbage, see FloatRow)
            if (value == _shownValue && ReferenceEquals(Format, _shownFormat) && _hint.enabled)
                return;
            _shownValue = value;
            _shownFormat = Format;
            _hint.text = value.ToString(Format, CultureInfo.InvariantCulture);
        }

        private void Tint()
        {
            Tween.Tint(_bg, _dragging ? Theme.SurfaceActive : (_hover ? Theme.SurfaceHover : Theme.Surface), 0.1f, Ease.OutQuad);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !_hasValue || _dragging)
                return;
            _input.gameObject.SetActive(true);
            _hint.enabled = false;
            _input.Text = _hint.text;
            _input.Field.ActivateInputField();
            _input.Field.Select();
            _typingSince = Time.frameCount; // the field only reports focus a frame later
        }

        private void Submit(string s)
        {
            _input.gameObject.SetActive(false);
            _hint.enabled = true;
            float v;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && OnTyped != null)
                OnTyped(v);
        }

        private void Update()
        {
            // typing ended without Enter (clicked elsewhere): drop it, like a cancelled edit
            if (_input.gameObject.activeSelf && _input.Field.isFocused == false && Time.frameCount > _typingSince + 2)
            {
                _input.gameObject.SetActive(false);
                _hint.enabled = true;
            }
        }

        public void OnScroll(PointerEventData e)
        {
            if (OnDelta == null || Mathf.Approximately(e.scrollDelta.y, 0f))
                return;
            float mod = 1f;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) mod = 10f;
            else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) mod = 0.1f;
            if (OnActive != null) OnActive(true);
            OnDelta(Mathf.Sign(e.scrollDelta.y) * Step * mod);
            if (OnActive != null) OnActive(false);
        }
    }
}
