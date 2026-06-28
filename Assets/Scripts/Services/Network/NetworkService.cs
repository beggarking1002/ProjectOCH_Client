using System;
using System.Collections.Generic;
using System.Net;
using Google.Protobuf;
using ServerCore;
using UnityEngine;

namespace Networking
{
	public sealed class NetworkService : IDisposable
	{
		readonly Queue<Action> _mainThreadJobs = new Queue<Action>();
		readonly object _lock = new object();

		string _host = "127.0.0.1";
		int _port = 7777;
		bool _verifyWithLoginPacket = true;
		bool _initialized;

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

		public void Initialize(string host, int port, bool verifyWithLoginPacket)
		{
			Configure(host, port, verifyWithLoginPacket);

			if (_initialized)
				return;

			PacketHandler.Instance.LoginReceived += OnLoginReceived;
			_initialized = true;
		}

		public void Configure(string host, int port, bool verifyWithLoginPacket)
		{
			_host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
			_port = port;
			_verifyWithLoginPacket = verifyWithLoginPacket;
		}

		public void Tick()
		{
			FlushMainThreadJobs();
			PacketHandler.Instance.Flush();
		}

		public void Dispose()
		{
			if (_initialized)
				PacketHandler.Instance.LoginReceived -= OnLoginReceived;

			Disconnect();
			_initialized = false;
		}

		public void Connect()
		{
			if (State == GameServerConnectionState.Connecting || IsConnected)
				return;

			if (TryCreateEndPoint(_host, _port, out IPEndPoint endPoint) == false)
				return;

			LastError = null;
			LastLogin = null;
			SetState(GameServerConnectionState.Connecting);

			_connector = new Connector
			{
				OnSuccessCallback = () => EnqueueMainThread(() =>
				{
					SetState(GameServerConnectionState.Connected);
					if (_verifyWithLoginPacket)
						SendLogin();
				}),
				OnFailedCallback = () => EnqueueMainThread(() =>
				{
					LastError = $"Failed to connect to {_host}:{_port}";
					SetState(GameServerConnectionState.Failed);
				}),
			};

			_connector.Connect(endPoint, CreateSession);
		}

		public void Connect(string host, int port, bool verifyWithLoginPacket)
		{
			Configure(host, port, verifyWithLoginPacket);
			Connect();
		}

		public void Disconnect()
		{
			_session?.Disconnect();
			_session = null;

			if (State != GameServerConnectionState.Disconnected)
				SetState(GameServerConnectionState.Disconnected);
		}

		public bool SendLogin()
		{
			if (Send(new Protocol.C_LOGIN()) == false)
			{
				SetState(GameServerConnectionState.Failed);
				return false;
			}

			SetState(GameServerConnectionState.Verifying);
			return true;
		}

		public bool EnterGame(ulong playerIndex = 0)
		{
			return Send(new Protocol.C_ENTER_GAME { PlayerIndex = playerIndex });
		}

		public bool SendChat(string message)
		{
			return Send(new Protocol.C_CHAT { Msg = message ?? string.Empty });
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
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_LOGIN);
					break;
				case Protocol.C_ENTER_GAME pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_ENTER_GAME);
					break;
				case Protocol.C_LEAVE_GAME pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_LEAVE_GAME);
					break;
				case Protocol.C_MOVE pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_MOVE);
					break;
				case Protocol.C_CHAT pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_CHAT);
					break;
				default:
					LastError = $"Unsupported client packet type: {packet.GetType().Name}";
					return false;
			}

			_session.Send(sendBuffer);
			return true;
		}

		ArraySegment<byte> MakeSendBuffer(IMessage packet, MsgId msgId)
		{
			byte[] payload = packet.ToByteArray();
			ushort packetSize = checked((ushort)(payload.Length + PacketSession.HeaderSize));
			byte[] sendBuffer = new byte[packetSize];

			Array.Copy(BitConverter.GetBytes(packetSize), 0, sendBuffer, 0, sizeof(ushort));
			Array.Copy(BitConverter.GetBytes((ushort)msgId), 0, sendBuffer, sizeof(ushort), sizeof(ushort));
			Array.Copy(payload, 0, sendBuffer, PacketSession.HeaderSize, payload.Length);

			return new ArraySegment<byte>(sendBuffer);
		}

		GameServerSession CreateSession()
		{
			GameServerSession createdSession = new GameServerSession();
			_session = createdSession;

			createdSession.Connected += endPoint => EnqueueMainThread(() =>
			{
				if (ReferenceEquals(_session, createdSession) == false)
					return;

				LastError = null;
				Debug.Log($"Game server socket connected: {endPoint}");
			});

			createdSession.Disconnected += _ => EnqueueMainThread(() =>
			{
				if (ReferenceEquals(_session, createdSession) == false)
					return;

				_session = null;
				SetState(GameServerConnectionState.Disconnected);
			});

			return createdSession;
		}

		void OnLoginReceived(Protocol.S_LOGIN pkt)
		{
			LastLogin = pkt;
			SetState(pkt.Success ? GameServerConnectionState.Verified : GameServerConnectionState.Failed);

			if (pkt.Success == false)
				LastError = "Server login verification failed.";

			Debug.Log($"Game server verification: success={pkt.Success}");
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

				try
				{
					job.Invoke();
				}
				catch (Exception ex)
				{
					Debug.LogException(ex);
				}
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
