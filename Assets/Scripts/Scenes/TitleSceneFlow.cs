using App;
using Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Scenes
{
	public sealed class TitleSceneFlow : MonoBehaviour
	{
		const string TitleSceneName = "TitleScene";
		const string FieldSceneName = "FieldScene";
		const string GameStartButtonName = "GameStartButton";

		static TitleSceneFlow _instance;

		Button _gameStartButton;

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
		}

		void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (scene.name == TitleSceneName)
			{
				BindGameStartButton(scene);
				return;
			}

			UnbindGameStartButton();
		}

		void BindGameStartButton(Scene scene)
		{
			UnbindGameStartButton();

			_gameStartButton = FindButton(scene, GameStartButtonName);
			if (_gameStartButton == null)
			{
				Debug.LogWarning($"{nameof(TitleSceneFlow)} could not find {GameStartButtonName} in {TitleSceneName}.");
				return;
			}

			_gameStartButton.onClick.AddListener(OnGameStartClicked);
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

			if (GameRoot.Instance.Network.EnterGame() == false)
			{
				Debug.LogWarning($"Failed to send C_ENTER_GAME. {GameRoot.Instance.Network.LastError}");
				return;
			}

			if (_gameStartButton != null)
				_gameStartButton.interactable = false;

			Debug.Log("Sent C_ENTER_GAME.");
		}

		void OnEnterGameReceived(S_ENTER_GAME packet)
		{
			if (packet.Success == false)
			{
				if (_gameStartButton != null)
					_gameStartButton.interactable = true;

				Debug.LogWarning("S_ENTER_GAME failed.");
				return;
			}

			Debug.Log("S_ENTER_GAME success. Loading FieldScene.");
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
	}
}
