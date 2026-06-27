using System;
using System.Collections.Generic;
using Google.Protobuf;
using ServerCore;
using UnityEngine;

	public enum PacketId : ushort
	{
		C_LOGIN = 1000,
		S_LOGIN = 1001,
		C_ENTER_GAME = 1002,
		S_ENTER_GAME = 1003,
		C_LEAVE_GAME = 1004,
		S_LEAVE_GAME = 1005,
		S_SPAWN = 1006,
		S_DESPAWN = 1007,
		C_MOVE = 1008,
		S_MOVE = 1009,
		C_CHAT = 1010,
		S_CHAT = 1011,
	}

	public sealed class PacketHandler
	{
		public static PacketHandler Instance { get; } = new PacketHandler();

		readonly Dictionary<ushort, Action<PacketSession, ArraySegment<byte>>> _handlers =
			new Dictionary<ushort, Action<PacketSession, ArraySegment<byte>>>();
		readonly Queue<Action> _mainThreadJobs = new Queue<Action>();
		readonly object _lock = new object();

		bool _initialized;

		public event Action<Protocol.S_LOGIN> LoginReceived;
		public event Action<Protocol.S_ENTER_GAME> EnterGameReceived;
		public event Action<Protocol.S_LEAVE_GAME> LeaveGameReceived;
		public event Action<Protocol.S_SPAWN> SpawnReceived;
		public event Action<Protocol.S_DESPAWN> DespawnReceived;
		public event Action<Protocol.S_MOVE> MoveReceived;
		public event Action<Protocol.S_CHAT> ChatReceived;

		public static void S_LOGINHandler(PacketSession session, IMessage packet)
		{
			Instance.EnqueueParsedPacket(session, packet as Protocol.S_LOGIN, Instance.LoginReceived);
		}

		public static void S_ENTER_GAMEHandler(PacketSession session, IMessage packet)
		{
			Instance.EnqueueParsedPacket(session, packet as Protocol.S_ENTER_GAME, Instance.EnterGameReceived);
		}

		public static void S_LEAVE_GAMEHandler(PacketSession session, IMessage packet)
		{
			Instance.EnqueueParsedPacket(session, packet as Protocol.S_LEAVE_GAME, Instance.LeaveGameReceived);
		}

		public static void S_SPAWNHandler(PacketSession session, IMessage packet)
		{
			Instance.EnqueueParsedPacket(session, packet as Protocol.S_SPAWN, Instance.SpawnReceived);
		}

		public static void S_DESPAWNHandler(PacketSession session, IMessage packet)
		{
			Instance.EnqueueParsedPacket(session, packet as Protocol.S_DESPAWN, Instance.DespawnReceived);
		}

		public static void S_MOVEHandler(PacketSession session, IMessage packet)
		{
			Instance.EnqueueParsedPacket(session, packet as Protocol.S_MOVE, Instance.MoveReceived);
		}

		public static void S_CHATHandler(PacketSession session, IMessage packet)
		{
			Instance.EnqueueParsedPacket(session, packet as Protocol.S_CHAT, Instance.ChatReceived);
		}

		public void Init()
		{
			if (_initialized)
				return;

			Register(PacketId.S_LOGIN, Protocol.S_LOGIN.Parser, (_, pkt) => LoginReceived?.Invoke(pkt));
			Register(PacketId.S_ENTER_GAME, Protocol.S_ENTER_GAME.Parser, (_, pkt) => EnterGameReceived?.Invoke(pkt));
			Register(PacketId.S_LEAVE_GAME, Protocol.S_LEAVE_GAME.Parser, (_, pkt) => LeaveGameReceived?.Invoke(pkt));
			Register(PacketId.S_SPAWN, Protocol.S_SPAWN.Parser, (_, pkt) => SpawnReceived?.Invoke(pkt));
			Register(PacketId.S_DESPAWN, Protocol.S_DESPAWN.Parser, (_, pkt) => DespawnReceived?.Invoke(pkt));
			Register(PacketId.S_MOVE, Protocol.S_MOVE.Parser, (_, pkt) => MoveReceived?.Invoke(pkt));
			Register(PacketId.S_CHAT, Protocol.S_CHAT.Parser, (_, pkt) => ChatReceived?.Invoke(pkt));

			_initialized = true;
		}

		public void HandlePacket(PacketSession session, ArraySegment<byte> buffer)
		{
			Init();

			if (buffer.Array == null || buffer.Count < PacketSession.HeaderSize)
				return;

			ushort packetId = BitConverter.ToUInt16(buffer.Array, buffer.Offset + sizeof(ushort));
			if (_handlers.TryGetValue(packetId, out Action<PacketSession, ArraySegment<byte>> handler) == false)
			{
				Debug.LogWarning($"Unhandled packet id: {packetId}");
				return;
			}

			handler.Invoke(session, buffer);
		}

		public void Flush()
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

		public ArraySegment<byte> MakeSendBuffer(Protocol.C_LOGIN pkt) => MakeSendBuffer(pkt, PacketId.C_LOGIN);
		public ArraySegment<byte> MakeSendBuffer(Protocol.C_ENTER_GAME pkt) => MakeSendBuffer(pkt, PacketId.C_ENTER_GAME);
		public ArraySegment<byte> MakeSendBuffer(Protocol.C_LEAVE_GAME pkt) => MakeSendBuffer(pkt, PacketId.C_LEAVE_GAME);
		public ArraySegment<byte> MakeSendBuffer(Protocol.C_MOVE pkt) => MakeSendBuffer(pkt, PacketId.C_MOVE);
		public ArraySegment<byte> MakeSendBuffer(Protocol.C_CHAT pkt) => MakeSendBuffer(pkt, PacketId.C_CHAT);

		void Register<T>(PacketId packetId, MessageParser<T> parser, Action<PacketSession, T> handler)
			where T : class, IMessage<T>
		{
			_handlers[(ushort)packetId] = (session, buffer) =>
			{
				int payloadOffset = buffer.Offset + PacketSession.HeaderSize;
				int payloadSize = buffer.Count - PacketSession.HeaderSize;
				T packet = parser.ParseFrom(buffer.Array, payloadOffset, payloadSize);

				EnqueueParsedPacket(session, packet, handler);
			};
		}

		void EnqueueParsedPacket<T>(PacketSession session, T packet, Action<T> handler)
			where T : class, IMessage
		{
			if (packet == null)
			{
				Debug.LogWarning($"Received unexpected packet type for {typeof(T).Name}.");
				return;
			}

			lock (_lock)
			{
				_mainThreadJobs.Enqueue(() => handler?.Invoke(packet));
			}
		}

		void EnqueueParsedPacket<T>(PacketSession session, T packet, Action<PacketSession, T> handler)
			where T : class, IMessage
		{
			if (packet == null)
			{
				Debug.LogWarning($"Received unexpected packet type for {typeof(T).Name}.");
				return;
			}

			lock (_lock)
			{
				_mainThreadJobs.Enqueue(() => handler?.Invoke(session, packet));
			}
		}

		ArraySegment<byte> MakeSendBuffer(IMessage pkt, PacketId packetId)
		{
			byte[] payload = pkt.ToByteArray();
			ushort packetSize = checked((ushort)(payload.Length + PacketSession.HeaderSize));
			byte[] sendBuffer = new byte[packetSize];

			Array.Copy(BitConverter.GetBytes(packetSize), 0, sendBuffer, 0, sizeof(ushort));
			Array.Copy(BitConverter.GetBytes((ushort)packetId), 0, sendBuffer, sizeof(ushort), sizeof(ushort));
			Array.Copy(payload, 0, sendBuffer, PacketSession.HeaderSize, payload.Length);

			return new ArraySegment<byte>(sendBuffer);
		}
	}
