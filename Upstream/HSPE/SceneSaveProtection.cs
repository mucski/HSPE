#if HONEYSELECT
using System;
using System.IO;
using System.Xml;
using ToolBox.Extensions;
using UnityEngine;

namespace HSPE
{
	// HSExtSave catches a late handler exception and silently omits that entire plugin's
	// section. Prepare the WHOLE HSPE document before FileMode.Create, then only copy it
	// in the handler. No live module enumeration can fail after native saving starts.
	internal static class SceneSaveProtection
	{
		private static string _preparedXml, _preparedPath;
		private static int _preparedFrame, _preparedEpoch;
		private static string _error;
		private static Rect _errorRect;
		private static Vector2 _errorScroll;
		private static RectTransform _errorBlocker;
		internal static bool HasError { get { return _error != null; } }

		internal static void ClearPrepared()
		{
			_preparedXml = null;
			_preparedPath = null;
		}

		internal static void ClearError()
		{
			_error = null;
			if (_errorBlocker != null) _errorBlocker.gameObject.SetActive(false);
		}

		internal static void Shutdown()
		{
			ClearPrepared(); ClearError();
			if (_errorBlocker != null) UnityEngine.Object.Destroy(_errorBlocker.gameObject);
			_errorBlocker = null;
		}

		internal static bool AllowSave(string path, bool prepare)
		{
			ClearPrepared();
			if (MainWindow._self == null) return true;
			return TryCapture(path, prepare, MainWindow._self.CaptureSceneXml);
		}

		private static bool TryCapture(string path, bool prepare, Func<string> capture)
		{
			ClearPrepared();
			try
			{
				string xml = capture();
				// XmlTextWriter/WriteRaw may accept invalid XML characters. Validate before
				// freezing, including any non-bone module's output, not just bone numbers.
				XmlReaderSettings settings = new XmlReaderSettings { ProhibitDtd = true, XmlResolver = null, CheckCharacters = true };
				using (StringReader text = new StringReader(xml))
				using (XmlReader reader = XmlReader.Create(text, settings))
				{
					XmlDocument document = new XmlDocument { XmlResolver = null };
					document.Load(reader);
					if (document.DocumentElement == null || document.DocumentElement.Name != "root")
						throw new InvalidDataException("Invalid HSPE scene document.");
				}
				if (prepare)
				{
					_preparedXml = xml;
					_preparedPath = path;
					_preparedFrame = Time.frameCount;
					_preparedEpoch = BoneCardPersistence.SceneEpoch;
				}
				ClearError();
				return true;
			}
			catch (Exception error)
			{
				_error = error.Message;
				_errorScroll = Vector2.zero;
				_errorRect = new Rect(Mathf.Max(0f, (Screen.width - 540f) / 2f), Mathf.Max(0f, (Screen.height - 245f) / 2f), 540f, 245f);
				Debug.LogError("HSPE: Scene save cancelled before opening the scene file. Edits remain in memory.\n" + error);
				return false;
			}
		}

		internal static string TakePrepared(string path)
		{
			string xml = _preparedPath == path && _preparedFrame == Time.frameCount && _preparedEpoch == BoneCardPersistence.SceneEpoch ? _preparedXml : null;
			ClearPrepared(); // One synchronous native save only; never reuse an old snapshot.
			return xml;
		}

		internal static void DrawError()
		{
			if (!HasError) { ClearError(); return; }
			if (_errorBlocker == null) _errorBlocker = IMGUIExtensions.CreateUGUIPanelForIMGUI();
			_errorBlocker.gameObject.SetActive(true);
			IMGUIExtensions.FitRectTransformToRect(_errorBlocker, _errorRect);
			int depth = GUI.depth;
			try
			{
				GUI.depth = -10000;
				_errorRect = GUILayout.Window(0x48535045, _errorRect, DrawErrorWindow, "HSPE: Scene not saved");
				// Unity maintains a separate window stack; GUI.depth alone does not bring
				// a newly created warning in front of the existing Advanced Mode window.
				GUI.BringWindowToFront(0x48535045);
				GUI.FocusWindow(0x48535045);
			}
			finally { GUI.depth = depth; }
		}

		private static void DrawErrorWindow(int id)
		{
			// The built-in HS1 window is translucent. Redraw its usual chrome over an
			// opaque backing so the warning remains readable over other editor windows.
			Rect bounds = new Rect(0f, 0f, _errorRect.width, _errorRect.height);
			Color color = GUI.color;
			GUI.color = Color.black;
			GUI.DrawTexture(bounds, Texture2D.whiteTexture);
			GUI.color = color;
			GUI.Box(bounds, "HSPE: Scene not saved", GUI.skin.window);
			GUIStyle label = new GUIStyle(GUI.skin.label) { wordWrap = true, stretchHeight = true };
			GUILayout.Label("HSPE could not prepare this scene's data. Saving was stopped before the scene file was opened. Your edits are still in memory.", label);
			_errorScroll = GUILayout.BeginScrollView(_errorScroll, GUILayout.Height(90f));
			GUILayout.Label(_error, label);
			GUILayout.EndScrollView();
			GUILayout.Label("Correct the problem and save again. Full details are in the game log.", label);
			if (GUILayout.Button("Dismiss", GUILayout.Height(25f))) ClearError();
		}
	}
}
#endif
