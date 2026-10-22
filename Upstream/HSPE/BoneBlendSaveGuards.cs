// HS1 save entry points verified against GameDecompiled/Assembly-CSharp and the installed assembly.
// Guard before preprocessing, screenshot capture or FileMode.Create, not in the late XML callback.
#if HONEYSELECT
using System;
using Harmony;
using HSPE.AMModules;
using Studio;

namespace HSPE
{
	[HarmonyPatch(typeof(Studio.Studio), "SaveScene", new Type[0])]
	internal static class BoneBlend_StudioSave_Guard
	{
		[HarmonyPriority(Priority.First)]
		internal static bool Prefix() { return BonesEditor.AllowSaveDuringBoneBlend() && BoneCardPersistence.AllowSceneSave() && SceneSaveProtection.AllowSave(null, false); }
	}

	[HarmonyPatch(typeof(SceneInfo), "Save", new[] { typeof(string) })]
	internal static class BoneBlend_SceneSave_Guard
	{
		[HarmonyPriority(Priority.First)]
		internal static bool Prefix(string _path, ref bool __result)
		{
			SceneSaveProtection.ClearPrepared();
			if (BonesEditor.AllowSaveDuringBoneBlend() && BoneCardPersistence.AllowSceneSave() && SceneSaveProtection.AllowSave(_path, true)) return true;
			__result = false;
			return false;
		}

		internal static void Postfix() { SceneSaveProtection.ClearPrepared(); }
	}

	[HarmonyPatch(typeof(PauseCtrl), "Save", new[] { typeof(OCIChar), typeof(string) })]
	internal static class BoneBlend_PoseSave_Guard
	{
		[HarmonyPriority(Priority.First)]
		internal static bool Prefix() { return BonesEditor.AllowSaveDuringBoneBlend(); }
	}

	[HarmonyPatch(typeof(Studio.Studio), "LoadScene", new[] { typeof(string) })]
	internal static class BoneBlend_SceneLoad_Guard
	{
		internal static void Prefix() { BonesEditor.CancelActiveBoneBlend("Preview cancelled: scene load requested."); }
	}

	[HarmonyPatch(typeof(Studio.Studio), "Duplicate", new Type[0])]
	internal static class BoneBlend_Duplicate_Guard
	{
		internal static void Prefix() { BonesEditor.CancelActiveBoneBlend("Preview cancelled before duplication."); }
	}

	[HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
	internal static class BoneBlend_CharacterChange_Guard
	{
		internal static void Prefix(OCIChar __instance) { BonesEditor.CancelBoneBlendForTarget(__instance); }
	}

	[HarmonyPatch(typeof(OCIChar), "LoadClothesFile", new[] { typeof(string) })]
	internal static class BoneBlend_ClothesChange_Guard
	{
		internal static void Prefix(OCIChar __instance) { BonesEditor.CancelBoneBlendForTarget(__instance); }
	}

	[HarmonyPatch(typeof(OCIChar), "SetCoordinateInfo", new[] { typeof(CharDefine.CoordinateType), typeof(bool) })]
	internal static class BoneBlend_CoordinateChange_Guard
	{
		internal static void Prefix(OCIChar __instance) { BonesEditor.CancelBoneBlendForTarget(__instance); }
	}

	[HarmonyPatch(typeof(PauseCtrl), "Load", new[] { typeof(OCIChar), typeof(string) })]
	internal static class BoneBlend_PoseLoad_Guard
	{
		internal static void Prefix(OCIChar _ociChar) { BonesEditor.CancelBoneBlendForTarget(_ociChar); }
	}
}
#endif
