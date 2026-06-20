using System;
using System.Collections.Generic;
using System.Net;
using Google.Protobuf;
using ServerCore;
using UnityEngine;

namespace OCH.Networking
{
	public enum GameServerConnectionState
	{
		Disconnected,
		Connecting,
		Connected,
		Verifying,
		Verified,
		Failed,
	}

	[DefaultExecutionOrder(-100)]
	public sealed class GameServerConnection : MonoBehaviour
	{
		public static GameServerConnection Instance { get; private set; }

		[SerializeField] string host = "127.0.0.1";
		[SerializeField] int port = 7777;
		[SerializeField] bool connectOnStart = true;
		[SerializeField] bool verifyWithLoginPacket = true;

		readonly Queue<Action> _mainThreadJobs = new Queue<Action>();
		readonly object _lock = new object();

		Connector _connector;
		GameServerSession _session;

		public GameServerConnectionState State { get; private set; } = GameServerConnectionState.Disconnected;
		public string LastError { get; private set; }
		public Protocol.S_LOGIN LastLogin { get; private set; }

		public bool IsConnected =>
			_session != null &&
			_session.IsConnected() &&
			(State == GameServerConnectionState.Connected ||
			 State == GameServerConnectionState.Verifying ||
			 State == GameServerConnectionState.Verified);

		public bool IsVerified => State == GameServerConnectionState.Verified;

		public event Action<GameServerConnectionState> StateChanged;
		public event Action<Protocol.S_LOGIN> LoginReceived;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		static void Bootstrap()
		{
			if (Instance != null)
				return;

			GameServerConnection existing = UnityEngine.Object.FindFirstObjectByType<GameServerConnection>();
			if (existing != null)
				return;

			GameObject go = new GameObject(nameof(GameServerConnection));
			DontDestroyOnLoad(go);
			go.AddComponent<GameServerConnection>();
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
			ClientPacketHandler.Instance.Init();
			ClientPacketHandler.Instance.LoginReceived += OnLoginReceived;
		}

		void Start()
		{
			if (connectOnStart)
				Connect();
		}

		void Update()
		{
			FlushMainThreadJobs();
			ClientPacketHandler.Instance.Flush();
		}

		void OnDestroy()
		{
			if (Instance == this)
				Instance = null;

			ClientPacketHandler.Instance.LoginReceived -= OnLoginReceived;
			Disconnect();
		}

		public void Connect()
		{
			if (State == GameServerConnectionState.Connecting || IsConnected)
				return;

			if (TryCreateEndPoint(host, port, out IPEndPoint endPoint) == false)
				return;

			LastError = null;
			LastLogin = null;
			SetState(GameServerConnectionState.Connecting);

			_connector = new Connector
			{
				OnSuccessCallback = () => EnqueueMainThread(() =>
				{
					SetState(GameServerConnectionState.Connected);
					if (verifyWithLoginPacket)
						SendLogin();
				}),
				OnFailedCallback = () => EnqueueMainThread(() =>
				{
					LastError = $"Failed to connect to {host}:{port}";
					SetState(GameServerConnectionState.Failed);
				}),
			};

			_connector.Connect(endPoint, CreateSession);
		}

		public void Disconnect()
		{
			_session?.Disconnect();
			_session = null;

			if (State != GameServerConnectionState.Disconnected)
				SetState(GameServerConnectionState.Disconnected);
		}

		public void SendLogin()
		{
			SetState(GameServerConnectionState.Verifying);
			Send(new Protocol.C_LOGIN());
		}

		public void EnterGame(ulong playerIndex = 0)
		{
			Send(new Protocol.C_ENTER_GAME { PlayerIndex = playerIndex });
		}

		public void SendChat(string message)
		{
			Send(new Protocol.C_CHAT { Msg = message ?? string.Empty });
		}

		public bool Send(IMessage packet)
		{
			if (_session == null || _session.IsConnected() == false)
			{
				LastError = "Cannot send packet because the game server is not connected.";
				return false;
			}

			ArraySegment<byte> sendBuffer;
			switch (packet)
			{
				case Protocol.C_LOGIN pkt:
					sendBuffer = ClientPacketHandler.Instance.MakeSendBuffer(pkt);
					break;
				case Protocol.C_ENTER_GAME pkt:
					sendBuffer = ClientPacketHandler.Instance.MakeSendBuffer(pkt);
					break;
				case Protocol.C_LEAVE_GAME pkt:
					sendBuffer = ClientPacketHandler.Instance.MakeSendBuffer(pkt);
					break;
				case Protocol.C_MOVE pkt:
					sendBuffer = ClientPacketHandler.Instance.MakeSendBuffer(pkt);
					break;
				case Protocol.C_CHAT pkt:
					sendBuffer = ClientPacketHandler.Instance.MakeSendBuffer(pkt);
					break;
				default:
					LastError = $"Unsupported client packet type: {packet.GetType().Name}";
					return false;
			}

			_session.Send(sendBuffer);
			return true;
		}

		GameServerSession CreateSession()
		{
			_session = new GameServerSession();
			_session.Connected += endPoint => EnqueueMainThread(() =>
			{
				LastError = null;
				Debug.Log($"Game server socket connected: {endPoint}");
			});
			_session.Disconnected += _ => EnqueueMainThread(() =>
			{
				_session = null;
				SetState(GameServerConnectionState.Disconnected);
			});
			return _session;
		}

		void OnLoginReceived(Protocol.S_LOGIN pkt)
		{
			LastLogin = pkt;
			SetState(pkt.Success ? GameServerConnectionState.Verified : GameServerConnectionState.Failed);

			if (pkt.Success == false)
				LastError = "Server login verification failed.";

			Debug.Log($"Game server verification: success={pkt.Success}, players={pkt.Players.Count}");
			LoginReceived?.Invoke(pkt);
		}

		bool TryCreateEndPoint(string targetHost, int targetPort, out IPEndPoint endPoint)
		{
			endPoint = null;

			if (targetPort <= 0 || targetPort > ushort.MaxValue)
			{
				LastError = $"Invalid server port: {targetPort}";
				SetState(GameServerConnectionState.Failed);
				return false;
			}

			try
			{
				IPAddress address;
				if (IPAddress.TryParse(targetHost, out address) == false)
					address = Dns.GetHostAddresses(targetHost)[0];

				endPoint = new IPEndPoint(address, targetPort);
				return true;
			}
			catch (Exception ex)
			{
				LastError = $"Invalid server address {targetHost}:{targetPort} ({ex.Message})";
				SetState(GameServerConnectionState.Failed);
				return false;
			}
		}

		void EnqueueMainThread(Action action)
		{
			lock (_lock)
			{
				_mainThreadJobs.Enqueue(action);
			}
		}

		void FlushMainThreadJobs()
		{
			while (true)
			{
				Action job;
				lock (_lock)
				{
					if (_mainThreadJobs.Count == 0)
						return;

					job = _mainThreadJobs.Dequeue();
				}

				job.Invoke();
			}
		}

		void SetState(GameServerConnectionState state)
		{
			if (State == state)
				return;

			State = state;
			Debug.Log($"Game server state: {State}");
			StateChanged?.Invoke(State);
		}
	}
}
