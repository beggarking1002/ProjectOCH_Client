using Networking;
using UnityEngine;

namespace App
{
	[DefaultExecutionOrder(-1000)]
	public sealed class GameRoot : MonoBehaviour
	{
		public static GameRoot Instance { get; private set; }

		[SerializeField] string gameServerHost = "127.0.0.1";
		[SerializeField] int gameServerPort = 7777;
		[SerializeField] bool connectToGameServerOnStart = true;
		[SerializeField] bool verifyWithLoginPacket = true;

		public AppServices Services { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Bootstrap()
		{
			if (Instance != null)
				return;

			GameRoot existing = FindFirstObjectByType<GameRoot>();
			if (existing != null)
				return;

			GameObject go = new GameObject("@GameRoot");
			DontDestroyOnLoad(go);
			go.AddComponent<GameRoot>();
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
