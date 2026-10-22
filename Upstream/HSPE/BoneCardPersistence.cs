#if HONEYSELECT
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using Harmony;
using HSPE.AMModules;
using Studio;
using UnityEngine;

namespace HSPE
{
	// Character PNG reads only produce detached data. Only explicit Add(path)/ChangeChara operations
	// can schedule application; scene construction, duplication and thumbnail readers cannot.
	internal static class BoneCardPersistence
	{
		private sealed class ReadRecord
		{
			internal WeakReference file;
			internal long serial;
			internal int frame;
			internal bool success;
			internal BonesEditor.CardBoneState state;
			internal string error;
		}
		private sealed class PreparedWrite
		{
			internal WeakReference file;
			internal string xml;
			internal int frame;
		}
		internal sealed class Replacement
		{
			internal long serial;
			internal bool loaded, nativeSuccess;
			internal ReadRecordData data;
			internal Pending previous;
		}
		internal sealed class ReadRecordData
		{
			internal BonesEditor.CardBoneState state;
			internal string error;
		}
		internal sealed class Pending
		{
			internal OCIChar target;
			internal CharFile file;
			internal ReadRecordData data;
			internal int generation;
			internal long serial;
			internal Transform root;
		}
		private static readonly List<ReadRecord> Reads = new List<ReadRecord>();
		private static readonly List<PreparedWrite> Writes = new List<PreparedWrite>();
		private static readonly Dictionary<OCIChar, Pending> PendingLoads = new Dictionary<OCIChar, Pending>();
		private static readonly Dictionary<OCIChar, Replacement> Replacements = new Dictionary<OCIChar, Replacement>();
		private static long _serial;
		private static MethodInfo _extendedWriter;
		private static bool _optionalHooksInstalled;
		internal static int SceneEpoch { get; private set; }

		internal static long Serial { get { return _serial; } }

		internal static void BeginRead(CharFile file)
		{
			RemoveRead(file);
			// Bounded and weak: library browsing must not retain character textures/files indefinitely.
			if (Reads.Count >= 256) Reads.RemoveAt(0);
			Reads.Add(new ReadRecord { file = new WeakReference(file), serial = ++_serial, frame = Time.frameCount });
		}
		private static ReadRecord FindRead(CharFile file)
		{
			foreach (ReadRecord record in Reads) if (ReferenceEquals(record.file.Target, file)) return record;
			return null;
		}
		private static void RemoveRead(CharFile file)
		{
			Reads.RemoveAll(r => !r.file.IsAlive || ReferenceEquals(r.file.Target, file));
		}
		internal static void FinishRead(CharFile file, bool success)
		{
			ReadRecord record = FindRead(file);
			if (record != null) record.success = success;
		}
		internal static void OnCharRead(CharFile file, XmlNode node)
		{
			ReadRecord record = FindRead(file);
			if (record == null) return; // Inline scene/duplicate/browse data is never a card-load operation.
			record.state = null;
			record.error = null;
			try
			{
				XmlNode payload = null;
				if (node != null) foreach (XmlNode child in node.ChildNodes)
				{
					if (child.Name != "boneCard") continue;
					if (payload != null) throw new InvalidDataException("Duplicate HSPE card sections.");
					payload = child;
				}
				if (payload != null) record.state = BonesEditor.DecodeCardBones(payload, false);
			}
			catch (Exception error) { record.error = "HSPE bone data was not loaded: " + error.Message; }
		}

		private static ReadRecordData ConsumeRead(CharFile file, long after)
		{
			ReadRecord record = FindRead(file);
			ReadRecordData data = record != null && record.success && record.serial > after
				? new ReadRecordData { state = record.state, error = record.error } : null;
			RemoveRead(file);
			return data;
		}

		internal static BonesEditor FindEditor(CharFile file)
		{
			foreach (PoseController controller in PoseController._poseControllers)
				if (controller != null && controller._target != null && controller._target.type == GenericOCITarget.Type.Character
					&& controller._target.ociChar.charInfo != null && ReferenceEquals(controller._target.ociChar.charInfo.chaFile, file)) return controller._bonesEditor;
			return null;
		}
		private static PoseController Controller(OCIChar target)
		{
			return target == null || target.guideObject == null || target.guideObject.transformTarget == null
				? null : target.guideObject.transformTarget.GetComponent<PoseController>();
		}

		internal static void Added(OCIChar target, long after)
		{
			if (target == null || target.charInfo == null) return;
			ReadRecordData data = ConsumeRead(target.charInfo.chaFile, after);
			if (data != null) Queue(target, data);
		}
		private static void Queue(OCIChar target, ReadRecordData data)
		{
			if (target.guideObject == null || target.guideObject.transformTarget == null) return;
			PoseController controller = Controller(target);
			long serial = ++_serial;
			if (controller != null) { controller._bonesEditor.lastCardOperation = serial; ++controller._bonesEditor.boneStateGeneration; }
			PendingLoads[target] = new Pending { target = target, file = target.charInfo.chaFile, data = data,
				root = target.guideObject.transformTarget, serial = serial, generation = controller == null ? 0 : controller._bonesEditor.boneStateGeneration };
			Trace("queued " + (data.state == null ? "ordinary/invalid" : data.state.bones.Count + " bones"));
		}
		internal static Replacement BeginReplacement(OCIChar target)
		{
			Pending previous;
			PendingLoads.TryGetValue(target, out previous);
			CancelTarget(target);
			Replacement operation = new Replacement { serial = _serial, previous = previous };
			Replacements[target] = operation;
			return operation;
		}
		// Injected immediately after the native CharFile.Load result, before Reload/InitFK.
		// A failed file read never deliberately clears old HSPE settings, even though native ChangeChara ignores its result.
		internal static bool LoadedForReplacement(bool success, OCIChar target)
		{
			Replacement operation;
			if (!Replacements.TryGetValue(target, out operation)) return success;
			operation.nativeSuccess = success;
			if (!success) return success;
			operation.data = ConsumeRead(target.charInfo.chaFile, operation.serial);
			if (operation.data == null) return success;
			PoseController controller = Controller(target);
			operation.loaded = controller == null || controller._bonesEditor.RetireBonesForCharacterReplacement();
			return success;
		}
		internal static void EndReplacement(OCIChar target, Replacement operation)
		{
			Replacement current;
			if (!Replacements.TryGetValue(target, out current) || !ReferenceEquals(operation, current)) return;
			Replacements.Remove(target);
			if (operation.loaded) Queue(target, operation.data);
			else if (!operation.nativeSuccess && operation.previous != null) PendingLoads[target] = operation.previous;
		}
		internal static void CancelTarget(OCIChar target)
		{
			if (target == null) return;
			PendingLoads.Remove(target);
			Replacements.Remove(target);
		}
		internal static void ResetScene()
		{
			++SceneEpoch;
			Reads.Clear(); Writes.Clear(); PendingLoads.Clear(); Replacements.Clear();
			Trace("scene reset: discarded pending card operations");
		}
		internal static bool ChangedSince(OCIChar target, long serial)
		{
			Pending pending;
			if (PendingLoads.TryGetValue(target, out pending) && pending.serial > serial) return true;
			PoseController controller = Controller(target);
			return controller != null && controller._bonesEditor.lastCardOperation > serial;
		}
		internal static bool AllowSceneSave()
		{
			foreach (Pending pending in PendingLoads.Values)
			{
				if (pending.root == null || pending.data.state == null) continue;
				Debug.LogWarning("HSPE: Wait for the character's card bone settings to finish loading, then save the scene again.");
				return false;
			}
			return true;
		}
		internal static void Tick()
		{
			Reads.RemoveAll(r => !r.file.IsAlive || r.frame < Time.frameCount - 1);
			Writes.RemoveAll(w => !w.file.IsAlive || w.frame < Time.frameCount);
			// All replacement calls are synchronous. A leftover entry means an exception skipped the postfix.
			Replacements.Clear();
			if (PendingLoads.Count == 0) return;
			List<OCIChar> remove = new List<OCIChar>();
			foreach (KeyValuePair<OCIChar, Pending> pair in PendingLoads)
			{
				Pending pending = pair.Value;
				if (pending.root == null || pending.target.charInfo == null || !ReferenceEquals(pending.file, pending.target.charInfo.chaFile)) remove.Add(pair.Key);
			}
			foreach (OCIChar target in remove) PendingLoads.Remove(target);
		}
		internal static void RigEvaluated(PoseController controller)
		{
			if (controller == null || controller._target == null || controller._bonesEditor == null) return;
			OCIChar target = controller._target.ociChar;
			Pending pending;
			if (target == null || !PendingLoads.TryGetValue(target, out pending)) return;
			if (pending.root != controller.transform || pending.generation != controller._bonesEditor.boneStateGeneration
				|| target.charInfo == null || !ReferenceEquals(pending.file, target.charInfo.chaFile)) { CancelTarget(target); return; }
			if (target.listBones == null || target.charBody == null || !target.charBody.LoadEnd) return;
			// This signal comes from that exact character's completed CharBody.LateUpdate, not a guessed frame delay.
			PendingLoads.Remove(target);
			controller._bonesEditor.lastCardOperation = pending.serial;
			controller._target.RefreshFKBones();
			BonesEditor editor = controller._bonesEditor;
			if (pending.data.state != null)
			{
				if (editor.ApplyCardBones(pending.data.state, false) && pending.data.state.bones.Count != 0) controller.enabled = true;
			}
			else
			{
				editor.saveBonesOnCard = false;
				editor.SetBoneCardStatus(pending.data.error ?? "This card has no HSPE bone edits.", pending.data.error != null);
			}
			Trace("completed card application once");
		}

		private static void Trace(string message) { if (BonesEditor.VerboseBlendLogs) Debug.Log("HSPE cards: " + message); }
		private static string CaptureExport(CharFile file)
		{
			BonesEditor editor = FindEditor(file);
			if (BonesEditor.OmitCardForBlendPreview())
			{
				if (editor != null) editor.SetBoneCardStatus("Card saved without HSPE bone data: blend preview was active.", false);
				return null;
			}
			foreach (Pending pending in PendingLoads.Values)
				if (ReferenceEquals(pending.file, file)) throw new InvalidOperationException("Wait for this character's bone settings to finish loading before saving its card.");
			if (editor == null || !editor.saveBonesOnCard) return null;
			return BonesEditor.EncodeCardBones(editor.CaptureCardBones(false), false);
		}
		internal static bool PrepareExport(CharFile file)
		{
			Writes.RemoveAll(w => !w.file.IsAlive || ReferenceEquals(w.file.Target, file));
			try
			{
				string xml = CaptureExport(file);
				Writes.Add(new PreparedWrite { file = new WeakReference(file), xml = xml, frame = Time.frameCount });
				return true;
			}
			catch (Exception error)
			{
				string message = "Card save cancelled before writing: " + error.Message;
				BonesEditor editor = FindEditor(file);
				if (editor != null) editor.SetBoneCardStatus(message, true);
				else Debug.LogWarning("HSPE cards: " + message);
				return false;
			}
		}
		internal static void PrepareWriter(CharFile file)
		{
			foreach (PreparedWrite write in Writes)
				if (write.frame == Time.frameCount && ReferenceEquals(write.file.Target, file)) return;
			if (!PrepareExport(file)) throw new InvalidDataException("HSPE bone export validation failed before character serialization.");
		}
		internal static void OnCharWrite(CharFile file, XmlTextWriter writer)
		{
			// Policy is also enforced here for third-party HSExtSave callers that bypass known producers.
			if (BonesEditor.OmitCardForBlendPreview())
			{
				Writes.RemoveAll(w => ReferenceEquals(w.file.Target, file));
				BonesEditor editor = FindEditor(file);
				if (editor != null) editor.SetBoneCardStatus("Card saved without HSPE bone data: blend preview was active.", false);
				return;
			}
			PreparedWrite prepared = Writes.Find(w => w.frame == Time.frameCount && ReferenceEquals(w.file.Target, file));
			string xml = prepared == null ? CaptureExport(file) : prepared.xml;
			Writes.RemoveAll(w => ReferenceEquals(w.file.Target, file));
			if (xml != null)
			{
				writer.WriteRaw(xml);
				BonesEditor editor = FindEditor(file);
				if (editor != null) editor.SetBoneCardStatus("HSPE bone settings included in the card save.", false);
			}
		}

		internal static bool SaveNativeCard(CharFile file, Stream stream)
		{
			// ONLY the standalone filename overload uses this wrapper. Never append to inline scene SaveWithoutPNG.
			using (BinaryWriter writer = new BinaryWriter(stream))
			{
				bool result = file.Save(writer);
				if (result)
				{
					if (_extendedWriter == null) _extendedWriter = typeof(HSExtSave.HSExtSave).Assembly.GetType("HSExtSave.CharFile_SaveWithoutPNG_Patches", true).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);
					_extendedWriter.Invoke(null, new object[] { file, writer });
				}
				return result;
			}
		}

		internal static void InstallOptionalSaveHooks()
		{
			if (_optionalHooksInstalled) return;
			_optionalHooksInstalled = true;
			// Optional reflection, no dependency on or edits to CharaMakerNeo. Its XML writer remains the owner of its stream.
			Type producer = null;
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				producer = assembly.GetType("CharaMakerNeo.Tools.CharFileExt", false);
				if (producer != null) break;
			}
			if (producer == null) return;
			HarmonyInstance harmony = HarmonyInstance.Create(HSPE._guid + ".cards");
			try
			{
				harmony.Patch(producer.GetMethod("SaveChara", new[] { typeof(OCIChar), typeof(bool), typeof(int) }), new HarmonyMethod(typeof(BoneCardPersistence).GetMethod("PrepareCharaMaker", BindingFlags.NonPublic | BindingFlags.Static)), null, null);
				harmony.Patch(producer.GetMethod("Save", new[] { typeof(CharFile), typeof(string), typeof(byte[]) }), new HarmonyMethod(typeof(BoneCardPersistence).GetMethod("PrepareCharaMakerFile", BindingFlags.NonPublic | BindingFlags.Static)), null, null);
			}
			catch (Exception error) { Debug.LogWarning("HSPE: CharaMakerNeo preflight hook unavailable; shared card-writer validation remains active. " + error); }
		}
		private static bool PrepareCharaMaker(OCIChar character) { return character == null || PrepareExport(character.charInfo.chaFile); }
		private static bool PrepareCharaMakerFile(CharFile chaFile) { return PrepareExport(chaFile); }
	}

	[HarmonyPatch(typeof(CharFile), "Load", new[] { typeof(string), typeof(bool), typeof(bool) })]
	internal static class BoneCard_FileRead
	{
		[HarmonyPriority(Priority.First)]
		internal static void Prefix(CharFile __instance) { BoneCardPersistence.BeginRead(__instance); }
		[HarmonyPriority(Priority.Last)]
		internal static void Postfix(CharFile __instance, bool __result) { BoneCardPersistence.FinishRead(__instance, __result); }
	}
	[HarmonyPatch(typeof(AddObjectFemale), "Add", new[] { typeof(string) })]
	internal static class BoneCard_AddFemale
	{
		internal static void Prefix(ref long __state) { __state = BoneCardPersistence.Serial; }
		internal static void Postfix(OCIChar __result, long __state) { BoneCardPersistence.Added(__result, __state); }
	}
	[HarmonyPatch(typeof(AddObjectMale), "Add", new[] { typeof(string) })]
	internal static class BoneCard_AddMale
	{
		internal static void Prefix(ref long __state) { __state = BoneCardPersistence.Serial; }
		internal static void Postfix(OCIChar __result, long __state) { BoneCardPersistence.Added(__result, __state); }
	}
	[HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
	internal static class BoneCard_Replace
	{
		internal static void Prefix(OCIChar __instance, ref BoneCardPersistence.Replacement __state) { __state = BoneCardPersistence.BeginReplacement(__instance); }
		[HarmonyPriority(Priority.Last)]
		internal static void Postfix(OCIChar __instance, BoneCardPersistence.Replacement __state) { BoneCardPersistence.EndReplacement(__instance, __state); }
		internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo load = typeof(CharFile).GetMethod("Load", new[] { typeof(string), typeof(bool), typeof(bool) });
			int matches = 0;
			foreach (CodeInstruction instruction in instructions)
			{
				yield return instruction;
				if (!Equals(instruction.operand, load)) continue;
				++matches;
				yield return new CodeInstruction(OpCodes.Ldarg_0);
				yield return new CodeInstruction(OpCodes.Call, typeof(BoneCardPersistence).GetMethod("LoadedForReplacement", BindingFlags.NonPublic | BindingFlags.Static));
			}
			if (matches != 1) throw new InvalidOperationException("Unexpected HS1 ChangeChara load seam; refusing replacement patch.");
		}
	}
	[HarmonyPatch(typeof(Studio.Studio), "InitScene", new[] { typeof(bool) })]
	internal static class BoneCard_SceneReset
	{
		internal static void Prefix() { BoneCardPersistence.ResetScene(); }
	}
	[HarmonyPatch(typeof(CharFile), "Save", new[] { typeof(BinaryWriter) })]
	internal static class BoneCard_WriterPreparation
	{
		internal static void Prefix(CharFile __instance) { BoneCardPersistence.PrepareWriter(__instance); }
	}
	[HarmonyPatch(typeof(CharFile), "Save", new[] { typeof(string) })]
	internal static class BoneCard_NativeFileSave
	{
		[HarmonyPriority(Priority.First)]
		internal static bool Prefix(CharFile __instance, ref bool __result)
		{
			if (BoneCardPersistence.PrepareExport(__instance)) return true;
			__result = false; return false;
		}
		internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo save = typeof(CharFile).GetMethod("Save", new[] { typeof(Stream) });
			int matches = 0;
			foreach (CodeInstruction instruction in instructions)
			{
				if (Equals(instruction.operand, save))
				{
					++matches;
					instruction.opcode = OpCodes.Call;
					instruction.operand = typeof(BoneCardPersistence).GetMethod("SaveNativeCard", BindingFlags.NonPublic | BindingFlags.Static);
				}
				yield return instruction;
			}
			if (matches != 1) throw new InvalidOperationException("Unexpected HS1 native card writer seam; refusing file-save patch.");
		}
	}
}
#endif
