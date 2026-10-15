using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ToolBox_Shared
{

	/// <summary>
	/// Class for keeping track of a set of keyboard shortcut key combinations and whether they're being pressed.
	/// </summary>
	public class Shortcut
	{
		readonly KeyCode key = KeyCode.None;
		readonly bool useAlt = false;
		readonly bool useCtrl = false;
		readonly bool useShift = false;

		bool UseNormalKey => key != KeyCode.None;
		bool ShortcutIsValid => useAlt || useCtrl || useShift || UseNormalKey == true;

		/// <summary>
		/// Whether the shortcut should be tested against.
		/// </summary>
		public bool IsActive { get; set; } = true;

		/// <summary>
		/// Returns whether the shortcut is being pressed.
		/// </summary>
		/// <returns>Returns whether the shortcut key combination was pressed.</returns>
		public bool ShortcutPressed()
		{
			if (!IsActive)
				return false;

			// Check normal key condition (if set and if pressed in this frame), return false if not met
			if ((UseNormalKey && !Input.GetKeyDown(key)) || !ShortcutIsValid)
				return false;

			// Check if other modifiers are actively pressed, return false if condition not met
			bool
				altKeyCorrectState = useAlt == (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)),
				ctrlKeyCorrectState = useCtrl == (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)),
				shiftKeyCorrectState = useShift == (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

			bool correctModifiersPressed = altKeyCorrectState && ctrlKeyCorrectState && shiftKeyCorrectState;

			if (!correctModifiersPressed)
				return false;

			// Determine which key is being pressed in this frame, return true only then


			// If not using normal key, only trigger on modifier key press action. Correct keys are already pressed at this point.

			// If normal key is being pressed in this frame, return true. Else (if unused), return true if any other modifier key is being pressed in this frame.
			return UseNormalKey
				|| Input.GetKeyDown(KeyCode.LeftAlt)
				|| Input.GetKeyDown(KeyCode.RightAlt)
				|| Input.GetKeyDown(KeyCode.LeftControl)
				|| Input.GetKeyDown(KeyCode.RightControl)
				|| Input.GetKeyDown(KeyCode.LeftShift)
				|| Input.GetKeyDown(KeyCode.RightShift);
		}

		/// <summary>
		/// Shortcut constructor.
		/// </summary>
		/// <param name="shortcutString">Desired shortcut as string. If provided, other fields are ignored.</param>
		/// <param name="_useAlt">Whether to use the Alt key.</param>
		/// <param name="_useCtrl">Whether to use the Ctrl key.</param>
		/// <param name="_useShift">Whether to use the Shift key.</param>
		/// <param name="_regularKey">Any <see cref="KeyCode"/> other than Ctrl/Alt/Shift.</param>
		/// <param name="_enabled">Whether the shortcut should be tested.</param>
		public Shortcut(string shortcutString = null, bool _useAlt = false, bool _useCtrl = false, bool _useShift = false, KeyCode _regularKey = KeyCode.None, bool _enabled = true)
		{
			if (string.IsNullOrEmpty(shortcutString))
			{
				useAlt = _useAlt;
				useCtrl = _useCtrl;
				useShift = _useShift;
				key = _regularKey;
				IsActive = _enabled;
				return;
			}

			try
			{
				string[]
					altList = new string[] { "alt", "rightalt", "leftalt", "altgr" },
					ctrlList = new string[] { "ctrl", "rightctrl", "leftctrl", "control", "command" },
					shiftList = new string[] { "shift", "rightshift", "leftshift" };

				//List<string> keys = ModPrefs.GetString(Name, "Shortcut", "shift+f", true).ToLower().Split('+').Distinct().ToList();
				char[] separators = new char[] { ' ', ',', '+', '-', '\t' };
				List<string> keys = shortcutString.ToLower().Split(separators).Distinct().ToList();

				useAlt = keys.Any(key => altList.Contains(key));
				useCtrl = keys.Any(key => ctrlList.Contains(key));
				useShift = keys.Any(key => shiftList.Contains(key));

				var keyString = keys.FirstOrDefault(key => !altList.Contains(key) && !ctrlList.Contains(key) && !shiftList.Contains(key));

				if (string.IsNullOrEmpty(keyString))
					return;

				key = Enum.GetValues(typeof(KeyCode))
					.Cast<KeyCode>()
					.FirstOrDefault(keyCode => keyCode.ToString().Equals(keyString, StringComparison.OrdinalIgnoreCase));

				IsActive = _enabled;
			}
			catch (Exception ex)
			{
				useAlt = _useAlt;
				useCtrl = _useCtrl;
				useShift = _useShift;
				key = _regularKey;
				IsActive = _enabled;
				UnityEngine.Debug.Log($"Failed to read shortcut from text ({shortcutString})!\r\n" + ex);
			}
		}
	}
}
