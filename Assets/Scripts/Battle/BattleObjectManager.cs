using System.Collections;
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
		const float SkillActionPresentationSeconds = 0.5f;
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
		readonly List<AxialCoord> _knownTargetTiles = new List<AxialCoord>();
		readonly List<AxialCoord> _validTargetTiles = new List<AxialCoord>();
		readonly List<AxialCoord> _affectedTargetTiles = new List<AxialCoord>();

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
		bool _isPlayingSkillActionSequence;
		Coroutine _skillActionSequenceCoroutine;
		bool _isLoadingGameData;
		BattleTargetPreview _targetPreview;
		bool _hasFireWallStartTarget;
		ulong _fireWallCasterPawnId;
		AxialCoord _fireWallStartAxial;

		public IReadOnlyDictionary<ulong, BattlePawn> Pawns => _pawns;
		public BattleMapGrid MapGrid => _mapGrid;
		public ulong BattleId => _battleId;
		public ulong BattleStateVersion => _battleStateVersion;
		public ulong CurrentTurnPawnId => _currentTurnPawnId;
		public BattleActionMode ActionMode => _actionMode;
		public bool IsAnimatingMove => _isAnimatingMove;
		public bool IsInteractionLocked => _isAnimatingMove || _isPlayingSkillActionSequence || _actionMode == BattleActionMode.WaitingServer;
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
			_targetPreview = _mapGrid != null
				? _mapGrid.GetComponent<BattleTargetPreview>() ?? _mapGrid.gameObject.AddComponent<BattleTargetPreview>()
				: null;

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

			if (mode != BattleActionMode.Skill3)
				ClearFireWallTargeting();

			_actionMode = mode;
			if (_actionMode == BattleActionMode.Move)
				_targetPreview?.Hide();
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
			ClearFireWallTargeting();
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
			RefreshTargetPreview();
		}

		void OnDestroy()
		{
			_destroyed = true;
			_isAnimatingMove = false;
			_isPlayingSkillActionSequence = false;
			if (_skillActionSequenceCoroutine != null)
				StopCoroutine(_skillActionSequenceCoroutine);
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
						case Protocol.PawnClass.SuenAxeSword:
							return pawnObject.AddComponent<SuenAxe>();
						case Protocol.PawnClass.BeigeIce:
						return pawnObject.AddComponent<BeigeIce>();
					case Protocol.PawnClass.BeigeFire:
						return pawnObject.AddComponent<BeigeFire>();
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
			ClearFireWallTargeting();
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

			if (TryGetSkillDefinition(casterPawn, skillSlot, out BattleSkillDefinition skill)
				&& IsTwoStageFireWall(skill))
			{
				HandleFireWallInput(casterPawnId, casterPawn, skillSlot, targetAxial);
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

		void HandleFireWallInput(ulong casterPawnId, BattlePawn casterPawn, int skillSlot, AxialCoord clickedAxial)
		{
			if (_hasFireWallStartTarget && _fireWallCasterPawnId == casterPawnId)
			{
				if (IsFireWallDirectionTarget(clickedAxial) == false)
				{
					Debug.Log($"Fire Wall direction must be one of the six in-bounds tiles adjacent to the starting tile. start={_fireWallStartAxial}, selected={clickedAxial}");
					return;
				}

				if (_battleId != 0 && GameRoot.Instance != null)
				{
					bool sent = GameRoot.Instance.Network.SendBattleSkill(
						_battleId,
						casterPawnId,
						skillSlot,
						_fireWallStartAxial.Q,
						_fireWallStartAxial.R,
						clickedAxial.Q,
						clickedAxial.R);
					if (sent)
					{
						Debug.Log($"Sent two-stage C_BATTLE_SKILL Fire Wall. battleId={_battleId}, casterPawnId={casterPawnId}, skillSlot={skillSlot}, start={_fireWallStartAxial}, lineDirection={clickedAxial}");
						ClearFireWallTargeting();
						_actionMode = BattleActionMode.WaitingServer;
					}
					else
					{
						Debug.LogWarning($"Failed to send two-stage C_BATTLE_SKILL Fire Wall. {GameRoot.Instance.Network.LastError}");
					}

					return;
				}

				Debug.Log($"Fire Wall debug selected. casterPawnId={casterPawnId}, skillSlot={skillSlot}, start={_fireWallStartAxial}, lineDirection={clickedAxial}");
				ClearFireWallTargeting();
				_actionMode = BattleActionMode.Move;
				return;
			}

			// Stage 1 keeps the normal Fire Wall target validation for its start tile.
			ulong resolvedTargetPawnId = FindPawnIdAtAxial(clickedAxial);
			if (ValidateSkillTarget(casterPawn, skillSlot, resolvedTargetPawnId, ref clickedAxial) == false)
				return;

			_hasFireWallStartTarget = true;
			_fireWallCasterPawnId = casterPawnId;
			_fireWallStartAxial = clickedAxial;
			Debug.Log($"Fire Wall start selected. casterPawnId={casterPawnId}, skillSlot={skillSlot}, start={clickedAxial}");
		}

		static bool IsTwoStageFireWall(BattleSkillDefinition skill)
		{
			return skill != null
				&& string.Equals(skill.SkillKey, "BEIGE_FIRE_FIRE_WALL", System.StringComparison.OrdinalIgnoreCase);
		}

		void ClearFireWallTargeting()
		{
			_hasFireWallStartTarget = false;
			_fireWallCasterPawnId = 0;
			_fireWallStartAxial = default;
		}

		bool ValidateSkillTarget(BattlePawn casterPawn, int skillSlot, ulong resolvedTargetPawnId, ref AxialCoord targetAxial)
		{
			BattleSkillDefinition skill = null;
			TryGetSkillDefinition(casterPawn, skillSlot, out skill);
			string targetType = skill != null ? skill.TargetType : GetSkillTargetType(casterPawn, skillSlot);
			if (string.IsNullOrWhiteSpace(targetType))
				targetType = "ENEMY_SINGLE";

			if (_mapGrid == null)
				return false;

			if (IsSelfCenteredAdjacentSkill(skill))
			{
				// Cleaner is sent as a SELF skill, but the player may confirm it either
				// by clicking Suen or one of the six already-previewed neighboring tiles.
				if (_mapGrid.IsTileInBounds(targetAxial) == false
					|| casterPawn.Axial.DistanceTo(targetAxial) > 1)
				{
					Debug.Log($"Cleaner must be confirmed on Suen or an adjacent tile. casterPawnId={casterPawn.PawnId}, axial={targetAxial}");
					return false;
				}

				targetAxial = casterPawn.Axial;
			}
			else if (targetType == "SELF" || targetType == "SELF_TOGGLE")
				targetAxial = casterPawn.Axial;

			if (_mapGrid.IsTileInBounds(targetAxial) == false)
			{
				Debug.Log($"Skill target is outside the battle map. casterPawnId={casterPawn.PawnId}, skillSlot={skillSlot}, axial={targetAxial}");
				return false;
			}

			if (skill != null)
			{
				int distance = casterPawn.Axial.DistanceTo(targetAxial);
				if (distance < skill.RangeMin || distance > skill.RangeMax)
				{
					Debug.Log($"Skill target is out of range. casterPawnId={casterPawn.PawnId}, skillSlot={skillSlot}, distance={distance}, range={skill.RangeMin}-{skill.RangeMax}");
					return false;
				}

				Protocol.BattleTileOverlayType requiredOverlay = ParseRequiredOverlayType(skill.RequiredOverlayType);
				if (requiredOverlay != Protocol.BattleTileOverlayType.None && _mapGrid.HasOverlay(targetAxial, requiredOverlay) == false)
				{
					Debug.Log($"Skill target is missing its required overlay. casterPawnId={casterPawn.PawnId}, skillSlot={skillSlot}, requiredOverlay={requiredOverlay}, axial={targetAxial}");
					return false;
				}
			}

			BattlePawn targetPawn = null;
			resolvedTargetPawnId = FindPawnIdAtAxial(targetAxial);
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
				case "ALLY_OR_SELF":
					if (targetPawn == null || targetPawn.IsMine == false)
					{
						Debug.Log($"Skill requires the caster or an allied target. casterPawnId={casterPawn.PawnId}, skillSlot={skillSlot}, resolvedTargetPawnId={resolvedTargetPawnId}, axial={targetAxial}");
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
				case "EMPTY_TILE":
					if (targetPawn != null)
					{
						Debug.Log($"Skill requires an empty tile. casterPawnId={casterPawn.PawnId}, skillSlot={skillSlot}, resolvedTargetPawnId={resolvedTargetPawnId}, axial={targetAxial}");
						return false;
					}

					return true;
				case "PICKUP_TILE":
					if (_mapGrid.HasEquipment(targetAxial, "AXE", casterPawn.PawnId) == false)
					{
						Debug.Log($"Skill requires the caster's axe equipment tile. casterPawnId={casterPawn.PawnId}, skillSlot={skillSlot}, axial={targetAxial}");
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

		bool TryGetSkillDefinition(BattlePawn casterPawn, int skillSlot, out BattleSkillDefinition skill)
		{
			skill = null;
			return _gameData != null
				&& casterPawn != null
				&& casterPawn.Info != null
				&& _gameData.TryGetSkill(casterPawn.Info.PawnClass, skillSlot, out skill);
		}

		static Protocol.BattleTileOverlayType ParseRequiredOverlayType(string value)
		{
			return string.Equals(value, "FIRE", System.StringComparison.OrdinalIgnoreCase)
				? Protocol.BattleTileOverlayType.Fire
				: Protocol.BattleTileOverlayType.None;
		}

		void RefreshTargetPreview()
		{
			if (_targetPreview == null || IsInteractionLocked)
			{
				_targetPreview?.Hide();
				return;
			}

			if (_actionMode == BattleActionMode.Move)
			{
				RefreshMovePreview();
				return;
			}

			int skillSlot = GetSkillSlot(_actionMode);
			if (skillSlot <= 0 || TryGetControllablePawnId(out ulong casterPawnId) == false
				|| _pawns.TryGetValue(casterPawnId, out BattlePawn casterPawn) == false
				|| TryGetSkillDefinition(casterPawn, skillSlot, out BattleSkillDefinition skill) == false)
			{
				_targetPreview.Hide();
				return;
			}

			if (IsTwoStageFireWall(skill) && _hasFireWallStartTarget && _fireWallCasterPawnId == casterPawnId)
			{
				RefreshFireWallDirectionPreview();
				return;
			}

			_validTargetTiles.Clear();
			_affectedTargetTiles.Clear();
			if (IsSelfCenteredAdjacentSkill(skill))
			{
				// The center is the confirmation target; the orange ring is the fixed
				// effect area and can also be clicked to confirm the same SELF packet.
				_validTargetTiles.Add(casterPawn.Axial);
				GetAffectedTargetTiles(casterPawn, skill, casterPawn.Axial, _affectedTargetTiles);
				_targetPreview.Show(_mapGrid, _validTargetTiles, _affectedTargetTiles);
				return;
			}

			if (skill.TargetType == "SELF" || skill.TargetType == "SELF_TOGGLE")
			{
				_validTargetTiles.Add(casterPawn.Axial);
			}
			else
			{
				_mapGrid.GetKnownTileAxials(_knownTargetTiles);
				for (int i = 0; i < _knownTargetTiles.Count; i++)
				{
					if (IsValidSkillPreviewTarget(casterPawn, skill, _knownTargetTiles[i]))
						_validTargetTiles.Add(_knownTargetTiles[i]);
				}
			}

			if (TryGetPointerAxial(out AxialCoord hoveredAxial)
				&& IsValidSkillPreviewTarget(casterPawn, skill, hoveredAxial))
			{
				GetAffectedTargetTiles(casterPawn, skill, hoveredAxial, _affectedTargetTiles);
			}

			_targetPreview.Show(_mapGrid, _validTargetTiles, _affectedTargetTiles);
		}

		void RefreshMovePreview()
		{
			if (TryGetControllablePawnId(out ulong pawnId) == false
				|| _pawns.TryGetValue(pawnId, out BattlePawn movingPawn) == false
				|| movingPawn == null
				|| movingPawn.CanMove == false
				|| movingPawn.MoveRange <= 0)
			{
				_targetPreview.Hide();
				return;
			}

			_validTargetTiles.Clear();
			_affectedTargetTiles.Clear();
			_mapGrid.GetKnownTileAxials(_knownTargetTiles);
			for (int i = 0; i < _knownTargetTiles.Count; i++)
			{
				AxialCoord axial = _knownTargetTiles[i];
				int distance = movingPawn.Axial.DistanceTo(axial);
				if (distance <= 0 || distance > movingPawn.MoveRange)
					continue;

				if (_mapGrid.IsWalkable(axial) == false || FindPawnIdAtAxial(axial) != 0)
					continue;

				_validTargetTiles.Add(axial);
			}

			_targetPreview.Show(_mapGrid, _validTargetTiles, _affectedTargetTiles);
		}

		bool IsValidSkillPreviewTarget(BattlePawn casterPawn, BattleSkillDefinition skill, AxialCoord targetAxial)
		{
			if (casterPawn == null || skill == null || _mapGrid == null)
				return false;

			string targetType = skill.TargetType;
			if (IsSelfCenteredAdjacentSkill(skill))
				return _mapGrid.IsTileInBounds(targetAxial)
					&& casterPawn.Axial.DistanceTo(targetAxial) <= 1;

			if (targetType == "SELF" || targetType == "SELF_TOGGLE")
				targetAxial = casterPawn.Axial;

			if (_mapGrid.IsTileInBounds(targetAxial) == false)
				return false;

			int distance = casterPawn.Axial.DistanceTo(targetAxial);
			if (distance < skill.RangeMin || distance > skill.RangeMax)
				return false;

			Protocol.BattleTileOverlayType requiredOverlay = ParseRequiredOverlayType(skill.RequiredOverlayType);
			if (requiredOverlay != Protocol.BattleTileOverlayType.None && _mapGrid.HasOverlay(targetAxial, requiredOverlay) == false)
				return false;

			ulong targetPawnId = FindPawnIdAtAxial(targetAxial);
			BattlePawn targetPawn = null;
			if (targetPawnId != 0)
				_pawns.TryGetValue(targetPawnId, out targetPawn);

			switch (targetType)
			{
				case "SELF":
				case "SELF_TOGGLE":
					return targetPawn == casterPawn;
				case "ALLY_SINGLE":
					return targetPawn != null && targetPawn.IsMine;
				case "ALLY_OR_SELF":
					return targetPawn != null && targetPawn.IsMine;
				case "ENEMY_SINGLE":
					return targetPawn != null && targetPawn.IsMine == false;
				case "TILE_OR_ENEMY":
					return targetPawn == null || targetPawn.IsMine == false;
				case "EMPTY_TILE":
					return targetPawn == null;
				case "PICKUP_TILE":
					return _mapGrid.HasEquipment(targetAxial, "AXE", casterPawn.PawnId);
				default:
					return targetPawn == null || targetPawn.IsMine == false;
			}
		}

		void GetAffectedTargetTiles(BattlePawn casterPawn, BattleSkillDefinition skill, AxialCoord targetAxial, List<AxialCoord> destination)
		{
			destination.Clear();
			if (IsSelfCenteredAdjacentSkill(skill))
			{
				for (int direction = 0; direction < 6; direction++)
					AddAffectedTile(_mapGrid.GetNeighbor(casterPawn.Axial, direction), destination);

				return;
			}

			AddAffectedTile(targetAxial, destination);
			// The final Fire Wall line is selected in a second stage, so its first-stage
			// hover only indicates the potential start tile.
			if (IsTwoStageFireWall(skill))
				return;

			if (skill.TargetShape == "RADIUS_1")
			{
				for (int direction = 0; direction < 6; direction++)
					AddAffectedTile(_mapGrid.GetNeighbor(targetAxial, direction), destination);
				return;
			}

			if (skill.TargetShape != "LINE_3" || casterPawn.Axial.DistanceTo(targetAxial) <= 0)
				return;

			int directionIndex = FindDirectionIndex(casterPawn.Axial, targetAxial);
			AxialCoord next = targetAxial;
			for (int distance = 1; distance <= 2; distance++)
			{
				next = _mapGrid.GetNeighbor(next, directionIndex);
				AddAffectedTile(next, destination);
			}
		}

		static bool IsSelfCenteredAdjacentSkill(BattleSkillDefinition skill)
		{
			return skill != null
				&& skill.TargetType == "SELF"
				&& skill.TargetShape == "ADJACENT_6";
		}

		void RefreshFireWallDirectionPreview()
		{
			_validTargetTiles.Clear();
			_affectedTargetTiles.Clear();
			AddAffectedTile(_fireWallStartAxial, _affectedTargetTiles);

			for (int direction = 0; direction < 6; direction++)
			{
				AxialCoord adjacent = _mapGrid.GetNeighbor(_fireWallStartAxial, direction);
				if (_mapGrid.IsTileInBounds(adjacent))
					_validTargetTiles.Add(adjacent);
			}

			if (TryGetPointerAxial(out AxialCoord hoveredAxial) && IsFireWallDirectionTarget(hoveredAxial))
			{
				AddAffectedTile(hoveredAxial, _affectedTargetTiles);
				int direction = FindAdjacentDirectionIndex(_fireWallStartAxial, hoveredAxial);
				if (direction >= 0)
					AddAffectedTile(_mapGrid.GetNeighbor(hoveredAxial, direction), _affectedTargetTiles);
			}

			_targetPreview.Show(_mapGrid, _validTargetTiles, _affectedTargetTiles);
		}

		bool IsFireWallDirectionTarget(AxialCoord targetAxial)
		{
			return _mapGrid != null
				&& _mapGrid.IsTileInBounds(targetAxial)
				&& FindAdjacentDirectionIndex(_fireWallStartAxial, targetAxial) >= 0;
		}

		int FindAdjacentDirectionIndex(AxialCoord source, AxialCoord target)
		{
			if (_mapGrid == null)
				return -1;

			for (int direction = 0; direction < 6; direction++)
			{
				if (_mapGrid.GetNeighbor(source, direction).Equals(target))
					return direction;
			}

			return -1;
		}

		void AddAffectedTile(AxialCoord axial, List<AxialCoord> destination)
		{
			if (_mapGrid.IsTileInBounds(axial) && destination.Contains(axial) == false)
				destination.Add(axial);
		}

		int FindDirectionIndex(AxialCoord source, AxialCoord target)
		{
			int distance = source.DistanceTo(target);
			for (int direction = 0; direction < 6; direction++)
			{
				if (_mapGrid.GetNeighbor(source, direction).DistanceTo(target) == distance - 1)
					return direction;
			}

			return 0;
		}

		bool TryGetPointerAxial(out AxialCoord axial)
		{
			axial = default;
			if (_mapGrid == null || IsPointerOverUi())
				return false;

			Camera camera = Camera.main;
			if (camera == null)
				return false;

			Vector2 screenPosition;
#if ENABLE_INPUT_SYSTEM
			Mouse mouse = Mouse.current;
			if (mouse == null)
				return false;

			screenPosition = mouse.position.ReadValue();
#elif ENABLE_LEGACY_INPUT_MANAGER
			screenPosition = Input.mousePosition;
#else
			return false;
#endif
			if (IsValidScreenPosition(camera, screenPosition) == false)
				return false;

			Plane mapPlane = new Plane(Vector3.forward, _mapGrid.PlaneTransform.position);
			Ray ray = camera.ScreenPointToRay(screenPosition);
			if (mapPlane.Raycast(ray, out float enter) == false)
				return false;

			axial = _mapGrid.WorldToAxial(ray.GetPoint(enter));
			return true;
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
				ClearFireWallTargeting();
				_actionMode = BattleActionMode.Move;
				Debug.LogWarning($"Battle skill rejected. casterPawnId={packet.CasterPawnId}, skillSlot={packet.SkillSlot}, reason={packet.Reason}");
				return;
			}

			if (TryApplyNewBattleStateVersion(packet.BattleStateVersion, nameof(S_BATTLE_SKILL)) == false)
				return;

			ClearFireWallTargeting();
			_actionMode = BattleActionMode.Move;

			_mapGrid?.ApplyTileDeltas(packet.TileDeltas);
			// target_pawn_id == 0 means a tile-only result. Pawn state always comes from
			// pawn_deltas, so no target Pawn lookup or direct HP update is performed here.

			if (packet.CasterPawnId != 0 && _pawns.TryGetValue(packet.CasterPawnId, out BattlePawn casterPawn))
			{
				// BattlePawnDelta intentionally has no axial field. Teleport is the one
				// skill response whose server-authoritative target axial is the caster's
				// new position, so apply it only after a successful server response.
				if (IsOverlayTeleport(casterPawn, packet.SkillSlot) && packet.TargetAxial != null)
					casterPawn.SetAxial(ToBattleAxial(packet.TargetAxial));

			}

			QueueSkillActionSequence(packet.CasterPawnId, packet.SkillSlot, packet.Logs, packet.PawnDeltas);

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

		void QueueSkillActionSequence(
			ulong casterPawnId,
			int skillSlot,
			IEnumerable<BattleActionLog> logs,
			IEnumerable<BattlePawnDelta> pawnDeltas)
		{
			if (_skillActionSequenceCoroutine != null)
				StopCoroutine(_skillActionSequenceCoroutine);

			List<BattleActionLog> orderedLogs = new List<BattleActionLog>();
			if (logs != null)
			{
				foreach (BattleActionLog log in logs)
				{
					if (log != null)
						orderedLogs.Add(log);
				}
			}

			List<BattlePawnDelta> finalPawnDeltas = new List<BattlePawnDelta>();
			if (pawnDeltas != null)
			{
				foreach (BattlePawnDelta delta in pawnDeltas)
				{
					if (delta != null)
						finalPawnDeltas.Add(delta);
				}
			}

			_skillActionSequenceCoroutine = StartCoroutine(PlaySkillActionSequence(casterPawnId, skillSlot, orderedLogs, finalPawnDeltas));
		}

		IEnumerator PlaySkillActionSequence(
			ulong casterPawnId,
			int skillSlot,
			List<BattleActionLog> orderedLogs,
			List<BattlePawnDelta> finalPawnDeltas)
		{
			_isPlayingSkillActionSequence = true;
			if (_pawns.TryGetValue(casterPawnId, out BattlePawn casterPawn))
				TriggerSkillAnimation(casterPawn, skillSlot);

			// Primary-action logs are resolved with the initiating skill. Counter logs
			// are kept in their packet order and receive their own 0.5 s presentation.
			for (int i = 0; i < orderedLogs.Count; i++)
			{
				BattleActionLog primaryLog = orderedLogs[i];
				if (primaryLog.IsCounter == false && primaryLog.AttackerPawnId == casterPawnId)
				{
					PlayMeleeAttackPresentation(primaryLog);
					break;
				}
			}

			for (int i = 0; i < orderedLogs.Count; i++)
			{
				if (orderedLogs[i].IsCounter == false)
				{
					ApplyCombatLogPresentation(orderedLogs[i]);
					AppendBattleLog(orderedLogs[i]);
				}
			}

			yield return new WaitForSecondsRealtime(SkillActionPresentationSeconds);

			for (int i = 0; i < orderedLogs.Count; i++)
			{
				BattleActionLog counterLog = orderedLogs[i];
				if (counterLog.IsCounter == false)
					continue;

				if (_pawns.TryGetValue(counterLog.AttackerPawnId, out BattlePawn counterPawn))
					counterPawn.TriggerSkill("Skill1");

				PlayMeleeAttackPresentation(counterLog);

				ApplyCombatLogPresentation(counterLog);
				AppendBattleLog(counterLog);
				yield return new WaitForSecondsRealtime(SkillActionPresentationSeconds);
			}

			ApplyPawnDeltas(finalPawnDeltas);
			_isPlayingSkillActionSequence = false;
			_skillActionSequenceCoroutine = null;
		}

		void ApplyCombatLogPresentation(BattleActionLog log)
		{
			if (log == null || log.DefenderPawnId == 0)
				return;

			if (_pawns.TryGetValue(log.DefenderPawnId, out BattlePawn defenderPawn))
			{
				if (log.IsEvaded && _pawns.TryGetValue(log.AttackerPawnId, out BattlePawn attackerPawn))
					defenderPawn.PlayEvadePresentation(attackerPawn.transform.position);

				defenderPawn.ApplyCombatLogPresentation(log.HpAfter, log.ArmorAfter);
			}
		}

		void PlayMeleeAttackPresentation(BattleActionLog log)
		{
			if (log == null
				|| _pawns.TryGetValue(log.AttackerPawnId, out BattlePawn attackerPawn) == false
				|| attackerPawn.IsMelee == false
				|| _pawns.TryGetValue(log.DefenderPawnId, out BattlePawn defenderPawn) == false)
				return;

			attackerPawn.PlayMeleeAttackPresentation(defenderPawn.transform.position);
		}

		bool IsOverlayTeleport(BattlePawn casterPawn, int skillSlot)
		{
			return TryGetSkillDefinition(casterPawn, skillSlot, out BattleSkillDefinition skill)
				&& skill.TargetType == "EMPTY_TILE"
				&& ParseRequiredOverlayType(skill.RequiredOverlayType) != Protocol.BattleTileOverlayType.None;
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
				ClearFireWallTargeting();
				_actionMode = BattleActionMode.Move;
				Debug.LogWarning($"Battle end turn rejected. pawnId={packet.PawnId}, reason={packet.Reason}");
				return;
			}

			if (TryApplyNewBattleStateVersion(packet.BattleStateVersion, nameof(S_BATTLE_END_TURN)) == false)
				return;

			ClearFireWallTargeting();
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
				AppendBattleLog(log);
			}
		}

		void AppendBattleLog(BattleActionLog log)
		{
			if (log == null)
				return;

			string line = FormatBattleLog(log);
			if (string.IsNullOrWhiteSpace(line))
				return;

			_battleLogLines.Enqueue(line);
			while (_battleLogLines.Count > MaxBattleLogLines)
				_battleLogLines.Dequeue();

			Debug.Log($"BattleLog: {line}");
			BattleActionLogApplied?.Invoke(log);
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
