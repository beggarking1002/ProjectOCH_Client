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
	public event Action<S_ENTER_BATTLE> EnterBattleReceived;
	public event Action<S_BATTLE_MOVE> BattleMoveReceived;
	public event Action<S_BATTLE_SKILL> BattleSkillReceived;
	public event Action<S_BATTLE_END_TURN> BattleEndTurnReceived;
	public event Action<S_BATTLE_INVITE_REQUEST> BattleInviteRequestReceived;
	public event Action<S_BATTLE_INVITE_RECEIVED> BattleInviteReceived;
	public event Action<S_BATTLE_INVITE_RESULT> BattleInviteResultReceived;
	public event Action<S_BATTLE_CLASS_SELECTION_START> BattleClassSelectionStartReceived;
	public event Action<S_BATTLE_CLASS_SELECTION_RESULT> BattleClassSelectionResultReceived;
	public event Action<S_BATTLE_PAWN_DEAD> BattlePawnDeadReceived;
	public event Action<S_BATTLE_RESULT> BattleResultReceived;
	public event Action<S_BATTLE_RESULT_ACK> BattleResultAckReceived;

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

	public static void S_ENTER_BATTLEHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_ENTER_BATTLEHandler");
		Instance.EnqueuePacket(packet as S_ENTER_BATTLE, Instance.EnterBattleReceived);
	}

	public static void S_BATTLE_MOVEHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_BATTLE_MOVEHandler");
		Instance.EnqueuePacket(packet as S_BATTLE_MOVE, Instance.BattleMoveReceived);
	}

	public static void S_BATTLE_SKILLHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_BATTLE_SKILLHandler");
		Instance.EnqueuePacket(packet as S_BATTLE_SKILL, Instance.BattleSkillReceived);
	}

	public static void S_BATTLE_END_TURNHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_BATTLE_END_TURNHandler");
		Instance.EnqueuePacket(packet as S_BATTLE_END_TURN, Instance.BattleEndTurnReceived);
	}

	public static void S_BATTLE_INVITE_REQUESTHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_BATTLE_INVITE_REQUESTHandler");
		Instance.EnqueuePacket(packet as S_BATTLE_INVITE_REQUEST, Instance.BattleInviteRequestReceived);
	}

	public static void S_BATTLE_INVITE_RECEIVEDHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_BATTLE_INVITE_RECEIVEDHandler");
		Instance.EnqueuePacket(packet as S_BATTLE_INVITE_RECEIVED, Instance.BattleInviteReceived);
	}

	public static void S_BATTLE_INVITE_RESULTHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_BATTLE_INVITE_RESULTHandler");
		Instance.EnqueuePacket(packet as S_BATTLE_INVITE_RESULT, Instance.BattleInviteResultReceived);
	}

	public static void S_BATTLE_CLASS_SELECTION_STARTHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_BATTLE_CLASS_SELECTION_STARTHandler");
		Instance.EnqueuePacket(packet as S_BATTLE_CLASS_SELECTION_START, Instance.BattleClassSelectionStartReceived);
	}

	public static void S_BATTLE_CLASS_SELECTION_RESULTHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_BATTLE_CLASS_SELECTION_RESULTHandler");
		Instance.EnqueuePacket(packet as S_BATTLE_CLASS_SELECTION_RESULT, Instance.BattleClassSelectionResultReceived);
	}

	public static void S_BATTLE_PAWN_DEADHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_BATTLE_PAWN_DEADHandler");
		Instance.EnqueuePacket(packet as S_BATTLE_PAWN_DEAD, Instance.BattlePawnDeadReceived);
	}

	public static void S_BATTLE_RESULTHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_BATTLE_RESULTHandler");
		Instance.EnqueuePacket(packet as S_BATTLE_RESULT, Instance.BattleResultReceived);
	}

	public static void S_BATTLE_RESULT_ACKHandler(PacketSession session, IMessage packet)
	{
		Debug.Log("S_BATTLE_RESULT_ACKHandler");
		Instance.EnqueuePacket(packet as S_BATTLE_RESULT_ACK, Instance.BattleResultAckReceived);
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
}
