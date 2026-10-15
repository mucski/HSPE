#if IPA
using IllusionInjector;
#else
using BepInEx;
using UnityEngine.SceneManagement;
#endif
using System;
using UnityEngine;
using System.Reflection;
using System.Diagnostics;
using System.Linq;

namespace ToolBox
{
	public abstract class GenericPlugin
#if BEPINEX
		: BaseUnityPlugin
#endif
	{
		public Binary binary { get; private set; }
		public int level { get; private set; } = -1;
#if IPA
		private static PluginComponent _pluginComponent;
		private Component _onGUIDispatcher = null;
		private Action onGuiAction;

		//public abstract string Name { get; }
		//public abstract string Version { get; }

		public virtual string Name => __name;
		public virtual string Version => __version;

		readonly string __name = Assembly.GetExecutingAssembly().GetName().Name;
		readonly string __version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion
#if ALPHA
			+ "-Alpha"
#elif BETA
			+ "-Beta"
#endif
			;
		public abstract string[] Filter { get; }
		public GameObject gameObject
		{
			get
			{
				if (_pluginComponent == null)
					_pluginComponent = UnityEngine.Object.FindObjectOfType<PluginComponent>();
				return _pluginComponent.gameObject;
			}
		}

		public void OnApplicationStart() => Awake();

		public void OnApplicationQuit() => OnDestroy();

		public void OnLevelWasInitialized(int level) { }

		public void OnLevelWasLoaded(int level)
		{
			this.level = level;
			LevelLoaded(level);
		}

		public void OnUpdate() => Update();

		public void OnFixedUpdate() => FixedUpdate();

		public void OnLateUpdate() => LateUpdate();
#endif

		protected virtual void Awake()
		{
#if BEPINEX
			SceneManager.sceneLoaded += this.LevelLoaded;
#endif
			switch (Application.productName)
			{
#if HONEYSELECT
				case "HoneySelect":
				case "Honey Select Unlimited":
#elif KOIKATSU
				case "Koikatsu Party":
				case "Koikatu":
				case "KoikatuVR":
#elif EMOTIONCREATORS
				case "EmotionCreators":
#elif AISHOUJO
				case "AI-Syoujyo":
#elif PLAYHOME
				case "PlayHome":
#elif HONEYSELECT2
				case "HoneySelect2":
#endif
					binary = Binary.Game;
					break;
#if HONEYSELECT
				case "StudioNEO":
#elif KOIKATSU
				case "CharaStudio":
#elif EMOTIONCREATORS
				case "":
#elif AISHOUJO || HONEYSELECT2
				case "StudioNEOV2":
#elif PLAYHOME
				case "PlayHomeStudio":
#endif
					binary = Binary.Studio;
					break;
			}
#if IPA
			_onGUIDispatcher = gameObject.GetComponents<Component>().FirstOrDefault(x => x.GetType().Name == nameof(OnGUIDispatcher));

			/*_onGUIDispatcher = gameObject.GetComponent<OnGUIDispatcher>();
			if (_onGUIDispatcher == null)
				_onGUIDispatcher = gameObject.gameObject.AddComponent<OnGUIDispatcher>();
			_onGUIDispatcher.AddListener(new Action(OnGUI));*/
			/*Component[] components = gameObject.GetComponents<Component>();
			foreach (Component c in components)
			{
				if (c.GetType().Name == nameof(OnGUIDispatcher))
				{
					_onGUIDispatcher = c;
					break;
				}
			}*/
			if (_onGUIDispatcher == null)
				_onGUIDispatcher = gameObject.gameObject.AddComponent<OnGUIDispatcher>();

			if (onGuiAction == null)
				onGuiAction = new Action(OnGUI);

			_onGUIDispatcher.GetType().GetMethod(nameof(OnGUIDispatcher.AddListener), BindingFlags.Instance | BindingFlags.Public).Invoke(_onGUIDispatcher, new object[] { onGuiAction });
#endif
		}

		protected virtual void OnDestroy()
		{
#if IPA
			if (_onGUIDispatcher && onGuiAction != null)
				_onGUIDispatcher.GetType().GetMethod(nameof(OnGUIDispatcher.RemoveListener), BindingFlags.Instance | BindingFlags.Public).Invoke(_onGUIDispatcher, new object[] { onGuiAction });
#endif
		}

		protected virtual void LevelLoaded(int l) { }

#if BEPINEX
		protected virtual void LevelLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
		{
			if (mode == LoadSceneMode.Single)
			{
				this.level = scene.buildIndex;
				this.LevelLoaded(scene.buildIndex);
			}
		}
#endif

		protected virtual void Update() { }

		protected virtual void LateUpdate() { }

		protected virtual void FixedUpdate() { }

		protected virtual void OnGUI() { }
	}

#if IPA
	internal class OnGUIDispatcher : MonoBehaviour
	{
		private event Action _onGUI;

		public void AddListener(Action listener) => _onGUI += listener;

		public void RemoveListener(Action listener) => _onGUI -= listener;

		private void OnGUI() => _onGUI?.Invoke();
	}
#endif

	public enum Binary
	{
		Game,
		Studio
	}
}
