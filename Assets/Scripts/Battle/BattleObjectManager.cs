using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using App;
using Field;
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
	public enum BattleTurnQueueUpdateKind
	{
		Initialize,
		Shift,
		Resync,
	}

	public sealed class BattleTurnQueueUpdate
	{
		public readonly IReadOnlyList<ulong> PawnIds;
		public readonly BattleTurnQueueUpdateKind Kind;
		public readonly IReadOnlyCollection<ulong> DeadPawnIds;

		public BattleTurnQueueUpdate(IReadOnlyList<ulong> pawnIds, BattleTurnQueueUpdateKind kind, IReadOnlyCollection<ulong> deadPawnIds)
		{
			PawnIds = pawnIds;
			Kind = kind;
			DeadPawnIds = deadPawnIds;
		}
	}

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
		readonly List<ulong> _upcomingTurnPawnIds = new List<ulong>(8);
		readonly Queue<string> _battleLogLines = new Queue<string>();
		readonly List<AxialCoord> _knownTargetTiles = new List<AxialCoord>();
		readonly List<AxialCoord> _skillRangeTiles = new List<AxialCoord>();
		readonly List<AxialCoord> _validTargetTiles = new List<AxialCoord>();
		readonly List<AxialCoord> _reachableMoveTiles = new List<AxialCoord>();
		readonly List<AxialCoord> _affectedTargetTiles = new List<AxialCoord>();
		readonly List<AxialCoord> _hoveredZocTiles = new List<AxialCoord>();
		readonly List<AxialCoord> _zocAttackerTiles = new List<AxialCoord>();
		readonly HashSet<AxialCoord> _zocFrontier = new HashSet<AxialCoord>();
		readonly HashSet<AxialCoord> _zocNextFrontier = new HashSet<AxialCoord>();
		readonly Queue<MoveSearchNode> _moveSearchQueue = new Queue<MoveSearchNode>();
		readonly HashSet<AxialCoord> _moveSearchVisited = new HashSet<AxialCoord>();

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
		bool _isAwaitingOptionalPositionSwap;
		Coroutine _skillActionSequenceCoroutine;
		readonly Dictionary<ulong, ulong> _deferredPawnDeaths = new Dictionary<ulong, ulong>();
		Coroutine _moveReactionSequenceCoroutine;
		bool _isLoadingGameData;
		BattleTargetPreview _targetPreview;
		BattleProjectilePresenter _projectilePresenter;
		BattleSpriteEffectPresenter _skillEffectPresenter;
		BattleFireTileEffectPresenter _fireTileEffectPresenter;
		bool _hasFireWallStartTarget;
		ulong _fireWallCasterPawnId;
		AxialCoord _fireWallStartAxial;

		readonly struct StatusTickPresentation
		{
			public readonly ulong PawnId;
			public readonly string StatusKey;
			public readonly int Amount;

			public StatusTickPresentation(ulong pawnId, string statusKey, int amount)
			{
				PawnId = pawnId;
				StatusKey = statusKey;
				Amount = amount;
			}
		}

		readonly struct MoveSearchNode
		{
			public readonly AxialCoord Axial;
			public readonly int Steps;

			public MoveSearchNode(AxialCoord axial, int steps)
			{
				Axial = axial;
				Steps = steps;
			}
		}

		public IReadOnlyDictionary<ulong, BattlePawn> Pawns => _pawns;
		public BattleMapGrid MapGrid => _mapGrid;
		public ulong BattleId => _battleId;
		public ulong BattleStateVersion => _battleStateVersion;
		public ulong CurrentTurnPawnId => _currentTurnPawnId;
		public BattleActionMode ActionMode => _actionMode;
		public bool IsAnimatingMove => _isAnimatingMove;
		public bool IsInteractionLocked => _isAnimatingMove || _isPlayingSkillActionSequence || _isAwaitingOptionalPositionSwap || _actionMode == BattleActionMode.WaitingServer;
		public bool IsCurrentTurnLocal => _currentTurnPawnId != 0
			&& _localPawnIds.Contains(_currentTurnPawnId)
			&& _pawns.TryGetValue(_currentTurnPawnId, out BattlePawn currentTurnPawn)
			&& currentTurnPawn != null
			&& currentTurnPawn.IsDead == false;
		public string BattleLogText => _battleLogLines.Count > 0 ? string.Join("\n", _battleLogLines) : "-";
		public IReadOnlyList<ulong> UpcomingTurnPawnIds => _upcomingTurnPawnIds;
		public event System.Action<BattleActionLog> BattleActionLogApplied;
		public event System.Action<BattlePawn, string, int> BattleStatusTickApplied;
		public event System.Action<BattleTurnQueueUpdate> TurnQueueUpdated;
		public event System.Action<ulong> BattlePawnDied;
		public event System.Action<System.Action<bool>> OptionalPositionSwapChoiceRequested;

		public void Initialize(BattleMapGrid mapGrid, string pawnAddress)
		{
			_mapGrid = mapGrid;
			_fallbackPawnAddress = pawnAddress;
			EnsureBattleCameraController();
			_targetPreview = _mapGrid != null
				? _mapGrid.GetComponent<BattleTargetPreview>() ?? _mapGrid.gameObject.AddComponent<BattleTargetPreview>()
				: null;
			_projectilePresenter = GetComponent<BattleProjectilePresenter>() ?? gameObject.AddComponent<BattleProjectilePresenter>();
			_skillEffectPresenter = GetComponent<BattleSpriteEffectPresenter>() ?? gameObject.AddComponent<BattleSpriteEffectPresenter>();
			_fireTileEffectPresenter = GetComponent<BattleFireTileEffectPresenter>() ?? gameObject.AddComponent<BattleFireTileEffectPresenter>();
			_fireTileEffectPresenter.Preload();

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

		static void EnsureBattleCameraController()
		{
			Camera camera = Camera.main;
			if (camera == null)
				return;

			CameraController controller = camera.GetComponent<CameraController>();
			if (controller == null)
				controller = camera.gameObject.AddComponent<CameraController>();

			controller.ConfigureFreePan();
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
			HandleCameraFocusInput();
			HandleMouseInput();
			RefreshTargetPreview();
		}

		// Shared by the global cursor so battle enemies are marked without adding colliders
		// solely for pointer detection.
		public bool TryGetPawnAtScreenPosition(Vector2 screenPosition, out BattlePawn pawn)
		{
			pawn = null;
			if (TryGetAxialAtScreenPosition(screenPosition, out AxialCoord axial) == false)
				return false;

			return TryGetPawnAtAxial(axial, out _, out pawn);
		}

		public BattleCursorHint GetCursorHint(Vector2 screenPosition)
		{
			if (TryGetAxialAtScreenPosition(screenPosition, out AxialCoord axial) == false
				|| _mapGrid.IsTileInBounds(axial) == false)
			{
				return BattleCursorHint.Default;
			}

			if (TryGetPawnAtAxial(axial, out _, out BattlePawn hoveredPawn) == false)
				return BattleCursorHint.Default;

			if (_actionMode == BattleActionMode.Move || IsInteractionLocked)
				return BattleCursorHint.Default;

			int skillSlot = GetSkillSlot(_actionMode);
			if (skillSlot <= 0
				|| TryGetControllablePawnId(out ulong casterPawnId) == false
				|| TryGetPawn(casterPawnId, out BattlePawn casterPawn) == false
				|| TryGetSkillDefinition(casterPawn, skillSlot, out BattleSkillDefinition skill) == false)
			{
				return BattleCursorHint.Default;
			}

			string targetType = skill.TargetType ?? string.Empty;
			if (targetType.IndexOf("ENEMY", System.StringComparison.OrdinalIgnoreCase) >= 0 && hoveredPawn.IsMine == false)
				return BattleCursorHint.Attack;

			if (targetType.IndexOf("ALLY", System.StringComparison.OrdinalIgnoreCase) >= 0 && hoveredPawn.IsMine)
				return BattleCursorHint.Assist;

			if (targetType.StartsWith("SELF", System.StringComparison.OrdinalIgnoreCase)
				&& hoveredPawn.PawnId == casterPawnId)
			{
				return BattleCursorHint.Assist;
			}

			return BattleCursorHint.Default;
		}

		// The world cursor follows the pointer continuously, but is visible only when
		// the same path search used by an actual move accepts the hovered tile.
		public bool TryGetMoveCursorWorldPosition(Vector2 screenPosition, out Vector3 worldPosition)
		{
			worldPosition = default;
			if (_actionMode != BattleActionMode.Move || IsInteractionLocked
				|| TryGetAxialAtScreenPosition(screenPosition, out AxialCoord axial) == false
				|| TryGetControllablePawnId(out ulong pawnId) == false
				|| TryGetPawn(pawnId, out BattlePawn pawn) == false
				|| IsReachableMoveTarget(pawn, axial) == false)
			{
				return false;
			}

			Camera camera = Camera.main;
			if (camera == null)
				return false;

			Ray ray = camera.ScreenPointToRay(screenPosition);
			Plane mapPlane = new Plane(Vector3.forward, _mapGrid.PlaneTransform.position);
			if (mapPlane.Raycast(ray, out float enter) == false)
				return false;

			worldPosition = ray.GetPoint(enter);
			return true;
		}

		bool TryGetAxialAtScreenPosition(Vector2 screenPosition, out AxialCoord axial)
		{
			axial = default;
			if (_mapGrid == null)
				return false;

			Camera camera = Camera.main;
			if (camera == null
				|| screenPosition.x < 0f || screenPosition.y < 0f
				|| screenPosition.x > camera.pixelWidth || screenPosition.y > camera.pixelHeight)
			{
				return false;
			}

			Ray ray = camera.ScreenPointToRay(screenPosition);
			Plane mapPlane = new Plane(Vector3.forward, _mapGrid.PlaneTransform.position);
			if (mapPlane.Raycast(ray, out float enter) == false)
				return false;

			axial = _mapGrid.WorldToAxial(ray.GetPoint(enter));
			return true;
		}

		void HandleCameraFocusInput()
		{
			if (WasCameraFocusKeyPressed() == false
				|| _currentTurnPawnId == 0
				|| _pawns.TryGetValue(_currentTurnPawnId, out BattlePawn currentTurnPawn) == false
				|| currentTurnPawn == null)
			{
				return;
			}

			Camera camera = Camera.main;
			CameraController controller = camera != null ? camera.GetComponent<CameraController>() : null;
			controller?.FocusOn(currentTurnPawn.transform);
		}

		static bool WasCameraFocusKeyPressed()
		{
#if ENABLE_INPUT_SYSTEM
			Keyboard keyboard = Keyboard.current;
			return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
			return Input.GetKeyDown(KeyCode.Space);
#else
			return false;
#endif
		}

		void OnDestroy()
		{
			_destroyed = true;
			_isAnimatingMove = false;
			_isPlayingSkillActionSequence = false;
			if (_skillActionSequenceCoroutine != null)
				StopCoroutine(_skillActionSequenceCoroutine);
			if (_moveReactionSequenceCoroutine != null)
				StopCoroutine(_moveReactionSequenceCoroutine);
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

			if (IsControllablePawnActionBlocked())
			{
				Debug.Log("Cannot change battle action mode while the current pawn is action-blocked.");
				return;
			}

			ReleasePawns();
			_localPawnIds.Clear();
			_upcomingTurnPawnIds.Clear();
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

			ApplyTurnQueueSnapshot(packet.UpcomingTurnPawnIds, BattleTurnQueueUpdateKind.Initialize, null);
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
					case Protocol.PawnClass.SuenParvis:
						return pawnObject.AddComponent<SuenParvis>();
					case Protocol.PawnClass.AlenSpear:
						return pawnObject.AddComponent<AlenSpear>();
					case Protocol.PawnClass.AlenSwordShield:
						return pawnObject.AddComponent<AlenSwordShield>();
					case Protocol.PawnClass.ZillianLongbow:
						return pawnObject.AddComponent<ZillianLongbow>();
					case Protocol.PawnClass.ZillianMace:
						return pawnObject.AddComponent<ZillianMace>();
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

			if (IsControllablePawnActionBlocked())
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

			if (TryGetControllablePawnId(out ulong movingPawnId) == false)
			{
				Debug.Log($"No controllable battle pawn. currentTurnPawnId={_currentTurnPawnId}, battleId={_battleId}");
				return;
			}

			if (_pawns.TryGetValue(movingPawnId, out BattlePawn myPawn) == false)
				return;

			if (IsReachableMoveTarget(myPawn, axial) == false)
			{
				Debug.Log($"Clicked unreachable battle tile. pawnId={movingPawnId}, axial={axial}");
				return;
			}

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
				if (IsZillianMaceOptionalSwapSkill(casterPawn, skillSlot))
				{
					RequestOptionalPositionSwapChoice(casterPawnId, skillSlot, targetAxial, resolvedTargetPawnId);
					return;
				}

				// The server resolves the target Pawn from target_axial. The legacy
				// target_pawn_id field is intentionally sent as zero by NetworkService.
				SendBattleSkillRequest(casterPawnId, skillSlot, targetAxial, resolvedTargetPawnId, false);

				return;
			}

			Debug.Log($"Skill debug selected. casterPawnId={casterPawnId}, skillSlot={skillSlot}, resolvedTargetPawnId={resolvedTargetPawnId}, axial={targetAxial}");
			_actionMode = BattleActionMode.Move;
		}

		static bool IsZillianMaceOptionalSwapSkill(BattlePawn casterPawn, int skillSlot)
		{
			return casterPawn is ZillianMace && skillSlot == 7;
		}

		void RequestOptionalPositionSwapChoice(ulong casterPawnId, int skillSlot, AxialCoord targetAxial, ulong resolvedTargetPawnId)
		{
			if (_isAwaitingOptionalPositionSwap)
				return;

			_isAwaitingOptionalPositionSwap = true;
			System.Action<bool> resolveChoice = requestSwap =>
			{
				if (_isAwaitingOptionalPositionSwap == false)
					return;

				_isAwaitingOptionalPositionSwap = false;
				SendBattleSkillRequest(casterPawnId, skillSlot, targetAxial, resolvedTargetPawnId, requestSwap);
			};

			if (OptionalPositionSwapChoiceRequested != null)
			{
				OptionalPositionSwapChoiceRequested.Invoke(resolveChoice);
				return;
			}

			Debug.LogWarning("Optional position-swap UI is unavailable. Sending the skill without a swap request.");
			resolveChoice(false);
		}

		void SendBattleSkillRequest(ulong casterPawnId, int skillSlot, AxialCoord targetAxial, ulong resolvedTargetPawnId, bool requestOptionalPositionSwap)
		{
			if (_battleId == 0 || GameRoot.Instance == null)
				return;

			bool sent = GameRoot.Instance.Network.SendBattleSkill(
				_battleId,
				casterPawnId,
				skillSlot,
				targetAxial.Q,
				targetAxial.R,
				null,
				null,
				requestOptionalPositionSwap);
			if (sent)
			{
				_actionMode = BattleActionMode.WaitingServer;
				Debug.Log($"Sent C_BATTLE_SKILL. battleId={_battleId}, casterPawnId={casterPawnId}, skillSlot={skillSlot}, targetPawnId=0, axial={targetAxial}, locallyResolvedPawnId={resolvedTargetPawnId}, requestOptionalPositionSwap={requestOptionalPositionSwap}");
			}
			else
			{
				Debug.LogWarning($"Failed to send C_BATTLE_SKILL. {GameRoot.Instance.Network.LastError}");
			}
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
			{
				if (targetAxial.Equals(casterPawn.Axial) == false)
				{
					Debug.Log($"Self skill must target the caster tile. casterPawnId={casterPawn.PawnId}, skillSlot={skillSlot}, selected={targetAxial}, self={casterPawn.Axial}");
					return false;
				}
			}

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

					if (IsParvisInstallSkill(skill) && _mapGrid.TryGetEquipment(targetAxial, out _, out _))
					{
						Debug.Log($"Parvis installation requires a tile without equipment. casterPawnId={casterPawn.PawnId}, axial={targetAxial}");
						return false;
					}

					return true;
				case "PICKUP_TILE":
					if (casterPawn is SuenParvis parvis && parvis.IsParvisOff == false)
						return false;

					string pickupEquipmentKey = GetPickupEquipmentKey(casterPawn);
					if (_mapGrid.HasEquipment(targetAxial, pickupEquipmentKey, casterPawn.PawnId) == false)
					{
						Debug.Log($"Skill requires the caster's {pickupEquipmentKey} equipment tile. casterPawnId={casterPawn.PawnId}, skillSlot={skillSlot}, axial={targetAxial}");
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
			if (TryGetSkillDefinition(casterPawn, skillSlot, out BattleSkillDefinition skill))
			{
				return skill.TargetType;
			}

			return string.Empty;
		}

		public bool TryGetSkillDefinition(BattlePawn casterPawn, int skillSlot, out BattleSkillDefinition skill)
		{
			skill = null;
			if (_gameData == null || casterPawn == null || casterPawn.Info == null)
				return false;

			if (casterPawn is SuenAxe suenAxe
				&& skillSlot == 7
				&& suenAxe.IsAxeOff == false)
			{
				// Pickup is valid only after throwing the axe. Keep the client from
				// resolving the static slot-7 CSV row while the axe is equipped.
				return false;
			}

			if (casterPawn is SuenParvis parvis)
			{
				if (parvis.TryGetActiveSkillKey(skillSlot, out string activeSkillKey))
					return _gameData.TryGetSkill(activeSkillKey, out skill);

				// Slot 7 is intentionally unavailable while Parvis is equipped. Do not
				// fall back to the static CSV row, which represents pickup only.
				return false;
			}

			return _gameData.TryGetSkill(casterPawn.Info.PawnClass, skillSlot, out skill);
		}

		static bool IsParvisInstallSkill(BattleSkillDefinition skill)
		{
			return skill != null && string.Equals(skill.SkillKey, "SUEN_PARVIS_INSTALL", System.StringComparison.OrdinalIgnoreCase);
		}

		static string GetPickupEquipmentKey(BattlePawn casterPawn)
		{
			return casterPawn is SuenParvis ? "PARVIS" : "AXE";
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

			if (IsControllablePawnActionBlocked())
			{
				_targetPreview.Hide();
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
				_targetPreview.ShowSkillRange(_mapGrid, _affectedTargetTiles, _validTargetTiles, _affectedTargetTiles);
				return;
			}

			_skillRangeTiles.Clear();
			if (skill.TargetType == "SELF" || skill.TargetType == "SELF_TOGGLE")
			{
				_skillRangeTiles.Add(casterPawn.Axial);
			}
			else
			{
				_mapGrid.GetKnownTileAxials(_knownTargetTiles);
				for (int i = 0; i < _knownTargetTiles.Count; i++)
				{
					AxialCoord axial = _knownTargetTiles[i];
					if (IsWithinSkillRange(casterPawn, skill, axial))
						_skillRangeTiles.Add(axial);
				}
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

			_targetPreview.ShowSkillRange(_mapGrid, _skillRangeTiles, _validTargetTiles, _affectedTargetTiles);
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
			_hoveredZocTiles.Clear();
			_zocAttackerTiles.Clear();
			BuildReachableMoveTiles(movingPawn, _validTargetTiles);

			// Leaving an enemy ZoC triggers from the mover's current tile, not from a
			// particular destination. Show the threatened attackers as soon as movement
			// mode is available so the warning is visible before the cursor moves.
			CollectMoveZocPreview(movingPawn);
			if (TryGetPointerAxial(out AxialCoord hoveredAxial))
				CollectHoveredZocRangePreview(movingPawn, hoveredAxial, _hoveredZocTiles);

			_targetPreview.ShowMoveWithZoc(_mapGrid, _validTargetTiles, _hoveredZocTiles, _zocAttackerTiles);
		}

		bool IsReachableMoveTarget(BattlePawn movingPawn, AxialCoord targetAxial)
		{
			BuildReachableMoveTiles(movingPawn, _reachableMoveTiles);
			return _reachableMoveTiles.Contains(targetAxial);
		}

		// Uses the same blocked-tile and occupant rules as the movement preview. This
		// intentionally searches paths rather than axial distance, so a pawn cannot
		// select a tile behind an obstacle or another pawn.
		void BuildReachableMoveTiles(BattlePawn movingPawn, List<AxialCoord> reachableTiles)
		{
			reachableTiles.Clear();
			_moveSearchQueue.Clear();
			_moveSearchVisited.Clear();

			if (_mapGrid == null || movingPawn == null || movingPawn.IsDead || movingPawn.CanMove == false || movingPawn.MoveRange <= 0)
				return;

			_moveSearchVisited.Add(movingPawn.Axial);
			_moveSearchQueue.Enqueue(new MoveSearchNode(movingPawn.Axial, 0));

			while (_moveSearchQueue.Count > 0)
			{
				MoveSearchNode current = _moveSearchQueue.Dequeue();
				if (current.Steps >= movingPawn.MoveRange)
					continue;

				for (int direction = 0; direction < 6; direction++)
				{
					AxialCoord next = _mapGrid.GetNeighbor(current.Axial, direction);
					if (_moveSearchVisited.Add(next) == false)
						continue;

					// A blocked or occupied tile is not a destination and cannot be passed through.
					if (_mapGrid.IsWalkable(next) == false || FindPawnIdAtAxial(next) != 0)
						continue;

					reachableTiles.Add(next);
					_moveSearchQueue.Enqueue(new MoveSearchNode(next, current.Steps + 1));
				}
			}
		}

		void CollectMoveZocPreview(BattlePawn movingPawn)
		{
			if (movingPawn == null)
				return;

			foreach (KeyValuePair<ulong, BattlePawn> pair in _pawns)
			{
				BattlePawn potentialAttacker = pair.Value;
				if (potentialAttacker == null
					|| potentialAttacker.IsDead
					|| potentialAttacker.IsMine == movingPawn.IsMine)
					continue;

				if (_gameData == null
					|| potentialAttacker.Info == null
					|| _gameData.TryGetZocProfile(potentialAttacker.Info.PawnClass, out BattleZocDefinition profile) == false
					|| profile.Enabled == false
					|| profile.TriggersOnEnemyMove == false
					|| profile.ReactionLimitPerTurn <= 0
					|| potentialAttacker.ZocReactionsUsedThisTurn >= profile.ReactionLimitPerTurn)
				{
					continue;
				}

				GetZocTiles(potentialAttacker, profile, _affectedTargetTiles);
				if (_affectedTargetTiles.Contains(movingPawn.Axial) == false)
					continue;

				AddAffectedTile(potentialAttacker.Axial, _zocAttackerTiles);
			}
		}

		void CollectHoveredZocRangePreview(BattlePawn movingPawn, AxialCoord hoveredAxial, List<AxialCoord> destination)
		{
			destination.Clear();
			if (movingPawn == null || _zocAttackerTiles.Contains(hoveredAxial) == false)
				return;

			ulong attackerPawnId = FindPawnIdAtAxial(hoveredAxial);
			if (attackerPawnId == 0
				|| _pawns.TryGetValue(attackerPawnId, out BattlePawn attackerPawn) == false
				|| attackerPawn == null
				|| attackerPawn.Info == null
				|| _gameData == null
				|| _gameData.TryGetZocProfile(attackerPawn.Info.PawnClass, out BattleZocDefinition profile) == false)
			{
				return;
			}

			GetZocTiles(attackerPawn, profile, destination);
		}

		void GetZocTiles(BattlePawn pawn, BattleZocDefinition profile, List<AxialCoord> destination)
		{
			destination.Clear();
			if (pawn == null || _mapGrid == null || profile == null)
				return;

			int range = profile.Range;
			if (range <= 0 || TryGetFacingDirectionIndex(pawn.FacingDirection, out int forwardDirection) == false)
				return;

			int sideDirectionCount = Mathf.Max(0, profile.FrontArcWidth - 1) / 2;

			_zocFrontier.Clear();
			_zocNextFrontier.Clear();
			_zocFrontier.Add(pawn.Axial);
			HashSet<AxialCoord> currentFrontier = _zocFrontier;
			HashSet<AxialCoord> nextFrontier = _zocNextFrontier;
			for (int distance = 1; distance <= range; distance++)
			{
				nextFrontier.Clear();
				foreach (AxialCoord source in currentFrontier)
				{
					for (int offset = -sideDirectionCount; offset <= sideDirectionCount; offset++)
					{
						AxialCoord axial = _mapGrid.GetNeighbor(source, forwardDirection + offset);
						if (_mapGrid.IsTileInBounds(axial) == false || pawn.Axial.DistanceTo(axial) != distance)
							continue;

						nextFrontier.Add(axial);
					}
				}

				foreach (AxialCoord axial in nextFrontier)
					AddAffectedTile(axial, destination);

				HashSet<AxialCoord> swap = currentFrontier;
				currentFrontier = nextFrontier;
				nextFrontier = swap;
			}
		}

		static bool TryGetFacingDirectionIndex(Protocol.BattleFacingDirection facingDirection, out int directionIndex)
		{
			directionIndex = (int)facingDirection - 1;
			return directionIndex >= 0 && directionIndex < 6;
		}

		bool IsControllablePawnActionBlocked()
		{
			return TryGetControllablePawnId(out ulong pawnId)
				&& _pawns.TryGetValue(pawnId, out BattlePawn pawn)
				&& pawn != null
				&& pawn.IsActionBlocked;
		}

		bool IsValidSkillPreviewTarget(BattlePawn casterPawn, BattleSkillDefinition skill, AxialCoord targetAxial)
		{
			if (casterPawn == null || skill == null || _mapGrid == null)
				return false;

			string targetType = skill.TargetType;
			if (IsSelfCenteredAdjacentSkill(skill))
				return _mapGrid.IsTileInBounds(targetAxial)
					&& casterPawn.Axial.DistanceTo(targetAxial) <= 1;

			if ((targetType == "SELF" || targetType == "SELF_TOGGLE") && targetAxial.Equals(casterPawn.Axial) == false)
				return false;

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
					return targetPawn == null && (IsParvisInstallSkill(skill) == false || _mapGrid.TryGetEquipment(targetAxial, out _, out _) == false);
				case "PICKUP_TILE":
					return (casterPawn is SuenParvis parvis && parvis.IsParvisOff == false) == false
						&& _mapGrid.HasEquipment(targetAxial, GetPickupEquipmentKey(casterPawn), casterPawn.PawnId);
				default:
					return targetPawn == null || targetPawn.IsMine == false;
			}
		}

		bool IsWithinSkillRange(BattlePawn casterPawn, BattleSkillDefinition skill, AxialCoord targetAxial)
		{
			return casterPawn != null
				&& skill != null
				&& _mapGrid != null
				&& _mapGrid.IsTileInBounds(targetAxial)
				&& casterPawn.Axial.DistanceTo(targetAxial) >= skill.RangeMin
				&& casterPawn.Axial.DistanceTo(targetAxial) <= skill.RangeMax;
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

			int lineLength = GetTargetLineLength(skill.TargetShape);
			if (lineLength <= 1 || casterPawn.Axial.DistanceTo(targetAxial) <= 0)
				return;

			int directionIndex = FindDirectionIndex(casterPawn.Axial, targetAxial);
			if (directionIndex < 0)
				return;

			AxialCoord next = targetAxial;
			for (int distance = 1; distance < lineLength; distance++)
			{
				next = _mapGrid.GetNeighbor(next, directionIndex);
				AddAffectedTile(next, destination);
			}
		}

		static int GetTargetLineLength(string targetShape)
		{
			if (string.Equals(targetShape, "LINE_2", System.StringComparison.OrdinalIgnoreCase))
				return 2;
			if (string.Equals(targetShape, "LINE_3", System.StringComparison.OrdinalIgnoreCase))
				return 3;
			return 0;
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
			if (packet.Start != null)
			{
				AxialCoord startAxial = ToBattleAxial(packet.Start);
				if (pawn.Axial.Equals(startAxial) == false)
					pawn.SetAxial(startAxial);
			}

			List<AxialCoord> movePath = new List<AxialCoord>();
			foreach (Protocol.AxialCoord pathStep in packet.Path)
			{
				if (pathStep != null)
					movePath.Add(ToBattleAxial(pathStep));
			}

			// Older servers do not populate path. Preserve their direct start-to-target
			// presentation until every server has upgraded.
			if (movePath.Count == 0)
				movePath.Add(targetAxial);

			ulong nextTurnPawnId = packet.NextTurnPawnId;
			ulong appliedStateVersion = _battleStateVersion;

			List<BattleActionLog> moveLogs = CopyBattleActionLogs(packet.Logs);
			List<BattlePawnDelta> movePawnDeltas = CopyBattlePawnDeltas(packet.PawnDeltas);
			if (packet.TurnQueueResynced)
				ApplyTurnQueueSnapshot(packet.UpcomingTurnPawnIds, BattleTurnQueueUpdateKind.Resync, CollectDeadPawnIds(packet.PawnDeltas));

			_isAnimatingMove = true;
			RefreshTurnIndicators();
			_moveReactionSequenceCoroutine = StartCoroutine(PlayMovePathAndResolveSequence(
				pawn,
				movePath,
				moveLogs,
				movePawnDeltas,
				nextTurnPawnId,
				appliedStateVersion));

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

			List<Protocol.BattleTileInfo> tileDeltas = CopyBattleTileDeltas(packet.TileDeltas);
			bool deferThrownAxeLanding = ShouldDeferThrownAxeLanding(packet.CasterPawnId, packet.SkillSlot, tileDeltas);
			if (deferThrownAxeLanding == false)
				_mapGrid?.ApplyTileDeltas(tileDeltas);
			// target_pawn_id == 0 means a tile-only result. Pawn state always comes from
			// pawn_deltas, so no target Pawn lookup or direct HP update is performed here.
			QueueSkillActionSequence(
				packet.CasterPawnId,
				packet.SkillSlot,
				packet.TargetAxial,
				packet.Logs,
				packet.PawnDeltas,
				deferThrownAxeLanding ? tileDeltas : null);
			if (packet.TurnQueueResynced)
				ApplyTurnQueueSnapshot(packet.UpcomingTurnPawnIds, BattleTurnQueueUpdateKind.Resync, CollectDeadPawnIds(packet.PawnDeltas));

			_currentTurnPawnId = packet.NextTurnPawnId;
			RefreshTurnIndicators();
			Debug.Log($"Applied S_BATTLE_SKILL. casterPawnId={packet.CasterPawnId}, skillSlot={packet.SkillSlot}, targetPawnId={packet.TargetPawnId}, targetKind={(packet.TargetPawnId == 0 ? "Tile" : "Pawn")}, damage={packet.Damage}, battleStateVersion={_battleStateVersion}, nextTurnPawnId={_currentTurnPawnId}");
		}

		void TriggerSkillAnimation(BattlePawn casterPawn, int skillSlot)
		{
			if (casterPawn == null)
				return;

			if (ShouldSkipSkillAnimation(casterPawn, skillSlot))
				return;

			if (TryGetSkillDefinition(casterPawn, skillSlot, out BattleSkillDefinition skill)
				&& _gameData.TryGetSkillView(skill.SkillKey, out BattleSkillViewDefinition view)
				&& string.IsNullOrWhiteSpace(view.AnimTrigger) == false)
			{
				casterPawn.TriggerSkill(view.AnimTrigger);
				return;
			}

			casterPawn.TriggerSkill(skillSlot);
		}

		static bool ShouldSkipSkillAnimation(BattlePawn casterPawn, int skillSlot)
		{
			// Equipment interactions do not have a cast gesture. In particular, Axe
			// pickup must apply its status delta right away so the held-axe sprite
			// changes without the generic skill presentation delay.
			if (casterPawn is SuenAxe && skillSlot == 7)
				return true;

			// When Parvis is installed, slot 2 changes to Sit Shot and must still
			// play its Skill2 gesture.
			return casterPawn is SuenParvis parvis
				&& ((skillSlot == 2 && parvis.IsParvisOff == false) || skillSlot == 7);
		}

		void QueueSkillActionSequence(
			ulong casterPawnId,
			int skillSlot,
			Protocol.AxialCoord targetAxial,
			IEnumerable<BattleActionLog> logs,
			IEnumerable<BattlePawnDelta> pawnDeltas,
			IEnumerable<Protocol.BattleTileInfo> deferredTileDeltas = null)
		{
			if (_skillActionSequenceCoroutine != null)
				StopCoroutine(_skillActionSequenceCoroutine);

			List<BattleActionLog> orderedLogs = CopyBattleActionLogs(logs);
			List<BattlePawnDelta> finalPawnDeltas = CopyBattlePawnDeltas(pawnDeltas);
			List<Protocol.BattleTileInfo> delayedTiles = CopyBattleTileDeltas(deferredTileDeltas);
			// The server supplies facing_direction for every attacker in the final
			// snapshot. Commit it before presentation so attacks, counters, ZOC
			// reactions, and evaded attacks all face their intended target.
			ApplyPawnFacingDirections(finalPawnDeltas);

			// Death packets can arrive in the same network frame as this result. Mark
			// the presentation as active before the coroutine gets its first update so
			// a lethal final state never hides a pawn ahead of the exchange animation.
			_isPlayingSkillActionSequence = true;
			_skillActionSequenceCoroutine = StartCoroutine(PlaySkillActionSequence(casterPawnId, skillSlot, targetAxial, orderedLogs, finalPawnDeltas, delayedTiles));
		}

		IEnumerator PlaySkillActionSequence(
			ulong casterPawnId,
			int skillSlot,
			Protocol.AxialCoord targetAxial,
			List<BattleActionLog> orderedLogs,
			List<BattlePawnDelta> finalPawnDeltas,
			List<Protocol.BattleTileInfo> delayedTileDeltas)
		{
			_isPlayingSkillActionSequence = true;
			bool didPresentInitiatingSkill = false;
			bool didApplyDelayedTiles = false;
			List<BattleActionLog> fireTileLogs = new List<BattleActionLog>();
			for (int i = 0; i < orderedLogs.Count; i++)
			{
				BattleActionLog log = orderedLogs[i];
				if (log == null)
					continue;

				// Environment damage must be presented after the final pawn deltas: a
				// charge, push, teleport, or swap may have changed the defender's tile.
				if (IsFireTileDamageLog(log))
				{
					fireTileLogs.Add(log);
					continue;
				}

				if (log.IsCounter)
				{
					if (_pawns.TryGetValue(log.AttackerPawnId, out BattlePawn counterPawn))
						counterPawn.TriggerSkill("Skill1");

				}
				else if (log.AttackerPawnId == casterPawnId)
				{
					if (didPresentInitiatingSkill == false
						&& _pawns.TryGetValue(casterPawnId, out BattlePawn casterPawn))
					{
						TriggerSkillAnimation(casterPawn, skillSlot);
						didPresentInitiatingSkill = true;
					}

				}
				else if (IsZocReactionLog(log))
				{
					// Sentinel's interrupt is delivered in the skill result as a normal
					// zoc action, not as a counter. Present it on its own log beat.
					TriggerZocReactionPresentation(log);
				}
				else
				{
					TriggerLoggedAttackPresentation(log);
				}

				yield return PlayAttackPresentation(log, log.AttackerPawnId == casterPawnId ? skillSlot : 0, null);
				// Weapon Technique's tile delta contains the thrown axe. Apply it only
				// after the projectile completes its flight, so the ground axe appears
				// at the moment it reaches the target rather than at cast start.
				if (didApplyDelayedTiles == false
					&& delayedTileDeltas.Count > 0
					&& log.AttackerPawnId == casterPawnId)
				{
					_mapGrid?.ApplyTileDeltas(delayedTileDeltas);
					didApplyDelayedTiles = true;
				}
				ApplyCombatLogPresentation(log);
				AppendBattleLog(log);
				yield return new WaitForSecondsRealtime(SkillActionPresentationSeconds);
			}

			if (didPresentInitiatingSkill == false && _pawns.TryGetValue(casterPawnId, out BattlePawn noLogCasterPawn))
			{
				if (ShouldSkipSkillAnimation(noLogCasterPawn, skillSlot) == false)
				{
					TriggerSkillAnimation(noLogCasterPawn, skillSlot);
					yield return PlaySkillProjectilePresentation(noLogCasterPawn, skillSlot, targetAxial);
					if (didApplyDelayedTiles == false && delayedTileDeltas.Count > 0)
					{
						_mapGrid?.ApplyTileDeltas(delayedTileDeltas);
						didApplyDelayedTiles = true;
					}
					yield return new WaitForSecondsRealtime(SkillActionPresentationSeconds);
				}
			}

			if (didApplyDelayedTiles == false && delayedTileDeltas.Count > 0)
				_mapGrid?.ApplyTileDeltas(delayedTileDeltas);

			ulong instantMovePawnId = IsBeigeFireTeleport(casterPawnId, skillSlot) ? casterPawnId : 0;
			ApplyPawnDeltas(finalPawnDeltas, instantMovePawnId);
			PresentZillianMaceStunSuccess(casterPawnId, skillSlot, finalPawnDeltas);
			yield return PresentFireTileDamageSequence(fireTileLogs);
			ApplyDeferredPawnDeaths();
			_isPlayingSkillActionSequence = false;
			_skillActionSequenceCoroutine = null;
		}

		void PresentZillianMaceStunSuccess(ulong casterPawnId, int skillSlot, IEnumerable<BattlePawnDelta> pawnDeltas)
		{
			if (skillSlot != 3
				|| _pawns.TryGetValue(casterPawnId, out BattlePawn casterPawn) == false
				|| casterPawn is ZillianMace == false
				|| pawnDeltas == null)
			{
				return;
			}

			foreach (BattlePawnDelta delta in pawnDeltas)
			{
				if (delta == null || DeltaContainsStun(delta) == false)
					continue;

				if (_pawns.TryGetValue(delta.PawnId, out BattlePawn stunnedPawn) && stunnedPawn != null)
					stunnedPawn.PlayControlSuccessPresentation("STUN!");
			}
		}

		static bool DeltaContainsStun(BattlePawnDelta delta)
		{
			if (delta?.Statuses == null)
				return false;

			foreach (BattleStatusState status in delta.Statuses)
			{
				if (status != null && string.Equals(status.StatusKey, "STUN", System.StringComparison.OrdinalIgnoreCase))
					return true;
			}

			return false;
		}

		IEnumerator PlayMovePathAndResolveSequence(
			BattlePawn movingPawn,
			IReadOnlyList<AxialCoord> path,
			List<BattleActionLog> moveLogs,
			List<BattlePawnDelta> finalPawnDeltas,
			ulong nextTurnPawnId,
			ulong appliedStateVersion)
		{
			Queue<BattleActionLog> fireTileLogs = new Queue<BattleActionLog>();
			List<BattleActionLog> reactionLogs = new List<BattleActionLog>();
			for (int i = 0; i < moveLogs.Count; i++)
			{
				BattleActionLog log = moveLogs[i];
				if (IsFireTileDamageLog(log))
					fireTileLogs.Enqueue(log);
				else
					reactionLogs.Add(log);
			}

			for (int i = 0; i < path.Count; i++)
			{
				bool stepCompleted = false;
				AxialCoord step = path[i];
				movingPawn.MoveToAxial(step, () => stepCompleted = true);
				while (stepCompleted == false)
					yield return null;

				// The server supplies one fire_tile log per damaging landing. Use the
				// replicated overlay only to align that log with the matching path step;
				// damage values remain entirely server-authoritative.
				if (fireTileLogs.Count > 0
					&& _mapGrid != null
					&& _mapGrid.HasOverlay(step, Protocol.BattleTileOverlayType.Fire))
				{
					PresentFireTileDamage(fireTileLogs.Dequeue());
				}
			}

			// A stale/missing local overlay must never suppress a server-confirmed
			// environment hit. Present any unmatched logs at the final landed tile.
			while (fireTileLogs.Count > 0)
			{
				PresentFireTileDamage(fireTileLogs.Dequeue());
				yield return new WaitForSecondsRealtime(0.1f);
			}

			_isAnimatingMove = false;
			if (_battleStateVersion != appliedStateVersion)
			{
				RefreshTurnIndicators();
				_moveReactionSequenceCoroutine = null;
				yield break;
			}

			if (reactionLogs.Count > 0)
			{
				_isPlayingSkillActionSequence = true;
				yield return PlayMoveReactionSequence(reactionLogs, finalPawnDeltas, nextTurnPawnId, appliedStateVersion);
			}
			else
			{
				ApplyPawnDeltas(finalPawnDeltas);
				CompleteMoveResult(nextTurnPawnId);
			}

			_moveReactionSequenceCoroutine = null;
		}

		IEnumerator PlayMoveReactionSequence(
			List<BattleActionLog> orderedLogs,
			List<BattlePawnDelta> finalPawnDeltas,
			ulong nextTurnPawnId,
			ulong appliedStateVersion)
		{
			List<BattleActionLog> fireTileLogs = new List<BattleActionLog>();
			for (int i = 0; i < orderedLogs.Count; i++)
			{
				BattleActionLog log = orderedLogs[i];
				if (log == null)
					continue;

				if (IsFireTileDamageLog(log))
				{
					fireTileLogs.Add(log);
					continue;
				}

				bool isZocReaction = IsZocReactionLog(log) || (i == 0 && log.IsCounter == false);
				if (isZocReaction)
					TriggerZocReactionPresentation(log);
				else if (log.IsCounter && _pawns.TryGetValue(log.AttackerPawnId, out BattlePawn counterPawn))
				{
					counterPawn.TriggerSkill("Skill1");
				}
				else
				{
					TriggerLoggedAttackPresentation(log);
				}

				yield return PlayAttackPresentation(log, 0, null);
				ApplyCombatLogPresentation(log);
				AppendBattleLog(log);
				yield return new WaitForSecondsRealtime(SkillActionPresentationSeconds);
			}

			if (_battleStateVersion == appliedStateVersion)
			{
				ApplyPawnDeltas(finalPawnDeltas);
				yield return PresentFireTileDamageSequence(fireTileLogs);
				CompleteMoveResult(nextTurnPawnId);
			}

			_isPlayingSkillActionSequence = false;
		}

		void TriggerZocReactionPresentation(BattleActionLog log)
		{
			if (log == null || _pawns.TryGetValue(log.AttackerPawnId, out BattlePawn attackerPawn) == false)
				return;

			int reactionSkillSlot = log.SkillSlot;
			if (_gameData != null
				&& attackerPawn.Info != null
				&& _gameData.TryGetZocProfile(attackerPawn.Info.PawnClass, out BattleZocDefinition profile)
				&& profile.ReactionSkillSlot > 0)
			{
				reactionSkillSlot = profile.ReactionSkillSlot;
			}

			if (reactionSkillSlot > 0)
				TriggerSkillAnimation(attackerPawn, reactionSkillSlot);
			else
				attackerPawn.TriggerSkill("Skill1");

		}

		void TriggerLoggedAttackPresentation(BattleActionLog log)
		{
			if (log == null || _pawns.TryGetValue(log.AttackerPawnId, out BattlePawn attackerPawn) == false)
				return;

			if (log.SkillSlot > 0)
				TriggerSkillAnimation(attackerPawn, log.SkillSlot);
			else
				attackerPawn.TriggerSkill("Skill1");

		}

		static bool IsZocReactionLog(BattleActionLog log)
		{
			return log != null
				&& string.IsNullOrWhiteSpace(log.ActionType) == false
				&& log.ActionType.IndexOf("ZOC", System.StringComparison.OrdinalIgnoreCase) >= 0;
		}

		static bool IsFireTileDamageLog(BattleActionLog log)
		{
			return log != null
				&& log.AttackerPawnId == 0
				&& string.Equals(log.ActionType, "fire_tile", System.StringComparison.OrdinalIgnoreCase);
		}

		IEnumerator PresentFireTileDamageSequence(IEnumerable<BattleActionLog> fireTileLogs)
		{
			if (fireTileLogs == null)
				yield break;

			foreach (BattleActionLog log in fireTileLogs)
			{
				if (log == null)
					continue;

				PresentFireTileDamage(log);
				yield return new WaitForSecondsRealtime(SkillActionPresentationSeconds);
			}
		}

		void PresentFireTileDamage(BattleActionLog log)
		{
			if (IsFireTileDamageLog(log) == false)
				return;

			if (_pawns.TryGetValue(log.DefenderPawnId, out BattlePawn defenderPawn) && defenderPawn != null)
				_fireTileEffectPresenter?.Play(defenderPawn.transform.position);

			ApplyCombatLogPresentation(log);
			AppendBattleLog(log);
		}

		void CompleteMoveResult(ulong nextTurnPawnId)
		{
			_currentTurnPawnId = nextTurnPawnId;
			_actionMode = BattleActionMode.Move;
			RefreshTurnIndicators();
		}

		static List<BattleActionLog> CopyBattleActionLogs(IEnumerable<BattleActionLog> logs)
		{
			List<BattleActionLog> result = new List<BattleActionLog>();
			if (logs == null)
				return result;

			foreach (BattleActionLog log in logs)
			{
				if (log != null)
					result.Add(log);
			}

			return result;
		}

		static List<BattlePawnDelta> CopyBattlePawnDeltas(IEnumerable<BattlePawnDelta> pawnDeltas)
		{
			List<BattlePawnDelta> result = new List<BattlePawnDelta>();
			if (pawnDeltas == null)
				return result;

			foreach (BattlePawnDelta delta in pawnDeltas)
			{
				if (delta != null)
					result.Add(delta);
			}

			return result;
		}

		static List<Protocol.BattleTileInfo> CopyBattleTileDeltas(IEnumerable<Protocol.BattleTileInfo> tileDeltas)
		{
			List<Protocol.BattleTileInfo> result = new List<Protocol.BattleTileInfo>();
			if (tileDeltas == null)
				return result;

			foreach (Protocol.BattleTileInfo tileDelta in tileDeltas)
			{
				if (tileDelta != null)
					result.Add(tileDelta.Clone());
			}

			return result;
		}

		bool ShouldDeferThrownAxeLanding(
			ulong casterPawnId,
			int skillSlot,
			IEnumerable<Protocol.BattleTileInfo> tileDeltas)
		{
			if (tileDeltas == null
				|| _pawns.TryGetValue(casterPawnId, out BattlePawn casterPawn) == false
				|| casterPawn is SuenAxe == false
				|| TryGetProjectileKey(casterPawn, skillSlot, out string projectileKey) == false
				|| string.Equals(projectileKey, "throwing_axe", System.StringComparison.OrdinalIgnoreCase) == false)
			{
				return false;
			}

			foreach (Protocol.BattleTileInfo tileDelta in tileDeltas)
			{
				if (tileDelta != null
					&& string.Equals(tileDelta.EquipmentKey, "AXE", System.StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		static List<ulong> CollectDeadPawnIds(IEnumerable<BattlePawnDelta> pawnDeltas)
		{
			List<ulong> deadPawnIds = new List<ulong>();
			if (pawnDeltas == null)
				return deadPawnIds;

			foreach (BattlePawnDelta delta in pawnDeltas)
			{
				if (delta != null && delta.IsDead && delta.PawnId != 0)
					deadPawnIds.Add(delta.PawnId);
			}

			return deadPawnIds;
		}

		void ApplyTurnQueueSnapshot(
			IEnumerable<ulong> pawnIds,
			BattleTurnQueueUpdateKind kind,
			IReadOnlyCollection<ulong> deadPawnIds)
		{
			if (pawnIds == null)
				return;

			_upcomingTurnPawnIds.Clear();
			foreach (ulong pawnId in pawnIds)
			{
				_upcomingTurnPawnIds.Add(pawnId);
				if (_upcomingTurnPawnIds.Count == 8)
					break;
			}

			if (_upcomingTurnPawnIds.Count == 0)
			{
				Debug.LogWarning($"Received an empty upcoming turn queue. source={kind}");
				return;
			}

			if (_upcomingTurnPawnIds.Count != 8)
				Debug.LogWarning($"Expected an 8-slot upcoming turn queue, but received {_upcomingTurnPawnIds.Count}. source={kind}");

			if (kind == BattleTurnQueueUpdateKind.Initialize
				&& _currentTurnPawnId != 0
				&& _upcomingTurnPawnIds[0] != _currentTurnPawnId)
			{
				Debug.LogWarning($"Turn queue current pawn mismatch. queue[0]={_upcomingTurnPawnIds[0]}, currentTurnPawnId={_currentTurnPawnId}");
			}

			TurnQueueUpdated?.Invoke(new BattleTurnQueueUpdate(
				new List<ulong>(_upcomingTurnPawnIds),
				kind,
				deadPawnIds ?? new List<ulong>()));
		}

		void ApplyTurnQueueShift(ulong enteringTurnPawnId)
		{
			// The server only sends the entering pawn for the ordinary one-step rotation.
			// Do not invent a queue when no authoritative 8-slot base snapshot exists.
			if (enteringTurnPawnId == 0 || _upcomingTurnPawnIds.Count != 8)
			{
				Debug.LogWarning($"Cannot shift the turn queue. entering={enteringTurnPawnId}, localCount={_upcomingTurnPawnIds.Count}");
				return;
			}

			_upcomingTurnPawnIds.RemoveAt(0);
			_upcomingTurnPawnIds.Add(enteringTurnPawnId);
			TurnQueueUpdated?.Invoke(new BattleTurnQueueUpdate(
				new List<ulong>(_upcomingTurnPawnIds),
				BattleTurnQueueUpdateKind.Shift,
				new List<ulong>()));
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
				|| (attackerPawn.IsMelee == false && (attackerPawn is SuenParvis == false || log.SkillSlot != 4))
				|| _pawns.TryGetValue(log.DefenderPawnId, out BattlePawn defenderPawn) == false)
				return;

			attackerPawn.PlayMeleeAttackPresentation(defenderPawn.transform.position);
		}

		IEnumerator PlayAttackPresentation(BattleActionLog log, int fallbackSkillSlot, Protocol.AxialCoord fallbackTargetAxial)
		{
			if (log == null
				|| _pawns.TryGetValue(log.AttackerPawnId, out BattlePawn attackerPawn) == false
				|| attackerPawn == null)
			{
				yield break;
			}

			int skillSlot = log.SkillSlot > 0 ? log.SkillSlot : fallbackSkillSlot;
			if (TryGetSkillVfxKey(attackerPawn, skillSlot, out string vfxKey)
				&& TryGetProjectileTargetWorldPosition(log.DefenderPawnId, fallbackTargetAxial, out Vector3 effectWorldPosition)
				&& _skillEffectPresenter != null)
			{
				yield return _skillEffectPresenter.Play(vfxKey, effectWorldPosition, GetSkillVfxScaleOverride(attackerPawn, skillSlot));
			}

			if (TryGetProjectileKey(attackerPawn, skillSlot, out string projectileKey)
				&& TryGetProjectileTargetWorldPosition(log.DefenderPawnId, fallbackTargetAxial, out Vector3 targetWorldPosition)
				&& _projectilePresenter != null)
			{
				yield return _projectilePresenter.Play(projectileKey, attackerPawn.GetProjectileOriginWorldPosition(), targetWorldPosition);
				yield break;
			}

			PlayMeleeAttackPresentation(log);
		}

		IEnumerator PlaySkillProjectilePresentation(BattlePawn casterPawn, int skillSlot, Protocol.AxialCoord targetAxial)
		{
			if (casterPawn == null)
			{
				yield break;
			}

			bool hasTargetWorldPosition = TryGetProjectileTargetWorldPosition(0, targetAxial, out Vector3 targetWorldPosition);
			if (hasTargetWorldPosition == false)
				targetWorldPosition = casterPawn.transform.position;

			if (TryGetSkillVfxKey(casterPawn, skillSlot, out string vfxKey) && _skillEffectPresenter != null)
				yield return _skillEffectPresenter.Play(vfxKey, targetWorldPosition, GetSkillVfxScaleOverride(casterPawn, skillSlot));

			if (hasTargetWorldPosition
				&& TryGetProjectileKey(casterPawn, skillSlot, out string projectileKey)
				&& _projectilePresenter != null)
				yield return _projectilePresenter.Play(projectileKey, casterPawn.GetProjectileOriginWorldPosition(), targetWorldPosition);
		}

		bool TryGetSkillVfxKey(BattlePawn casterPawn, int skillSlot, out string vfxKey)
		{
			vfxKey = null;
			if (casterPawn == null
				|| skillSlot <= 0
				|| TryGetSkillDefinition(casterPawn, skillSlot, out BattleSkillDefinition skill) == false
				|| _gameData == null
				|| _gameData.TryGetSkillView(skill.SkillKey, out BattleSkillViewDefinition view) == false
				|| string.IsNullOrWhiteSpace(view.VfxKey))
			{
				return false;
			}

			vfxKey = view.VfxKey;
			return true;
		}

		Vector2? GetSkillVfxScaleOverride(BattlePawn casterPawn, int skillSlot)
		{
			if (casterPawn == null
				|| _mapGrid == null
				|| TryGetSkillDefinition(casterPawn, skillSlot, out BattleSkillDefinition skill) == false
				|| string.Equals(skill.SkillKey, "BEIGE_ICE_COLD_HARD_WORKER", System.StringComparison.OrdinalIgnoreCase) == false)
			{
				return null;
			}

			const int radius = 2;
			const float sourceFrameWidth = 5.52f; // 2208 / 4 pixels at 100 PPU.
			const float sourceFrameHeight = 4f; // 1600 / 4 pixels at 100 PPU.
			const float rangeOverhang = 1.2f;
			float minX = float.MaxValue;
			float maxX = float.MinValue;
			float minY = float.MaxValue;
			float maxY = float.MinValue;
			for (int direction = 0; direction < 6; direction++)
			{
				AxialCoord edge = casterPawn.Axial;
				for (int step = 0; step < radius; step++)
					edge = _mapGrid.GetNeighbor(edge, direction);

				Vector3 point = _mapGrid.AxialToWorldCenter(edge);
				minX = Mathf.Min(minX, point.x);
				maxX = Mathf.Max(maxX, point.x);
				minY = Mathf.Min(minY, point.y);
				maxY = Mathf.Max(maxY, point.y);
			}

			return new Vector2(
				((maxX - minX) / sourceFrameWidth) * rangeOverhang,
				((maxY - minY) / sourceFrameHeight) * rangeOverhang);
		}

		bool TryGetProjectileKey(BattlePawn casterPawn, int skillSlot, out string projectileKey)
		{
			projectileKey = null;
			if (casterPawn == null
				|| skillSlot <= 0
				|| TryGetSkillDefinition(casterPawn, skillSlot, out BattleSkillDefinition skill) == false
				|| _gameData == null
				|| _gameData.TryGetSkillView(skill.SkillKey, out BattleSkillViewDefinition view) == false
				|| string.IsNullOrWhiteSpace(view.ProjectileKey))
			{
				return false;
			}

			projectileKey = view.ProjectileKey;
			return true;
		}

		bool TryGetProjectileTargetWorldPosition(ulong targetPawnId, Protocol.AxialCoord targetAxial, out Vector3 targetWorldPosition)
		{
			if (targetPawnId != 0
				&& _pawns.TryGetValue(targetPawnId, out BattlePawn targetPawn)
				&& targetPawn != null)
			{
				targetWorldPosition = targetPawn.transform.position;
				return true;
			}

			if (targetAxial != null && _mapGrid != null)
			{
				AxialCoord battleAxial = new AxialCoord(targetAxial.Q, targetAxial.R);
				if (_mapGrid.IsTileInBounds(battleAxial) == false)
				{
					targetWorldPosition = default;
					return false;
				}

				targetWorldPosition = _mapGrid.AxialToWorldCenter(battleAxial);
				return true;
			}

			targetWorldPosition = default;
			return false;
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

			List<StatusTickPresentation> turnStartTicks = CollectTurnStartStatusTicks(packet.NextTurnPawnId, packet.PawnDeltas);
			ApplyPawnDeltas(packet.PawnDeltas);
			PresentStatusTicks(turnStartTicks);
			_mapGrid?.ApplyTileDeltas(packet.TileDeltas);
			AppendBattleLogs(packet.Logs);
			if (packet.TurnQueueResynced)
				ApplyTurnQueueSnapshot(packet.UpcomingTurnPawnIds, BattleTurnQueueUpdateKind.Resync, CollectDeadPawnIds(packet.PawnDeltas));
			else
				ApplyTurnQueueShift(packet.EnteringTurnPawnId);

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

			if (_isPlayingSkillActionSequence)
			{
				_deferredPawnDeaths[packet.PawnId] = packet.KillerPawnId;
				Debug.Log($"Deferred S_BATTLE_PAWN_DEAD until combat presentation ends. battleId={_battleId}, pawnId={packet.PawnId}, killerPawnId={packet.KillerPawnId}");
				return;
			}

			ApplyPawnDead(packet.PawnId, packet.KillerPawnId);
			BattlePawnDied?.Invoke(packet.PawnId);
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

		void ApplyDeferredPawnDeaths()
		{
			if (_deferredPawnDeaths.Count == 0)
				return;

			foreach (KeyValuePair<ulong, ulong> death in _deferredPawnDeaths)
			{
				ApplyPawnDead(death.Key, death.Value);
				BattlePawnDied?.Invoke(death.Key);
			}

			_deferredPawnDeaths.Clear();
			RefreshTurnIndicators();
		}

		bool IsBeigeFireTeleport(ulong casterPawnId, int skillSlot)
		{
			return skillSlot == 5
				&& _pawns.TryGetValue(casterPawnId, out BattlePawn casterPawn)
				&& casterPawn is BeigeFire;
		}

		void ApplyPawnDeltas(IEnumerable<BattlePawnDelta> pawnDeltas, ulong instantMovePawnId = 0)
		{
			if (pawnDeltas == null)
				return;

			foreach (BattlePawnDelta delta in pawnDeltas)
			{
				if (delta == null || delta.PawnId == 0)
					continue;

				if (_pawns.TryGetValue(delta.PawnId, out BattlePawn pawn))
					pawn.ApplyDelta(delta, delta.PawnId == instantMovePawnId);
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

		void ApplyPawnFacingDirections(IEnumerable<BattlePawnDelta> pawnDeltas)
		{
			if (pawnDeltas == null)
				return;

			foreach (BattlePawnDelta delta in pawnDeltas)
			{
				if (delta == null || delta.PawnId == 0)
					continue;

				if (_pawns.TryGetValue(delta.PawnId, out BattlePawn pawn))
					pawn.ApplyFacingDirectionFromDelta(delta);
			}
		}

		List<StatusTickPresentation> CollectTurnStartStatusTicks(ulong nextTurnPawnId, IEnumerable<BattlePawnDelta> pawnDeltas)
		{
			List<StatusTickPresentation> result = new List<StatusTickPresentation>();
			if (nextTurnPawnId == 0 || pawnDeltas == null
				|| _pawns.TryGetValue(nextTurnPawnId, out BattlePawn nextPawn) == false
				|| nextPawn == null
				|| nextPawn.Statuses.ContainsKey("BLEED") == false)
			{
				return result;
			}

			foreach (BattlePawnDelta delta in pawnDeltas)
			{
				if (delta == null || delta.PawnId != nextTurnPawnId)
					continue;

				int damage = nextPawn.Hp - delta.Hp;
				if (damage > 0)
					result.Add(new StatusTickPresentation(nextTurnPawnId, "BLEED", damage));
				break;
			}

			return result;
		}

		void PresentStatusTicks(IEnumerable<StatusTickPresentation> ticks)
		{
			if (ticks == null)
				return;

			foreach (StatusTickPresentation tick in ticks)
			{
				if (_pawns.TryGetValue(tick.PawnId, out BattlePawn pawn) && pawn != null)
					BattleStatusTickApplied?.Invoke(pawn, tick.StatusKey, tick.Amount);
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
			if (IsFireTileDamageLog(log))
				return $"FIRE TILE: ->{log.DefenderPawnId} dmg={log.Damage} hp={log.HpAfter} armor={log.ArmorAfter}";

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
