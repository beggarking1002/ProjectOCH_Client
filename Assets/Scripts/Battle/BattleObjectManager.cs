using System.Collections.Generic;
using System.Threading.Tasks;
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
		const string BattlePawnBaseAddress = "PawnBase";
		const string BattlePawnVisualRootName = "visual";
		// Presentation assets and local pawn behavior are selected by the protocol class.
		// The hierarchy is only for client-side behavior; server snapshots remain authoritative.
		static readonly Dictionary<Protocol.PawnClass, string> PawnVisualAddressByClass = new Dictionary<Protocol.PawnClass, string>
		{
			{ Protocol.PawnClass.SuenAxeSword, "Pawn_Suen_AxeSword" },
			{ Protocol.PawnClass.SuenParvis, "Pawn_Suen_Parvis" },
			{ Protocol.PawnClass.BeigeFire, "Pawn_Beige_Fire" },
			{ Protocol.PawnClass.BeigeIce, "Pawn_Beige_Ice" },
			{ Protocol.PawnClass.ZillianLongbow, "Pawn_Zillian_Longbow" },
			{ Protocol.PawnClass.ZillianMace, "Pawn_Zillian_Mace" },
			{ Protocol.PawnClass.AlenSpear, "Pawn_Alen_Spear" },
			{ Protocol.PawnClass.AlenSwordShield, "Pawn_Alen_SwordShield" },
			{ Protocol.PawnClass.SeraNecromancer, "Pawn_Sera_Necromancer" },
			{ Protocol.PawnClass.SeraWarlock, "Pawn_Sera_Warlock" },
			{ Protocol.PawnClass.DarkhandSword, "Pawn_Darkhand_Sword" },
		};

		readonly Dictionary<ulong, BattlePawn> _pawns = new Dictionary<ulong, BattlePawn>();
		readonly Dictionary<ulong, AsyncOperationHandle<GameObject>> _pawnHandles = new Dictionary<ulong, AsyncOperationHandle<GameObject>>();
		readonly Dictionary<ulong, AsyncOperationHandle<GameObject>> _pawnVisualHandles = new Dictionary<ulong, AsyncOperationHandle<GameObject>>();
		readonly HashSet<ulong> _localPawnIds = new HashSet<ulong>();
		readonly Queue<string> _battleLogLines = new Queue<string>();

		BattleMapGrid _mapGrid;
		BattleGameDataRepository _gameData;
		string _fallbackPawnAddress;
		ulong _battleId;
		ulong _battleStateVersion;
		ulong _currentTurnPawnId;
		BattleActionMode _actionMode = BattleActionMode.Move;
		bool _destroyed;
		bool _missingCameraLogged;
		bool _isAnimatingMove;
		bool _isLoadingGameData;

		public IReadOnlyDictionary<ulong, BattlePawn> Pawns => _pawns;
		public BattleMapGrid MapGrid => _mapGrid;
		public ulong BattleId => _battleId;
		public ulong BattleStateVersion => _battleStateVersion;
		public ulong CurrentTurnPawnId => _currentTurnPawnId;
		public BattleActionMode ActionMode => _actionMode;
		public bool IsAnimatingMove => _isAnimatingMove;
		public bool IsInteractionLocked => _isAnimatingMove || _actionMode == BattleActionMode.WaitingServer;
		public bool IsCurrentTurnLocal => _currentTurnPawnId != 0
			&& _localPawnIds.Contains(_currentTurnPawnId)
			&& _pawns.TryGetValue(_currentTurnPawnId, out BattlePawn currentTurnPawn)
			&& currentTurnPawn != null
			&& currentTurnPawn.IsDead == false;
		public string BattleLogText => _battleLogLines.Count > 0 ? string.Join("\n", _battleLogLines) : "-";
		public event System.Action<BattleActionLog> BattleActionLogApplied;

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

			_ = LoadGameDataAsync();
		}

		async Task LoadGameDataAsync()
		{
			if (_isLoadingGameData)
				return;

			_isLoadingGameData = true;
			_gameData = await BattleGameDataRepository.LoadAsync();
			_isLoadingGameData = false;
			ValidatePawnClassPresentationMappings();
		}

		void ValidatePawnClassPresentationMappings()
		{
			if (_gameData == null)
				return;

			foreach (KeyValuePair<string, PawnClass> pair in _gameData.ClassKeyToPawnClass)
			{
				if (pair.Value == PawnClass.None)
					continue;

				if (PawnVisualAddressByClass.ContainsKey(pair.Value) == false)
					Debug.LogWarning($"PawnClass has GameData but no visual mapping. classKey={pair.Key}, pawnClass={pair.Value}");
			}
		}

		public void SetActionMode(BattleActionMode mode)
		{
			if (IsInteractionLocked)
			{
				Debug.Log("Cannot change battle action mode while battle interaction is locked.");
				return;
			}

			_actionMode = mode;
			Debug.Log($"Battle action mode changed: {_actionMode}");
		}

		public void DebugEndTurn()
		{
			if (IsInteractionLocked)
			{
				Debug.Log("Cannot end turn while battle interaction is locked.");
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

		public bool TryGetPawn(ulong pawnId, out BattlePawn pawn)
		{
			return _pawns.TryGetValue(pawnId, out pawn) && pawn != null && pawn.IsDead == false;
		}

		public bool TryGetPawnAtAxial(AxialCoord axial, out ulong pawnId, out BattlePawn pawn)
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
			_isAnimatingMove = false;
			PacketHandler.Instance.BattleMoveReceived -= OnBattleMoveReceived;
			PacketHandler.Instance.BattleSkillReceived -= OnBattleSkillReceived;
			PacketHandler.Instance.BattleEndTurnReceived -= OnBattleEndTurnReceived;
			PacketHandler.Instance.BattlePawnDeadReceived -= OnBattlePawnDeadReceived;
			ReleasePawns();
		}

		public async void SpawnDebugPawns()
		{
			await SpawnDebugPawnsAsync();
		}

		public async Task SpawnDebugPawnsAsync()
		{
			_battleId = 0;
			_battleStateVersion = 0;
			_currentTurnPawnId = 1;
			_isAnimatingMove = false;
			_localPawnIds.Clear();

			await SpawnPawnAsync(1, true, new AxialCoord(-2, -2));
			await SpawnPawnAsync(2, false, new AxialCoord(2, 2));
			RefreshTurnIndicators();
		}

		public async void SpawnFromEnterBattle(S_ENTER_BATTLE packet)
		{
			await SpawnFromEnterBattleAsync(packet);
		}

		public async Task SpawnFromEnterBattleAsync(S_ENTER_BATTLE packet)
		{
			if (packet == null || packet.Success == false)
				return;

			if (_battleId == packet.BattleId && _battleId != 0 && TryApplyNewBattleStateVersion(packet.BattleStateVersion, nameof(S_ENTER_BATTLE)) == false)
			{
				return;
			}

			ReleasePawns();
			_localPawnIds.Clear();
			_battleLogLines.Clear();
			_battleId = packet.BattleId;
			// A new battle owns an independent version sequence. A duplicate enter packet for
			// the current battle was rejected above; this assignment initializes a new one.
			_battleStateVersion = packet.BattleStateVersion;
			_currentTurnPawnId = packet.CurrentTurnPawnId;
			_isAnimatingMove = false;
			_mapGrid?.ApplyTileSnapshot(packet.Tiles);

			foreach (BattlePawnInfo pawnInfo in packet.AlliedPawns)
				await SpawnPawnAsync(pawnInfo.PawnId, true, ToBattleAxial(pawnInfo.Axial), pawnInfo);

			foreach (BattlePawnInfo pawnInfo in packet.EnemyPawns)
				await SpawnPawnAsync(pawnInfo.PawnId, false, ToBattleAxial(pawnInfo.Axial), pawnInfo);

			RefreshTurnIndicators();
			Debug.Log($"Spawned battle pawns from server. battleId={_battleId}, battleStateVersion={_battleStateVersion}, currentTurnPawnId={_currentTurnPawnId}, allied={packet.AlliedPawns.Count}, enemy={packet.EnemyPawns.Count}");
		}

		public async System.Threading.Tasks.Task<BattlePawn> SpawnPawnAsync(ulong pawnId, bool isMine, AxialCoord axial, BattlePawnInfo info = null)
		{
			if (_mapGrid == null)
			{
				Debug.LogError($"{nameof(BattleObjectManager)} requires a {nameof(BattleMapGrid)} before spawning pawns.");
				return null;
			}

			string visualAddress = GetPawnAddress(info);
			if (string.IsNullOrWhiteSpace(visualAddress))
			{
				Debug.LogError($"{nameof(BattleObjectManager)} requires a pawn address.");
				return null;
			}

			if (_pawns.TryGetValue(pawnId, out BattlePawn existing))
			{
				existing.SetAxial(axial);
				return existing;
			}

			AsyncOperationHandle<GameObject> baseHandle = Addressables.InstantiateAsync(BattlePawnBaseAddress);
			_pawnHandles[pawnId] = baseHandle;

			await baseHandle.Task;

			if (_destroyed)
			{
				if (baseHandle.IsValid())
					Addressables.ReleaseInstance(baseHandle);
				return null;
			}

			if (baseHandle.Status != AsyncOperationStatus.Succeeded)
			{
				Debug.LogError($"Failed to load battle pawn base addressable: {BattlePawnBaseAddress}");
				if (baseHandle.IsValid())
					Addressables.ReleaseInstance(baseHandle);
				_pawnHandles.Remove(pawnId);
				return null;
			}

			GameObject pawnObject = baseHandle.Result;
			pawnObject.name = isMine ? $"BattlePawn_My_{pawnId}_{visualAddress}" : $"BattlePawn_Enemy_{pawnId}_{visualAddress}";
			SceneManager.MoveGameObjectToScene(pawnObject, gameObject.scene);

			Transform visualRoot = FindVisualRoot(pawnObject.transform);
			if (visualRoot == null)
			{
				Debug.LogError($"{BattlePawnBaseAddress} requires a child named '{BattlePawnVisualRootName}'.");
				ReleasePawnHandles(pawnId);
				return null;
			}

			AsyncOperationHandle<GameObject> visualHandle = Addressables.InstantiateAsync(visualAddress);
			_pawnVisualHandles[pawnId] = visualHandle;
			await visualHandle.Task;

			if (_destroyed)
			{
				ReleasePawnHandles(pawnId);
				return null;
			}

			if (visualHandle.Status != AsyncOperationStatus.Succeeded)
			{
				Debug.LogError($"Failed to load battle pawn visual addressable: {visualAddress}");
				ReleasePawnHandles(pawnId);
				return null;
			}

			GameObject visualObject = visualHandle.Result;
			visualObject.name = visualAddress;
			SceneManager.MoveGameObjectToScene(visualObject, gameObject.scene);
			visualObject.transform.SetParent(visualRoot, false);
			visualObject.transform.localPosition = Vector3.zero;
			visualObject.transform.localRotation = Quaternion.identity;
			visualObject.transform.localScale = Vector3.one;

			BattlePawn pawn = AddPawnBehavior(pawnObject, info);

			pawn.Initialize(pawnId, isMine, _mapGrid, axial, Color.white, info);

			_pawns[pawnId] = pawn;
			if (isMine)
				_localPawnIds.Add(pawnId);

			RefreshTurnIndicators();
			Debug.Log($"Spawned battle pawn: id={pawnId}, class={info?.PawnClass.ToString() ?? "Debug"}, base={BattlePawnBaseAddress}, visual={visualAddress}, mine={isMine}, axial={axial}, world={pawn.transform.position}");
			return pawn;
		}

		static BattlePawn AddPawnBehavior(GameObject pawnObject, BattlePawnInfo info)
		{
			if (info != null)
			{
				switch (info.PawnClass)
				{
					case Protocol.PawnClass.BeigeIce:
						return pawnObject.AddComponent<BeigeIce>();
					case Protocol.PawnClass.BeigeFire:
						return pawnObject.AddComponent<Beige>();
				}
			}

			return pawnObject.AddComponent<BattlePawn>();
		}

		string GetPawnAddress(BattlePawnInfo info)
		{
			if (info == null)
				return _fallbackPawnAddress;

			if (PawnVisualAddressByClass.TryGetValue(info.PawnClass, out string visualAddress))
				return visualAddress;

			Debug.LogWarning($"Missing PawnClass presentation mapping. pawnClass={info.PawnClass}, fallbackAddress={_fallbackPawnAddress}");
			return _fallbackPawnAddress;
		}

		public void DespawnPawn(ulong pawnId)
		{
			_pawns.Remove(pawnId);
			_localPawnIds.Remove(pawnId);
			ReleasePawnHandles(pawnId);
		}

		void ReleasePawnHandles(ulong pawnId)
		{
			if (_pawnVisualHandles.TryGetValue(pawnId, out AsyncOperationHandle<GameObject> visualHandle))
			{
				if (visualHandle.IsValid())
					Addressables.ReleaseInstance(visualHandle);

				_pawnVisualHandles.Remove(pawnId);
			}

			if (_pawnHandles.TryGetValue(pawnId, out AsyncOperationHandle<GameObject> handle))
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);

				_pawnHandles.Remove(pawnId);
			}
		}

		void ReleasePawns()
		{
			foreach (AsyncOperationHandle<GameObject> handle in _pawnVisualHandles.Values)
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
			}

			foreach (AsyncOperationHandle<GameObject> handle in _pawnHandles.Values)
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
			}

			_pawns.Clear();
			_pawnVisualHandles.Clear();
			_pawnHandles.Clear();
			_localPawnIds.Clear();
		}

		bool TryApplyNewBattleStateVersion(ulong packetVersion, string packetName)
		{
			if (packetVersion < _battleStateVersion)
			{
				Debug.Log($"Ignored outdated {packetName}. battleId={_battleId}, packetVersion={packetVersion}, localVersion={_battleStateVersion}");
				return false;
			}

			if (packetVersion == _battleStateVersion)
			{
				Debug.Log($"Ignored duplicate {packetName}. battleId={_battleId}, battleStateVersion={packetVersion}");
				return false;
			}

			_battleStateVersion = packetVersion;
			return true;
		}

		static Transform FindVisualRoot(Transform root)
		{
			Transform direct = root.Find(BattlePawnVisualRootName);
			if (direct != null)
				return direct;

			Transform[] children = root.GetComponentsInChildren<Transform>(true);
			for (int i = 0; i < children.Length; i++)
			{
				if (children[i] != root && children[i].name == BattlePawnVisualRootName)
					return children[i];
			}

			return null;
		}

		void RefreshTurnIndicators()
		{
			foreach (KeyValuePair<ulong, BattlePawn> pair in _pawns)
			{
				if (pair.Value == null)
					continue;

				pair.Value.SetTurnIndicatorVisible(pair.Key == _currentTurnPawnId && pair.Value.IsDead == false);
			}
		}

		void HandleMouseInput()
		{
			if (IsInteractionLocked)
				return;

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

			if (_pawns.TryGetValue(movingPawnId, out BattlePawn myPawn) == false)
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

			_isAnimatingMove = true;
			myPawn.MoveToAxial(axial, () =>
			{
				_isAnimatingMove = false;
			});
			Debug.Log($"Move debug battle pawn to tile center. axial={axial}, world={myPawn.transform.position}");
		}

		void HandleSkillInput(AxialCoord targetAxial)
		{
			if (IsInteractionLocked)
				return;

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

			if (_pawns.TryGetValue(casterPawnId, out BattlePawn casterPawn) == false || casterPawn == null)
			{
				Debug.LogWarning($"Cannot use battle skill because caster pawn is missing. casterPawnId={casterPawnId}, skillSlot={skillSlot}");
				return;
			}

			ulong resolvedTargetPawnId = FindPawnIdAtAxial(targetAxial);
			if (ValidateSkillTarget(casterPawn, skillSlot, resolvedTargetPawnId, ref targetAxial) == false)
				return;

			// Self-target validation can replace the clicked tile with the caster's tile.
			resolvedTargetPawnId = FindPawnIdAtAxial(targetAxial);

			if (_battleId != 0 && GameRoot.Instance != null)
			{
				// The server resolves the target Pawn from target_axial. The legacy
				// target_pawn_id field is intentionally sent as zero by NetworkService.
				bool sent = GameRoot.Instance.Network.SendBattleSkill(_battleId, casterPawnId, skillSlot, targetAxial.Q, targetAxial.R);
				if (sent)
				{
					_actionMode = BattleActionMode.WaitingServer;
					Debug.Log($"Sent C_BATTLE_SKILL. battleId={_battleId}, casterPawnId={casterPawnId}, skillSlot={skillSlot}, targetPawnId=0, axial={targetAxial}, locallyResolvedPawnId={resolvedTargetPawnId}");
				}
				else
				{
					Debug.LogWarning($"Failed to send C_BATTLE_SKILL. {GameRoot.Instance.Network.LastError}");
				}

				return;
			}

			Debug.Log($"Skill debug selected. casterPawnId={casterPawnId}, skillSlot={skillSlot}, resolvedTargetPawnId={resolvedTargetPawnId}, axial={targetAxial}");
			_actionMode = BattleActionMode.Move;
		}

		bool ValidateSkillTarget(BattlePawn casterPawn, int skillSlot, ulong resolvedTargetPawnId, ref AxialCoord targetAxial)
		{
			string targetType = GetSkillTargetType(casterPawn, skillSlot);
			if (string.IsNullOrWhiteSpace(targetType))
				targetType = "ENEMY_SINGLE";

			BattlePawn targetPawn = null;
			if (resolvedTargetPawnId != 0)
				_pawns.TryGetValue(resolvedTargetPawnId, out targetPawn);

			switch (targetType)
			{
				case "SELF":
				case "SELF_TOGGLE":
					targetAxial = casterPawn.Axial;
					return true;
				case "ALLY_SINGLE":
					if (targetPawn == null || targetPawn.IsMine == false)
					{
						Debug.Log($"Skill requires allied target. casterPawnId={casterPawn.PawnId}, skillSlot={skillSlot}, resolvedTargetPawnId={resolvedTargetPawnId}, axial={targetAxial}");
						return false;
					}

					return true;
				case "ENEMY_SINGLE":
					if (targetPawn == null || targetPawn.IsMine)
					{
						Debug.Log($"Skill requires enemy target. casterPawnId={casterPawn.PawnId}, skillSlot={skillSlot}, resolvedTargetPawnId={resolvedTargetPawnId}, axial={targetAxial}");
						return false;
					}

					return true;
				case "TILE_OR_ENEMY":
					if (targetPawn != null && targetPawn.IsMine)
					{
						Debug.Log($"Skill cannot target allied pawn. casterPawnId={casterPawn.PawnId}, skillSlot={skillSlot}, resolvedTargetPawnId={resolvedTargetPawnId}, axial={targetAxial}");
						return false;
					}

					return true;
				default:
					if (targetPawn != null && targetPawn.IsMine)
					{
						Debug.Log($"Skill target rejected by fallback ally guard. casterPawnId={casterPawn.PawnId}, skillSlot={skillSlot}, targetType={targetType}, resolvedTargetPawnId={resolvedTargetPawnId}, axial={targetAxial}");
						return false;
					}

					return true;
			}
		}

		string GetSkillTargetType(BattlePawn casterPawn, int skillSlot)
		{
			if (_gameData != null
				&& casterPawn != null
				&& casterPawn.Info != null
				&& _gameData.TryGetSkill(casterPawn.Info.PawnClass, skillSlot, out BattleSkillDefinition skill))
			{
				return skill.TargetType;
			}

			return string.Empty;
		}

		bool TryGetControllablePawnId(out ulong pawnId)
		{
			if (_currentTurnPawnId != 0
				&& _pawns.TryGetValue(_currentTurnPawnId, out BattlePawn currentPawn)
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
				if (_pawns.TryGetValue(localPawnId, out BattlePawn localPawn) && localPawn != null && localPawn.IsDead == false)
				{
					pawnId = localPawnId;
					return true;
				}
			}

			if (_pawns.TryGetValue(1, out BattlePawn fallbackPawn) && fallbackPawn != null && fallbackPawn.IsDead == false)
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
				_isAnimatingMove = false;
				_actionMode = BattleActionMode.Move;
				Debug.LogWarning($"Battle move rejected. pawnId={packet.PawnId}, result={packet.Result}, reason={packet.Reason}");
				return;
			}

			if (_pawns.TryGetValue(packet.PawnId, out BattlePawn pawn) == false)
			{
				_isAnimatingMove = false;
				_actionMode = BattleActionMode.Move;
				Debug.LogWarning($"Cannot apply S_BATTLE_MOVE because pawn is missing. pawnId={packet.PawnId}");
				return;
			}

			if (TryApplyNewBattleStateVersion(packet.BattleStateVersion, nameof(S_BATTLE_MOVE)) == false)
				return;

			AxialCoord targetAxial = packet.Target != null ? ToBattleAxial(packet.Target) : pawn.Axial;
			ulong nextTurnPawnId = packet.NextTurnPawnId;
			ulong appliedStateVersion = _battleStateVersion;

			ApplyPawnDeltas(packet.PawnDeltas);
			AppendBattleLogs(packet.Logs);

			_isAnimatingMove = true;
			RefreshTurnIndicators();
			pawn.MoveToAxial(targetAxial, () =>
			{
				_isAnimatingMove = false;
				if (_battleStateVersion != appliedStateVersion)
				{
					RefreshTurnIndicators();
					return;
				}

				_currentTurnPawnId = nextTurnPawnId;
				_actionMode = BattleActionMode.Move;
				RefreshTurnIndicators();
			});

			Debug.Log($"Applied S_BATTLE_MOVE. pawnId={packet.PawnId}, target={targetAxial}, battleStateVersion={_battleStateVersion}, nextTurnPawnId={nextTurnPawnId}");
		}

		void OnBattleSkillReceived(S_BATTLE_SKILL packet)
		{
			if (packet == null || packet.BattleId != _battleId)
				return;

			if (packet.Success == false)
			{
				_actionMode = BattleActionMode.Move;
				Debug.LogWarning($"Battle skill rejected. casterPawnId={packet.CasterPawnId}, skillSlot={packet.SkillSlot}, reason={packet.Reason}");
				return;
			}

			if (TryApplyNewBattleStateVersion(packet.BattleStateVersion, nameof(S_BATTLE_SKILL)) == false)
				return;

			_actionMode = BattleActionMode.Move;

			ApplyPawnDeltas(packet.PawnDeltas);
			_mapGrid?.ApplyTileDeltas(packet.TileDeltas);
			// target_pawn_id == 0 means a tile-only result. Pawn state always comes from
			// pawn_deltas, so no target Pawn lookup or direct HP update is performed here.

			if (packet.CasterPawnId != 0 && _pawns.TryGetValue(packet.CasterPawnId, out BattlePawn casterPawn))
				TriggerSkillAnimation(casterPawn, packet.SkillSlot);

			AppendBattleLogs(packet.Logs);

			_currentTurnPawnId = packet.NextTurnPawnId;
			RefreshTurnIndicators();
			Debug.Log($"Applied S_BATTLE_SKILL. casterPawnId={packet.CasterPawnId}, skillSlot={packet.SkillSlot}, targetPawnId={packet.TargetPawnId}, targetKind={(packet.TargetPawnId == 0 ? "Tile" : "Pawn")}, damage={packet.Damage}, battleStateVersion={_battleStateVersion}, nextTurnPawnId={_currentTurnPawnId}");
		}

		void TriggerSkillAnimation(BattlePawn casterPawn, int skillSlot)
		{
			if (casterPawn == null)
				return;

			if (_gameData != null
				&& casterPawn.Info != null
				&& _gameData.TryGetSkill(casterPawn.Info.PawnClass, skillSlot, out BattleSkillDefinition skill)
				&& _gameData.TryGetSkillView(skill.SkillKey, out BattleSkillViewDefinition view)
				&& string.IsNullOrWhiteSpace(view.AnimTrigger) == false)
			{
				casterPawn.TriggerSkill(view.AnimTrigger);
				return;
			}

			casterPawn.TriggerSkill(skillSlot);
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

			if (packet.Success == false)
			{
				_actionMode = BattleActionMode.Move;
				Debug.LogWarning($"Battle end turn rejected. pawnId={packet.PawnId}, reason={packet.Reason}");
				return;
			}

			if (TryApplyNewBattleStateVersion(packet.BattleStateVersion, nameof(S_BATTLE_END_TURN)) == false)
				return;

			_actionMode = BattleActionMode.Move;

			ApplyPawnDeltas(packet.PawnDeltas);
			_mapGrid?.ApplyTileDeltas(packet.TileDeltas);
			AppendBattleLogs(packet.Logs);

			_currentTurnPawnId = packet.NextTurnPawnId;
			RefreshTurnIndicators();
			Debug.Log($"Applied S_BATTLE_END_TURN. battleId={_battleId}, pawnId={packet.PawnId}, battleStateVersion={_battleStateVersion}, nextTurnPawnId={_currentTurnPawnId}, isCurrentTurnLocal={IsCurrentTurnLocal}");
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

			if (_pawns.TryGetValue(pawnId, out BattlePawn pawn) == false || pawn == null)
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

				if (_pawns.TryGetValue(delta.PawnId, out BattlePawn pawn))
					pawn.ApplyDelta(delta);
			}
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
				BattleActionLogApplied?.Invoke(log);
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
			if (log.IsBackAttack)
				flags += " BACK";

			return $"{action}: {log.AttackerPawnId}->{log.DefenderPawnId} dmg={log.Damage} hp={log.HpAfter} armor={log.ArmorAfter}{flags}";
		}

		ulong FindPawnIdAtAxial(AxialCoord axial)
		{
			foreach (KeyValuePair<ulong, BattlePawn> pair in _pawns)
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

			foreach (KeyValuePair<ulong, BattlePawn> pair in _pawns)
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
					return 2;
				case BattleActionMode.Skill2:
					return 3;
				case BattleActionMode.Skill3:
					return 4;
				case BattleActionMode.Skill4:
					return 5;
				case BattleActionMode.Ultimate:
					return 6;
				case BattleActionMode.SubAction:
					return 7;
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
