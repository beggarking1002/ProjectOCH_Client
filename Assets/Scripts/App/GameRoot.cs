using Networking;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace App
{
	[DefaultExecutionOrder(-1000)]
	public sealed class GameRoot : MonoBehaviour
	{
		const string UiFontAddress = "Font_Maplestory_Light";
		static Font _uiFont;
		static AsyncOperationHandle<Font> _uiFontHandle;
		static TMP_FontAsset _tmpUiFont;

		public static GameRoot Instance { get; private set; }
		public static Font UiFont
		{
			get
			{
				if (_uiFont != null)
					return _uiFont;

				_uiFontHandle = Addressables.LoadAssetAsync<Font>(UiFontAddress);
				_uiFont = _uiFontHandle.WaitForCompletion();
				if (_uiFont != null)
					return _uiFont;

				Debug.LogError($"Failed to load project UI font address: {UiFontAddress}");
				return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			}
		}
		public static TMP_FontAsset UiTmpFont => _tmpUiFont != null
			? _tmpUiFont
			: _tmpUiFont = TMP_FontAsset.CreateFontAsset(UiFont);

		[SerializeField] string gameServerHost = "127.0.0.1";
		[SerializeField] int gameServerPort = 7777;
		[SerializeField] bool connectToGameServerOnStart = true;
		[SerializeField] bool verifyWithLoginPacket = true;

		public AppServices Services { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Bootstrap()
		{
			SceneManager.sceneLoaded -= ApplySceneFont;
			SceneManager.sceneLoaded += ApplySceneFont;

			if (Instance != null)
				return;

			GameRoot existing = FindFirstObjectByType<GameRoot>();
			if (existing != null)
				return;

			GameObject go = new GameObject("@GameRoot");
			DontDestroyOnLoad(go);
			go.AddComponent<GameRoot>();
		}

		public static void ApplyUiFont(GameObject root)
		{
			if (root == null)
				return;

			Font font = UiFont;
			foreach (Text text in root.GetComponentsInChildren<Text>(true))
				text.font = font;
			foreach (TextMesh textMesh in root.GetComponentsInChildren<TextMesh>(true))
				ApplyWorldTextFont(textMesh);

			TMP_FontAsset tmpFont = UiTmpFont;
			if (tmpFont == null)
				return;

			foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
				text.font = tmpFont;
		}

		// TextMesh stores glyph UVs in the font's atlas. Updating only TextMesh.font
		// leaves its renderer on the previous font material, which renders scrambled glyphs.
		public static void ApplyWorldTextFont(TextMesh textMesh)
		{
			if (textMesh == null)
				return;

			Font font = UiFont;
			textMesh.font = font;
			MeshRenderer renderer = textMesh.GetComponent<MeshRenderer>();
			if (renderer != null)
				renderer.sharedMaterial = font.material;
		}

		static void ApplySceneFont(Scene scene, LoadSceneMode mode)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
				ApplyUiFont(root);
		}

		void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(gameObject);
				return;
			}

			Instance = this;
			DontDestroyOnLoad(gameObject);
			Application.runInBackground = true;

			Services = new AppServices();
			Services.Initialize(gameServerHost, gameServerPort, verifyWithLoginPacket);
		}

		void Start()
		{
			if (connectToGameServerOnStart)
				Services.Network.Connect();
		}

		void Update()
		{
			Services?.Tick();
		}

		void OnDestroy()
		{
			if (Instance == this)
				Instance = null;

			Services?.Dispose();
			Services = null;
		}

		public void ConfigureGameServer(string host, int port, bool verifyLogin)
		{
			gameServerHost = host;
			gameServerPort = port;
			verifyWithLoginPacket = verifyLogin;
			Services?.Network.Configure(host, port, verifyLogin);
		}

		public NetworkService Network => Services.Network;
	}
}
