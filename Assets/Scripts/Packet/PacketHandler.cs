using System;
using System.Collections.Generic;
using Google.Protobuf;
using Protocol;
using ServerCore;
using UnityEngine;

public sealed class PacketHandler
{
	public static PacketHandler Instance { get; } = new PacketHandler();

	readonly Queue<Action> _mainThreadJobs = new Queue<Action>();
	readonly object _lock = new object();

	public event Action<S_LOGIN> LoginReceived;
	public event Action<S_ENTER_GAME> EnterGameReceived;
	public event Action<S_LEAVE_GAME> LeaveGameReceived;
	public event Action<S_SPAWN> SpawnReceived;
	public event Action<S_DESPAWN> DespawnReceived;
	public event Action<S_MOVE> MoveReceived;
	public event Action<S_CHAT> ChatReceived;

	public static void S_LOGINHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_LOGINHandler");
		Instance.EnqueuePacket(packet as S_LOGIN, Instance.LoginReceived);
	}

	public static void S_ENTER_GAMEHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_ENTER_GAMEHandler");
		Instance.EnqueuePacket(packet as S_ENTER_GAME, Instance.EnterGameReceived);
	}

	public static void S_LEAVE_GAMEHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_LEAVE_GAMEHandler");
		Instance.EnqueuePacket(packet as S_LEAVE_GAME, Instance.LeaveGameReceived);
	}

	public static void S_SPAWNHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_SPAWNHandler");
		Instance.EnqueuePacket(packet as S_SPAWN, Instance.SpawnReceived);
	}

	public static void S_DESPAWNHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_DESPAWNHandler");
		Instance.EnqueuePacket(packet as S_DESPAWN, Instance.DespawnReceived);
	}

	public static void S_MOVEHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_MOVEHandler");
		Instance.EnqueuePacket(packet as S_MOVE, Instance.MoveReceived);
	}

	public static void S_CHATHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_CHATHandler");
		Instance.EnqueuePacket(packet as S_CHAT, Instance.ChatReceived);
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

	public ArraySegment<byte> MakeSendBuffer(C_LOGIN pkt) => MakeSendBuffer(pkt, MsgId.C_LOGIN);
	public ArraySegment<byte> MakeSendBuffer(C_ENTER_GAME pkt) => MakeSendBuffer(pkt, MsgId.C_ENTER_GAME);
	public ArraySegment<byte> MakeSendBuffer(C_LEAVE_GAME pkt) => MakeSendBuffer(pkt, MsgId.C_LEAVE_GAME);
	public ArraySegment<byte> MakeSendBuffer(C_MOVE pkt) => MakeSendBuffer(pkt, MsgId.C_MOVE);
	public ArraySegment<byte> MakeSendBuffer(C_CHAT pkt) => MakeSendBuffer(pkt, MsgId.C_CHAT);

	void EnqueuePacket<T>(T packet, Action<T> handler)
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

	ArraySegment<byte> MakeSendBuffer(IMessage pkt, MsgId msgId)
	{
		byte[] payload = pkt.ToByteArray();
		ushort packetSize = checked((ushort)(payload.Length + PacketSession.HeaderSize));
		byte[] sendBuffer = new byte[packetSize];

		Array.Copy(BitConverter.GetBytes(packetSize), 0, sendBuffer, 0, sizeof(ushort));
		Array.Copy(BitConverter.GetBytes((ushort)msgId), 0, sendBuffer, sizeof(ushort), sizeof(ushort));
		Array.Copy(payload, 0, sendBuffer, PacketSession.HeaderSize, payload.Length);

		return new ArraySegment<byte>(sendBuffer);
	}
}
