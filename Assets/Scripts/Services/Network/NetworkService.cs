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
		readonly Dictionary<ulong, Protocol.ObjectInfo> _knownPlayers = new Dictionary<ulong, Protocol.ObjectInfo>();
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
		public Protocol.S_ENTER_GAME LastEnterGame { get; private set; }
		public Protocol.S_ENTER_BATTLE LastEnterBattle { get; private set; }
		public Protocol.S_ENTER_VILLAGE LastEnterVillage { get; private set; }
		public Protocol.S_EXPEDITION_STATE LastExpeditionState { get; private set; }
		public Protocol.S_VILLAGE_SHOP_STATE LastVillageShopState { get; private set; }
		public Protocol.S_RESET_PLAYER_DATA LastPlayerDataReset { get; private set; }
		public Protocol.S_VILLAGE_QUEST_STATE LastVillageQuestState { get; private set; }
		public Protocol.S_QUEST_TRACKER_STATE LastQuestTrackerState { get; private set; }
		public Protocol.S_FIELD_PAWN_SELECT LastFieldPawnSelection { get; private set; }
		public Protocol.S_BATTLE_INVITE_REQUEST LastBattleInviteRequest { get; private set; }
		public Protocol.S_BATTLE_INVITE_RECEIVED LastBattleInviteReceived { get; private set; }
		public Protocol.S_BATTLE_INVITE_RESULT LastBattleInviteResult { get; private set; }
		public Protocol.S_BATTLE_CLASS_SELECTION_START LastBattleClassSelectionStart { get; private set; }
		public Protocol.S_BATTLE_CLASS_SELECTION_RESULT LastBattleClassSelectionResult { get; private set; }
		public Protocol.S_BATTLE_END_TURN LastBattleEndTurn { get; private set; }
		public Protocol.S_BATTLE_PAWN_DEAD LastBattlePawnDead { get; private set; }
		public Protocol.S_BATTLE_RESULT LastBattleResult { get; private set; }
		public Protocol.S_BATTLE_RESULT_ACK LastBattleResultAck { get; private set; }

		public bool IsConnected =>
			_session != null &&
			_session.IsConnected() &&
			(State == GameServerConnectionState.Connected ||
			 State == GameServerConnectionState.Verifying ||
			 State == GameServerConnectionState.Verified);

		public bool IsVerified => State == GameServerConnectionState.Verified;

		public event Action<GameServerConnectionState> StateChanged;
		public event Action<Protocol.S_LOGIN> LoginReceived;
		public event Action<Protocol.S_ENTER_GAME> EnterGameReceived;
		public event Action<Protocol.S_SPAWN> SpawnReceived;
		public event Action<Protocol.S_DESPAWN> DespawnReceived;
		public event Action<Protocol.S_MOVE> MoveReceived;
		public event Action<Protocol.S_ENTER_BATTLE> EnterBattleReceived;
		public event Action<Protocol.S_ENTER_VILLAGE> EnterVillageReceived;
		public event Action<Protocol.S_EXPEDITION_STATE> ExpeditionStateReceived;
		public event Action<Protocol.S_VILLAGE_SHOP_STATE> VillageShopStateReceived;
		public event Action<Protocol.S_RESET_PLAYER_DATA> PlayerDataResetReceived;
		public event Action<Protocol.S_VILLAGE_QUEST_STATE> VillageQuestStateReceived;
		public event Action<Protocol.S_QUEST_TRACKER_STATE> QuestTrackerStateReceived;
		public event Action<Protocol.S_FIELD_PAWN_SELECT> FieldPawnSelectionReceived;
		public event Action<Protocol.S_BATTLE_MOVE> BattleMoveReceived;
		public event Action<Protocol.S_BATTLE_SKILL> BattleSkillReceived;
		public event Action<Protocol.S_BATTLE_END_TURN> BattleEndTurnReceived;
		public event Action<Protocol.S_BATTLE_INVITE_REQUEST> BattleInviteRequestReceived;
		public event Action<Protocol.S_BATTLE_INVITE_RECEIVED> BattleInviteReceived;
		public event Action<Protocol.S_BATTLE_INVITE_RESULT> BattleInviteResultReceived;
		public event Action<Protocol.S_BATTLE_CLASS_SELECTION_START> BattleClassSelectionStartReceived;
		public event Action<Protocol.S_BATTLE_CLASS_SELECTION_RESULT> BattleClassSelectionResultReceived;
		public event Action<Protocol.S_BATTLE_PAWN_DEAD> BattlePawnDeadReceived;
		public event Action<Protocol.S_BATTLE_RESULT> BattleResultReceived;
		public event Action<Protocol.S_BATTLE_RESULT_ACK> BattleResultAckReceived;

		public void Initialize(string host, int port, bool verifyWithLoginPacket)
		{
			Configure(host, port, verifyWithLoginPacket);

			if (_initialized)
				return;

			PacketHandler.Instance.LoginReceived += OnLoginReceived;
			PacketHandler.Instance.EnterGameReceived += OnEnterGameReceived;
			PacketHandler.Instance.SpawnReceived += OnSpawnReceived;
			PacketHandler.Instance.DespawnReceived += OnDespawnReceived;
			PacketHandler.Instance.MoveReceived += OnMoveReceived;
			PacketHandler.Instance.EnterBattleReceived += OnEnterBattleReceived;
			PacketHandler.Instance.EnterVillageReceived += OnEnterVillageReceived;
			PacketHandler.Instance.ExpeditionStateReceived += OnExpeditionStateReceived;
			PacketHandler.Instance.VillageShopStateReceived += OnVillageShopStateReceived;
			PacketHandler.Instance.PlayerDataResetReceived += OnPlayerDataResetReceived;
			PacketHandler.Instance.VillageQuestStateReceived += OnVillageQuestStateReceived;
			PacketHandler.Instance.QuestTrackerStateReceived += OnQuestTrackerStateReceived;
			PacketHandler.Instance.FieldPawnSelectionReceived += OnFieldPawnSelectionReceived;
			PacketHandler.Instance.BattleMoveReceived += OnBattleMoveReceived;
			PacketHandler.Instance.BattleSkillReceived += OnBattleSkillReceived;
			PacketHandler.Instance.BattleEndTurnReceived += OnBattleEndTurnReceived;
			PacketHandler.Instance.BattleInviteRequestReceived += OnBattleInviteRequestReceived;
			PacketHandler.Instance.BattleInviteReceived += OnBattleInviteReceived;
			PacketHandler.Instance.BattleInviteResultReceived += OnBattleInviteResultReceived;
			PacketHandler.Instance.BattleClassSelectionStartReceived += OnBattleClassSelectionStartReceived;
			PacketHandler.Instance.BattleClassSelectionResultReceived += OnBattleClassSelectionResultReceived;
			PacketHandler.Instance.BattlePawnDeadReceived += OnBattlePawnDeadReceived;
			PacketHandler.Instance.BattleResultReceived += OnBattleResultReceived;
			PacketHandler.Instance.BattleResultAckReceived += OnBattleResultAckReceived;
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
			{
				PacketHandler.Instance.LoginReceived -= OnLoginReceived;
				PacketHandler.Instance.EnterGameReceived -= OnEnterGameReceived;
				PacketHandler.Instance.SpawnReceived -= OnSpawnReceived;
				PacketHandler.Instance.DespawnReceived -= OnDespawnReceived;
				PacketHandler.Instance.MoveReceived -= OnMoveReceived;
				PacketHandler.Instance.EnterBattleReceived -= OnEnterBattleReceived;
				PacketHandler.Instance.EnterVillageReceived -= OnEnterVillageReceived;
				PacketHandler.Instance.ExpeditionStateReceived -= OnExpeditionStateReceived;
				PacketHandler.Instance.VillageShopStateReceived -= OnVillageShopStateReceived;
				PacketHandler.Instance.PlayerDataResetReceived -= OnPlayerDataResetReceived;
				PacketHandler.Instance.VillageQuestStateReceived -= OnVillageQuestStateReceived;
				PacketHandler.Instance.QuestTrackerStateReceived -= OnQuestTrackerStateReceived;
				PacketHandler.Instance.FieldPawnSelectionReceived -= OnFieldPawnSelectionReceived;
				PacketHandler.Instance.BattleMoveReceived -= OnBattleMoveReceived;
				PacketHandler.Instance.BattleSkillReceived -= OnBattleSkillReceived;
				PacketHandler.Instance.BattleEndTurnReceived -= OnBattleEndTurnReceived;
				PacketHandler.Instance.BattleInviteRequestReceived -= OnBattleInviteRequestReceived;
				PacketHandler.Instance.BattleInviteReceived -= OnBattleInviteReceived;
				PacketHandler.Instance.BattleInviteResultReceived -= OnBattleInviteResultReceived;
				PacketHandler.Instance.BattleClassSelectionStartReceived -= OnBattleClassSelectionStartReceived;
				PacketHandler.Instance.BattleClassSelectionResultReceived -= OnBattleClassSelectionResultReceived;
				PacketHandler.Instance.BattlePawnDeadReceived -= OnBattlePawnDeadReceived;
				PacketHandler.Instance.BattleResultReceived -= OnBattleResultReceived;
				PacketHandler.Instance.BattleResultAckReceived -= OnBattleResultAckReceived;
			}

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
			LastEnterGame = null;
			LastEnterBattle = null;
			LastEnterVillage = null;
			LastExpeditionState = null;
			LastVillageShopState = null;
			LastPlayerDataReset = null;
			LastVillageQuestState = null;
			LastQuestTrackerState = null;
			LastFieldPawnSelection = null;
			LastBattleInviteRequest = null;
			LastBattleInviteReceived = null;
			LastBattleInviteResult = null;
			LastBattleClassSelectionStart = null;
			LastBattleClassSelectionResult = null;
			LastBattleEndTurn = null;
			LastBattlePawnDead = null;
			LastBattleResult = null;
			LastBattleResultAck = null;
			_knownPlayers.Clear();
			SetState(GameServerConnectionState.Connecting);

			_connector = new Connector
			{
				OnSuccessCallback = () => EnqueueMainThread(() =>
				{
					SetState(GameServerConnectionState.Connected);
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

		public bool SendLogin(string authorizationCode = null, string codeVerifier = null, string redirectUri = null)
		{
			if (Send(new Protocol.C_LOGIN
			{
				GoogleAuthorizationCode = authorizationCode ?? string.Empty,
				GoogleCodeVerifier = codeVerifier ?? string.Empty,
				GoogleRedirectUri = redirectUri ?? string.Empty,
			}) == false)
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

		public bool EnterBattle()
		{
			return Send(new Protocol.C_ENTER_BATTLE());
		}

		public bool EnterVillage(string mapId, int cellX, int cellY)
		{
			if (string.IsNullOrWhiteSpace(mapId))
			{
				LastError = "Map id is required to enter a village.";
				return false;
			}

			return Send(new Protocol.C_ENTER_VILLAGE
			{
				MapId = mapId,
				CellX = cellX,
				CellY = cellY,
			});
		}

		public bool OpenVillageShop(string villageId)
		{
			return Send(new Protocol.C_VILLAGE_SHOP_OPEN { VillageId = villageId ?? string.Empty });
		}

		public bool BuyVillageItem(string villageId, string itemId, int quantity = 1)
		{
			return Send(new Protocol.C_VILLAGE_SHOP_BUY
			{
				VillageId = villageId ?? string.Empty,
				ItemId = itemId ?? string.Empty,
				Quantity = quantity,
			});
		}

		public bool SellVillageItem(string villageId, ulong stackId, int quantity = 1)
		{
			return Send(new Protocol.C_VILLAGE_SHOP_SELL
			{
				VillageId = villageId ?? string.Empty,
				StackId = stackId,
				Quantity = quantity,
			});
		}

		public bool ResetPlayerData()
		{
			return Send(new Protocol.C_RESET_PLAYER_DATA { Confirmation = "RESET" });
		}

		public bool OpenVillageQuestBoard(string villageId)
		{
			return Send(new Protocol.C_VILLAGE_QUEST_BOARD_OPEN { VillageId = villageId ?? string.Empty });
		}

		public bool OpenQuestTracker()
		{
			return Send(new Protocol.C_QUEST_TRACKER_OPEN());
		}

		public bool AbandonQuest(string questId)
		{
			return Send(new Protocol.C_QUEST_ABANDON { QuestId = questId ?? string.Empty });
		}

		public bool SelectFieldPawn(Protocol.PawnClass pawnClass)
		{
			return Send(new Protocol.C_FIELD_PAWN_SELECT { PawnClass = pawnClass });
		}

		public bool AcceptQuest(string questId)
		{
			return Send(new Protocol.C_QUEST_ACCEPT { QuestId = questId ?? string.Empty });
		}

		public bool ClaimQuestReward(string questId)
		{
			return Send(new Protocol.C_QUEST_CLAIM_REWARD { QuestId = questId ?? string.Empty });
		}

		public bool SendBattleInvite(ulong targetPlayerId)
		{
			return Send(new Protocol.C_BATTLE_INVITE
			{
				TargetPlayerId = targetPlayerId,
			});
		}

		public bool SendBattleInviteResponse(ulong requesterPlayerId, bool accept)
		{
			return Send(new Protocol.C_BATTLE_INVITE_RESPONSE
			{
				RequesterPlayerId = requesterPlayerId,
				Accept = accept,
			});
		}

		public bool SendBattleClassSelection(IList<Protocol.PawnClass> selectedPawnClasses)
		{
			if (selectedPawnClasses == null || selectedPawnClasses.Count != 4)
			{
				LastError = "Exactly four pawn classes must be selected.";
				return false;
			}

			Protocol.C_BATTLE_CLASS_SELECTION packet = new Protocol.C_BATTLE_CLASS_SELECTION();
			packet.SelectedPawnClasses.Add(selectedPawnClasses);
			return Send(packet);
		}

		public bool SendBattleMove(ulong battleId, ulong pawnId, int q, int r)
		{
			return Send(new Protocol.C_BATTLE_MOVE
			{
				BattleId = battleId,
				PawnId = pawnId,
				Target = new Protocol.AxialCoord { Q = q, R = r },
			});
		}

		public bool SendBattleSkill(ulong battleId, ulong casterPawnId, int skillSlot, int q, int r, int? lineDirectionQ = null, int? lineDirectionR = null, bool requestOptionalPositionSwap = false)
		{
			Protocol.C_BATTLE_SKILL packet = new Protocol.C_BATTLE_SKILL
			{
				BattleId = battleId,
				CasterPawnId = casterPawnId,
				SkillSlot = skillSlot,
				// The server resolves any Pawn from TargetAxial. Keep the legacy field at zero.
				TargetPawnId = 0,
				TargetAxial = new Protocol.AxialCoord { Q = q, R = r },
			};

			// Fire Wall is a two-stage skill. Its second selected tile supplies the
			// adjacent line direction while TargetAxial remains the starting tile.
			if (lineDirectionQ.HasValue && lineDirectionR.HasValue)
			{
				packet.LineDirectionAxial = new Protocol.AxialCoord
				{
					Q = lineDirectionQ.Value,
					R = lineDirectionR.Value,
				};
			}

			if (requestOptionalPositionSwap)
				packet.RequestOptionalPositionSwap = true;

			return Send(packet);
		}

		public bool SendBattleEndTurn(ulong battleId, ulong pawnId)
		{
			return Send(new Protocol.C_BATTLE_END_TURN
			{
				BattleId = battleId,
				PawnId = pawnId,
			});
		}

		public bool SendBattleResultAck(ulong battleId)
		{
			return Send(new Protocol.C_BATTLE_RESULT_ACK
			{
				BattleId = battleId,
			});
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
				case Protocol.C_ENTER_BATTLE pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_ENTER_BATTLE);
					break;
				case Protocol.C_ENTER_VILLAGE pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_ENTER_VILLAGE);
					break;
				case Protocol.C_VILLAGE_SHOP_OPEN pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_VILLAGE_SHOP_OPEN);
					break;
				case Protocol.C_VILLAGE_SHOP_BUY pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_VILLAGE_SHOP_BUY);
					break;
				case Protocol.C_VILLAGE_SHOP_SELL pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_VILLAGE_SHOP_SELL);
					break;
				case Protocol.C_RESET_PLAYER_DATA pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_RESET_PLAYER_DATA);
					break;
				case Protocol.C_VILLAGE_QUEST_BOARD_OPEN pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_VILLAGE_QUEST_BOARD_OPEN);
					break;
				case Protocol.C_QUEST_TRACKER_OPEN pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_QUEST_TRACKER_OPEN);
					break;
				case Protocol.C_QUEST_ABANDON pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_QUEST_ABANDON);
					break;
				case Protocol.C_FIELD_PAWN_SELECT pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_FIELD_PAWN_SELECT);
					break;
				case Protocol.C_QUEST_ACCEPT pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_QUEST_ACCEPT);
					break;
				case Protocol.C_QUEST_CLAIM_REWARD pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_QUEST_CLAIM_REWARD);
					break;
				case Protocol.C_BATTLE_MOVE pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_BATTLE_MOVE);
					break;
				case Protocol.C_BATTLE_SKILL pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_BATTLE_SKILL);
					break;
				case Protocol.C_BATTLE_END_TURN pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_BATTLE_END_TURN);
					break;
				case Protocol.C_BATTLE_INVITE pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_BATTLE_INVITE);
					break;
				case Protocol.C_BATTLE_INVITE_RESPONSE pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_BATTLE_INVITE_RESPONSE);
					break;
				case Protocol.C_BATTLE_CLASS_SELECTION pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_BATTLE_CLASS_SELECTION);
					break;
				case Protocol.C_BATTLE_RESULT_ACK pkt:
					sendBuffer = MakeSendBuffer(pkt, MsgId.C_BATTLE_RESULT_ACK);
					break;
				default:
					LastError = $"Unsupported client packet type: {packet.GetType().Name}";
					return false;
			}

			_session.Send(sendBuffer);
			return true;
		}

		public List<Protocol.ObjectInfo> GetKnownPlayersSnapshot()
		{
			List<Protocol.ObjectInfo> snapshot = new List<Protocol.ObjectInfo>(_knownPlayers.Count);
			foreach (Protocol.ObjectInfo player in _knownPlayers.Values)
				snapshot.Add(player.Clone());

			return snapshot;
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
			// Keep the live socket reusable after an OAuth cancellation or rejection.
			SetState(pkt.Success ? GameServerConnectionState.Verified : GameServerConnectionState.Connected);

			if (pkt.Success == false)
				LastError = string.IsNullOrWhiteSpace(pkt.Reason) ? "Server login verification failed." : pkt.Reason;

			Debug.Log($"Game server verification: success={pkt.Success} accountId={pkt.AccountId} reason={pkt.Reason}");
			LoginReceived?.Invoke(pkt);
		}

		void OnEnterGameReceived(Protocol.S_ENTER_GAME pkt)
		{
			LastEnterGame = pkt;
			if (pkt.Success && pkt.Player != null && pkt.Player.ObjectId != 0)
				_knownPlayers[pkt.Player.ObjectId] = pkt.Player.Clone();

			EnterGameReceived?.Invoke(pkt);
		}

		void OnSpawnReceived(Protocol.S_SPAWN pkt)
		{
			if (pkt != null)
			{
				foreach (Protocol.ObjectInfo player in pkt.Players)
				{
					if (player != null && player.ObjectId != 0)
						_knownPlayers[player.ObjectId] = player.Clone();
				}
			}

			SpawnReceived?.Invoke(pkt);
		}

		void OnDespawnReceived(Protocol.S_DESPAWN pkt)
		{
			if (pkt != null)
			{
				foreach (ulong objectId in pkt.ObjectIds)
					_knownPlayers.Remove(objectId);
			}

			DespawnReceived?.Invoke(pkt);
		}

		void OnMoveReceived(Protocol.S_MOVE pkt)
		{
			if (pkt != null && pkt.ObjectId != 0 && pkt.Target != null)
			{
				if (_knownPlayers.TryGetValue(pkt.ObjectId, out Protocol.ObjectInfo player) == false)
				{
					player = new Protocol.ObjectInfo { ObjectId = pkt.ObjectId };
					_knownPlayers[pkt.ObjectId] = player;
				}

				player.Position = pkt.Target.Clone();
			}

			MoveReceived?.Invoke(pkt);
		}

		void OnEnterBattleReceived(Protocol.S_ENTER_BATTLE pkt)
		{
			LastEnterBattle = pkt;
			if (pkt == null)
				return;

			if (pkt.Success == false)
			{
				LastError = string.IsNullOrWhiteSpace(pkt.Reason) ? "Server rejected battle entry." : pkt.Reason;
				Debug.LogWarning($"S_ENTER_BATTLE failed. reason={LastError}");
			}
			else
			{
				Debug.Log($"S_ENTER_BATTLE success. battleId={pkt.BattleId}, mapId={pkt.MapId}, allied={pkt.AlliedPawns.Count}, enemy={pkt.EnemyPawns.Count}, currentTurnPawnId={pkt.CurrentTurnPawnId}");
			}

			EnterBattleReceived?.Invoke(pkt);
		}

		void OnFieldPawnSelectionReceived(Protocol.S_FIELD_PAWN_SELECT pkt)
		{
			LastFieldPawnSelection = pkt?.Clone();
			if (pkt == null)
				return;

			if (pkt.Success && pkt.ObjectId != 0)
			{
				if (_knownPlayers.TryGetValue(pkt.ObjectId, out Protocol.ObjectInfo player) == false)
				{
					player = new Protocol.ObjectInfo { ObjectId = pkt.ObjectId };
					_knownPlayers[pkt.ObjectId] = player;
				}
				player.FieldPawnClass = pkt.PawnClass;
				if (LastEnterGame?.Player?.ObjectId == pkt.ObjectId)
					LastEnterGame.Player.FieldPawnClass = pkt.PawnClass;
				LastError = null;
				Debug.Log($"S_FIELD_PAWN_SELECT success. objectId={pkt.ObjectId}, pawnClass={pkt.PawnClass}");
			}
			else
			{
				LastError = string.IsNullOrWhiteSpace(pkt.Reason) ? "Field pawn selection was rejected." : pkt.Reason;
				Debug.LogWarning($"S_FIELD_PAWN_SELECT failed. reason={LastError}");
			}

			FieldPawnSelectionReceived?.Invoke(pkt);
		}

		void OnEnterVillageReceived(Protocol.S_ENTER_VILLAGE pkt)
		{
			LastEnterVillage = pkt;
			if (pkt == null)
				return;

			if (pkt.Success == false)
			{
				LastError = string.IsNullOrWhiteSpace(pkt.Reason) ? "Server rejected village entry." : pkt.Reason;
				Debug.LogWarning($"S_ENTER_VILLAGE failed. reason={LastError}");
			}
			else
			{
				Debug.Log($"S_ENTER_VILLAGE success. villageId={pkt.VillageId}");
			}

			EnterVillageReceived?.Invoke(pkt);
		}

		void OnExpeditionStateReceived(Protocol.S_EXPEDITION_STATE pkt)
		{
			if (pkt == null)
				return;
			LastExpeditionState = pkt.Clone();
			ExpeditionStateReceived?.Invoke(pkt);
		}

		void OnVillageShopStateReceived(Protocol.S_VILLAGE_SHOP_STATE pkt)
		{
			if (pkt == null)
				return;
			LastVillageShopState = pkt.Clone();
			if (pkt.Expedition != null)
			{
				LastExpeditionState = pkt.Expedition.Clone();
				ExpeditionStateReceived?.Invoke(pkt.Expedition);
			}
			if (pkt.Success == false)
				LastError = string.IsNullOrWhiteSpace(pkt.Reason) ? "Village shop request failed." : pkt.Reason;
			else
				LastError = null;
			VillageShopStateReceived?.Invoke(pkt);
		}

		void OnPlayerDataResetReceived(Protocol.S_RESET_PLAYER_DATA pkt)
		{
			LastPlayerDataReset = pkt?.Clone();
			if (pkt != null && pkt.Success == false)
				LastError = string.IsNullOrWhiteSpace(pkt.Reason) ? "Player data reset failed." : pkt.Reason;
			else if (pkt != null && pkt.Success)
			{
				LastError = null;
				LastVillageQuestState = null;
			}
			PlayerDataResetReceived?.Invoke(pkt);
		}

		void OnVillageQuestStateReceived(Protocol.S_VILLAGE_QUEST_STATE pkt)
		{
			if (pkt == null)
				return;
			LastVillageQuestState = pkt.Clone();
			if (pkt.Expedition != null)
			{
				LastExpeditionState = pkt.Expedition.Clone();
				ExpeditionStateReceived?.Invoke(pkt.Expedition);
			}
			LastError = pkt.Success ? null : (string.IsNullOrWhiteSpace(pkt.Reason) ? "Village quest request failed." : pkt.Reason);
			VillageQuestStateReceived?.Invoke(pkt);
		}

		void OnQuestTrackerStateReceived(Protocol.S_QUEST_TRACKER_STATE pkt)
		{
			if (pkt == null)
				return;
			LastQuestTrackerState = pkt.Clone();
			if (pkt.Expedition != null)
			{
				LastExpeditionState = pkt.Expedition.Clone();
				ExpeditionStateReceived?.Invoke(pkt.Expedition);
			}
			LastError = pkt.Success ? null : (string.IsNullOrWhiteSpace(pkt.Reason) ? "Quest tracker request failed." : pkt.Reason);
			QuestTrackerStateReceived?.Invoke(pkt);
		}

		void OnBattleMoveReceived(Protocol.S_BATTLE_MOVE pkt)
		{
			if (pkt == null)
				return;

			if (pkt.Success == false)
				Debug.LogWarning($"S_BATTLE_MOVE failed. pawnId={pkt.PawnId}, result={pkt.Result}, reason={pkt.Reason}");

			BattleMoveReceived?.Invoke(pkt);
		}

		void OnBattleSkillReceived(Protocol.S_BATTLE_SKILL pkt)
		{
			if (pkt == null)
				return;

			if (pkt.Success == false)
				Debug.LogWarning($"S_BATTLE_SKILL failed. casterPawnId={pkt.CasterPawnId}, skillSlot={pkt.SkillSlot}, reason={pkt.Reason}");
			else
				Debug.Log($"S_BATTLE_SKILL success. casterPawnId={pkt.CasterPawnId}, skillSlot={pkt.SkillSlot}, targetPawnId={pkt.TargetPawnId}, damage={pkt.Damage}, targetHp={pkt.TargetHp}, nextTurnPawnId={pkt.NextTurnPawnId}");

			BattleSkillReceived?.Invoke(pkt);
		}

		void OnBattleEndTurnReceived(Protocol.S_BATTLE_END_TURN pkt)
		{
			LastBattleEndTurn = pkt;
			if (pkt == null)
				return;

			if (pkt.Success == false)
				Debug.LogWarning($"S_BATTLE_END_TURN failed. pawnId={pkt.PawnId}, reason={pkt.Reason}");
			else
				Debug.Log($"S_BATTLE_END_TURN success. pawnId={pkt.PawnId}, nextTurnPawnId={pkt.NextTurnPawnId}");

			BattleEndTurnReceived?.Invoke(pkt);
		}

		void OnBattleInviteRequestReceived(Protocol.S_BATTLE_INVITE_REQUEST pkt)
		{
			LastBattleInviteRequest = pkt;
			if (pkt == null)
				return;

			if (pkt.Success == false)
			{
				LastError = string.IsNullOrWhiteSpace(pkt.Reason) ? "Server rejected battle invite." : pkt.Reason;
				Debug.LogWarning($"S_BATTLE_INVITE_REQUEST failed. targetPlayerId={pkt.TargetPlayerId}, reason={LastError}");
			}
			else
			{
				Debug.Log($"S_BATTLE_INVITE_REQUEST success. requesterPlayerId={pkt.RequesterPlayerId}, targetPlayerId={pkt.TargetPlayerId}");
			}

			BattleInviteRequestReceived?.Invoke(pkt);
		}

		void OnBattleInviteReceived(Protocol.S_BATTLE_INVITE_RECEIVED pkt)
		{
			LastBattleInviteReceived = pkt;
			if (pkt == null)
				return;

			Debug.Log($"S_BATTLE_INVITE_RECEIVED. requesterPlayerId={pkt.RequesterPlayerId}");
			BattleInviteReceived?.Invoke(pkt);
		}

		void OnBattleInviteResultReceived(Protocol.S_BATTLE_INVITE_RESULT pkt)
		{
			LastBattleInviteResult = pkt;
			if (pkt == null)
				return;

			if (pkt.Accepted)
				Debug.Log($"S_BATTLE_INVITE_RESULT accepted. requesterPlayerId={pkt.RequesterPlayerId}, targetPlayerId={pkt.TargetPlayerId}");
			else
				Debug.Log($"S_BATTLE_INVITE_RESULT declined. requesterPlayerId={pkt.RequesterPlayerId}, targetPlayerId={pkt.TargetPlayerId}, reason={pkt.Reason}");

			BattleInviteResultReceived?.Invoke(pkt);
		}

		void OnBattleClassSelectionStartReceived(Protocol.S_BATTLE_CLASS_SELECTION_START pkt)
		{
			LastBattleClassSelectionStart = pkt;
			if (pkt == null)
				return;

			Debug.Log($"S_BATTLE_CLASS_SELECTION_START. requesterPlayerId={pkt.RequesterPlayerId}, targetPlayerId={pkt.TargetPlayerId}");
			BattleClassSelectionStartReceived?.Invoke(pkt);
		}

		void OnBattleClassSelectionResultReceived(Protocol.S_BATTLE_CLASS_SELECTION_RESULT pkt)
		{
			LastBattleClassSelectionResult = pkt;
			if (pkt == null)
				return;

			if (pkt.Success == false)
			{
				LastError = string.IsNullOrWhiteSpace(pkt.Reason) ? "Battle class selection was rejected." : pkt.Reason;
				Debug.LogWarning($"S_BATTLE_CLASS_SELECTION_RESULT failed. reason={LastError}");
			}
			else
			{
				Debug.Log($"S_BATTLE_CLASS_SELECTION_RESULT success. waitingForOpponent={pkt.WaitingForOpponent}");
			}

			BattleClassSelectionResultReceived?.Invoke(pkt);
		}

		void OnBattlePawnDeadReceived(Protocol.S_BATTLE_PAWN_DEAD pkt)
		{
			LastBattlePawnDead = pkt;
			if (pkt == null)
				return;

			Debug.Log($"S_BATTLE_PAWN_DEAD. battleId={pkt.BattleId}, pawnId={pkt.PawnId}, killerPawnId={pkt.KillerPawnId}");
			BattlePawnDeadReceived?.Invoke(pkt);
		}

		void OnBattleResultReceived(Protocol.S_BATTLE_RESULT pkt)
		{
			LastBattleResult = pkt;
			if (pkt == null)
				return;

			Debug.Log($"S_BATTLE_RESULT. battleId={pkt.BattleId}, victory={pkt.Victory}");
			BattleResultReceived?.Invoke(pkt);
		}

		void OnBattleResultAckReceived(Protocol.S_BATTLE_RESULT_ACK pkt)
		{
			LastBattleResultAck = pkt;
			if (pkt == null)
				return;

			if (pkt.Success)
				Debug.Log($"S_BATTLE_RESULT_ACK success. battleId={pkt.BattleId}");
			else
				Debug.LogWarning($"S_BATTLE_RESULT_ACK failed. battleId={pkt.BattleId}, reason={pkt.Reason}");

			BattleResultAckReceived?.Invoke(pkt);
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
