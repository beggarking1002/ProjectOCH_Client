using System;
using Google.Protobuf;
using App;
using Networking;
using UnityEngine;

namespace Networking
{
	[DefaultExecutionOrder(-100)]
	public sealed class GameServerConnection : MonoBehaviour
	{
		public static GameServerConnection Instance { get; private set; }

		[SerializeField] string host = "127.0.0.1";
		[SerializeField] int port = 7777;
		[SerializeField] bool connectOnStart = true;
		[SerializeField] bool verifyWithLoginPacket = true;

		public GameServerConnectionState State => Network?.State ?? GameServerConnectionState.Disconnected;
		public string LastError => Network?.LastError;
		public Protocol.S_LOGIN LastLogin => Network?.LastLogin;
		public bool IsConnected => Network?.IsConnected == true;
		public bool IsVerified => Network?.IsVerified == true;

		public event Action<GameServerConnectionState> StateChanged;
		public event Action<Protocol.S_LOGIN> LoginReceived;

		NetworkService Network => GameRoot.Instance != null ? GameRoot.Instance.Network : null;

		void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(gameObject);
				return;
			}

			Instance = this;
			DontDestroyOnLoad(gameObject);

			GameRoot.Instance?.ConfigureGameServer(host, port, verifyWithLoginPacket);

			if (Network != null)
			{
				Network.StateChanged += OnStateChanged;
				Network.LoginReceived += OnLoginReceived;
			}
		}

		void Start()
		{
			if (connectOnStart)
				Connect();
		}

		void OnDestroy()
		{
			if (Network != null)
			{
				Network.StateChanged -= OnStateChanged;
				Network.LoginReceived -= OnLoginReceived;
			}

			if (Instance == this)
				Instance = null;
		}

		public void Connect()
		{
			Network?.Connect(host, port, verifyWithLoginPacket);
		}

		public void Disconnect()
		{
			Network?.Disconnect();
		}

		public bool SendLogin()
		{
			return Network?.SendLogin() == true;
		}

		public bool EnterGame(ulong playerIndex = 0)
		{
			return Network?.EnterGame(playerIndex) == true;
		}

		public bool EnterBattle()
		{
			return Network?.EnterBattle() == true;
		}

		public bool SendBattleMove(ulong battleId, ulong pawnId, int q, int r)
		{
			return Network?.SendBattleMove(battleId, pawnId, q, r) == true;
		}

		public bool SendBattleSkill(ulong battleId, ulong casterPawnId, int skillSlot, ulong targetPawnId, int q, int r)
		{
			return Network?.SendBattleSkill(battleId, casterPawnId, skillSlot, targetPawnId, q, r) == true;
		}

		public bool SendBattleEndTurn(ulong battleId, ulong pawnId)
		{
			return Network?.SendBattleEndTurn(battleId, pawnId) == true;
		}

		public bool SendChat(string message)
		{
			return Network?.SendChat(message) == true;
		}

		public bool Send(IMessage packet)
		{
			return Network?.Send(packet) == true;
		}

		void OnStateChanged(GameServerConnectionState state)
		{
			StateChanged?.Invoke(state);
		}

		void OnLoginReceived(Protocol.S_LOGIN packet)
		{
			LoginReceived?.Invoke(packet);
		}
	}
}
