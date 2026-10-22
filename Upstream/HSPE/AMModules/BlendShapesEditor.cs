using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
#if IPA
using Harmony;
#elif BEPINEX
using HarmonyLib;
#endif
using Studio;
using ToolBox;
using ToolBox.Extensions;
using UnityEngine;
#if HONEYSELECT || KOIKATSU || AISHOUJO || HONEYSELECT2
using InstanceDict = System.Collections.Generic.Dictionary<FaceBlendShape, HSPE.AMModules.BlendShapesEditor>;
using InstancePair = System.Collections.Generic.KeyValuePair<FaceBlendShape, HSPE.AMModules.BlendShapesEditor>;
#elif PLAYHOME
using InstanceDict = System.Collections.Generic.Dictionary<Human, HSPE.AMModules.BlendShapesEditor>;
using InstancePair = System.Collections.Generic.KeyValuePair<Human, HSPE.AMModules.BlendShapesEditor>;
#endif

namespace HSPE.AMModules
{
	public partial class BlendShapesEditor : AdvancedModeModule
	{
		#region Constants
		private static readonly Dictionary<string, string> _skinnedMeshAliases = new Dictionary<string, string>
		{
#if HONEYSELECT || PLAYHOME
			{"cf_O_head", "Eyes / Mouth"},
			{"cf_O_ha", "Teeth"},
			{"cf_O_matuge", "Eyelashes"},
			{"cf_O_mayuge", "Eyebrows"},
			{"cf_O_sita", "Tongue"},
			{"cf_O_namida01", "Tears 1"},
			{"cf_O_namida02", "Tears 2"},

			{"cm_O_head", "Face"},
			{"cm_O_ha", "Teeth"},
			{"cm_O_mayuge", "Eyebrows"},
			{"cm_O_sita", "Tongue"},
			{"O_hige00", "Jaw"},
#elif KOIKATSU
			{"cf_O_face",  "Eyes / Mouth"},
			{"cf_O_tooth",  "Teeth"},
			{"cf_O_canine",  "Canines"},
			{"cf_O_eyeline",  "Upper Eyelashes"},
			{"cf_O_eyeline_low",  "Lower Eyelashes"},
			{"cf_O_mayuge",  "Eyebrows"},
			{"cf_Ohitomi_L",  "Left Eye White"},
			{"cf_Ohitomi_R",  "Right Eye White"},
			{"o_tang",  "Tongue"},
			{"cf_O_namida_L",  "Tears L"},
			{"cf_O_namida_M",  "Tears M"},
			{"cf_O_namida_S",  "Tears S"},
#elif AISHOUJO
			{"o_eyelashes",  "Eyelashes"},
			{"o_head",  "Head"},
			{"o_namida",  "Tears"},
			{"o_tang",  "Tongue"},
			{"o_tooth",  "Teeth"},
#elif HONEYSELECT2
			{"o_eyelashes",  "Eyelashes"},
			{"o_head",  "Head"},
			{"o_namida",  "Tears"},
			{"o_tang",  "Tongue"},
			{"o_tooth",  "Teeth"},
#endif
		};
		private static readonly string _presetsPath;
		#endregion

		#region Statics
		private static InstanceDict _instanceByFaceBlendShape = new InstanceDict();
		private static string[] _presets = new string[0];
		internal static readonly Dictionary<string, string> _blendShapeAliases = new Dictionary<string, string>();
		private static readonly Dictionary<int, string> _femaleSeparators = new Dictionary<int, string>();
		private static readonly Dictionary<int, string> _maleSeparators = new Dictionary<int, string>();
		private static readonly int _femaleEyesComponentsCount;
		private static readonly int _maleEyesComponentsCount;
		#endregion
		
		#region Private Types
#if HONEYSELECT || KOIKATSU
		[HarmonyPatch(typeof(FaceBlendShape), "LateUpdate", new Type[0])]
		private class FaceBlendShape_Patches
		{
			[HarmonyAfter("com.joan6694.hsplugins.instrumentation")]
			public static void Postfix(FaceBlendShape __instance)
			{
				if (_instanceByFaceBlendShape.TryGetValue(__instance, out BlendShapesEditor editor))
					editor.FaceBlendShapeOnPostLateUpdate();
			}
		}
#elif PLAYHOME
		[HarmonyPatch(typeof(Human), "LateUpdate")]
		private class Human_Patches
		{
			[HarmonyAfter("com.joan6694.hsplugins.instrumentation")]
			public static void Postfix(Human __instance)
			{
				BlendShapesEditor editor;
				if (_instanceByFaceBlendShape.TryGetValue(__instance, out editor))
					editor.FaceBlendShapeOnPostLateUpdate();
			}
		}
#elif AISHOUJO || HONEYSELECT2
		[HarmonyPatch(typeof(FaceBlendShape), "OnLateUpdate")]
		private class FaceBlendShape_Patches
		{
			[HarmonyAfter("com.joan6694.hsplugins.instrumentation")]
			public static void Postfix(FaceBlendShape __instance)
			{
				BlendShapesEditor editor;
				if (_instanceByFaceBlendShape.TryGetValue(__instance, out editor))
					editor.FaceBlendShapeOnPostLateUpdate();
			}
		}
#endif
		#endregion

		#region Private Variables
		private Vector2 _skinnedMeshRenderersScroll;
		private Vector2 _blendShapesScroll;
		private readonly List<SkinnedMeshRenderer> _skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
		private readonly Dictionary<string, SkinnedMeshRenderer> _skinnedMeshRenderersByPath = new Dictionary<string, SkinnedMeshRenderer>();
		private readonly Dictionary<SkinnedMeshRenderer, SkinnedMeshRendererData> _dirtySkinnedMeshRenderers = new Dictionary<SkinnedMeshRenderer, SkinnedMeshRendererData>();
		private readonly Dictionary<string, SkinnedMeshRendererData> _headlessDirtySkinnedMeshRenderers = new Dictionary<string, SkinnedMeshRendererData>();
		private int _headlessReconstructionTimeout = 0;
		private SkinnedMeshRenderer _headRenderer;
		private SkinnedMeshRenderer _skinnedMeshTarget;
		private bool _linkEyesComponents = true;
		private readonly Dictionary<SkinnedMeshRenderer, SkinnedMeshRendererWrapper> _links = new Dictionary<SkinnedMeshRenderer, SkinnedMeshRendererWrapper>();
		private string _search = "";
		private readonly GenericOCITarget _target;
		private readonly Dictionary<XmlNode, SkinnedMeshRenderer> _secondPassLoadingNodes = new Dictionary<XmlNode, SkinnedMeshRenderer>();
		private bool _showSaveLoadWindow = false;
		private Vector2 _presetsScroll;
		private string _presetName = "";
		private bool _removePresetMode;
		private int _renameIndex = -1;
		private string _renameString = "";
		private int _lastEditedBlendShape = -1;
		private bool _isBusy = false;
		#endregion

		#region Public Fields
		public override AdvancedModeModuleType type => AdvancedModeModuleType.BlendShapes;
		public override string displayName => "Blend Shapes";
		public override bool shouldDisplay => _skinnedMeshRenderers.Any(r => r && r.sharedMesh && r.sharedMesh.blendShapeCount > 0);
		#endregion

		#region Unity Methods
		static BlendShapesEditor()
		{
#if HONEYSELECT || PLAYHOME
			_femaleSeparators.Add(0, "Eyes");
			_femaleSeparators.Add(58, "Mouth");
			_maleSeparators.Add(0, "Eyes");
			_maleSeparators.Add(14, "Mouth");
			_femaleEyesComponentsCount = 58;
			_maleEyesComponentsCount = 14;
#elif KOIKATSU
			_femaleSeparators.Add(0, "Eyes");
			_femaleSeparators.Add(28, "Mouth");
			_maleSeparators = _femaleSeparators;
			_femaleEyesComponentsCount = 28;
			_maleEyesComponentsCount = _femaleEyesComponentsCount;
#elif AISHOUJO || HONEYSELECT2
			_femaleSeparators.Add(0, "Eyes");
			_femaleSeparators.Add(16, "Eyebrows");
			_femaleSeparators.Add(26, "Mouth");
			_maleSeparators = _femaleSeparators;
			_femaleEyesComponentsCount = 16;
			_maleEyesComponentsCount = _femaleEyesComponentsCount;
#endif
			_presetsPath = Path.Combine(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), HSPE._name), "BlendShapesPresets");
		}

		public BlendShapesEditor(PoseController parent, GenericOCITarget target) : base(parent)
		{
			_parent.onLateUpdate += LateUpdate;
			_parent.onDisable += OnDisable;
			_target = target;
			MainWindow._self.ExecuteDelayed(() =>
			{
				RefreshSkinnedMeshRendererList();
				if (_target.type == GenericOCITarget.Type.Character)
					Init();
			});
		}

		private void LateUpdate()
		{
			if (_headlessReconstructionTimeout >= 0)
			{
				_headlessReconstructionTimeout--;
				foreach (var pair in new Dictionary<string, SkinnedMeshRendererData>(_headlessDirtySkinnedMeshRenderers))
				{
					Transform t = _parent.transform.Find(pair.Key);
					if (t)
					{
						SkinnedMeshRenderer renderer = t.GetComponent<SkinnedMeshRenderer>();
						if (renderer)
						{
							_dirtySkinnedMeshRenderers.Add(renderer, pair.Value);
							_headlessDirtySkinnedMeshRenderers.Remove(pair.Key);
						}
					}
				}
			}
			else if (_headlessDirtySkinnedMeshRenderers.Count != 0)
				_headlessDirtySkinnedMeshRenderers.Clear();
			if (_target.type == GenericOCITarget.Type.Item)
				ApplyBlendShapeWeights();
		}

		public override void OnGUI()
		{
			if (!_showSaveLoadWindow)
				return;
			Rect windowRect = Rect.MinMaxRect(MainWindow._self._advancedModeRect.xMin - 180, MainWindow._self._advancedModeRect.yMin, MainWindow._self._advancedModeRect.xMin, MainWindow._self._advancedModeRect.yMax);
			IMGUIExtensions.DrawBackground(windowRect);
			GUILayout.Window(MainWindow._uniqueId + 1, windowRect, SaveLoadWindow, "Presets");
		}

		private void OnDisable()
		{
			if (_dirtySkinnedMeshRenderers.Count != 0)
				foreach (KeyValuePair<SkinnedMeshRenderer, SkinnedMeshRendererData> kvp in _dirtySkinnedMeshRenderers)
					foreach (KeyValuePair<int, BlendShapeData> weight in kvp.Value.dirtyBlendShapes)
					{
						if (kvp.Key && kvp.Value != null)
							kvp.Key.TrySetBlendShapeWeight(weight.Key, weight.Value.originalWeight);
					}
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			_parent.onLateUpdate -= LateUpdate;
			_parent.onDisable -= OnDisable;
			InstanceDict newInstances = new InstanceDict();
			foreach (InstancePair pair in _instanceByFaceBlendShape)
			{
				if (pair.Key != null)
					newInstances.Add(pair.Key, pair.Value);
			}
			_instanceByFaceBlendShape = newInstances;
		}
		#endregion

		#region Public Methods
		public override void OnCharacterReplaced()
		{
			_isBusy = true;
			if (_instanceByFaceBlendShape.Any(x => !x.Key))
			{
				var newInstanceByFBS = new InstanceDict();
				foreach (InstancePair pair in _instanceByFaceBlendShape)
				{
					if (pair.Key)
					{
						newInstanceByFBS.Add(pair.Key, pair.Value);
					}
				}
				_instanceByFaceBlendShape = newInstanceByFBS;
			}
			_links.Clear();

			RefreshSkinnedMeshRendererList();
			MainWindow._self.ExecuteDelayed(() =>
			{
				RefreshSkinnedMeshRendererList();
				Init();
				_isBusy = false;
			});
		}

		public override void OnLoadClothesFile()
		{
			RefreshSkinnedMeshRendererList();
			MainWindow._self.ExecuteDelayed(RefreshSkinnedMeshRendererList);
		}
#if HONEYSELECT || KOIKATSU
#if HONEYSELECT
		public override void OnCoordinateReplaced(CharDefine.CoordinateType coordinateType, bool force)
#elif KOIKATSU
		public override void OnCoordinateReplaced(ChaFileDefine.CoordinateType coordinateType, bool force)
#endif
		{
			_isBusy = true;
			RefreshSkinnedMeshRendererList();
			MainWindow._self.ExecuteDelayed(() =>
			{
				RefreshSkinnedMeshRendererList();
				_isBusy = false;
			});
		}
#endif

		public override void OnParentage(TreeNodeObject parent, TreeNodeObject child)
		{
			RefreshSkinnedMeshRendererList();
			MainWindow._self.ExecuteDelayed(RefreshSkinnedMeshRendererList);
		}

		public override void GUILogic()
		{
			Color c = GUI.color;
			GUILayout.BeginHorizontal();

			GUILayout.BeginVertical(GUILayout.ExpandWidth(false));

			_skinnedMeshRenderersScroll = GUILayout.BeginScrollView(_skinnedMeshRenderersScroll, false, true, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar, GUI.skin.box, GUILayout.ExpandWidth(false));
			foreach (SkinnedMeshRenderer r in _skinnedMeshRenderers)
			{
				if (!r || !r.sharedMesh || r.sharedMesh.blendShapeCount == 0)
					continue;

				if (_dirtySkinnedMeshRenderers.ContainsKey(r))
				{
					GUI.color = ColorPresets.Changed;
				}

				if (ReferenceEquals(r, _skinnedMeshTarget))
				{
					GUI.color = ColorPresets.Selected;
				}

				if (!_skinnedMeshAliases.TryGetValue(r.name, out string dName))
				{
					dName = r.name;
				}

				if (GUILayout.Button($"{dName}{(_dirtySkinnedMeshRenderers.ContainsKey(r) ? "*" : "")}"))
				{
					_skinnedMeshTarget = r;
					_lastEditedBlendShape = -1;
				}
				GUI.color = c;
			}
			GUILayout.EndScrollView();

			GUI.color = Color.green;
			if (GUILayout.Button("Save/Load preset"))
			{
				_showSaveLoadWindow = true;
				RefreshPresets();
			}
			GUI.color = c;

			if (_target.type == GenericOCITarget.Type.Character)
				_linkEyesComponents = GUILayout.Toggle(_linkEyesComponents, "Link eyes components");
			if (GUILayout.Button("Force refresh list"))
				RefreshSkinnedMeshRendererList();
			GUI.color = ColorPresets.Tomato;
			if (GUILayout.Button("Reset all"))
				ResetAll();
			GUI.color = c;
			GUILayout.EndVertical();


			if (_skinnedMeshTarget)
			{
				GUILayout.BeginVertical(GUI.skin.box);
				GUILayout.BeginHorizontal();
				GUILayout.Label("Search", GUILayout.ExpandWidth(false));
				_search = GUILayout.TextField(_search, GUILayout.ExpandWidth(true));
				if (GUILayout.Button("X", GUILayout.ExpandWidth(false)))
					_search = "";
				GUILayout.EndHorizontal();

				_blendShapesScroll = GUILayout.BeginScrollView(_blendShapesScroll, false, true, GUILayout.ExpandWidth(false));

				_dirtySkinnedMeshRenderers.TryGetValue(_skinnedMeshTarget, out SkinnedMeshRendererData data);
				bool zeroResult = true;
				for (int i = 0; i < _skinnedMeshTarget.sharedMesh.blendShapeCount; ++i)
				{
					if (_headRenderer == _skinnedMeshTarget)
					{
						Dictionary<int, string> separatorDict = _target.isFemale ? _femaleSeparators : _maleSeparators;
						if (separatorDict.TryGetValue(i, out string s))
							GUILayout.Label(s, GUI.skin.box);
					}
					string blendShapeName = _skinnedMeshTarget.sharedMesh.GetBlendShapeName(i);
					if (!_blendShapeAliases.TryGetValue(blendShapeName, out string blendShapeAlias))
						blendShapeAlias = null;
					if ((blendShapeAlias != null && blendShapeAlias.HasText(_search)) || blendShapeName.HasText(_search))
					{
						zeroResult = false;
						float blendShapeWeight;

						if (data != null && data.dirtyBlendShapes.TryGetValue(i, out BlendShapeData bsData))
						{
							blendShapeWeight = bsData.weight;
							GUI.color = ColorPresets.Changed;
						}
						else
							blendShapeWeight = _skinnedMeshTarget.GetBlendShapeWeight(i);

						GUILayout.BeginHorizontal();

						GUILayout.BeginVertical(GUILayout.ExpandHeight(false));

						GUILayout.BeginHorizontal();
						if (_renameIndex != i)
						{
							GUILayout.Label($"{i} {blendShapeAlias ?? blendShapeName}");
							GUILayout.FlexibleSpace();
						}
						else
						{
							GUILayout.Label(i.ToString(), GUILayout.ExpandWidth(false));
							_renameString = GUILayout.TextField(_renameString, GUILayout.ExpandWidth(true));
						}
						if (GUILayout.Button(_renameIndex != i ? "Rename" : "Save", GUILayout.ExpandWidth(false)))
						{
							if (_renameIndex != i)
							{
								_renameIndex = i;
								_renameString = blendShapeAlias ?? blendShapeName;
							}
							else
							{
								_renameIndex = -1;
								// changed (HSPE fork): moved to SetBlendShapeAlias (Kit\Advanced\BlendShapesEditor.Kit.cs)
								SetBlendShapeAlias(blendShapeName, _renameString);
							}
						}
						GUILayout.Label(blendShapeWeight.ToString("000"), GUILayout.ExpandWidth(false));
						GUILayout.EndHorizontal();

						GUILayout.BeginHorizontal();
						float newBlendShapeWeight = GUILayout.HorizontalSlider(blendShapeWeight, 0f, 100f);
						if (GUILayout.Button("-1", GUILayout.ExpandWidth(false)))
							newBlendShapeWeight -= 1;
						if (GUILayout.Button("+1", GUILayout.ExpandWidth(false)))
							newBlendShapeWeight += 1;
						newBlendShapeWeight = Mathf.Clamp(newBlendShapeWeight, 0, 100);
						GUILayout.EndHorizontal();
						// changed (HSPE fork): moved to SetWeightLinked (Kit\Advanced\BlendShapesEditor.Kit.cs)
						if (Mathf.Approximately(newBlendShapeWeight, blendShapeWeight) == false)
							SetWeightLinked(_skinnedMeshTarget, i, newBlendShapeWeight);
						GUILayout.EndVertical();

						GUI.color = ColorPresets.Tomato;

						// changed (HSPE fork): moved to ResetShapeLinked (Kit\Advanced\BlendShapesEditor.Kit.cs)
						if (GUILayout.Button("Reset", GUILayout.ExpandWidth(false), GUILayout.Height(50)) && data != null && data.dirtyBlendShapes.TryGetValue(i, out bsData))
							ResetShapeLinked(_skinnedMeshTarget, i);
						GUILayout.EndHorizontal();
						GUI.color = c;
					}
				}

				if (zeroResult)
				{
					GUILayout.BeginHorizontal();
					GUILayout.FlexibleSpace();
					GUILayout.EndHorizontal();
				}

				GUILayout.EndScrollView();

				GUILayout.BeginHorizontal();
				GUI.color = ColorPresets.Tomato;
				GUILayout.FlexibleSpace();
				// changed (HSPE fork): moved to ResetRendererLinked (Kit\Advanced\BlendShapesEditor.Kit.cs)
				if (GUILayout.Button("Reset", GUILayout.ExpandWidth(false)))
					ResetRendererLinked(_skinnedMeshTarget);
				GUI.color = c;
				GUILayout.EndHorizontal();

				GUILayout.EndVertical();
			}
			else
			{
				GUILayout.BeginVertical();
				GUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.EndVertical();
			}


			GUILayout.EndHorizontal();
		}

		private BlendShapeData SetBlendShapeWeight(SkinnedMeshRenderer renderer, int index, float weight)
		{
			BlendShapeData bsData = SetBlendShapeDirty(renderer, index);
			bsData.weight = weight;
			return bsData;
		}

		public void LoadFrom(BlendShapesEditor other)
		{
			MainWindow._self.ExecuteDelayed(() =>
			{
				foreach (var kvp in other._dirtySkinnedMeshRenderers)
				{
					Transform obj = _parent.transform.Find(kvp.Key.transform.GetPathFrom(other._parent.transform));
					if (obj)
					{
						SkinnedMeshRenderer renderer = obj.GetComponent<SkinnedMeshRenderer>();
						_dirtySkinnedMeshRenderers.Add(renderer, new SkinnedMeshRendererData(kvp.Value));
					}
				}
				_blendShapesScroll = other._blendShapesScroll;
				_skinnedMeshRenderersScroll = other._skinnedMeshRenderersScroll;
			}, 2);
		}
		#endregion

		#region Private Methods
		private void RefreshPresets()
		{
			if (Directory.Exists(_presetsPath))
			{
				_presets = Directory.GetFiles(_presetsPath, "*.xml");
				for (int i = 0; i < _presets.Length; i++)
					_presets[i] = Path.GetFileNameWithoutExtension(_presets[i]);
			}
		}

		private void SaveLoadWindow(int id)
		{
			GUILayout.BeginVertical();

			_presetsScroll = GUILayout.BeginScrollView(_presetsScroll, false, true, GUILayout.ExpandHeight(true));
			foreach (string preset in _presets)
			{
				if (GUILayout.Button(preset))
				{
					if (_removePresetMode)
						DeletePreset(preset + ".xml");
					else
						LoadPreset(preset + ".xml");
				}
			}
			GUILayout.EndScrollView();

			Color c = GUI.color;
			GUILayout.BeginVertical();
			if (_presets.Any(p => p.Equals(_presetName, StringComparison.OrdinalIgnoreCase)))
				GUI.color = Color.red;
			GUILayout.BeginHorizontal();
			GUILayout.Label("Name", GUILayout.ExpandWidth(false));
			_presetName = GUILayout.TextField(_presetName);
			GUILayout.EndHorizontal();
			GUI.color = c;

			GUI.enabled = _presetName.Length != 0;
			if (GUILayout.Button("Save"))
			{
				_presetName = _presetName.Trim();
				_presetName = string.Join("_", _presetName.Split(Path.GetInvalidFileNameChars()));
				if (_presetName.Length != 0)
				{
					SavePreset(_presetName + ".xml");
					RefreshPresets();
					_removePresetMode = false;
				}

			}
			GUI.enabled = true;
			if (_removePresetMode)
				GUI.color = Color.red;
			GUI.enabled = _presets.Length != 0;
			if (GUILayout.Button(_removePresetMode ? "Click on preset" : "Delete"))
				_removePresetMode = !_removePresetMode;
			GUI.enabled = true;
			GUI.color = c;

			if (GUILayout.Button("Open folder"))
				System.Diagnostics.Process.Start(_presetsPath);
			if (GUILayout.Button("Close"))
				_showSaveLoadWindow = false;

			GUILayout.EndVertical();

			GUILayout.EndVertical();
		}


		private void SavePreset(string name)
		{
			if (!Directory.Exists(_presetsPath))
				Directory.CreateDirectory(_presetsPath);
			using (XmlTextWriter writer = new XmlTextWriter(Path.Combine(_presetsPath, name), Encoding.UTF8))
			{
				writer.WriteStartElement("root");
				SaveXml(writer);
				writer.WriteEndElement();
			}
		}

		private void LoadPreset(string name)
		{
			string path = Path.Combine(_presetsPath, name);
			if (File.Exists(path))
			{
				XmlDocument doc = new XmlDocument();
				doc.Load(path);
				LoadXml(doc.FirstChild);
			}
		}

		private void DeletePreset(string name)
		{
			File.Delete(Path.GetFullPath(Path.Combine(_presetsPath, name)));
			_removePresetMode = false;
			RefreshPresets();
		}

		private void Init()
		{
			_headRenderer = null;
#if HONEYSELECT
			//_instanceByFaceBlendShape.Add(_target.ociChar.charBody.fbsCtrl, this);

			// note: no idea why the same key appears multiple times, unclear how this should be resolved at this time
			if (!_instanceByFaceBlendShape.ContainsKey(_target.ociChar.charBody.fbsCtrl))
				_instanceByFaceBlendShape.Add(_target.ociChar.charBody.fbsCtrl, this);
			//_instanceByFaceBlendShape[_target.ociChar.charBody.fbsCtrl] = this;
#elif PLAYHOME
			_instanceByFaceBlendShape.Add(this._target.ociChar.charInfo.human, this);
#elif KOIKATSU
			_instanceByFaceBlendShape.Add(this._target.ociChar.charInfo.fbsCtrl, this);
#elif AISHOUJO || HONEYSELECT2
			_instanceByFaceBlendShape.Add(this._target.ociChar.charInfo.fbsCtrl, this);
#endif
			foreach (SkinnedMeshRenderer renderer in _skinnedMeshRenderers)
			{
				switch (renderer.name)
				{
#if HONEYSELECT || KOIKATSU || PLAYHOME
					case "cf_O_head":
					case "cf_O_face":
#elif AISHOUJO || HONEYSELECT2
					case "o_head":
#endif
						_headRenderer = renderer;
						break;
				}

				switch (renderer.name)
				{
#if HONEYSELECT || PLAYHOME
					case "cf_O_head":
					case "cf_O_matuge":
					case "cf_O_namida01":
					case "cf_O_namida02":
#elif KOIKATSU
					case "cf_O_face":
					case "cf_O_eyeline":
					case "cf_O_eyeline_low":
					case "cf_Ohitomi_L":
					case "cf_Ohitomi_R":
					case "cf_O_namida_L":
					case "cf_O_namida_M":
					case "cf_O_namida_S":
#elif AISHOUJO || HONEYSELECT2
					case "o_eyelashes":
					case "o_namida":
					case "o_head":
#endif
						var wrapper = new SkinnedMeshRendererWrapper
						{
							renderer = renderer,
							links = new List<SkinnedMeshRendererWrapper>()
						};
						//_links.Add(renderer, wrapper);
						_links[renderer] = wrapper;

						break;
				}
			}

			foreach (var pair in _links)
			{
				foreach (var pair2 in _links)
				{
					if (pair.Key != pair2.Key)
						pair.Value.links.Add(pair2.Value);
				}
			}
		}

		private void ResetAll()
		{
			foreach (SkinnedMeshRenderer renderer in _skinnedMeshRenderers)
				SetMeshRendererNotDirty(renderer);
		}

		private void FaceBlendShapeOnPostLateUpdate()
		{
			if (_parent.enabled)
				ApplyBlendShapeWeights();
		}

		private void ApplyBlendShapeWeights()
		{
			if (_dirtySkinnedMeshRenderers.Count == 0)
				return;
			foreach (var kvp in _dirtySkinnedMeshRenderers)
			{
				foreach (KeyValuePair<int, BlendShapeData> weight in kvp.Value.dirtyBlendShapes)
				{
					// ????
					var data = kvp.Value;
					if (kvp.Key == null) UnityEngine.Debug.Log("null kvp.key: " + data.path);
					if (kvp.Value == null) UnityEngine.Debug.Log("null kvp.Value: " + data.path);

					if (kvp.Key && kvp.Value != null)
						kvp.Key.TrySetBlendShapeWeight(weight.Key, weight.Value.weight);
				}
			}
		}



		private void SetMeshRendererNotDirty(SkinnedMeshRenderer renderer)
		{
			if (_dirtySkinnedMeshRenderers.ContainsKey(renderer))
			{
				SkinnedMeshRendererData data = _dirtySkinnedMeshRenderers[renderer];
				foreach (KeyValuePair<int, BlendShapeData> kvp in data.dirtyBlendShapes)
				{
					renderer.TrySetBlendShapeWeight(kvp.Key, kvp.Value.originalWeight);
				}
				_dirtySkinnedMeshRenderers.Remove(renderer);
			}
		}

		private SkinnedMeshRendererData SetMeshRendererDirty(SkinnedMeshRenderer renderer)
		{
			if (!_dirtySkinnedMeshRenderers.TryGetValue(renderer, out SkinnedMeshRendererData data))
			{
				data = new SkinnedMeshRendererData
				{
					path = renderer.transform.GetPathFrom(_parent.transform)
				};
				_dirtySkinnedMeshRenderers.Add(renderer, data);
			}
			return data;
		}

		private BlendShapeData SetBlendShapeDirty(SkinnedMeshRenderer renderer, int index)
		{
			SkinnedMeshRendererData data = SetMeshRendererDirty(renderer);
			if (data.dirtyBlendShapes.TryGetValue(index, out BlendShapeData bsData) == false)
			{
				bsData = new BlendShapeData
				{
					originalWeight = renderer.GetBlendShapeWeight(index)
				};
				data.dirtyBlendShapes.Add(index, bsData);
			}
			return bsData;
		}

		private BlendShapeData SetBlendShapeDirty(SkinnedMeshRendererData data, int index)
		{
			if (!data.dirtyBlendShapes.TryGetValue(index, out BlendShapeData bsData))
			{
				bsData = new BlendShapeData();
				data.dirtyBlendShapes.Add(index, bsData);
			}
			return bsData;
		}

		public void Trigger_RefreshSkinnedMeshRendererList()
		{
			OnCharacterReplaced();
			//RefreshSkinnedMeshRendererList();
		}

		private void RefreshSkinnedMeshRendererList()
		{
			SkinnedMeshRenderer[] existingSMRenderers = _parent.GetComponentsInChildren<SkinnedMeshRenderer>(true);

			// note: simplify deleting
			foreach (SkinnedMeshRenderer r in _skinnedMeshRenderers.Except(existingSMRenderers).ToList())
			{
				if (_dirtySkinnedMeshRenderers.TryGetValue(r, out SkinnedMeshRendererData data))
				{
					if (data.path != null)
						_headlessDirtySkinnedMeshRenderers.Add(data.path, data); // note: TODO: data.path may be null
					else
					{
						//UnityEngine.Debug.Log("Found SkinnedMeshRendererData with no path:\r\n" + string.Join("\r\n", data.dirtyBlendShapes.Select( x => $"{x.Key}: weight: {x.Value.weight} | original weight: {x.Value.originalWeight}").ToArray()));
					}
					_headlessReconstructionTimeout = 5;
					_dirtySkinnedMeshRenderers.Remove(r);
				}
				_skinnedMeshRenderers.Remove(r);
			}

			// note: simplify the adding
			// warning: something throws NullReferenceException in All(); possible that _parent PoseController can sometimes end up with invalid (not up to date) children?
			bool RendererNotChildOfGO(SkinnedMeshRenderer r, GameObject go)
			{
				return !go || !go.transform || !r.transform.IsChildOf(go.transform);
			}

			var renderersToAdd = existingSMRenderers
				.Except(_skinnedMeshRenderers)
				.Where(r => r && r.transform && _parent._childObjects.All(child => RendererNotChildOfGO(r, child))); /*(child == null || child.transform == null) ^ !r.transform.IsChildOf(child.transform)));*/
			_skinnedMeshRenderers.AddRange(renderersToAdd);

			#region note: refactored
			/*
			List<SkinnedMeshRenderer> toDelete = null;
			foreach (SkinnedMeshRenderer r in _skinnedMeshRenderers)
				if (skinnedMeshRenderers.Contains(r) == false)
				{
					if (toDelete == null)
						toDelete = new List<SkinnedMeshRenderer>();
					toDelete.Add(r);
				}
			if (toDelete != null)
			{
				foreach (SkinnedMeshRenderer r in toDelete)
				{
					SkinnedMeshRendererData data;
					if (_dirtySkinnedMeshRenderers.TryGetValue(r, out data))
					{
						_headlessDirtySkinnedMeshRenderers.Add(data.path, data);
						_headlessReconstructionTimeout = 5;
						_dirtySkinnedMeshRenderers.Remove(r);
					}
					_skinnedMeshRenderers.Remove(r);
				}
			}

			List<SkinnedMeshRenderer> toAdd = null;
			foreach (SkinnedMeshRenderer r in skinnedMeshRenderers)
				if (_skinnedMeshRenderers.Contains(r) == false && _parent._childObjects.All(child => r.transform.IsChildOf(child.transform) == false))
				{
					if (toAdd == null)
						toAdd = new List<SkinnedMeshRenderer>();
					toAdd.Add(r);
				}
			if (toAdd != null)
			{
				foreach (SkinnedMeshRenderer r in toAdd)
					_skinnedMeshRenderers.Add(r);
			}
			*/
			#endregion

			_skinnedMeshRenderersByPath.Clear();
			foreach (SkinnedMeshRenderer renderer in _skinnedMeshRenderers)
			{
				string path = renderer.transform.GetPathFrom(_parent.transform);
				if (!_skinnedMeshRenderersByPath.ContainsKey(path))
					_skinnedMeshRenderersByPath.Add(path, renderer);
			}

			if (_skinnedMeshRenderers.Count != 0 && _skinnedMeshTarget)
				_skinnedMeshTarget = _skinnedMeshRenderers.FirstOrDefault(s => s.sharedMesh.blendShapeCount > 0);


			// ????
			/*foreach (KeyValuePair<SkinnedMeshRenderer, SkinnedMeshRendererData> kvp in _dirtySkinnedMeshRenderers)
			{
				var diff = kvp.Value.dirtyBlendShapes.Count - kvp.Key.sharedMesh.blendShapeCount;
				if (diff > 0)
				{
					UnityEngine.Debug.Log("diff: " + diff);
					for (int i = 0; i < diff; i++)
					{
						var key = kvp.Value.dirtyBlendShapes.Last().Key;
						kvp.Value.dirtyBlendShapes.Remove(key);
					}
				}
			}*/
		}

#endregion
	}
}
