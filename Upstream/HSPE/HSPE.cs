using System;
using System.Collections.Generic;
using System.Reflection;
#if IPA
using Harmony;
using IllusionPlugin;
#elif BEPINEX
using HarmonyLib;
using BepInEx;
#endif
using HSPE.AMModules;
using UILib;
using ToolBox;
using ToolBox.Extensions;
using UnityEngine.SceneManagement;

/*
 Changelog:
	v2.17.0:
	- FK and Hands pages: right-click menu (copy arm / hand / leg to the other side, swap pose) as on the IK page
	- FK and Hands pages: Ctrl-click selects several bones, right-click selects a whole finger or every finger of a
	  hand; the rotate pads turn all of them
	- Bones page search finds bones of swapped hair / clothes and reloaded characters
	v2.16.0:
	- Merged Gru's HSPE 2.15.0 bone-presets fork (presets, blend, Reset All, bone settings on cards, scene save
	  protection) into the HSUIKit fork (new windows, copy / paste / presets, Face window); both groups' files load
	v2.15.0:
	- Per-character portable bone edits on character cards, with scene authority and clean replacement
	- Card saves during blend preview omit bone data without blocking the save or changing the preview
	v2.14.1:
	- Added confirmed Reset All in the main Bones tab, restoring only tracked edits and their FK guides
	v2.14.0:
	- Added reversible bone preset blending with frozen current-state fallback and selectable channels
	- Preview leaves dirty edits and FK guides untouched; explicit Apply, Cancel and guarded scene/pose/card saves
	v2.13.1:
	- Integrated bone presets into Advanced Mode with separate Load/Create views and per-value previews
	- Protected preset interaction from Studio camera dragging, wheel zoom and typing shortcuts
	v2.13.0.1:
	- Bone preset list clicks now select and preview; presets apply only through the explicit Load selected button
	v2.13.0:
	- Added selective bone modification presets with preserve-existing and overwrite-saved load modes
	v2.12.0.1:
	- Additional checks when working with blendshapdes, destroying colliders, applying weights and saving scenes that were loaded using the 'load (old)' feature
	- Changing a character's head now forces an update to the BlendShapesEditor list
	- Duplicating stuff in the studio should no longer throw errors
	v2.12.0.2:
	- new font colors
	- toggling Adv. mode now toggles changes to bust
	- blend shapes update when changing head-related parts
 */

namespace HSPE
{
#if BEPINEX
	[BepInPlugin(_guid, _name, _versionNum)]
	[BepInDependency("com.bepis.bepinex.extendedsave")]
#if KOIKATSU
	[BepInProcess("CharaStudio")]
#elif AISHOUJO || HONEYSELECT2
	[BepInProcess("StudioNEOV2")]
#endif
#endif
	internal class HSPE : GenericPlugin
#if HONEYSELECT || PLAYHOME
	, IEnhancedPlugin
#endif
	{
#if HONEYSELECT
		internal const string _name = "HSPE";
		internal const string _guid = "com.joan6694.illusionplugins.poseeditor";
#elif PLAYHOME
		internal const string _name = "PHPE";
		internal const string _guid = "com.joan6694.illusionplugins.poseeditor";
#elif KOIKATSU
		internal const string _name = "KKPE";
		internal const string _guid = "com.joan6694.kkplugins.kkpe";
		internal const int saveVersion = 0;
#elif AISHOUJO
		internal const string _name = "AIPE";
		internal const string _guid = "com.joan6694.illusionplugins.poseeditor";
		internal const int saveVersion = 0;
#elif HONEYSELECT2
		internal const string _name = "HS2PE";
		internal const string _guid = "com.joan6694.illusionplugins.poseeditor";
		internal const int saveVersion = 0;
#endif
		internal const string _versionNum = "2.17.0";

#if IPA
		public override string Name { get { return _name; } }
		//public override string Version { get { return _versionNum; } }
#if HONEYSELECT
		public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#elif PLAYHOME
		public override string[] Filter { get { return new[] { "PlayHomeStudio32bit", "PlayHomeStudio64bit" }; } }
#endif
#endif

		// addition (KitGuard): false when HSUIKit.dll is missing or too old; then only the HSUIKit windows are skipped
		// (Joan's canvas / IMGUI Advanced window are used) and a red banner says why. Awake runs from OnApplicationStart.
		internal static bool _kitAvailable;

		protected override void Awake()
		{
			base.Awake();
			_kitAvailable = global::HSKitGuard.KitGuard.Check(_name, "1.3.0"); // the new windows are built against HSUIKit 1.3.0
			HarmonyExtensions.CreateInstance(_guid).PatchAllSafe();
		}

#if AISHOUJO || HONEYSELECT2
		protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
		{
			base.LevelLoaded(scene, mode);
			if (mode == LoadSceneMode.Single && scene.name.Equals("Studio"))
				this.gameObject.AddComponent<MainWindow>();
		}
#else
		protected override void LevelLoaded(int level)
		{
			base.LevelLoaded(level);
#if HONEYSELECT
			if (level == 3)
#elif KOIKATSU
			if (level == 1)
#elif PLAYHOME
			if (level == 1)
#endif
				gameObject.AddComponent<MainWindow>();
		}
#endif
	}
}
