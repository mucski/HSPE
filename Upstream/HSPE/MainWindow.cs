using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
#if AISHOUJO || HONEYSELECT2
using AIChara;
using Illusion.Extensions;
#endif
#if IPA
using Harmony;
#elif BEPINEX
using HarmonyLib;
#endif
using HSPE.AMModules;
using RootMotion.FinalIK;
using Studio;
using ToolBox;
using ToolBox.Extensions;
using UILib;
using UILib.EventHandlers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Vectrosity;
using Input = UnityEngine.Input;
using Path = System.IO.Path;
#if KOIKATSU || AISHOUJO || HONEYSELECT2
using ExtensibleSaveFormat;
using TMPro;
#endif

namespace HSPE
{
	public class MainWindow : MonoBehaviour
	{
		#region Public Static Variables
		internal static MainWindow _self;
		#endregion

		#region Private Types
		private class FKBoneEntry
		{
			public Toggle toggle;
			public Text text;
			public GameObject target;
		}
		#endregion

		#region Constants
#if HONEYSELECT
		private const string _config = "configNEO.xml";
		internal const string _pluginDir = "Plugins\\HSPE\\";
		private const string _studioSavesDir = "StudioNEOScenes\\";
#elif PLAYHOME
		private const string _config = "config.xml";
		internal const string _pluginDir = "Plugins\\PHPE\\";
#elif KOIKATSU
		private const string _config = "config.xml";
		private const string _extSaveKey = "kkpe";
#elif AISHOUJO
		private const string _config = "config.xml";
		private const string _extSaveKey = "aipe";
#elif HONEYSELECT2
		private const string _config = "config.xml";
		private const string _extSaveKey = "hs2pe";
#endif
		#endregion

		#region Private Variables
		internal PoseController _poseTarget;
		internal CameraEventsDispatcher _cameraEventsDispatcher;
		internal const int _uniqueId = ('H' << 24) | ('S' << 16) | ('P' << 8) | 'E';
		private string _assemblyLocation;
		private HashSet<TreeNodeObject> _selectedNodes;
		private TreeNodeObject _lastSelectedNode;
		// changed (HSPE fork): selection, drag flags, intensity, World/Local, IK/FK mode and the drag math moved
		// to Kit.Small.PoseInput, shared by this canvas and the kit window
		internal readonly global::HSPE.Kit.Small.PoseInput _input = new global::HSPE.Kit.Small.PoseInput();
		// addition (HSPE fork): the kit small window; null = Joan's canvas (UseKitUI off or kit failed to start)
		internal global::HSPE.Kit.Small.KitSmallWindow _kitSmall;
		// addition (HSPE fork): the kit Advanced window; null = Joan's IMGUI window only
		internal global::HSPE.Kit.Advanced.KitAdvancedWindow _kitAdvanced;
		internal KeyCode _mainWindowKeyCode = KeyCode.H; // changed (HSPE fork): private -> internal (kit Options)
		private int _lastObjectCount = 0;
		private int _lastIndex = 0;
		internal Rect _advancedModeRect = new Rect(Screen.width - 650, Screen.height - 370, 650, 370);
		private float _mainWindowSize = 1f;
		private Canvas _ui;
		private GameObject _nothingText;
		private Transform _controls;
		private Slider _movementIntensity;
		private Text _intensityValueText;
		private RectTransform _optionsWindow;
		private readonly Button[] _effectorsButtons = new Button[9];
		private readonly Button[] _bendGoalsButtons = new Button[4];
		private readonly Text[] _effectorsTexts = new Text[9];
		private readonly Text[] _bendGoalsTexts = new Text[4];
		private readonly Button[] _positionButtons = new Button[3];
		private readonly Button[] _rotationButtons = new Button[3];
		internal Button _shortcutKeyButton; // changed (HSPE fork): private -> internal (kit Options)
		internal bool _shortcutRegisterMode = false; // changed (HSPE fork): private -> internal (kit Options)
		private Toggle _optimizeIKToggle;
		private Image _hspeButtonImage;
		private Toggle _crotchCorrectionToggle;
		private Toggle _leftFootCorrectionToggle;
		private Toggle _rightFootCorrectionToggle;
		internal Toggle _crotchCorrectionByDefaultToggle; // changed (HSPE fork): private -> internal (kit Options)
		internal Toggle _anklesCorrectionByDefaultToggle; // changed (HSPE fork): private -> internal (kit Options)
		private bool _crotchCorrectionByDefault;
		private bool _anklesCorrectionByDefault;
		private int _lastScreenWidth = Screen.width;
		private int _lastScreenHeight = Screen.height;
		private Button _copyLeftArmButton;
		private Button _copyRightArmButton;
		private Button _copyLeftHandButton;
		private Button _copyRightHandButton;
		private Button _copyLeftLegButton;
		private Button _copyRightLegButton;
		private Button _swapPostButton;
		private Transform _ikBonesButtons;
		private Transform _fkBonesButtons;
		private ScrollRect _fkScrollRect;
		private GameObject _fkBoneTogglePrefab;
		private ToggleGroup _fkToggleGroup;
		private readonly List<FKBoneEntry> _fkBoneEntries = new List<FKBoneEntry>();
		private Dictionary<Transform, GuideObject> _dicGuideObject = new Dictionary<Transform, GuideObject>();
		private RectTransform _imguiBackground;
		#endregion

		#region Public Accessors
		public Texture2D vectorEndCap { get; private set; }
		public Texture2D vectorMiddle { get; private set; }
		public bool crotchCorrectionByDefault { get { return _crotchCorrectionByDefaultToggle.isOn; } }
		public bool anklesCorrectionByDefault { get { return _anklesCorrectionByDefaultToggle.isOn; } }
		#endregion

		#region Unity Methods
		protected virtual void Awake()
		{
			_self = this;

			if (Resources.FindObjectsOfTypeAll<IKExecutionOrder>().Length == 0)
				gameObject.AddComponent<IKExecutionOrder>().IKComponents = new IK[0];

			_assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

			string path = Path.Combine(Path.Combine(_assemblyLocation, HSPE._name), _config);
			if (File.Exists(path))
			{
				XmlDocument doc = new XmlDocument();
				doc.Load(path);

				foreach (XmlNode node in doc.FirstChild.ChildNodes)
				{
					switch (node.Name)
					{
						case "mainWindowSize":
							if (node.Attributes["value"] != null)
								_mainWindowSize = XmlConvert.ToSingle(node.Attributes["value"].Value);
							break;
						case "mainWindowShortcut":
							if (node.Attributes["value"] != null)
							{
								try
								{
									_mainWindowKeyCode = (KeyCode)Enum.Parse(typeof(KeyCode), node.Attributes["value"].Value);
								}
								catch { }
							}
							break;
						case "advancedModeWindowSize":
							if (node.Attributes["x"] != null && node.Attributes["y"] != null)
							{
								_advancedModeRect.xMin = _advancedModeRect.xMax - XmlConvert.ToInt32(node.Attributes["x"].Value);
								_advancedModeRect.yMin = _advancedModeRect.yMax - XmlConvert.ToInt32(node.Attributes["y"].Value);
							}
							break;
						case "femaleShortcuts":
							foreach (XmlNode shortcut in node.ChildNodes)
								if (shortcut.Attributes["path"] != null)
									BonesEditor._femaleShortcuts.Add(shortcut.Attributes["path"].Value, shortcut.Attributes["path"].Value.Split('/').Last());
							break;
						case "maleShortcuts":
							foreach (XmlNode shortcut in node.ChildNodes)
								if (shortcut.Attributes["path"] != null)
									BonesEditor._maleShortcuts.Add(shortcut.Attributes["path"].Value, shortcut.Attributes["path"].Value.Split('/').Last());
							break;
						case "itemShortcuts":
							foreach (XmlNode shortcut in node.ChildNodes)
								if (shortcut.Attributes["path"] != null)
									BonesEditor._itemShortcuts.Add(shortcut.Attributes["path"].Value, shortcut.Attributes["path"].Value.Split('/').Last());
							break;
						case "boneAliases":
							foreach (XmlNode alias in node.ChildNodes)
								if (alias.Attributes["key"] != null && alias.Attributes["value"] != null)
									BonesEditor._boneAliases.Add(alias.Attributes["key"].Value, alias.Attributes["value"].Value);
							break;
						case "blendShapeAliases":
							foreach (XmlNode alias in node.ChildNodes)
								if (alias.Attributes["key"] != null && alias.Attributes["value"] != null)
									BlendShapesEditor._blendShapeAliases.Add(alias.Attributes["key"].Value, alias.Attributes["value"].Value);
							break;
						case "crotchCorrectionByDefault":
							if (node.Attributes["value"] != null)
								_crotchCorrectionByDefault = XmlConvert.ToBoolean(node.Attributes["value"].Value);
							break;
						case "anklesCorrectionByDefault":
							if (node.Attributes["value"] != null)
								_anklesCorrectionByDefault = XmlConvert.ToBoolean(node.Attributes["value"].Value);
							break;
					}
				}
			}
			PoseController.InstallOnParentageEvent();
			OCIChar_ChangeChara_Patches.onChangeChara += OnCharacterReplaced;
			OCIChar_LoadClothesFile_Patches.onLoadClothesFile += OnLoadClothesFile;
#if HONEYSELECT
			OCIChar_SetCoordinateInfo_Patches.onSetCoordinateInfo += OnCoordinateReplaced;
#endif
			_dicGuideObject = (Dictionary<Transform, GuideObject>)GuideObjectManager.Instance.GetPrivate("dicGuideObject");
#if IPA
			HSExtSave.HSExtSave.RegisterHandler("hspe", null, null, OnSceneLoad, OnSceneImport, OnSceneSave, null, null);
#elif BEPINEX
			ExtendedSave.CardBeingLoaded += this.OnCharaLoad;
			ExtendedSave.CardBeingSaved += this.OnCharaSave;
			ExtendedSave.SceneBeingLoaded += this.OnSceneLoad;
			ExtendedSave.SceneBeingImported += this.OnSceneImport;
			ExtendedSave.SceneBeingSaved += this.OnSceneSave;
#endif
			this.ExecuteDelayed(() =>
			{
				TimelineCompatibility.Init(() =>
				{
					BonesEditor.TimelineCompatibility.Populate();
					CollidersEditor.TimelineCompatibility.Populate();
					DynamicBonesEditor.TimelineCompatibility.Populate();
					BlendShapesEditor.TimelineCompatibility.Populate();
					BoobsEditor.TimelineCompatibility.Populate();
					IKEditor.TimelineCompatibility.Populate();
					Kit.Face.FaceTimeline.Populate(); // addition (HSPE fork, face window): the "Face" track
				});
			}, 3);
		}

		private void Start()
		{
			_cameraEventsDispatcher = Camera.main.gameObject.AddComponent<CameraEventsDispatcher>();
			SpawnGUI();
			_crotchCorrectionByDefaultToggle.isOn = _crotchCorrectionByDefault;
			_anklesCorrectionByDefaultToggle.isOn = _anklesCorrectionByDefault;
			_selectedNodes = (HashSet<TreeNodeObject>)Studio.Studio.Instance.treeNodeCtrl.GetPrivate("hashSelectNode");
			// addition (HSPE fork): kit small window replaces the canvas unless UseKitUI is off; any failure falls
			// back to Joan's canvas
			if (HSPE._kitAvailable) // addition (KitGuard): no HSUIKit.dll / too old = Joan's canvas and IMGUI Advanced window
			{
				_kitSmall = CreateKitSmall();
				if (_kitSmall != null) // addition (HSPE fork)
					_kitAdvanced = CreateKitAdvanced();
			}
		}

		// addition (KitGuard): own methods so HSUIKit-dependent code is only JIT-compiled once the version check passed
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private global::HSPE.Kit.Small.KitSmallWindow CreateKitSmall()
		{
			return global::HSPE.Kit.Small.KitSmallWindow.TryCreate(this);
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private global::HSPE.Kit.Advanced.KitAdvancedWindow CreateKitAdvanced()
		{
			return global::HSPE.Kit.Advanced.KitAdvancedWindow.TryCreate(this);
		}

		// addition (HSPE fork): toolbar button and hotkey route to whichever small window is in use
		internal bool mainWindowVisible { get { return _kitSmall != null ? _kitSmall.visible : _ui.gameObject.activeSelf; } }

		internal void ToggleMainWindow()
		{
			if (_kitSmall != null)
				_kitSmall.Toggle();
			else
			{
				_ui.gameObject.SetActive(!_ui.gameObject.activeSelf);
				SetToolbarButtonOn(_ui.gameObject.activeSelf); // changed (HSPE fork): the button may not exist
			}
		}

		internal void SetToolbarButtonOn(bool on)
		{
			if (_hspeButtonImage != null)
				_hspeButtonImage.color = on ? Color.green : Color.white;
		}

		internal PoseController poseTarget { get { return _poseTarget; } }

		internal void ToggleAdvancedModeForTarget()
		{
			if (_poseTarget != null)
				_poseTarget.ToggleAdvancedMode();
		}

		protected virtual void Update()
		{
			if (_lastScreenWidth != Screen.width || _lastScreenHeight != Screen.height)
				OnWindowResize();
			_lastScreenWidth = Screen.width;
			_lastScreenHeight = Screen.height;

			int objectCount = Studio.Studio.Instance.dicObjectCtrl.Count;
			if (objectCount != _lastObjectCount)
			{
				if (objectCount > _lastObjectCount)
					OnObjectAdded();
				_lastIndex = Studio.Studio.Instance.sceneInfo.CheckNewIndex();
			}
			_lastObjectCount = Studio.Studio.Instance.dicObjectCtrl.Count;


			PoseController last = _poseTarget;
			// note: unclear why the selectNode wasn't used, instead.
			//TreeNodeObject treeNodeObject = this._selectedNodes.FirstOrDefault();
			TreeNodeObject treeNodeObject = Studio.Studio.Instance.treeNodeCtrl.selectNode;
			if (_lastSelectedNode != treeNodeObject)
			{
				if (treeNodeObject != null)
				{
					if (Studio.Studio.Instance.dicInfo.TryGetValue(treeNodeObject, out ObjectCtrlInfo info))
						_poseTarget = info.guideObject.transformTarget.GetComponent<PoseController>();
				}
				else
					_poseTarget = null;
			}
			_lastSelectedNode = treeNodeObject;

			if (last != _poseTarget)
				OnTargetChange(last);
			GUILogic();
			if (_kitAdvanced != null) // addition (HSPE fork): shows / hides the kit Advanced window with Joan's flag
				_kitAdvanced.Sync();

			StaticUpdate();
		}

		// "Static" Update stuff for the other classes
		private void StaticUpdate()
		{
			if (AdvancedModeModule._repeatCalled)
				AdvancedModeModule._repeatTimer += Time.unscaledDeltaTime;
			else
				AdvancedModeModule._repeatTimer = 0f;
			AdvancedModeModule._repeatCalled = false;
		}

		protected virtual void OnGUI()
		{
			// changed (HSPE fork): Joan's IMGUI window only draws when the kit Advanced window isn't in use
			// (UseKitUI off, or the kit failed to start)
			if (_poseTarget != null && PoseController._drawAdvancedMode && _kitAdvanced == null)
			{
				_imguiBackground.gameObject.SetActive(true);
				IMGUIExtensions.DrawBackground(_advancedModeRect);
				_advancedModeRect = GUILayout.Window(_uniqueId, _advancedModeRect, _poseTarget.AdvancedModeWindow, "Advanced mode");
				IMGUIExtensions.FitRectTransformToRect(_imguiBackground, _advancedModeRect);
			}
			else if (_imguiBackground)
				_imguiBackground.gameObject.SetActive(false);
		}

		protected virtual void OnDestroy()
		{
			string folder = Path.Combine(_assemblyLocation, HSPE._name);
			if (Directory.Exists(folder) == false)
				Directory.CreateDirectory(folder);
			string path = Path.Combine(folder, _config);
			using (FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write))
			{
				using (XmlTextWriter xmlWriter = new XmlTextWriter(fileStream, Encoding.UTF8))
				{
					xmlWriter.Formatting = Formatting.Indented;
					xmlWriter.WriteStartElement("root");
					xmlWriter.WriteAttributeString("version", HSPE._versionNum.ToString());
					xmlWriter.WriteStartElement("mainWindowSize");
					xmlWriter.WriteAttributeString("value", XmlConvert.ToString(_mainWindowSize));
					xmlWriter.WriteEndElement();

					xmlWriter.WriteStartElement("mainWindowShortcut");
					xmlWriter.WriteAttributeString("value", _mainWindowKeyCode.ToString());
					xmlWriter.WriteEndElement();

					xmlWriter.WriteStartElement("advancedModeWindowSize");
					xmlWriter.WriteAttributeString("x", XmlConvert.ToString((int)_advancedModeRect.width));
					xmlWriter.WriteAttributeString("y", XmlConvert.ToString((int)_advancedModeRect.height));
					xmlWriter.WriteEndElement();

					xmlWriter.WriteStartElement("femaleShortcuts");
					foreach (KeyValuePair<string, string> kvp in BonesEditor._femaleShortcuts)
					{
						xmlWriter.WriteStartElement("shortcut");
						xmlWriter.WriteAttributeString("path", kvp.Key);
						xmlWriter.WriteEndElement();
					}
					xmlWriter.WriteEndElement();

					xmlWriter.WriteStartElement("maleShortcuts");
					foreach (KeyValuePair<string, string> kvp in BonesEditor._maleShortcuts)
					{
						xmlWriter.WriteStartElement("shortcut");
						xmlWriter.WriteAttributeString("path", kvp.Key);
						xmlWriter.WriteEndElement();
					}
					xmlWriter.WriteEndElement();

					xmlWriter.WriteStartElement("itemShortcuts");
					foreach (KeyValuePair<string, string> kvp in BonesEditor._itemShortcuts)
					{
						xmlWriter.WriteStartElement("shortcut");
						xmlWriter.WriteAttributeString("path", kvp.Key);
						xmlWriter.WriteEndElement();
					}
					xmlWriter.WriteEndElement();

					xmlWriter.WriteStartElement("boneAliases");
					foreach (KeyValuePair<string, string> kvp in BonesEditor._boneAliases)
					{
						xmlWriter.WriteStartElement("alias");
						xmlWriter.WriteAttributeString("key", kvp.Key);
						xmlWriter.WriteAttributeString("value", kvp.Value);
						xmlWriter.WriteEndElement();
					}
					xmlWriter.WriteEndElement();

					xmlWriter.WriteStartElement("blendShapeAliases");
					foreach (KeyValuePair<string, string> kvp in BlendShapesEditor._blendShapeAliases)
					{
						xmlWriter.WriteStartElement("alias");
						xmlWriter.WriteAttributeString("key", kvp.Key);
						xmlWriter.WriteAttributeString("value", kvp.Value);
						xmlWriter.WriteEndElement();
					}
					xmlWriter.WriteEndElement();

					xmlWriter.WriteStartElement("crotchCorrectionByDefault");
					xmlWriter.WriteAttributeString("value", XmlConvert.ToString(_crotchCorrectionByDefaultToggle.isOn));
					xmlWriter.WriteEndElement();

					xmlWriter.WriteStartElement("anklesCorrectionByDefault");
					xmlWriter.WriteAttributeString("value", XmlConvert.ToString(_anklesCorrectionByDefaultToggle.isOn));
					xmlWriter.WriteEndElement();

					xmlWriter.WriteEndElement();
				}
			}
		}
		#endregion

		#region GUI
		private void SpawnGUI()
		{
			//UIUtility.Init("UILib.Resources.HSPEResources.unity3d");
			//UIUtility.Init("HSPE.Resources.DefaultResources.unity3d");
			UIUtility.Init();

#if HONEYSELECT || PLAYHOME
			AssetBundle bundle = AssetBundle.LoadFromMemory(Assembly.GetExecutingAssembly().GetResource("HSPE.Resources.HSPEResources.unity3d"));
#elif KOIKATSU
			AssetBundle bundle = AssetBundle.LoadFromMemory(Assembly.GetExecutingAssembly().GetResource("HSPE.Resources.KKPEResources.unity3d"));
#elif AISHOUJO
			AssetBundle bundle = AssetBundle.LoadFromMemory(Assembly.GetExecutingAssembly().GetResource("HSPE.Resources.AIPEResources.unity3d"));
#elif HONEYSELECT2
			AssetBundle bundle = AssetBundle.LoadFromMemory(Assembly.GetExecutingAssembly().GetResource("HSPE.Resources.HS2PEResources.unity3d"));
#endif
			Texture2D texture = bundle.LoadAsset<Texture2D>("Icon");
			vectorEndCap = bundle.LoadAsset<Texture2D>("VectorEndCap");
			vectorMiddle = bundle.LoadAsset<Texture2D>("VectorMiddle");
			VectorLine.SetEndCap("vector", EndCap.Back, 0f, -1f, 1f, 4f, vectorMiddle, vectorEndCap);
			VectorLine.canvas.sortingOrder -= 40;

			// changed (HSPE fork): the toolbar button can be turned off (modprefs.ini [HSPE] ToolbarButton=0); ON by
			// default, as in the original HSPE
			if (IllusionPlugin.ModPrefs.GetBool("HSPE", "ToolbarButton", true, true))
			{
				IEnumerable<Transform> children = GameObject.Find("StudioScene/Canvas System Menu/01_Button").transform.Children();
				Vector2 anchoredPosition = new Vector2(0f, float.MaxValue);
				foreach (Transform child in children)
				{
					RectTransform rChild = (RectTransform)child;
					if (rChild.anchoredPosition.x >= anchoredPosition.x && rChild.anchoredPosition.y <= anchoredPosition.y)
						anchoredPosition = rChild.anchoredPosition;
				}

				while (children.FirstOrDefault(c => (((RectTransform)c).anchoredPosition - anchoredPosition).sqrMagnitude < 4f) != null)
					anchoredPosition.y += 40f;

				RectTransform original = (RectTransform)children.First();
				Button hspeButton = Instantiate(original.gameObject).GetComponent<Button>();
				hspeButton.name = "Button HSPE";
				hspeButton.interactable = true;
				hspeButton.transform.SetParent(original.parent, true);
				hspeButton.transform.localScale = original.localScale;
				((RectTransform)hspeButton.transform).anchoredPosition = anchoredPosition;
				hspeButton.onClick = new Button.ButtonClickedEvent();
				hspeButton.onClick.AddListener(ToggleMainWindow); // changed (HSPE fork): routes to the kit window when used
				hspeButton.gameObject.AddComponent<PointerDownHandler>().onPointerDown += eventData =>
				{
					if (eventData.button == PointerEventData.InputButton.Right && _poseTarget != null)
						_poseTarget.ToggleAdvancedMode();
				};
				_hspeButtonImage = hspeButton.targetGraphic as Image;
				_hspeButtonImage.sprite = Sprite.Create(texture, new Rect(0f, 0f, 32, 32), new Vector2(16, 16));
				_hspeButtonImage.color = Color.white;
			}

			GameObject uiPrefab;
#if HONEYSELECT || PLAYHOME
			uiPrefab = bundle.LoadAsset<GameObject>("HSPECanvas");
#elif KOIKATSU
			uiPrefab = bundle.LoadAsset<GameObject>("KKPECanvas");
#elif AISHOUJO
			uiPrefab = bundle.LoadAsset<GameObject>("AIPECanvas");
#elif HONEYSELECT2
			uiPrefab = bundle.LoadAsset<GameObject>("HS2PECanvas");
#endif
			_ui = Instantiate(uiPrefab).GetComponent<Canvas>();
			uiPrefab.hideFlags |= HideFlags.HideInHierarchy;
			_fkBoneTogglePrefab = bundle.LoadAsset<GameObject>("FKBoneTogglePrefab");
			_fkBoneTogglePrefab.hideFlags = HideFlags.HideInHierarchy;
			bundle.Unload(false);

			RectTransform bg = (RectTransform)_ui.transform.Find("BG");
			Transform topContainer = bg.Find("Top Container");
			UIUtility.MakeObjectDraggable((RectTransform)topContainer, bg);

			Toggle ikToggle = _ui.transform.Find("BG/Top Container/Buttons/IK").GetComponent<Toggle>();
			ikToggle.onValueChanged.AddListener((b) =>
			{
				_ikBonesButtons.gameObject.SetActive(ikToggle.isOn);
				_fkBonesButtons.gameObject.SetActive(!ikToggle.isOn);
				_input.modeIK = ikToggle.isOn; // changed (HSPE fork)
			});
			Toggle fkToggle = _ui.transform.Find("BG/Top Container/Buttons/FK").GetComponent<Toggle>();
			fkToggle.onValueChanged.AddListener((b) =>
			{
				_fkBonesButtons.gameObject.SetActive(fkToggle.isOn);
				_ikBonesButtons.gameObject.SetActive(!fkToggle.isOn);
				_input.modeIK = !fkToggle.isOn; // changed (HSPE fork)
			});

			_nothingText = _ui.transform.Find("BG/Nothing Text").gameObject;
			_controls = _ui.transform.Find("BG/Controls");
			_ikBonesButtons = _ui.transform.Find("BG/Controls/IK Bones Buttons");
			_fkBonesButtons = _ui.transform.Find("BG/Controls/FK Bones Buttons");

			Button rightShoulder = _ui.transform.Find("BG/Controls/IK Bones Buttons/Right Shoulder Button").GetComponent<Button>();
			rightShoulder.onClick.AddListener(() => SetBoneTarget(FullBodyBipedEffector.RightShoulder));
			Text t = rightShoulder.GetComponentInChildren<Text>();
			_effectorsButtons[(int)FullBodyBipedEffector.RightShoulder] = rightShoulder;
			_effectorsTexts[(int)FullBodyBipedEffector.RightShoulder] = t;

			Button leftShoulder = _ui.transform.Find("BG/Controls/IK Bones Buttons/Left Shoulder Button").GetComponent<Button>();
			leftShoulder.onClick.AddListener(() => SetBoneTarget(FullBodyBipedEffector.LeftShoulder));
			t = leftShoulder.GetComponentInChildren<Text>();
			_effectorsButtons[(int)FullBodyBipedEffector.LeftShoulder] = leftShoulder;
			_effectorsTexts[(int)FullBodyBipedEffector.LeftShoulder] = t;

			Button rightArmBendGoal = _ui.transform.Find("BG/Controls/IK Bones Buttons/Right Arm Bend Goal Button").GetComponent<Button>();
			rightArmBendGoal.onClick.AddListener(() => SetBendGoalTarget(FullBodyBipedChain.RightArm));
			t = rightArmBendGoal.GetComponentInChildren<Text>();
			_bendGoalsButtons[(int)FullBodyBipedChain.RightArm] = rightArmBendGoal;
			_bendGoalsTexts[(int)FullBodyBipedChain.RightArm] = t;

			Button leftArmBendGoal = _ui.transform.Find("BG/Controls/IK Bones Buttons/Left Arm Bend Goal Button").GetComponent<Button>();
			leftArmBendGoal.onClick.AddListener(() => SetBendGoalTarget(FullBodyBipedChain.LeftArm));
			t = leftArmBendGoal.GetComponentInChildren<Text>();
			_bendGoalsButtons[(int)FullBodyBipedChain.LeftArm] = leftArmBendGoal;
			_bendGoalsTexts[(int)FullBodyBipedChain.LeftArm] = t;

			Button rightHand = _ui.transform.Find("BG/Controls/IK Bones Buttons/Right Hand Button").GetComponent<Button>();
			rightHand.onClick.AddListener(() => SetBoneTarget(FullBodyBipedEffector.RightHand));
			t = rightHand.GetComponentInChildren<Text>();
			_effectorsButtons[(int)FullBodyBipedEffector.RightHand] = rightHand;
			_effectorsTexts[(int)FullBodyBipedEffector.RightHand] = t;

			Button leftHand = _ui.transform.Find("BG/Controls/IK Bones Buttons/Left Hand Button").GetComponent<Button>();
			leftHand.onClick.AddListener(() => SetBoneTarget(FullBodyBipedEffector.LeftHand));
			t = leftHand.GetComponentInChildren<Text>();
			_effectorsButtons[(int)FullBodyBipedEffector.LeftHand] = leftHand;
			_effectorsTexts[(int)FullBodyBipedEffector.LeftHand] = t;

			Button body = _ui.transform.Find("BG/Controls/IK Bones Buttons/Body Button").GetComponent<Button>();
			body.onClick.AddListener(() => SetBoneTarget(FullBodyBipedEffector.Body));
			t = body.GetComponentInChildren<Text>();
			_effectorsButtons[(int)FullBodyBipedEffector.Body] = body;
			_effectorsTexts[(int)FullBodyBipedEffector.Body] = t;

			Button rightThigh = _ui.transform.Find("BG/Controls/IK Bones Buttons/Right Thigh Button").GetComponent<Button>();
			rightThigh.onClick.AddListener(() => SetBoneTarget(FullBodyBipedEffector.RightThigh));
			t = rightThigh.GetComponentInChildren<Text>();
			_effectorsButtons[(int)FullBodyBipedEffector.RightThigh] = rightThigh;
			_effectorsTexts[(int)FullBodyBipedEffector.RightThigh] = t;

			Button leftThigh = _ui.transform.Find("BG/Controls/IK Bones Buttons/Left Thigh Button").GetComponent<Button>();
			leftThigh.onClick.AddListener(() => SetBoneTarget(FullBodyBipedEffector.LeftThigh));
			t = leftThigh.GetComponentInChildren<Text>();
			_effectorsButtons[(int)FullBodyBipedEffector.LeftThigh] = leftThigh;
			_effectorsTexts[(int)FullBodyBipedEffector.LeftThigh] = t;

			Button rightLegBendGoal = _ui.transform.Find("BG/Controls/IK Bones Buttons/Right Leg Bend Goal Button").GetComponent<Button>();
			rightLegBendGoal.onClick.AddListener(() => SetBendGoalTarget(FullBodyBipedChain.RightLeg));
			t = rightLegBendGoal.GetComponentInChildren<Text>();
			_bendGoalsButtons[(int)FullBodyBipedChain.RightLeg] = rightLegBendGoal;
			_bendGoalsTexts[(int)FullBodyBipedChain.RightLeg] = t;

			Button leftLegBendGoal = _ui.transform.Find("BG/Controls/IK Bones Buttons/Left Leg Bend Goal Button").GetComponent<Button>();
			leftLegBendGoal.onClick.AddListener(() => SetBendGoalTarget(FullBodyBipedChain.LeftLeg));
			t = leftLegBendGoal.GetComponentInChildren<Text>();
			_bendGoalsButtons[(int)FullBodyBipedChain.LeftLeg] = leftLegBendGoal;
			_bendGoalsTexts[(int)FullBodyBipedChain.LeftLeg] = t;

			Button rightFoot = _ui.transform.Find("BG/Controls/IK Bones Buttons/Right Foot Button").GetComponent<Button>();
			rightFoot.onClick.AddListener(() => SetBoneTarget(FullBodyBipedEffector.RightFoot));
			t = rightFoot.GetComponentInChildren<Text>();
			_effectorsButtons[(int)FullBodyBipedEffector.RightFoot] = rightFoot;
			_effectorsTexts[(int)FullBodyBipedEffector.RightFoot] = t;

			Button leftFoot = _ui.transform.Find("BG/Controls/IK Bones Buttons/Left Foot Button").GetComponent<Button>();
			leftFoot.onClick.AddListener(() => SetBoneTarget(FullBodyBipedEffector.LeftFoot));
			t = leftFoot.GetComponentInChildren<Text>();
			_effectorsButtons[(int)FullBodyBipedEffector.LeftFoot] = leftFoot;
			_effectorsTexts[(int)FullBodyBipedEffector.LeftFoot] = t;

			_fkScrollRect = _fkBonesButtons.GetComponentInChildren<ScrollRect>();
			_fkScrollRect.movementType = ScrollRect.MovementType.Clamped;
			_fkToggleGroup = _fkBonesButtons.GetComponentInChildren<ToggleGroup>();

			Button xMoveButton = _ui.transform.Find("BG/Controls/Buttons/MoveRotateButtons/X Move Button").GetComponent<Button>();
			xMoveButton.onClick.AddListener(() => EventSystem.current.SetSelectedGameObject(null));
			xMoveButton.gameObject.AddComponent<PointerDownHandler>().onPointerDown += (eventData) =>
			{
				if (eventData.button == PointerEventData.InputButton.Left)
					_input.xMove = true; // changed (HSPE fork)
			};
			_positionButtons[0] = xMoveButton;

			Button yMoveButton = _ui.transform.Find("BG/Controls/Buttons/MoveRotateButtons/Y Move Button").GetComponent<Button>();
			yMoveButton.onClick.AddListener(() => EventSystem.current.SetSelectedGameObject(null));
			yMoveButton.gameObject.AddComponent<PointerDownHandler>().onPointerDown += (eventData) =>
			{
				if (eventData.button == PointerEventData.InputButton.Left)
					_input.yMove = true; // changed (HSPE fork)
			};
			_positionButtons[1] = yMoveButton;

			Button zMoveButton = _ui.transform.Find("BG/Controls/Buttons/MoveRotateButtons/Z Move Button").GetComponent<Button>();
			zMoveButton.onClick.AddListener(() => EventSystem.current.SetSelectedGameObject(null));
			zMoveButton.gameObject.AddComponent<PointerDownHandler>().onPointerDown += (eventData) =>
			{
				if (eventData.button == PointerEventData.InputButton.Left)
					_input.zMove = true; // changed (HSPE fork)
			};
			_positionButtons[2] = zMoveButton;

			Button rotXButton = _ui.transform.Find("BG/Controls/Buttons/MoveRotateButtons/Rot X Button").GetComponent<Button>();
			rotXButton.onClick.AddListener(() => EventSystem.current.SetSelectedGameObject(null));
			rotXButton.gameObject.AddComponent<PointerDownHandler>().onPointerDown += (eventData) =>
			{
				if (eventData.button == PointerEventData.InputButton.Left)
					_input.xRot = true; // changed (HSPE fork)
			};
			_rotationButtons[0] = rotXButton;

			Button rotYButton = _ui.transform.Find("BG/Controls/Buttons/MoveRotateButtons/Rot Y Button").GetComponent<Button>();
			rotYButton.onClick.AddListener(() => EventSystem.current.SetSelectedGameObject(null));
			rotYButton.gameObject.AddComponent<PointerDownHandler>().onPointerDown += (eventData) =>
			{
				if (eventData.button == PointerEventData.InputButton.Left)
					_input.yRot = true; // changed (HSPE fork)
			};
			_rotationButtons[1] = rotYButton;

			Button rotZButton = _ui.transform.Find("BG/Controls/Buttons/MoveRotateButtons/Rot Z Button").GetComponent<Button>();
			rotZButton.onClick.AddListener(() => EventSystem.current.SetSelectedGameObject(null));
			rotZButton.gameObject.AddComponent<PointerDownHandler>().onPointerDown += (eventData) =>
			{
				if (eventData.button == PointerEventData.InputButton.Left)
					_input.zRot = true; // changed (HSPE fork)
			};
			_rotationButtons[2] = rotZButton;

			_copyLeftArmButton = _ui.transform.Find("BG/Controls/Other buttons/Copy Limbs/Copy Right Arm Button").GetComponent<Button>();
			_copyLeftArmButton.onClick.AddListener(() =>
			{
				if (_poseTarget)
					((CharaPoseController)_poseTarget).CopyLimbToTwin(FullBodyBipedChain.RightArm, OIBoneInfo.BoneGroup.RightArm);
			});

			_copyRightArmButton = _ui.transform.Find("BG/Controls/Other buttons/Copy Limbs/Copy Left Arm Button").GetComponent<Button>();
			_copyRightArmButton.onClick.AddListener(() =>
			{
				if (_poseTarget)
					((CharaPoseController)_poseTarget).CopyLimbToTwin(FullBodyBipedChain.LeftArm, OIBoneInfo.BoneGroup.LeftArm);
			});

			_copyLeftHandButton = _ui.transform.Find("BG/Controls/Other buttons/Copy Limbs/Copy Right Hand Button").GetComponent<Button>();
			_copyLeftHandButton.onClick.AddListener(() =>
			{
				if (_poseTarget)
					((CharaPoseController)_poseTarget).CopyHandToTwin(OIBoneInfo.BoneGroup.RightHand);
			});

			_copyRightHandButton = _ui.transform.Find("BG/Controls/Other buttons/Copy Limbs/Copy Left Hand Button").GetComponent<Button>();
			_copyRightHandButton.onClick.AddListener(() =>
			{
				if (_poseTarget)
					((CharaPoseController)_poseTarget).CopyHandToTwin(OIBoneInfo.BoneGroup.LeftHand);
			});

			_copyLeftLegButton = _ui.transform.Find("BG/Controls/Other buttons/Copy Limbs/Copy Right Leg Button").GetComponent<Button>();
			_copyLeftLegButton.onClick.AddListener(() =>
			{
				if (_poseTarget)
					((CharaPoseController)_poseTarget).CopyLimbToTwin(FullBodyBipedChain.RightLeg, OIBoneInfo.BoneGroup.RightLeg);
			});

			_copyRightLegButton = _ui.transform.Find("BG/Controls/Other buttons/Copy Limbs/Copy Left LegButton").GetComponent<Button>();
			_copyRightLegButton.onClick.AddListener(() =>
			{
				if (_poseTarget)
					((CharaPoseController)_poseTarget).CopyLimbToTwin(FullBodyBipedChain.LeftLeg, OIBoneInfo.BoneGroup.LeftLeg);
			});

			_swapPostButton = _ui.transform.Find("BG/Controls/Other buttons/Other/Swap Pose Button").GetComponent<Button>();
			_swapPostButton.onClick.AddListener(() =>
			{
				if (_poseTarget)
					((CharaPoseController)_poseTarget).SwapPose();
			});

			Button advancedModeButton = _ui.transform.Find("BG/Controls/Buttons/Simple Options/Advanced Mode Button").GetComponent<Button>();
			advancedModeButton.onClick.AddListener(() =>
			{
				if (_poseTarget)
					_poseTarget.ToggleAdvancedMode();
			});

			_movementIntensity = _ui.transform.Find("BG/Controls/Buttons/Simple Options/Intensity Container/Movement Intensity Slider").GetComponent<Slider>();
			_movementIntensity.onValueChanged.AddListener(value =>
			{
				// changed (HSPE fork): same math, now in PoseInput
				_input.intensity = global::HSPE.Kit.Small.PoseInput.IntensityFromSlider(_movementIntensity.value);
				_intensityValueText.text = global::HSPE.Kit.Small.PoseInput.IntensityText(_input.intensity);
			});

			_intensityValueText = _ui.transform.Find("BG/Controls/Buttons/Simple Options/Intensity Container/Movement Intensity Value").GetComponent<Text>();

			Button positionOp = _ui.transform.Find("BG/Controls/Buttons/Simple Options/Pos Operation Container/Position Operation").GetComponent<Button>();
			Text buttonText = positionOp.GetComponentInChildren<Text>();
			positionOp.onClick.AddListener(() =>
			{
				_input.positionWorld = !_input.positionWorld; // changed (HSPE fork)
				buttonText.text = _input.positionWorld ? "World" : "Local";
			});

			_optimizeIKToggle = _ui.transform.Find("BG/Controls/Buttons/Simple Options/Optimize IK Container/Optimize IK").GetComponent<Toggle>();
			_optimizeIKToggle.onValueChanged.AddListener((b) =>
			{
				if (_poseTarget != null)
					((CharaPoseController)_poseTarget).optimizeIK = _optimizeIKToggle.isOn;
			});

			Button optionsButton = _ui.transform.Find("BG/Controls/Buttons/Simple Options/Options Button").GetComponent<Button>();
			optionsButton.onClick.AddListener(() =>
			{
				if (_shortcutRegisterMode)
					_shortcutKeyButton.onClick.Invoke();
				_optionsWindow.gameObject.SetActive(!_optionsWindow.gameObject.activeSelf);
			});
			_ui.gameObject.SetActive(false);

			SetBoneTarget(FullBodyBipedEffector.Body);
			//this.OnTargetChange(null);

			_optionsWindow = (RectTransform)_ui.transform.Find("Options Window");

			topContainer = _optionsWindow.Find("Top Container");
			UIUtility.MakeObjectDraggable(topContainer as RectTransform, _optionsWindow);

			Vector2 sizeDelta = bg.sizeDelta;
#if HONEYSELECT
			Text xMoveText = xMoveButton.GetComponentInChildren<Text>();
			Text yMoveText = yMoveButton.GetComponentInChildren<Text>();
			Text zMoveText = zMoveButton.GetComponentInChildren<Text>();
			Text xRotText = rotXButton.GetComponentInChildren<Text>();
			Text yRotText = rotYButton.GetComponentInChildren<Text>();
			Text zRotText = rotZButton.GetComponentInChildren<Text>();
			int moveFontSize = xMoveText.fontSize;
			int rotFontSize = xRotText.fontSize;
#endif

			Button normalButton = _ui.transform.Find("Options Window/Options/Main Window Size Container/Normal Button").GetComponent<Button>();
			normalButton.onClick.AddListener(() =>
			{
				_mainWindowSize = 1f;
				bg.sizeDelta = sizeDelta * _mainWindowSize;
#if HONEYSELECT
				xMoveText.fontSize = (int)(moveFontSize * _mainWindowSize);
				yMoveText.fontSize = (int)(moveFontSize * _mainWindowSize);
				zMoveText.fontSize = (int)(moveFontSize * _mainWindowSize);
				xRotText.fontSize = (int)(rotFontSize * _mainWindowSize);
				yRotText.fontSize = (int)(rotFontSize * _mainWindowSize);
				zRotText.fontSize = (int)(rotFontSize * _mainWindowSize);
#endif
			});

			Button largeButton = _ui.transform.Find("Options Window/Options/Main Window Size Container/Large Button").GetComponent<Button>();
			largeButton.onClick.AddListener(() =>
			{
				_mainWindowSize = 1.25f;
				bg.sizeDelta = sizeDelta * _mainWindowSize;
#if HONEYSELECT
				xMoveText.fontSize = (int)(moveFontSize * _mainWindowSize);
				yMoveText.fontSize = (int)(moveFontSize * _mainWindowSize);
				zMoveText.fontSize = (int)(moveFontSize * _mainWindowSize);
				xRotText.fontSize = (int)(rotFontSize * _mainWindowSize);
				yRotText.fontSize = (int)(rotFontSize * _mainWindowSize);
				zRotText.fontSize = (int)(rotFontSize * _mainWindowSize);
#endif
			});

			Button veryLargeButton = _ui.transform.Find("Options Window/Options/Main Window Size Container/Very Large Button").GetComponent<Button>();
			veryLargeButton.onClick.AddListener(() =>
			{
				_mainWindowSize = 1.5f;
				bg.sizeDelta = sizeDelta * _mainWindowSize;
#if HONEYSELECT
				xMoveText.fontSize = (int)(moveFontSize * _mainWindowSize);
				yMoveText.fontSize = (int)(moveFontSize * _mainWindowSize);
				zMoveText.fontSize = (int)(moveFontSize * _mainWindowSize);
				xRotText.fontSize = (int)(rotFontSize * _mainWindowSize);
				yRotText.fontSize = (int)(rotFontSize * _mainWindowSize);
				zRotText.fontSize = (int)(rotFontSize * _mainWindowSize);
#endif
			});
			bg.sizeDelta = sizeDelta * _mainWindowSize;
#if HONEYSELECT
			xMoveText.fontSize = (int)(moveFontSize * _mainWindowSize);
			yMoveText.fontSize = (int)(moveFontSize * _mainWindowSize);
			zMoveText.fontSize = (int)(moveFontSize * _mainWindowSize);
			xRotText.fontSize = (int)(rotFontSize * _mainWindowSize);
			yRotText.fontSize = (int)(rotFontSize * _mainWindowSize);
			zRotText.fontSize = (int)(rotFontSize * _mainWindowSize);
#endif
			_optionsWindow.anchoredPosition += new Vector2((sizeDelta.x * _mainWindowSize) - sizeDelta.x, 0f);

			Slider xSlider = _ui.transform.Find("Options Window/Options/Advanced Mode Window Size Container/Width Slider").GetComponent<Slider>();
			xSlider.onValueChanged.AddListener((f) =>
			{
				_advancedModeRect.xMin = _advancedModeRect.xMax - xSlider.value;
			});
			xSlider.maxValue = Screen.width * 0.8f;
			xSlider.value = _advancedModeRect.width;

			Slider ySlider = _ui.transform.Find("Options Window/Options/Advanced Mode Window Size Container/Height Slider").GetComponent<Slider>();
			ySlider.onValueChanged.AddListener((f) =>
			{
				_advancedModeRect.yMin = _advancedModeRect.yMax - ySlider.value;
			});
			ySlider.maxValue = Screen.height * 0.8f;
			ySlider.value = _advancedModeRect.height;

			_shortcutKeyButton = _ui.transform.Find("Options Window/Options/Shortcut Key Container/Listener Button").GetComponent<Button>();
			Text text = _shortcutKeyButton.GetComponentInChildren<Text>();
			_shortcutKeyButton.onClick.AddListener(() =>
			{
				_shortcutRegisterMode = !_shortcutRegisterMode;
				text.text = _shortcutRegisterMode ? "Press a Key" : _mainWindowKeyCode.ToString();
			});

			_crotchCorrectionByDefaultToggle = _ui.transform.Find("Options Window/Options/Joint Correction Container/Crotch/Toggle").GetComponent<Toggle>();

			_anklesCorrectionByDefaultToggle = _ui.transform.Find("Options Window/Options/Joint Correction Container/Ankles/Toggle").GetComponent<Toggle>();

			_optionsWindow.gameObject.SetActive(false);
			LayoutRebuilder.ForceRebuildLayoutImmediate(_ui.transform.GetChild(0).transform as RectTransform);

			// Additional UI
#if HONEYSELECT || PLAYHOME
			{
				RectTransform parent = GameObject.Find("StudioScene").transform.Find("Canvas Main Menu/02_Manipulate/00_Chara/06_Joint") as RectTransform;
				RawImage container = UIUtility.CreateRawImage("Additional Container", parent);
				container.rectTransform.SetRect(Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, -60f), Vector2.zero);
				container.color = new Color32(105, 108, 111, 255);

				GameObject textPrefab = parent.Find("Text Left Leg").gameObject;
				Text crotchText = Instantiate(textPrefab).GetComponent<Text>();
				crotchText.rectTransform.SetParent(parent);
				crotchText.rectTransform.localPosition = Vector3.zero;
				crotchText.rectTransform.SetRect(textPrefab.transform);
				crotchText.rectTransform.localScale = textPrefab.transform.localScale;
				crotchText.rectTransform.SetParent(container.rectTransform);
				crotchText.rectTransform.offsetMin += new Vector2(0f, -20f);
				crotchText.rectTransform.offsetMax += new Vector2(0f, -20f);
				crotchText.text = " Crotch";
				GameObject togglePrefab = parent.Find("Toggle Left Leg").gameObject;
				_crotchCorrectionToggle = Instantiate(togglePrefab).GetComponent<Toggle>();
				RectTransform rt = _crotchCorrectionToggle.transform as RectTransform;
				rt.SetParent(parent);
				rt.localPosition = Vector3.zero;
				rt.SetRect(togglePrefab.transform);
				rt.localScale = togglePrefab.transform.localScale;
				rt.SetParent(container.rectTransform);
				rt.offsetMin += new Vector2(0f, -20f);
				rt.offsetMax += new Vector2(0f, -20f);
				_crotchCorrectionToggle.onValueChanged = new Toggle.ToggleEvent();
				_crotchCorrectionToggle.onValueChanged.AddListener((b) =>
				{
					if (_poseTarget != null)
						((CharaPoseController)_poseTarget).crotchJointCorrection = _crotchCorrectionToggle.isOn;
				});

				Text leftFootText = Instantiate(textPrefab).GetComponent<Text>();
				leftFootText.rectTransform.SetParent(parent);
				leftFootText.rectTransform.localPosition = Vector3.zero;
				leftFootText.rectTransform.SetRect(textPrefab.transform);
				leftFootText.rectTransform.localScale = textPrefab.transform.localScale;
				leftFootText.rectTransform.SetParent(container.rectTransform);
				leftFootText.rectTransform.offsetMin += new Vector2(0f, -40f);
				leftFootText.rectTransform.offsetMax += new Vector2(0f, -40f);
				leftFootText.text = " Left Ankle";
				_leftFootCorrectionToggle = Instantiate(togglePrefab).GetComponent<Toggle>();
				rt = _leftFootCorrectionToggle.transform as RectTransform;
				rt.SetParent(parent);
				rt.localPosition = Vector3.zero;
				rt.SetRect(togglePrefab.transform);
				rt.localScale = togglePrefab.transform.localScale;
				rt.SetParent(container.rectTransform);
				rt.offsetMin += new Vector2(0f, -40f);
				rt.offsetMax += new Vector2(0f, -40f);
				_leftFootCorrectionToggle.onValueChanged = new Toggle.ToggleEvent();
				_leftFootCorrectionToggle.onValueChanged.AddListener((b) =>
				{
					if (_poseTarget != null)
						((CharaPoseController)_poseTarget).leftFootJointCorrection = _leftFootCorrectionToggle.isOn;
				});

				Text rightFootText = Instantiate(textPrefab).GetComponent<Text>();
				rightFootText.rectTransform.SetParent(parent);
				rightFootText.rectTransform.localPosition = Vector3.zero;
				rightFootText.rectTransform.SetRect(textPrefab.transform);
				rightFootText.rectTransform.localScale = textPrefab.transform.localScale;
				rightFootText.rectTransform.SetParent(container.rectTransform);
				rightFootText.rectTransform.offsetMin += new Vector2(0f, -60f);
				rightFootText.rectTransform.offsetMax += new Vector2(0f, -60f);
				rightFootText.text = " Right Ankle";
				_rightFootCorrectionToggle = Instantiate(togglePrefab).GetComponent<Toggle>();
				rt = _rightFootCorrectionToggle.transform as RectTransform;
				rt.SetParent(parent);
				rt.localPosition = Vector3.zero;
				rt.SetRect(togglePrefab.transform);
				rt.localScale = togglePrefab.transform.localScale;
				rt.SetParent(container.rectTransform);
				rt.offsetMin += new Vector2(0f, -60f);
				rt.offsetMax += new Vector2(0f, -60f);
				_rightFootCorrectionToggle.onValueChanged = new Toggle.ToggleEvent();
				_rightFootCorrectionToggle.onValueChanged.AddListener((b) =>
				{
					if (_poseTarget != null)
						((CharaPoseController)_poseTarget).rightFootJointCorrection = _rightFootCorrectionToggle.isOn;
				});
			}
#elif KOIKATSU || AISHOUJO || HONEYSELECT2
			{
				RectTransform parent = GameObject.Find("StudioScene").transform.Find("Canvas Main Menu/02_Manipulate/00_Chara/06_Joint") as RectTransform;
				RawImage container = UIUtility.CreateRawImage("Additional Container", parent);
				container.rectTransform.SetRect(Vector2.zero, Vector2.zero, new Vector2(0f, -60f), new Vector2(parent.rect.width, 0f));
				container.color = new Color32(110, 110, 116, 223);

				GameObject prefab = parent.Find("Left Leg (1)").gameObject;
				RectTransform crotchContainer = Instantiate(prefab).transform as RectTransform;
				crotchContainer.SetParent(container.rectTransform);
				crotchContainer.pivot = new Vector2(0f, 1f);
				crotchContainer.localPosition = Vector3.zero;
				crotchContainer.localScale = prefab.transform.localScale;
				crotchContainer.anchoredPosition = new Vector2(0f, 0f);
				crotchContainer.GetComponentInChildren<TextMeshProUGUI>().text = "Crotch";

				this._crotchCorrectionToggle = crotchContainer.GetComponentInChildren<Toggle>();
				this._crotchCorrectionToggle.onValueChanged = new Toggle.ToggleEvent();
				this._crotchCorrectionToggle.onValueChanged.AddListener((b) =>
				{
					if (this._poseTarget != null)
						((CharaPoseController)this._poseTarget).crotchJointCorrection = this._crotchCorrectionToggle.isOn;
				});

				RectTransform leftFootContainer = Instantiate(prefab).transform as RectTransform;
				leftFootContainer.SetParent(container.rectTransform);
				leftFootContainer.pivot = new Vector2(0f, 1f);
				leftFootContainer.localPosition = Vector3.zero;
				leftFootContainer.localScale = prefab.transform.localScale;
				leftFootContainer.anchoredPosition = new Vector2(0f, -20f);
				leftFootContainer.GetComponentInChildren<TextMeshProUGUI>().text = "Left Ankle";
				this._leftFootCorrectionToggle = leftFootContainer.GetComponentInChildren<Toggle>();
				this._leftFootCorrectionToggle.onValueChanged = new Toggle.ToggleEvent();
				this._leftFootCorrectionToggle.onValueChanged.AddListener((b) =>
				{
					if (this._poseTarget != null)
						((CharaPoseController)this._poseTarget).leftFootJointCorrection = this._leftFootCorrectionToggle.isOn;
				});

				RectTransform rightFootContainer = Instantiate(prefab).transform as RectTransform;

				rightFootContainer.SetParent(container.rectTransform);
				rightFootContainer.pivot = new Vector2(0f, 1f);
				rightFootContainer.localPosition = Vector3.zero;
				rightFootContainer.localScale = prefab.transform.localScale;
				rightFootContainer.anchoredPosition = new Vector2(0f, -40f);

				rightFootContainer.GetComponentInChildren<TextMeshProUGUI>().text = "Right Ankle";
				this._rightFootCorrectionToggle = rightFootContainer.GetComponentInChildren<Toggle>();
				this._rightFootCorrectionToggle.onValueChanged = new Toggle.ToggleEvent();
				this._rightFootCorrectionToggle.onValueChanged.AddListener((b) =>
				{
					if (this._poseTarget != null)
						((CharaPoseController)this._poseTarget).rightFootJointCorrection = this._rightFootCorrectionToggle.isOn;
				});
			}
#endif
			foreach (Text childText in _ui.GetComponentsInChildren<Text>(true))
				childText.font = UIUtility.defaultFont;

			_imguiBackground = IMGUIExtensions.CreateUGUIPanelForIMGUI();
		}

		private void OnWindowResize()
		{
			if (_advancedModeRect.xMax > Screen.width)
				_advancedModeRect.x -= _advancedModeRect.xMax - Screen.width;
			if (_advancedModeRect.yMax > Screen.height)
				_advancedModeRect.y -= _advancedModeRect.yMax - Screen.height;
		}


		private void SetBoneTarget(FullBodyBipedEffector bone)
		{
			ResetBoneButtons();
			_input.SelectEffector(bone, global::HSPE.Kit.Small.PoseInput.CtrlHeld()); // changed (HSPE fork)
			SelectBoneButtons();
			EventSystem.current.SetSelectedGameObject(null);
		}

		private void SetBendGoalTarget(FullBodyBipedChain bendGoal)
		{
			ResetBoneButtons();
			_input.SelectBendGoal(bendGoal, global::HSPE.Kit.Small.PoseInput.CtrlHeld()); // changed (HSPE fork)
			SelectBoneButtons();
			EventSystem.current.SetSelectedGameObject(null);
		}

		private void ResetBoneButtons()
		{
			foreach (FullBodyBipedChain bendGoal in _input.ikBendGoalTargets) // changed (HSPE fork)
			{
				Button b = _bendGoalsButtons[(int)bendGoal];
				ColorBlock cb = b.colors;
				cb.normalColor = Color.Lerp(Color.blue, Color.white, 0.5f);
				cb.highlightedColor = cb.normalColor;
				b.colors = cb;
				_bendGoalsTexts[(int)bendGoal].fontStyle = FontStyle.Normal;
			}
			foreach (FullBodyBipedEffector effector in _input.ikBoneTargets) // changed (HSPE fork)
			{
				Button b = _effectorsButtons[(int)effector];
				ColorBlock cb = b.colors;
				cb.normalColor = Color.Lerp(Color.red, Color.white, 0.5f);
				cb.highlightedColor = cb.normalColor;
				b.colors = cb;
				_effectorsTexts[(int)effector].fontStyle = FontStyle.Normal;
			}
		}

		private void SelectBoneButtons()
		{
			foreach (FullBodyBipedChain bendGoal in _input.ikBendGoalTargets) // changed (HSPE fork)
			{
				Button b = _bendGoalsButtons[(int)bendGoal];
				ColorBlock cb = b.colors;
				cb.normalColor = UIUtility.lightGreenColor;
				cb.highlightedColor = cb.normalColor;
				b.colors = cb;
				_bendGoalsTexts[(int)bendGoal].fontStyle = FontStyle.Bold;
			}
			foreach (FullBodyBipedEffector effector in _input.ikBoneTargets) // changed (HSPE fork)
			{
				Button b = _effectorsButtons[(int)effector];
				ColorBlock cb = b.colors;
				cb.normalColor = UIUtility.lightGreenColor;
				cb.highlightedColor = cb.normalColor;
				b.colors = cb;
				_effectorsTexts[(int)effector].fontStyle = FontStyle.Bold;
			}
		}

		private void GUILogic()
		{
			if (_shortcutRegisterMode)
			{
				if (Input.inputString.Length != 0)
				{
					try
					{
						KeyCode kc = (KeyCode)Enum.Parse(typeof(KeyCode), Input.inputString.ToUpper());
						if (kc != KeyCode.Escape && kc != KeyCode.Return && kc != KeyCode.Mouse0 && kc != KeyCode.Mouse1 && kc != KeyCode.Mouse2 && kc != KeyCode.Mouse3 && kc != KeyCode.Mouse4 && kc != KeyCode.Mouse5 && kc != KeyCode.Mouse6)
						{
							_mainWindowKeyCode = kc;
							_shortcutKeyButton.onClick.Invoke();
						}
					}
					catch { }
				}
			}
			// changed (HSPE fork): toggles whichever small window is in use; the kit window ignores the key while
			// the user types in a text field
			else if (Input.GetKeyDown(_mainWindowKeyCode) && (_kitSmall == null || global::HSPE.Kit.Small.KitSmallWindow.TypingInField() == false))
				ToggleMainWindow();

			if (Input.GetMouseButtonUp(0))
				_input.ReleaseDrags(); // changed (HSPE fork)

			if (mainWindowVisible == false || _poseTarget == null) // changed (HSPE fork): either window
				return;
			CharaPoseController charaPoseTarget = _poseTarget as CharaPoseController;
			bool isCharacter = charaPoseTarget != null;
			_input.Tick(_poseTarget); // changed (HSPE fork): Joan's drag math, moved unchanged to PoseInput.Tick

			if (_kitSmall != null) // addition (HSPE fork): the kit window refreshes its own controls
				return;

			if (_input.modeIK)
			{
				for (int i = 0; i < _effectorsButtons.Length; i++)
					_effectorsButtons[i].interactable = isCharacter && charaPoseTarget.IsPartEnabled((FullBodyBipedEffector)i);

				for (int i = 0; i < _bendGoalsButtons.Length; i++)
					_bendGoalsButtons[i].interactable = isCharacter && charaPoseTarget.IsPartEnabled((FullBodyBipedChain)i);

				for (int i = 0; i < _positionButtons.Length; i++)
					_positionButtons[i].interactable = _input.CanMove(_poseTarget);
			}

			bool interactableRotation = _input.CanRotate(_poseTarget); // changed (HSPE fork): same rule, in PoseInput

			for (int i = 0; i < _rotationButtons.Length; i++)
				_rotationButtons[i].interactable = interactableRotation;
		}
		#endregion

		#region Public Methods
		public void OnDuplicate(ObjectCtrlInfo source, ObjectCtrlInfo destination)
		{
			PoseController destinationController;
			if (destination is OCIChar)
				destinationController = destination.guideObject.transformTarget.gameObject.AddComponent<CharaPoseController>();
			else
				destinationController = destination.guideObject.transformTarget.gameObject.AddComponent<PoseController>();
			destinationController.LoadFrom(source.guideObject.transformTarget.gameObject.GetComponent<PoseController>());
		}
		#endregion

		#region Private Methods
		private void OnObjectAdded()
		{
			foreach (KeyValuePair<int, ObjectCtrlInfo> kvp in Studio.Studio.Instance.dicObjectCtrl)
			{
				if (kvp.Key >= _lastIndex)
				{
					switch (kvp.Value.objectInfo.kind)
					{
						case 0:
							if (kvp.Value.guideObject.transformTarget.GetComponent<PoseController>() == null)
								kvp.Value.guideObject.transformTarget.gameObject.AddComponent<CharaPoseController>();
							break;
						case 1:
							if (kvp.Value.guideObject.transformTarget.GetComponent<PoseController>() == null)
							{
								PoseController controller = kvp.Value.guideObject.transformTarget.gameObject.AddComponent<PoseController>();
								if (controller._collidersEditor._isLoneCollider == false)
									this.ExecuteDelayed(() => { controller.enabled = false; }, 2);
							}
							break;
					}
				}
			}
		}

		private void OnTargetChange(PoseController last)
		{
			if (_poseTarget != null)
			{
				bool isCharacter = _poseTarget.target.type == GenericOCITarget.Type.Character;
				if (isCharacter)
				{
					CharaPoseController poseTarget = (CharaPoseController)_poseTarget;

					_optimizeIKToggle.isOn = poseTarget.optimizeIK;
					_crotchCorrectionToggle.isOn = poseTarget.crotchJointCorrection;
					_leftFootCorrectionToggle.isOn = poseTarget.leftFootJointCorrection;
					_rightFootCorrectionToggle.isOn = poseTarget.rightFootJointCorrection;
				}
				_optimizeIKToggle.interactable = isCharacter;
				_copyLeftArmButton.interactable = isCharacter;
				_copyRightArmButton.interactable = isCharacter;
				_copyLeftHandButton.interactable = isCharacter;
				_copyRightHandButton.interactable = isCharacter;
				_copyLeftLegButton.interactable = isCharacter;
				_copyRightLegButton.interactable = isCharacter;
				_swapPostButton.interactable = isCharacter;
				_nothingText.gameObject.SetActive(false);
				_controls.gameObject.SetActive(true);
				_poseTarget.target.RefreshFKBones();
				RefreshFKBonesList();
			}
			else
			{
				_nothingText.gameObject.SetActive(true);
				_controls.gameObject.SetActive(false);
			}
			PoseController.SelectionChanged(_poseTarget);
			if (_kitSmall != null) // addition (HSPE fork)
				_kitSmall.OnTargetChanged();
		}

		private void OnCharacterReplaced(OCIChar chara)
		{
			if (_poseTarget != null && _poseTarget.target.oci == chara)
				this.ExecuteDelayed(RefreshFKBonesList);
		}

		private void OnLoadClothesFile(OCIChar chara)
		{
			if (_poseTarget != null && _poseTarget.target.oci == chara)
				this.ExecuteDelayed(RefreshFKBonesList);
		}

#if HONEYSELECT || KOIKATSU
#if HONEYSELECT
		private void OnCoordinateReplaced(OCIChar chara, CharDefine.CoordinateType coord, bool force)
#elif KOIKATSU
		private void OnCoordinateReplaced(OCIChar chara, ChaFileDefine.CoordinateType type, bool force)
#endif
		{
			if (_poseTarget != null && _poseTarget.target.oci == chara)
				this.ExecuteDelayed(RefreshFKBonesList);
		}
#endif

		private void RefreshFKBonesList()
		{
			int i = 0;
			List<KeyValuePair<GameObject, OCIChar.BoneInfo>> list = _poseTarget.target.fkObjects.ToList();

			for (; i < list.Count; ++i)
			{
				FKBoneEntry entry;
				KeyValuePair<GameObject, OCIChar.BoneInfo> pair = list[i];
				if (i < _fkBoneEntries.Count)
					entry = _fkBoneEntries[i];
				else
				{
					entry = new FKBoneEntry();
					entry.toggle = GameObject.Instantiate(_fkBoneTogglePrefab).GetComponent<Toggle>();
					entry.toggle.gameObject.hideFlags = HideFlags.None;
					entry.text = entry.toggle.GetComponentInChildren<Text>();

					entry.toggle.transform.SetParent(_fkScrollRect.content);
					entry.toggle.transform.localScale = Vector3.one;
					entry.toggle.group = _fkToggleGroup;
					entry.text.font = UIUtility.defaultFont;
					_fkBoneEntries.Add(entry);
				}
				if (pair.Key == null)
				{
					entry.toggle.gameObject.SetActive(false);
					entry.target = null;
					continue;
				}

				entry.text.text = pair.Key.name;

				entry.toggle.onValueChanged = new Toggle.ToggleEvent();
				entry.toggle.isOn = GuideObjectManager.Instance.selectObject.transformTarget == pair.Key.transform;
				entry.toggle.onValueChanged.AddListener((b) =>
				{
					if (entry.toggle.isOn)
					{
						if (_poseTarget.target.fkEnabled == false || pair.Value.active == false)
						{
							entry.toggle.isOn = false;
							SelectCurrentFKBoneEntry();
							return;
						}
						GuideObjectManager.Instance.selectObject = _dicGuideObject[pair.Key.transform];
					}
				});
				entry.target = pair.Key;
				entry.toggle.gameObject.SetActive(true);
			}
			for (; i < _fkBoneEntries.Count; ++i)
			{
				FKBoneEntry entry = _fkBoneEntries[i];

				entry.toggle.gameObject.SetActive(false);
				entry.target = null;
			}
			LayoutRebuilder.ForceRebuildLayoutImmediate(_fkScrollRect.content);
		}

		private FKBoneEntry SelectCurrentFKBoneEntry()
		{
			FKBoneEntry entry = null;
			if (GuideObjectManager.Instance.selectObject != null)
			{
				entry = _fkBoneEntries.Find(e => e.target == GuideObjectManager.Instance.selectObject.transformTarget.gameObject);
				if (entry != null)
				{
					Toggle.ToggleEvent cachedEvent = entry.toggle.onValueChanged;
					entry.toggle.onValueChanged = new Toggle.ToggleEvent();
					entry.toggle.isOn = true;
					entry.toggle.onValueChanged = cachedEvent;
				}
			}
			return entry;
		}

		[HarmonyPatch(typeof(GuideSelect), "OnPointerClick", new[] { typeof(PointerEventData) })]
		private static class GuideSelect_OnPointerClick_Patches
		{
			private static void Postfix()
			{
				_self.ExecuteDelayed(() =>
				{
					_self._fkToggleGroup.SetAllTogglesOff();
					FKBoneEntry entry = _self.SelectCurrentFKBoneEntry();
					if (entry != null)
					{
						_self._fkScrollRect.content.anchoredPosition = new Vector2(_self._fkScrollRect.content.anchoredPosition.x, _self._fkScrollRect.transform.InverseTransformPoint(_self._fkScrollRect.content.position).y - _self._fkScrollRect.transform.InverseTransformPoint(entry.toggle.transform.position).y - 10);
						_self._fkScrollRect.normalizedPosition = new Vector2(_self._fkScrollRect.normalizedPosition.x, Mathf.Clamp01(_self._fkScrollRect.normalizedPosition.y));
					}
				});
			}
		}

		[HarmonyPatch(typeof(Studio.CameraControl), "InputMouseProc", new Type[0])]
		private static class CameraControl_LateUpdate_Patches
		{
			private static bool Prefix()
			{
				if (_self._poseTarget != null && _self._poseTarget.isDraggingDynamicBone)
					return false;
				return true;
			}
		}
		#endregion

		#region Saves
#if IPA
		private void OnSceneLoad(string scenePath, XmlNode node)
		{
			_lastObjectCount = 0;
			scenePath = Path.GetFileNameWithoutExtension(scenePath) + ".sav";
#if HONEYSELECT
			string dir = _pluginDir + _studioSavesDir;
			string path = dir + scenePath;
			if (File.Exists(path))
			{
				XmlDocument doc = new XmlDocument();
				doc.Load(path);
				node = doc;
			}
#endif
			if (node != null)
				node = node.FirstChild;
			this.ExecuteDelayed(() =>
			{
				LoadDefaultVersion(node, new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList());
			}, 3);
		}

		private void OnSceneImport(string scenePath, XmlNode node)
		{
			scenePath = Path.GetFileNameWithoutExtension(scenePath) + ".sav";
#if HONEYSELECT
			string dir = _pluginDir + _studioSavesDir;
			string path = dir + scenePath;
			if (File.Exists(path))
			{
				XmlDocument doc = new XmlDocument();
				doc.Load(path);
				node = doc;
			}
#endif
			if (node != null)
				node = node.FirstChild;
			Dictionary<int, ObjectCtrlInfo> toIgnore = new Dictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl);
			this.ExecuteDelayed(() =>
			{
				LoadDefaultVersion(node, Studio.Studio.Instance.dicObjectCtrl.Where(e => toIgnore.ContainsKey(e.Key) == false).OrderBy(e => SceneInfo_Import_Patches._newToOldKeys[e.Key]).ToList());
			}, 3);
		}

		private void OnSceneSave(string scenePath, XmlTextWriter xmlWriter)
		{
			xmlWriter.WriteStartElement("root");
			xmlWriter.WriteAttributeString("version", HSPE._versionNum);
			SortedDictionary<int, ObjectCtrlInfo> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl);
			foreach (KeyValuePair<int, ObjectCtrlInfo> kvp in dic)
			{
				if (kvp.Value is OCIChar chara)
				{
					xmlWriter.WriteStartElement("characterInfo");
#if PLAYHOME
					xmlWriter.WriteAttributeString("name", ((OCIChar)kvp.Value).charInfo.fileStatus.name);
#else
					xmlWriter.WriteAttributeString("name", chara.charInfo.customInfo.name);
#endif
					xmlWriter.WriteAttributeString("index", XmlConvert.ToString(kvp.Key));

					SaveElement(kvp.Value, xmlWriter);

					xmlWriter.WriteEndElement();
				}
				else if (kvp.Value is OCIItem item)
				{
					xmlWriter.WriteStartElement("itemInfo");
					xmlWriter.WriteAttributeString("name", item.treeNodeObject.textName);
					xmlWriter.WriteAttributeString("index", XmlConvert.ToString(kvp.Key));

					SaveElement(kvp.Value, xmlWriter);

					xmlWriter.WriteEndElement();
				}
			}
			xmlWriter.WriteEndElement();
		}

		private void LoadDefaultVersion(XmlNode node, List<KeyValuePair<int, ObjectCtrlInfo>> dic)
		{
			//this.ExecuteDelayed(() => {
			//    UnityEngine.Debug.LogError("objects in scene " + Studio.Studio.Instance.dicObjectCtrl.Count);
			//}, 6);
			if (node == null || node.Name != "root")
				return;
			string v = node.Attributes["version"].Value;
			int i = 0;
			foreach (XmlNode childNode in node.ChildNodes)
			{
				switch (childNode.Name)
				{
					case "characterInfo":
						OCIChar ociChar = null;
						while (i < dic.Count && (ociChar = dic[i].Value as OCIChar) == null)
							++i;
						if (i == dic.Count)
							break;
						LoadElement(ociChar, childNode);
						++i;
						break;
					case "itemInfo":
						OCIItem ociItem = null;
						while (i < dic.Count && (ociItem = dic[i].Value as OCIItem) == null)
							++i;
						if (i == dic.Count)
							break;
						LoadElement(ociItem, childNode);
						++i;
						break;
				}
			}
		}
#elif BEPINEX
		private void OnCharaLoad(ChaFile file)
		{
			PluginData data = ExtendedSave.GetExtendedDataById(file, _extSaveKey);
			if (data == null)
				return;
			XmlDocument doc = new XmlDocument();
			doc.LoadXml((string)data.data["characterInfo"]);
			XmlNode node = doc.FirstChild;
			if (node == null)
				return;
			string v = node.Attributes["version"].Value;
			this.ExecuteDelayed(() =>
			{
				foreach (KeyValuePair<int, ObjectCtrlInfo> pair in Studio.Studio.Instance.dicObjectCtrl)
				{
					OCIChar ociChar = pair.Value as OCIChar;
					if (ociChar != null && ociChar.charInfo.chaFile == file)
						this.LoadElement(ociChar, node);
				}
			});
		}

		private void OnCharaSave(ChaFile file)
		{
			foreach (KeyValuePair<int, ObjectCtrlInfo> pair in Studio.Studio.Instance.dicObjectCtrl)
			{
				OCIChar ociChar = pair.Value as OCIChar;
				if (ociChar != null && ociChar.charInfo.chaFile == file)
				{
					using (StringWriter stringWriter = new StringWriter())
					using (XmlTextWriter xmlWriter = new XmlTextWriter(stringWriter))
					{
						xmlWriter.WriteStartElement("characterInfo");

						xmlWriter.WriteAttributeString("version", HSPE._versionNum);
						xmlWriter.WriteAttributeString("name", ociChar.charInfo.chaFile.parameter.fullname);

						this.SaveElement(ociChar, xmlWriter);

						xmlWriter.WriteEndElement();

						PluginData data = new PluginData();
						data.version = HSPE.saveVersion;
						data.data.Add("characterInfo", stringWriter.ToString());
						ExtendedSave.SetExtendedDataById(file, _extSaveKey, data);
					}
				}
			}
		}

		private void OnSceneLoad(string path)
		{
			PluginData data = ExtendedSave.GetSceneExtendedDataById(_extSaveKey);
			if (data == null)
				return;
			XmlDocument doc = new XmlDocument();
			doc.LoadXml((string)data.data["sceneInfo"]);
			XmlNode node = doc.FirstChild;
			if (node == null)
				return;
			this.ExecuteDelayed(() =>
			{
				this.LoadSceneGeneric(node, new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList());
			}, 3);
		}

		private void OnSceneImport(string path)
		{
			PluginData data = ExtendedSave.GetSceneExtendedDataById(_extSaveKey);
			if (data == null)
				return;
			XmlDocument doc = new XmlDocument();
			doc.LoadXml((string)data.data["sceneInfo"]);
			XmlNode node = doc.FirstChild;
			if (node == null)
				return;
			Dictionary<int, ObjectCtrlInfo> toIgnore = new Dictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl);
			this.ExecuteDelayed(() =>
			{
				this.LoadSceneGeneric(node, Studio.Studio.Instance.dicObjectCtrl.Where(e => toIgnore.ContainsKey(e.Key) == false).OrderBy(e => SceneInfo_Import_Patches._newToOldKeys[e.Key]).ToList());
			}, 3);
		}

		/// <summary>
		/// Other plugins should use this to force load some data.
		/// </summary>
		/// <param name="node"></param>
		public void ExternalLoadScene(XmlNode node)
		{
			this.LoadSceneGeneric(node, new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList());
		}

		private void LoadSceneGeneric(XmlNode node, List<KeyValuePair<int, ObjectCtrlInfo>> dic)
		{
			if (node == null || node.Name != "root")
				return;
			string v = node.Attributes["version"].Value;
			int i = 0;
			foreach (XmlNode childNode in node.ChildNodes)
			{
				switch (childNode.Name)
				{
					case "itemInfo":
						OCIItem ociItem = null;
						while (i < dic.Count && (ociItem = dic[i].Value as OCIItem) == null)
							++i;
						if (i == dic.Count)
							break;
						this.LoadElement(ociItem, childNode);
						++i;
						break;
				}
			}
		}

		private void OnSceneSave(string path)
		{
			using (StringWriter stringWriter = new StringWriter())
			using (XmlTextWriter xmlWriter = new XmlTextWriter(stringWriter))
			{

				xmlWriter.WriteStartElement("root");
				xmlWriter.WriteAttributeString("version", HSPE._versionNum);
				SortedDictionary<int, ObjectCtrlInfo> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl);
				foreach (KeyValuePair<int, ObjectCtrlInfo> kvp in dic)
				{
					OCIItem item = kvp.Value as OCIItem;
					if (item != null)
					{
						xmlWriter.WriteStartElement("itemInfo");
						xmlWriter.WriteAttributeString("name", item.treeNodeObject.textName);
						xmlWriter.WriteAttributeString("index", XmlConvert.ToString(kvp.Key));

						this.SaveElement(item, xmlWriter);

						xmlWriter.WriteEndElement();
					}
				}
				xmlWriter.WriteEndElement();

				PluginData data = new PluginData();
				data.version = HSPE.saveVersion;
				data.data.Add("sceneInfo", stringWriter.ToString());
				ExtendedSave.SetSceneExtendedDataById(_extSaveKey, data);
			}
		}
#endif

		private void LoadElement(OCIChar oci, XmlNode node)
		{
			PoseController controller = oci.guideObject.transformTarget.GetComponent<PoseController>();
			if (controller == null)
				controller = oci.guideObject.transformTarget.gameObject.AddComponent<CharaPoseController>();
			bool controllerEnabled = true;
			if (node.Attributes != null && node.Attributes["enabled"] != null)
				controllerEnabled = XmlConvert.ToBoolean(node.Attributes["enabled"].Value);
			controller.ScheduleLoad(node, e =>
			{
				controller.enabled = controllerEnabled;
			});

		}

		private void LoadElement(OCIItem oci, XmlNode node)
		{
			PoseController controller = oci.guideObject.transformTarget.GetComponent<PoseController>();
			if (controller == null)
				controller = oci.guideObject.transformTarget.gameObject.AddComponent<PoseController>();
			bool controllerEnabled = false;
			if (node.Attributes != null && node.Attributes["enabled"] != null)
				controllerEnabled = XmlConvert.ToBoolean(node.Attributes["enabled"].Value);
			controller.ScheduleLoad(node, e =>
			{
				controller.enabled = controllerEnabled;
			});

		}

		private void SaveElement(ObjectCtrlInfo oci, XmlTextWriter xmlWriter)
		{
			// note: This seems to throw a null reference exception, unclear why. Might have to do with vanilla studio scenes
			// so, gonna do null checks here

			PoseController controller = oci.guideObject.transformTarget.GetComponent<PoseController>();

			if (controller == null)
			{
				UnityEngine.Debug.LogError("[HSPE] Found null PoseController while saving!");
				return;
			}

			xmlWriter.WriteAttributeString("enabled", XmlConvert.ToString(controller.enabled));
			controller.SaveXml(xmlWriter);
		}
		#endregion
	}
}
