using App;
using Protocol;
using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Scenes
{
	public sealed class TitleSceneFlow : MonoBehaviour
	{
		const string TitleSceneName = "TitleScene";
		const string FieldSceneName = "FieldScene";
		const string GameStartButtonName = "GameStartButton";
		const string TitleBackgroundName = "TitleBackground";
		const string TitleBackgroundAddress = "TitleSceneBackground";

		static TitleSceneFlow _instance;

		Button _gameStartButton;
		Image _titleBackground;
		AsyncOperationHandle<Sprite> _titleBackgroundHandle;
		bool _hasTitleBackgroundHandle;
		int _titleBackgroundLoadVersion;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Bootstrap()
		{
			if (_instance != null)
				return;

			GameObject go = new GameObject("@TitleSceneFlow");
			DontDestroyOnLoad(go);
			_instance = go.AddComponent<TitleSceneFlow>();
		}

		void OnEnable()
		{
			SceneManager.sceneLoaded += OnSceneLoaded;
			PacketHandler.Instance.EnterGameReceived += OnEnterGameReceived;

			Scene activeScene = SceneManager.GetActiveScene();
			if (activeScene.isLoaded && activeScene.name == TitleSceneName)
				BindGameStartButton(activeScene);
		}

		void OnDisable()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			PacketHandler.Instance.EnterGameReceived -= OnEnterGameReceived;
			UnbindGameStartButton();
			ReleaseTitleBackground();
		}

		void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (scene.name == TitleSceneName)
			{
				BindGameStartButton(scene);
				return;
			}

			UnbindGameStartButton();
			ReleaseTitleBackground();
		}

		void BindGameStartButton(Scene scene)
		{
			UnbindGameStartButton();
			LoadTitleBackgroundAsync(scene);

			_gameStartButton = FindButton(scene, GameStartButtonName);
			if (_gameStartButton == null)
			{
				Debug.LogWarning($"{nameof(TitleSceneFlow)} could not find {GameStartButtonName} in {TitleSceneName}.");
				return;
			}

			_gameStartButton.onClick.AddListener(OnGameStartClicked);
		}

		async void LoadTitleBackgroundAsync(Scene scene)
		{
			if (_titleBackground != null || scene.name != TitleSceneName)
				return;

			Canvas canvas = FindCanvas(scene);
			if (canvas == null)
			{
				Debug.LogWarning($"{nameof(TitleSceneFlow)} could not find a Canvas in {TitleSceneName}.");
				return;
			}

			GameObject backgroundObject = new GameObject(TitleBackgroundName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(AspectRatioFitter));
			backgroundObject.transform.SetParent(canvas.transform, false);
			backgroundObject.transform.SetAsFirstSibling();
			RectTransform rect = backgroundObject.GetComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;

			_titleBackground = backgroundObject.GetComponent<Image>();
			_titleBackground.raycastTarget = false;
			AspectRatioFitter fitter = backgroundObject.GetComponent<AspectRatioFitter>();
			fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
			fitter.aspectRatio = 1672f / 941f;

			int loadVersion = ++_titleBackgroundLoadVersion;
			AsyncOperationHandle<Sprite> handle = Addressables.LoadAssetAsync<Sprite>(TitleBackgroundAddress);
			_titleBackgroundHandle = handle;
			_hasTitleBackgroundHandle = true;
			await handle.Task;

			if (loadVersion != _titleBackgroundLoadVersion || scene.isLoaded == false || SceneManager.GetActiveScene().name != TitleSceneName)
				return;

			if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null && _titleBackground != null)
			{
				_titleBackground.sprite = handle.Result;
				_titleBackground.enabled = true;
				return;
			}

			Debug.LogWarning($"Failed to load title background addressable: {TitleBackgroundAddress}");
			ReleaseTitleBackground();
		}

		void ReleaseTitleBackground()
		{
			_titleBackgroundLoadVersion++;
			if (_hasTitleBackgroundHandle && _titleBackgroundHandle.IsValid())
				Addressables.Release(_titleBackgroundHandle);

			_hasTitleBackgroundHandle = false;
			if (_titleBackground != null)
				Destroy(_titleBackground.gameObject);

			_titleBackground = null;
		}

		void UnbindGameStartButton()
		{
			if (_gameStartButton == null)
				return;

			_gameStartButton.onClick.RemoveListener(OnGameStartClicked);
			_gameStartButton = null;
		}

		void OnGameStartClicked()
		{
			if (GameRoot.Instance == null)
			{
				Debug.LogWarning("Cannot enter game because GameRoot is not initialized.");
				return;
			}

			ulong playerIndex = GetCommandLinePlayerIndex();
			if (GameRoot.Instance.Network.EnterGame(playerIndex) == false)
			{
				Debug.LogWarning($"Failed to send C_ENTER_GAME. {GameRoot.Instance.Network.LastError}");
				return;
			}

			if (_gameStartButton != null)
				_gameStartButton.interactable = false;

			Debug.Log($"Sent C_ENTER_GAME. playerIndex={playerIndex}");
		}

		async void OnEnterGameReceived(S_ENTER_GAME packet)
		{
			if (packet.Success == false)
			{
				if (_gameStartButton != null)
					_gameStartButton.interactable = true;

				Debug.LogWarning("S_ENTER_GAME failed.");
				return;
			}

			Debug.Log("S_ENTER_GAME success. Loading FieldScene.");
			await SceneTransitionOverlay.ShowAsync();
			SceneManager.LoadScene(FieldSceneName);
		}

		static Button FindButton(Scene scene, string buttonName)
		{
			GameObject[] roots = scene.GetRootGameObjects();
			for (int i = 0; i < roots.Length; i++)
			{
				Button[] buttons = roots[i].GetComponentsInChildren<Button>(true);
				for (int j = 0; j < buttons.Length; j++)
				{
					if (buttons[j].name == buttonName)
						return buttons[j];
				}
			}

			return null;
		}

		static Canvas FindCanvas(Scene scene)
		{
			GameObject[] roots = scene.GetRootGameObjects();
			for (int i = 0; i < roots.Length; i++)
			{
				Canvas canvas = roots[i].GetComponentInChildren<Canvas>(true);
				if (canvas != null)
					return canvas;
			}

			return null;
		}

		static ulong GetCommandLinePlayerIndex()
		{
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length - 1; i++)
			{
				if (args[i] == "-playerIndex" && ulong.TryParse(args[i + 1], out ulong playerIndex))
					return playerIndex;
			}

			return 0;
		}
	}
}
