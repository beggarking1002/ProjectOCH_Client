using System.Collections.Generic;
using App;
using Protocol;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class BattleObjectManager : MonoBehaviour
	{
		const int MaxBattleLogLines = 6;

		readonly Dictionary<ulong, BattlePawnController> _pawns = new Dictionary<ulong, BattlePawnController>();
		readonly Dictionary<ulong, AsyncOperationHandle<GameObject>> _pawnHandles = new Dictionary<ulong, AsyncOperationHandle<GameObject>>();
		readonly HashSet<ulong> _localPawnIds = new HashSet<ulong>();
		readonly Queue<string> _battleLogLines = new Queue<string>();

		BattleMapGrid _mapGrid;
		string _fallbackPawnAddress;
		ulong _battleId;
		ulong _currentTurnPawnId;
		BattleActionMode _actionMode = BattleActionMode.Move;
		bool _destroyed;
		bool _missingCameraLogged;

		public IReadOnlyDictionary<ulong, BattlePawnController> Pawns => _pawns;
		public BattleMapGrid MapGrid => _mapGrid;
		public ulong BattleId => _battleId;
		public ulong CurrentTurnPawnId => _currentTurnPawnId;
		public BattleActionMode ActionMode => _actionMode;
		public bool IsCurrentTurnLocal => _currentTurnPawnId != 0
			&& _localPawnIds.Contains(_currentTurnPawnId)
			&& _pawns.TryGetValue(_currentTurnPawnId, out BattlePawnController currentTurnPawn)
			&& currentTurnPawn != null
			&& currentTurnPawn.IsDead == false;
		public string BattleLogText => _battleLogLines.Count > 0 ? string.Join("\n", _battleLogLines) : "-";

		public void Initialize(BattleMapGrid mapGrid, string pawnAddress)
		{
			_mapGrid = mapGrid;
			_fallbackPawnAddress = pawnAddress;

			PacketHandler.Instance.BattleMoveReceived -= OnBattleMoveReceived;
			PacketHandler.Instance.BattleMoveReceived += OnBattleMoveReceived;
			PacketHandler.Instance.BattleSkillReceived -= OnBattleSkillReceived;
			PacketHandler.Instance.BattleSkillReceived += OnBattleSkillReceived;
			PacketHandler.Instance.BattleEndTurnReceived -= OnBattleEndTurnReceived;
			PacketHandler.Instance.BattleEndTurnReceived += OnBattleEndTurnReceived;
			PacketHandler.Instance.BattlePawnDeadReceived -= OnBattlePawnDeadReceived;
			PacketHandler.Instance.BattlePawnDeadReceived += OnBattlePawnDeadReceived;
		}

		public void SetActionMode(BattleActionMode mode)
		{
			if (_actionMode == BattleActionMode.WaitingServer)
			{
				Debug.Log("Cannot change battle action mode while waiting for server.");
				return;
			}

			_actionMode = mode;
			Debug.Log($"Battle action mode changed: {_actionMode}");
		}

		public void DebugEndTurn()
		{
			if (_actionMode == BattleActionMode.WaitingServer)
			{
				Debug.Log("Cannot end turn while waiting for server.");
				return;
			}

			if (_battleId != 0)
			{
				if (TryGetControllablePawnId(out ulong pawnId) == false)
				{
					Debug.Log($"No controllable battle pawn for end turn. currentTurnPawnId={_currentTurnPawnId}, battleId={_battleId}");
					return;
				}

				if (GameRoot.Instance == null)
				{
					Debug.LogWarning("Failed to send C_BATTLE_END_TURN. GameRoot is missing.");
					return;
				}

				bool sent = GameRoot.Instance.Network.SendBattleEndTurn(_battleId, pawnId);
				if (sent)
				{
					_actionMode = BattleActionMode.WaitingServer;
					Debug.Log($"Sent C_BATTLE_END_TURN. battleId={_battleId}, pawnId={pawnId}");
				}
				else
				{
					Debug.LogWarning($"Failed to send C_BATTLE_END_TURN. {GameRoot.Instance.Network.LastError}");
				}

				return;
			}

			ulong nextTurnPawnId = GetNextDebugTurnPawnId();
			if (nextTurnPawnId == 0)
			{
				Debug.Log("Cannot end debug turn because no battle pawns exist.");
				return;
			}

			_currentTurnPawnId = nextTurnPawnId;
			_actionMode = BattleActionMode.Move;
			RefreshTurnIndicators();
			Debug.Log($"Debug end turn. nextTurnPawnId={_currentTurnPawnId}");
		}

		public bool TryGetPawn(ulong pawnId, out BattlePawnController pawn)
		{
			return _pawns.TryGetValue(pawnId, out pawn) && pawn != null && pawn.IsDead == false;
		}

		public bool TryGetPawnAtAxial(AxialCoord axial, out ulong pawnId, out BattlePawnController pawn)
		{
			pawnId = FindPawnIdAtAxial(axial);
			if (pawnId == 0)
			{
				pawn = null;
				return false;
			}

			return TryGetPawn(pawnId, out pawn);
		}

		public bool IsTileWalkable(AxialCoord axial)
		{
			return _mapGrid != null && _mapGrid.IsWalkable(axial);
		}

		void Update()
		{
			HandleMouseInput();
		}

		void OnDestroy()
		{
			_destroyed = true;
			PacketHandler.Instance.BattleMoveReceived -= OnBattleMoveReceived;
			PacketHandler.Instance.BattleSkillReceived -= OnBattleSkillReceived;
			PacketHandler.Instance.BattleEndTurnReceived -= OnBattleEndTurnReceived;
			PacketHandler.Instance.BattlePawnDeadReceived -= OnBattlePawnDeadReceived;
			ReleasePawns();
		}

		public async void SpawnDebugPawns()
		{
			_battleId = 0;
			_currentTurnPawnId = 1;
			_localPawnIds.Clear();

			await SpawnPawnAsync(1, true, new AxialCoord(-2, -2));
			await SpawnPawnAsync(2, false, new AxialCoord(2, 2));
			RefreshTurnIndicators();
		}

		public async void SpawnFromEnterBattle(S_ENTER_BATTLE packet)
		{
			if (packet == null || packet.Success == false)
				return;

			ReleasePawns();
			_localPawnIds.Clear();
			_battleLogLines.Clear();
			_battleId = packet.BattleId;
			_currentTurnPawnId = packet.CurrentTurnPawnId;

			foreach (BattlePawnInfo pawnInfo in packet.AlliedPawns)
				await SpawnPawnAsync(pawnInfo.PawnId, true, ToBattleAxial(pawnInfo.Axial), pawnInfo);

			foreach (BattlePawnInfo pawnInfo in packet.EnemyPawns)
				await SpawnPawnAsync(pawnInfo.PawnId, false, ToBattleAxial(pawnInfo.Axial), pawnInfo);

			RefreshTurnIndicators();
			Debug.Log($"Spawned battle pawns from server. battleId={_battleId}, currentTurnPawnId={_currentTurnPawnId}, allied={packet.AlliedPawns.Count}, enemy={packet.EnemyPawns.Count}");
		}

		public async System.Threading.Tasks.Task<BattlePawnController> SpawnPawnAsync(ulong pawnId, bool isMine, AxialCoord axial, BattlePawnInfo info = null)
		{
			if (_mapGrid == null)
			{
				Debug.LogError($"{nameof(BattleObjectManager)} requires a {nameof(BattleMapGrid)} before spawning pawns.");
				return null;
			}

			string pawnAddress = GetPawnAddress(info);
			if (string.IsNullOrWhiteSpace(pawnAddress))
			{
				Debug.LogError($"{nameof(BattleObjectManager)} requires a pawn address.");
				return null;
			}

			if (_pawns.TryGetValue(pawnId, out BattlePawnController existing))
			{
				existing.SetAxial(axial);
				return existing;
			}

			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(pawnAddress);
			_pawnHandles[pawnId] = handle;

			await handle.Task;

			if (_destroyed)
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				return null;
			}

			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				Debug.LogError($"Failed to load battle pawn addressable: {pawnAddress}");
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				_pawnHandles.Remove(pawnId);
				return null;
			}

			GameObject pawnObject = handle.Result;
			pawnObject.name = isMine ? $"BattlePawn_My_{pawnId}_{pawnAddress}" : $"BattlePawn_Enemy_{pawnId}_{pawnAddress}";
			SceneManager.MoveGameObjectToScene(pawnObject, gameObject.scene);

			BattlePawnController pawn = pawnObject.GetComponent<BattlePawnController>();
			if (pawn == null)
				pawn = pawnObject.AddComponent<BattlePawnController>();

			Color tint = isMine ? Color.white : new Color(1f, 0.75f, 0.75f, 1f);
			pawn.Initialize(pawnId, isMine, _mapGrid, axial, tint, info);

			_pawns[pawnId] = pawn;
			if (isMine)
				_localPawnIds.Add(pawnId);

			RefreshTurnIndicators();
			Debug.Log($"Spawned battle pawn: id={pawnId}, class={info?.PawnClass.ToString() ?? "Debug"}, address={pawnAddress}, mine={isMine}, axial={axial}, world={pawn.transform.position}");
			return pawn;
		}

		string GetPawnAddress(BattlePawnInfo info)
		{
			if (info == null)
				return _fallbackPawnAddress;

			switch (info.PawnClass)
			{
				case Protocol.PawnClass.SuenAxeSword:
					return "Pawn_Suen_AxeSword";
				case Protocol.PawnClass.SuenParvis:
					return "Pawn_Suen_Parvis";
				case Protocol.PawnClass.BeigeFire:
					return "Pawn_Beige_Fire";
				case Protocol.PawnClass.BeigeIce:
					return "Pawn_Beige_Ice";
				case Protocol.PawnClass.ZillianLongbow:
					return "Pawn_Zillian_Longbow";
				case Protocol.PawnClass.ZillianMace:
					return "Pawn_Zillian_Mace";
				case Protocol.PawnClass.AlenSpear:
					return "Pawn_Alen_Spear";
				case Protocol.PawnClass.AlenSwordShield:
					return "Pawn_Alen_SwordShield";
				case Protocol.PawnClass.SeraNecromancer:
					return "Pawn_Sera_Necromancer";
				case Protocol.PawnClass.SeraWarlock:
					return "Pawn_Sera_Warlock";
				case Protocol.PawnClass.DarkhandSword:
					return "Pawn_Darkhand_Sword";
				default:
					Debug.LogWarning($"Unknown pawn class {info.PawnClass}. Fallback address={_fallbackPawnAddress}");
					return _fallbackPawnAddress;
			}
		}

		public void DespawnPawn(ulong pawnId)
		{
			_pawns.Remove(pawnId);
			_localPawnIds.Remove(pawnId);

			if (_pawnHandles.TryGetValue(pawnId, out AsyncOperationHandle<GameObject> handle))
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);

				_pawnHandles.Remove(pawnId);
			}
		}

		void ReleasePawns()
		{
			foreach (AsyncOperationHandle<GameObject> handle in _pawnHandles.Values)
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
			}

			_pawns.Clear();
			_pawnHandles.Clear();
			_localPawnIds.Clear();
		}

		void RefreshTurnIndicators()
		{
			foreach (KeyValuePair<ulong, BattlePawnController> pair in _pawns)
			{
				if (pair.Value == null)
					continue;

				pair.Value.SetTurnIndicatorVisible(pair.Key == _currentTurnPawnId && pair.Value.IsDead == false);
			}
		}

		void HandleMouseInput()
		{
			if (_mapGrid == null || TryGetPointerDown(out Vector2 screenPosition) == false)
				return;

			if (IsPointerOverUi())
				return;

			Camera camera = Camera.main;
			if (camera == null)
			{
				if (_missingCameraLogged == false)
				{
					_missingCameraLogged = true;
					Debug.LogWarning($"{nameof(BattleObjectManager)} requires a MainCamera to convert mouse input.");
				}

				return;
			}

			if (IsValidScreenPosition(camera, screenPosition) == false)
				return;

			Ray ray = camera.ScreenPointToRay(screenPosition);
			Plane mapPlane = new Plane(Vector3.forward, _mapGrid.PlaneTransform.position);
			if (mapPlane.Raycast(ray, out float enter) == false)
				return;

			Vector3 worldPosition = ray.GetPoint(enter);
			AxialCoord axial = _mapGrid.WorldToAxial(worldPosition);

			if (_actionMode != BattleActionMode.Move)
			{
				HandleSkillInput(axial);
				return;
			}

			if (_mapGrid.IsWalkable(axial) == false)
			{
				Debug.Log($"Clicked blocked battle tile. axial={axial}");
				return;
			}

			if (TryGetControllablePawnId(out ulong movingPawnId) == false)
			{
				Debug.Log($"No controllable battle pawn. currentTurnPawnId={_currentTurnPawnId}, battleId={_battleId}");
				return;
			}

			if (_pawns.TryGetValue(movingPawnId, out BattlePawnController myPawn) == false)
				return;

			if (_battleId != 0 && GameRoot.Instance != null)
			{
				bool sent = GameRoot.Instance.Network.SendBattleMove(_battleId, movingPawnId, axial.Q, axial.R);
				if (sent)
				{
					_actionMode = BattleActionMode.WaitingServer;
					Debug.Log($"Sent C_BATTLE_MOVE. battleId={_battleId}, pawnId={movingPawnId}, axial={axial}");
				}
				else
				{
					Debug.LogWarning($"Failed to send C_BATTLE_MOVE. {GameRoot.Instance.Network.LastError}");
				}

				return;
			}

			myPawn.SetAxial(axial);
			Debug.Log($"Move debug battle pawn to tile center. axial={axial}, world={myPawn.transform.position}");
		}

		void HandleSkillInput(AxialCoord targetAxial)
		{
			int skillSlot = GetSkillSlot(_actionMode);
			if (skillSlot <= 0)
			{
				Debug.Log($"Battle action is not implemented yet. mode={_actionMode}, axial={targetAxial}");
				return;
			}

			if (TryGetControllablePawnId(out ulong casterPawnId) == false)
			{
				Debug.Log($"No controllable battle pawn for skill. currentTurnPawnId={_currentTurnPawnId}, battleId={_battleId}");
				return;
			}

			ulong targetPawnId = FindPawnIdAtAxial(targetAxial);
			if (_battleId != 0 && GameRoot.Instance != null)
			{
				bool sent = GameRoot.Instance.Network.SendBattleSkill(_battleId, casterPawnId, skillSlot, targetPawnId, targetAxial.Q, targetAxial.R);
				if (sent)
				{
					_actionMode = BattleActionMode.WaitingServer;
					Debug.Log($"Sent C_BATTLE_SKILL. battleId={_battleId}, casterPawnId={casterPawnId}, skillSlot={skillSlot}, targetPawnId={targetPawnId}, axial={targetAxial}");
				}
				else
				{
					Debug.LogWarning($"Failed to send C_BATTLE_SKILL. {GameRoot.Instance.Network.LastError}");
				}

				return;
			}

			Debug.Log($"Skill debug selected. casterPawnId={casterPawnId}, skillSlot={skillSlot}, targetPawnId={targetPawnId}, axial={targetAxial}");
			_actionMode = BattleActionMode.Move;
		}

		bool TryGetControllablePawnId(out ulong pawnId)
		{
			if (_currentTurnPawnId != 0
				&& _pawns.TryGetValue(_currentTurnPawnId, out BattlePawnController currentPawn)
				&& currentPawn != null
				&& currentPawn.IsDead == false)
			{
				if (_battleId == 0 || _localPawnIds.Contains(_currentTurnPawnId))
				{
					pawnId = _currentTurnPawnId;
					return true;
				}
			}

			if (_battleId != 0)
			{
				pawnId = 0;
				return false;
			}

			foreach (ulong localPawnId in _localPawnIds)
			{
				if (_pawns.TryGetValue(localPawnId, out BattlePawnController localPawn) && localPawn != null && localPawn.IsDead == false)
				{
					pawnId = localPawnId;
					return true;
				}
			}

			if (_pawns.TryGetValue(1, out BattlePawnController fallbackPawn) && fallbackPawn != null && fallbackPawn.IsDead == false)
			{
				pawnId = 1;
				return true;
			}

			pawnId = 0;
			return false;
		}

		void OnBattleMoveReceived(S_BATTLE_MOVE packet)
		{
			if (packet == null || packet.BattleId != _battleId)
				return;

			if (packet.Success == false)
			{
				_actionMode = BattleActionMode.Move;
				Debug.LogWarning($"Battle move rejected. pawnId={packet.PawnId}, result={packet.Result}, reason={packet.Reason}");
				return;
			}

			if (_pawns.TryGetValue(packet.PawnId, out BattlePawnController pawn) == false)
				return;

			if (packet.Target != null)
				pawn.SetAxial(ToBattleAxial(packet.Target));

			ApplyPawnDeltas(packet.PawnDeltas);
			ApplyTurnState(packet.PawnId, packet.RemainingAp, packet.CanMove);
			AppendBattleLogs(packet.Logs);

			_currentTurnPawnId = packet.NextTurnPawnId;
			_actionMode = BattleActionMode.Move;
			RefreshTurnIndicators();
			Debug.Log($"Applied S_BATTLE_MOVE. pawnId={packet.PawnId}, target={pawn.Axial}, remainingAp={packet.RemainingAp}, canMove={packet.CanMove}, nextTurnPawnId={_currentTurnPawnId}");
		}

		void OnBattleSkillReceived(S_BATTLE_SKILL packet)
		{
			if (packet == null || packet.BattleId != _battleId)
				return;

			_actionMode = BattleActionMode.Move;

			if (packet.Success == false)
			{
				Debug.LogWarning($"Battle skill rejected. casterPawnId={packet.CasterPawnId}, skillSlot={packet.SkillSlot}, reason={packet.Reason}");
				return;
			}

			ApplyPawnDeltas(packet.PawnDeltas);
			ApplyTurnState(packet.CasterPawnId, packet.RemainingAp, packet.CanMove, packet.UsedSubActionThisTurn, packet.UsedUltimate);

			if (packet.TargetPawnId != 0 && _pawns.TryGetValue(packet.TargetPawnId, out BattlePawnController targetPawn))
			{
				targetPawn.ApplyHp(packet.TargetHp);
				targetPawn.ApplyArmor(packet.TargetArmor);
			}

			AppendBattleLogs(packet.Logs);

			_currentTurnPawnId = packet.NextTurnPawnId;
			RefreshTurnIndicators();
			Debug.Log($"Applied S_BATTLE_SKILL. casterPawnId={packet.CasterPawnId}, skillSlot={packet.SkillSlot}, targetPawnId={packet.TargetPawnId}, damage={packet.Damage}, targetHp={packet.TargetHp}, targetArmor={packet.TargetArmor}, remainingAp={packet.RemainingAp}, canMove={packet.CanMove}, nextTurnPawnId={_currentTurnPawnId}");
		}

		void OnBattleEndTurnReceived(S_BATTLE_END_TURN packet)
		{
			if (packet == null)
				return;

			if (packet.BattleId != _battleId)
			{
				Debug.LogWarning($"Ignored S_BATTLE_END_TURN because battleId mismatched. packetBattleId={packet.BattleId}, localBattleId={_battleId}, pawnId={packet.PawnId}, nextTurnPawnId={packet.NextTurnPawnId}");
				return;
			}

			_actionMode = BattleActionMode.Move;

			if (packet.Success == false)
			{
				Debug.LogWarning($"Battle end turn rejected. pawnId={packet.PawnId}, reason={packet.Reason}");
				return;
			}

			ApplyPawnDeltas(packet.PawnDeltas);
			ApplyTurnState(packet.PawnId, packet.RemainingAp, packet.CanMove, packet.UsedSubActionThisTurn, packet.UsedUltimate);
			AppendBattleLogs(packet.Logs);

			_currentTurnPawnId = packet.NextTurnPawnId;
			RefreshTurnIndicators();
			Debug.Log($"Applied S_BATTLE_END_TURN. battleId={_battleId}, pawnId={packet.PawnId}, remainingAp={packet.RemainingAp}, canMove={packet.CanMove}, nextTurnPawnId={_currentTurnPawnId}, isCurrentTurnLocal={IsCurrentTurnLocal}");
		}

		void OnBattlePawnDeadReceived(S_BATTLE_PAWN_DEAD packet)
		{
			if (packet == null)
				return;

			if (packet.BattleId != _battleId)
			{
				Debug.LogWarning($"Ignored S_BATTLE_PAWN_DEAD because battleId mismatched. packetBattleId={packet.BattleId}, localBattleId={_battleId}, pawnId={packet.PawnId}, killerPawnId={packet.KillerPawnId}");
				return;
			}

			ApplyPawnDead(packet.PawnId, packet.KillerPawnId);
			RefreshTurnIndicators();
			Debug.Log($"Applied S_BATTLE_PAWN_DEAD. battleId={_battleId}, pawnId={packet.PawnId}, killerPawnId={packet.KillerPawnId}");
		}

		void ApplyPawnDead(ulong pawnId, ulong killerPawnId)
		{
			if (pawnId == 0)
				return;

			if (_pawns.TryGetValue(pawnId, out BattlePawnController pawn) == false || pawn == null)
			{
				Debug.LogWarning($"Cannot apply pawn death because pawn is missing. pawnId={pawnId}, killerPawnId={killerPawnId}");
				return;
			}

			pawn.ApplyDead(killerPawnId);
		}

		void ApplyPawnDeltas(IEnumerable<BattlePawnDelta> pawnDeltas)
		{
			if (pawnDeltas == null)
				return;

			foreach (BattlePawnDelta delta in pawnDeltas)
			{
				if (delta == null || delta.PawnId == 0)
					continue;

				if (_pawns.TryGetValue(delta.PawnId, out BattlePawnController pawn))
					pawn.ApplyDelta(delta);
			}
		}

		void ApplyTurnState(ulong pawnId, int remainingAp, bool canMove)
		{
			if (pawnId != 0 && _pawns.TryGetValue(pawnId, out BattlePawnController pawn))
				pawn.ApplyTurnState(remainingAp, canMove);
		}

		void ApplyTurnState(ulong pawnId, int remainingAp, bool canMove, bool usedSubActionThisTurn, bool usedUltimate)
		{
			if (pawnId != 0 && _pawns.TryGetValue(pawnId, out BattlePawnController pawn))
				pawn.ApplyTurnState(remainingAp, canMove, usedSubActionThisTurn, usedUltimate);
		}

		void AppendBattleLogs(IEnumerable<BattleActionLog> logs)
		{
			if (logs == null)
				return;

			foreach (BattleActionLog log in logs)
			{
				if (log == null)
					continue;

				string line = FormatBattleLog(log);
				if (string.IsNullOrWhiteSpace(line))
					continue;

				_battleLogLines.Enqueue(line);
				while (_battleLogLines.Count > MaxBattleLogLines)
					_battleLogLines.Dequeue();

				Debug.Log($"BattleLog: {line}");
			}
		}

		static string FormatBattleLog(BattleActionLog log)
		{
			string action = string.IsNullOrWhiteSpace(log.ActionType) ? $"Skill{log.SkillSlot}" : log.ActionType;
			string flags = "";
			if (log.IsCritical)
				flags += " CRIT";
			if (log.IsEvaded)
				flags += " EVADE";
			if (log.IsGuarded)
				flags += " GUARD";
			if (log.IsPerfectGuarded)
				flags += " PERFECT";
			if (log.IsCounter)
				flags += " COUNTER";

			return $"{action}: {log.AttackerPawnId}->{log.DefenderPawnId} dmg={log.Damage} hp={log.HpAfter} armor={log.ArmorAfter}{flags}";
		}

		ulong FindPawnIdAtAxial(AxialCoord axial)
		{
			foreach (KeyValuePair<ulong, BattlePawnController> pair in _pawns)
			{
				if (pair.Value != null && pair.Value.IsDead == false && pair.Value.Axial.Equals(axial))
					return pair.Key;
			}

			return 0;
		}

		ulong GetNextDebugTurnPawnId()
		{
			ulong smallestPawnId = 0;
			ulong nextPawnId = 0;

			foreach (KeyValuePair<ulong, BattlePawnController> pair in _pawns)
			{
				if (pair.Value == null || pair.Value.IsDead)
					continue;

				ulong pawnId = pair.Key;
				if (smallestPawnId == 0 || pawnId < smallestPawnId)
					smallestPawnId = pawnId;

				if (pawnId > _currentTurnPawnId && (nextPawnId == 0 || pawnId < nextPawnId))
					nextPawnId = pawnId;
			}

			return nextPawnId != 0 ? nextPawnId : smallestPawnId;
		}

		static int GetSkillSlot(BattleActionMode mode)
		{
			switch (mode)
			{
				case BattleActionMode.Skill1:
					return 1;
				case BattleActionMode.Skill2:
					return 2;
				case BattleActionMode.Skill3:
					return 3;
				case BattleActionMode.Skill4:
					return 4;
				case BattleActionMode.Ultimate:
					return 5;
				default:
					return 0;
			}
		}

		bool TryGetPointerDown(out Vector2 screenPosition)
		{
#if ENABLE_INPUT_SYSTEM
			Mouse mouse = Mouse.current;
			if (mouse != null && mouse.leftButton.wasPressedThisFrame)
			{
				screenPosition = mouse.position.ReadValue();
				return true;
			}
#elif ENABLE_LEGACY_INPUT_MANAGER
			if (Input.GetMouseButtonDown(0))
			{
				screenPosition = Input.mousePosition;
				return true;
			}
#endif
			screenPosition = default;
			return false;
		}

		static bool IsPointerOverUi()
		{
			EventSystem eventSystem = EventSystem.current;
			return eventSystem != null && eventSystem.IsPointerOverGameObject();
		}

		static bool IsValidScreenPosition(Camera camera, Vector2 screenPosition)
		{
			if (float.IsNaN(screenPosition.x) || float.IsNaN(screenPosition.y))
				return false;

			if (float.IsInfinity(screenPosition.x) || float.IsInfinity(screenPosition.y))
				return false;

			return screenPosition.x >= 0f
				&& screenPosition.y >= 0f
				&& screenPosition.x <= camera.pixelWidth
				&& screenPosition.y <= camera.pixelHeight;
		}

		static AxialCoord ToBattleAxial(Protocol.AxialCoord axial)
		{
			if (axial == null)
				return default;

			return new AxialCoord(axial.Q, axial.R);
		}
	}
}
