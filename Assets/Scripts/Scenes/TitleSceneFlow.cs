using App;
using Auth;
using Field;
using Protocol;
using System;
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
		Text _loginStatusText;

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
			PacketHandler.Instance.LoginReceived += OnLoginReceived;
			PacketHandler.Instance.EnterGameReceived += OnEnterGameReceived;

			Scene activeScene = SceneManager.GetActiveScene();
			if (activeScene.isLoaded && activeScene.name == TitleSceneName)
				BindGameStartButton(activeScene);
		}

		void OnDisable()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			PacketHandler.Instance.LoginReceived -= OnLoginReceived;
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
			BindLoginStatus(scene);
			bool googleConfigured = GoogleOAuthLogin.IsConfigured;
			bool developmentLogin = HasCommandLineFlag("-developmentLogin");
			_gameStartButton.interactable = googleConfigured || developmentLogin;
			if (googleConfigured)
				SetLoginStatus("Google 계정으로 로그인합니다.", new Color(0.82f, 0.9f, 1f));
			else if (developmentLogin)
			{
				ulong playerIndex = GetCommandLinePlayerIndex();
				string idText = playerIndex != 0 ? playerIndex.ToString() : "임시 자동 할당";
				SetLoginStatus($"개발용 로그인 · Google 계정 저장 안 됨 · ID {idText}", new Color(1f, 0.72f, 0.35f));
			}
			else
				SetLoginStatus("Google 로그인 설정 필요 · StreamingAssets/GoogleAuth.json 없음", new Color(1f, 0.45f, 0.4f));
		}

		void UnbindGameStartButton()
		{
			if (_gameStartButton == null)
				return;

			_gameStartButton.onClick.RemoveListener(OnGameStartClicked);
			_gameStartButton = null;
		}

		async void OnGameStartClicked()
		{
			if (GameRoot.Instance == null)
			{
				Debug.LogWarning("Cannot enter game because GameRoot is not initialized.");
				return;
			}

			if (!GameRoot.Instance.Network.IsConnected)
			{
				Debug.LogWarning("Cannot log in because the game server is not connected.");
				return;
			}

			bool googleConfigured = GoogleOAuthLogin.IsConfigured;
			bool developmentLogin = HasCommandLineFlag("-developmentLogin");
			if (!googleConfigured && !developmentLogin)
			{
				const string reason = "GoogleAuth.json이 없어 로그인할 수 없습니다.";
				Debug.LogWarning(reason);
				SetLoginStatus(reason, new Color(1f, 0.45f, 0.4f));
				return;
			}

			if (_gameStartButton != null)
				_gameStartButton.interactable = false;

			try
			{
				bool sent;
				if (googleConfigured)
				{
					SetLoginStatus("브라우저에서 Google 계정을 선택해 주세요.", new Color(0.82f, 0.9f, 1f));
					GoogleOAuthResult result = await GoogleOAuthLogin.AuthorizeAsync();
					sent = GameRoot.Instance.Network.SendLogin(result.AuthorizationCode, result.CodeVerifier, result.RedirectUri);
				}
				else
				{
					// Keeps local development usable until GoogleAuth.json is configured.
					SetLoginStatus("개발용 로그인 요청 중...", new Color(1f, 0.72f, 0.35f));
					sent = GameRoot.Instance.Network.SendLogin();
				}

				if (!sent)
					throw new InvalidOperationException(GameRoot.Instance.Network.LastError ?? "Failed to send C_LOGIN.");
			}
			catch (Exception exception)
			{
				Debug.LogWarning($"Login failed: {exception.Message}");
				SetLoginStatus($"로그인 실패 · {exception.Message}", new Color(1f, 0.45f, 0.4f));
				if (_gameStartButton != null)
					_gameStartButton.interactable = true;
			}
		}

		void OnLoginReceived(S_LOGIN packet)
		{
			if (!packet.Success)
			{
				Debug.LogWarning($"S_LOGIN failed: {packet.Reason}");
				SetLoginStatus($"로그인 실패 · {packet.Reason}", new Color(1f, 0.45f, 0.4f));
				if (_gameStartButton != null)
					_gameStartButton.interactable = true;
				return;
			}

			if (packet.AccountId != 0)
			{
				string name = string.IsNullOrWhiteSpace(packet.DisplayName) ? "Google 계정" : packet.DisplayName;
				SetLoginStatus($"로그인 성공 · {name} · 계정 ID {packet.AccountId}", new Color(0.55f, 1f, 0.65f));
				Debug.Log($"Google login success. displayName={name} accountId={packet.AccountId}");
			}
			else
			{
				SetLoginStatus("개발용 로그인 성공 · 영구 계정 아님", new Color(1f, 0.72f, 0.35f));
				Debug.LogWarning("Development login succeeded without a persistent Google account.");
			}

			ulong developmentPlayerIndex = GetCommandLinePlayerIndex();
			if (!GameRoot.Instance.Network.EnterGame(developmentPlayerIndex))
			{
				Debug.LogWarning($"Failed to send C_ENTER_GAME. {GameRoot.Instance.Network.LastError}");
				if (_gameStartButton != null)
					_gameStartButton.interactable = true;
			}
		}

		async void OnEnterGameReceived(S_ENTER_GAME packet)
		{
			// TitleSceneFlow persists across scenes, but S_ENTER_GAME is also sent when
			// a player returns to the field after battle. BattleSceneFlow owns that
			// transition; handling it here as well would issue two concurrent
			// FieldScene loads.
			if (SceneManager.GetActiveScene().name != TitleSceneName)
				return;

			if (packet.Success == false)
			{
				if (_gameStartButton != null)
					_gameStartButton.interactable = true;

				Debug.LogWarning("S_ENTER_GAME failed.");
				return;
			}

			ulong playerObjectId = packet.Player?.ObjectId ?? 0;
			Debug.Log($"S_ENTER_GAME success. playerObjectId={playerObjectId} accountId={GameRoot.Instance.Network.LastLogin?.AccountId ?? 0}. Loading FieldScene.");
			await SceneTransitionOverlay.ShowAsync();
			await FieldVillageArtworkCache.PreloadAsync();
			SceneManager.LoadScene(FieldSceneName);
		}

		void BindLoginStatus(Scene scene)
		{
			Text[] texts = FindComponents<Text>(scene);
			for (int i = 0; i < texts.Length; i++)
			{
				if (texts[i].name == "LoginStatusText")
				{
					_loginStatusText = texts[i];
					return;
				}
			}

			RectTransform parent = _gameStartButton.transform.parent as RectTransform;
			if (parent == null)
				return;

			GameObject statusObject = new GameObject("LoginStatusText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
			statusObject.layer = parent.gameObject.layer;
			statusObject.transform.SetParent(parent, false);
			RectTransform rect = statusObject.GetComponent<RectTransform>();
			rect.anchorMin = new Vector2(0.5f, 0.5f);
			rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.anchoredPosition = new Vector2(0f, 65f);
			rect.sizeDelta = new Vector2(1000f, 50f);

			_loginStatusText = statusObject.GetComponent<Text>();
			_loginStatusText.font = GameRoot.UiFont != null ? GameRoot.UiFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
			_loginStatusText.fontSize = 22;
			_loginStatusText.alignment = TextAnchor.MiddleCenter;
			_loginStatusText.raycastTarget = false;
			_loginStatusText.horizontalOverflow = HorizontalWrapMode.Overflow;
			_loginStatusText.verticalOverflow = VerticalWrapMode.Overflow;
		}

		void SetLoginStatus(string message, Color color)
		{
			if (_loginStatusText == null)
				return;
			_loginStatusText.text = message;
			_loginStatusText.color = color;
		}

		static T[] FindComponents<T>(Scene scene) where T : Component
		{
			var results = new System.Collections.Generic.List<T>();
			GameObject[] roots = scene.GetRootGameObjects();
			for (int i = 0; i < roots.Length; i++)
				results.AddRange(roots[i].GetComponentsInChildren<T>(true));
			return results.ToArray();
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

		static bool HasCommandLineFlag(string flag)
		{
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i < args.Length; i++)
			{
				if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
					return true;
			}
			return false;
		}
	}
}
