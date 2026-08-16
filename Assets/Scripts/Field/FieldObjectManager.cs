using System.Collections.Generic;
using App;
using Protocol;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Field
{
	[DefaultExecutionOrder(-50)]
	[DisallowMultipleComponent]
	public sealed class FieldObjectManager : MonoBehaviour
	{
		const string FieldVillageUiAddress = "FieldVillageUI";
		const string PlayerInventoryUiAddress = "PlayerInventoryUI";

		readonly Dictionary<ulong, FieldPawnController> _pawns = new Dictionary<ulong, FieldPawnController>();
		readonly Dictionary<ulong, AsyncOperationHandle<GameObject>> _pawnHandles = new Dictionary<ulong, AsyncOperationHandle<GameObject>>();

		FieldMapWalkArea _walkArea;
		string _pawnAddress;
		bool _initialized;
		bool _destroyed;
		bool _battleEnterRequested;
		ulong _myObjectId;
		FieldBattleInviteUI _battleInviteUi;
		FieldBattleClassSelectionUI _battleClassSelectionUi;
		AsyncOperationHandle<GameObject> _fieldVillageUiHandle;
		GameObject _fieldVillageUi;
		bool _hasFieldVillageUiHandle;
		bool _isFieldVillageUiLoading;
		AsyncOperationHandle<GameObject> _playerInventoryUiHandle;
		GameObject _playerInventoryUi;
		bool _hasPlayerInventoryUiHandle;
		bool _isPlayerInventoryUiLoading;
		FieldVillageRepository _villageRepository;
		bool _isVillageDataLoading;
		bool _villageEnterRequested;
		Vector2Int? _pendingVillageCell;

		public ulong MyObjectId => _myObjectId;
		public FieldPawnController MyPawn => _myObjectId != 0 && _pawns.TryGetValue(_myObjectId, out FieldPawnController pawn) ? pawn : null;
		public bool IsLocalPawnReady => MyPawn != null;
		public event System.Action<FieldObjectManager> LocalPawnReady;

		public void Initialize(FieldMapWalkArea walkArea, string pawnAddress)
		{
			_walkArea = walkArea;
			_pawnAddress = pawnAddress;

			if (_initialized == false && GameRoot.Instance != null)
			{
				SubscribeNetwork();
				_initialized = true;
			}

			Protocol.S_ENTER_GAME lastEnterGame = GameRoot.Instance != null ? GameRoot.Instance.Network.LastEnterGame : null;
			if (lastEnterGame != null)
				HandleEnterGame(lastEnterGame);
			else
				SpawnFallbackLocalPawn();

			EnsureBattleInviteUi();
			EnsureBattleClassSelectionUi();
			LoadVillageDataAsync();
			WarmItemIconsAsync();
			SpawnKnownPlayers();
		}

		void OnDestroy()
		{
			_destroyed = true;
			UnsubscribeNetwork();
			ReleasePawns();
			ReleaseFieldVillageUi();
			ReleasePlayerInventoryUi();
		}

		void Update()
		{
			HandleVillageClickInput();
			UpdatePendingVillageEntry();
			HandleBattleInviteClickInput();
			HandleBattleEnterDebugInput();
			HandleFieldVillageUiDebugInput();
			HandlePlayerInventoryUiDebugInput();
		}

		async void LoadVillageDataAsync()
		{
			if (_isVillageDataLoading || _villageRepository != null)
				return;

			_isVillageDataLoading = true;
			_villageRepository = await FieldVillageRepository.LoadAsync();
			_isVillageDataLoading = false;
		}

		async void WarmItemIconsAsync()
		{
			FieldEconomyRepository repository = await FieldEconomyRepository.LoadAsync();
			if (_destroyed || repository == null)
				return;
			await repository.PreloadAllItemIconsAsync();
		}

		void HandleVillageClickInput()
		{
			if (_walkArea == null || _villageEnterRequested || TryGetPointerDown(out Vector2 screenPosition) == false)
				return;
			if (FieldPointerInputBlocker.IsConsumedThisFrame || FieldBattleInviteUI.IsBlockingInput || FieldBattleClassSelectionUI.IsBlockingInput || IsPointerOverUi())
				return;

			Camera camera = Camera.main;
			if (camera == null || IsValidScreenPosition(camera, screenPosition) == false)
				return;

			Ray ray = camera.ScreenPointToRay(screenPosition);
			Plane mapPlane = new Plane(Vector3.forward, _walkArea.PlaneTransform.position);
			if (mapPlane.Raycast(ray, out float enter) == false)
				return;

			Vector3 worldPosition = ray.GetPoint(enter);
			if (_walkArea.TryGetVillageAt(worldPosition, out Vector2Int cell, out _) == false)
				return;

			FieldPointerInputBlocker.ConsumeCurrentFrame();
			ApproachOrEnterVillage(cell);
		}

		void ApproachOrEnterVillage(Vector2Int villageCell)
		{
			FieldPawnController pawn = MyPawn;
			if (pawn == null || _walkArea.TryGetCell(pawn.transform.position, out Vector2Int pawnCell) == false)
			{
				Debug.LogWarning("Cannot approach village because the local field pawn is not ready.");
				return;
			}

			_pendingVillageCell = villageCell;
			if (FieldMapWalkArea.GetHexDistance(pawnCell, villageCell) <= 2)
			{
				UpdatePendingVillageEntry();
				return;
			}

			if (GameRoot.Instance == null)
			{
				_pendingVillageCell = null;
				Debug.LogWarning("Cannot move toward a village because GameRoot is not initialized.");
				return;
			}

			if (_walkArea.TryGetVillageApproachCell(pawn.transform.position, villageCell, out Vector2Int approachCell) == false)
			{
				_pendingVillageCell = null;
				Debug.LogWarning($"Cannot find a reachable approach tile for village cell ({villageCell.x}, {villageCell.y}).");
				return;
			}

			Vector3 targetWorldPosition = _walkArea.GetCellCenterWorld(approachCell, pawn.transform.position.z);
			Protocol.C_MOVE movePacket = new Protocol.C_MOVE
			{
				Target = FieldPositionCodec.ToFixed(targetWorldPosition),
			};
			if (GameRoot.Instance.Network.Send(movePacket) == false)
			{
				_pendingVillageCell = null;
				Debug.LogWarning($"Failed to move toward village. {GameRoot.Instance.Network.LastError}");
				return;
			}

			Debug.Log($"Moving toward village interaction range. villageCell=({villageCell.x}, {villageCell.y}) approachCell=({approachCell.x}, {approachCell.y})");
		}

		void UpdatePendingVillageEntry()
		{
			if (_pendingVillageCell.HasValue == false || _villageEnterRequested)
				return;

			FieldPawnController pawn = MyPawn;
			if (pawn == null || _walkArea == null || _walkArea.TryGetCell(pawn.transform.position, out Vector2Int pawnCell) == false)
				return;

			Vector2Int villageCell = _pendingVillageCell.Value;
			if (FieldMapWalkArea.GetHexDistance(pawnCell, villageCell) > 2)
				return;

			_pendingVillageCell = null;
			RequestVillageEntry(villageCell);
		}

		void RequestVillageEntry(Vector2Int cell)
		{
			if (GameRoot.Instance == null)
			{
				Debug.LogWarning("Cannot send C_ENTER_VILLAGE because GameRoot is not initialized.");
				return;
			}

			if (GameRoot.Instance.Network.EnterVillage(_walkArea.MapId, cell.x, cell.y) == false)
			{
				Debug.LogWarning($"Failed to send C_ENTER_VILLAGE. {GameRoot.Instance.Network.LastError}");
				return;
			}

			_villageEnterRequested = true;
			Debug.Log($"Sent C_ENTER_VILLAGE. mapId={_walkArea.MapId}, cell=({cell.x}, {cell.y})");
		}

		async void ShowFieldVillageUi(FieldVillageDefinition village)
		{
			if (_fieldVillageUi != null)
			{
				ConfigureAndShowVillageUi(village);
				return;
			}
			if (_isFieldVillageUiLoading)
				return;

			_isFieldVillageUiLoading = true;
			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(FieldVillageUiAddress);
			_fieldVillageUiHandle = handle;
			_hasFieldVillageUiHandle = true;
			await handle.Task;
			_isFieldVillageUiLoading = false;

			if (_destroyed || handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
			{
				if (_destroyed == false)
					Debug.LogError($"Failed to load field village UI: {FieldVillageUiAddress}");
				ReleaseFieldVillageUi();
				return;
			}

			_fieldVillageUi = handle.Result;
			Button leaveButton = _fieldVillageUi.transform.Find("Window/LeaveButton")?.GetComponent<Button>();
			if (leaveButton != null)
				leaveButton.onClick.AddListener(HideFieldVillageUi);
			ConfigureAndShowVillageUi(village);
		}

		void ConfigureAndShowVillageUi(FieldVillageDefinition village)
		{
			_fieldVillageUi.SetActive(true);
			FieldVillageUI villageUi = _fieldVillageUi.GetComponent<FieldVillageUI>();
			villageUi?.ShowVillage(village);
		}

		void HandleFieldVillageUiDebugInput()
		{
			if (WasFieldVillageUiToggleKeyPressed() == false || _isFieldVillageUiLoading)
				return;

			if (_fieldVillageUi != null)
			{
				_fieldVillageUi.SetActive(_fieldVillageUi.activeSelf == false);
				return;
			}

			if (_villageRepository != null && _villageRepository.TryGetFirstVillage(out FieldVillageDefinition village))
				ShowFieldVillageUi(village);
			else
				Debug.LogWarning("Village data is still loading. Try F7 again in a moment.");
		}

		void HideFieldVillageUi()
		{
			if (_fieldVillageUi != null)
				_fieldVillageUi.SetActive(false);
		}

		void ReleaseFieldVillageUi()
		{
			if (_hasFieldVillageUiHandle && _fieldVillageUiHandle.IsValid())
				Addressables.ReleaseInstance(_fieldVillageUiHandle);

			_fieldVillageUi = null;
			_fieldVillageUiHandle = default;
			_hasFieldVillageUiHandle = false;
			_isFieldVillageUiLoading = false;
		}

		void HandlePlayerInventoryUiDebugInput()
		{
			if (WasPlayerInventoryUiToggleKeyPressed() == false)
				return;
			TogglePlayerInventoryUi();
		}

		public async void TogglePlayerInventoryUi()
		{
			if (_isPlayerInventoryUiLoading)
				return;

			if (_playerInventoryUi != null)
			{
				_playerInventoryUi.SetActive(_playerInventoryUi.activeSelf == false);
				return;
			}

			_isPlayerInventoryUiLoading = true;
			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(PlayerInventoryUiAddress);
			_playerInventoryUiHandle = handle;
			_hasPlayerInventoryUiHandle = true;
			await handle.Task;
			_isPlayerInventoryUiLoading = false;

			if (_destroyed || handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
			{
				if (_destroyed == false)
					Debug.LogError($"Failed to load player inventory UI: {PlayerInventoryUiAddress}");
				ReleasePlayerInventoryUi();
				return;
			}

			_playerInventoryUi = handle.Result;
			Button closeButton = _playerInventoryUi.transform.Find("Window/CloseButton")?.GetComponent<Button>();
			if (closeButton != null)
				closeButton.onClick.AddListener(HidePlayerInventoryUi);

			Debug.Log("Player inventory UI preview opened. Press F8 to toggle it.");
		}

		void HidePlayerInventoryUi()
		{
			if (_playerInventoryUi != null)
				_playerInventoryUi.SetActive(false);
		}

		void ReleasePlayerInventoryUi()
		{
			if (_hasPlayerInventoryUiHandle && _playerInventoryUiHandle.IsValid())
				Addressables.ReleaseInstance(_playerInventoryUiHandle);

			_playerInventoryUi = null;
			_playerInventoryUiHandle = default;
			_hasPlayerInventoryUiHandle = false;
			_isPlayerInventoryUiLoading = false;
		}

		void EnsureBattleInviteUi()
		{
			if (_battleInviteUi != null)
				return;

			_battleInviteUi = GetComponent<FieldBattleInviteUI>();
			if (_battleInviteUi == null)
				_battleInviteUi = gameObject.AddComponent<FieldBattleInviteUI>();
		}

		void EnsureBattleClassSelectionUi()
		{
			if (_battleClassSelectionUi != null)
				return;

			_battleClassSelectionUi = GetComponent<FieldBattleClassSelectionUI>();
			if (_battleClassSelectionUi == null)
				_battleClassSelectionUi = gameObject.AddComponent<FieldBattleClassSelectionUI>();
		}

		void HandleBattleInviteClickInput()
		{
			if (_battleInviteUi == null || FieldPointerInputBlocker.IsConsumedThisFrame || FieldBattleInviteUI.IsBlockingInput || FieldBattleClassSelectionUI.IsBlockingInput || TryGetPointerDown(out Vector2 screenPosition) == false)
				return;

			if (IsPointerOverUi())
				return;

			if (TryGetRemotePawnAt(screenPosition, out FieldPawnController targetPawn) == false)
				return;

			FieldPointerInputBlocker.ConsumeCurrentFrame();
			_battleInviteUi.ShowInviteConfirm(targetPawn.ObjectId);
		}

		bool TryGetRemotePawnAt(Vector2 screenPosition, out FieldPawnController targetPawn)
		{
			targetPawn = null;

			if (_walkArea == null)
				return false;

			Camera camera = Camera.main;
			if (camera == null || IsValidScreenPosition(camera, screenPosition) == false)
				return false;

			Ray ray = camera.ScreenPointToRay(screenPosition);
			Plane mapPlane = new Plane(Vector3.forward, _walkArea.PlaneTransform.position);
			if (mapPlane.Raycast(ray, out float enter) == false)
				return false;

			Vector3 worldPosition = ray.GetPoint(enter);
			float nearestDistance = float.MaxValue;

			foreach (FieldPawnController pawn in _pawns.Values)
			{
				if (pawn == null || pawn.IsMine || pawn.ObjectId == 0)
					continue;

				float radius = GetClickRadius(pawn);
				float distance = Vector2.Distance(worldPosition, pawn.transform.position);
				if (distance > radius || distance >= nearestDistance)
					continue;

				nearestDistance = distance;
				targetPawn = pawn;
			}

			return targetPawn != null;
		}

		// Shared by the global cursor so enemy players are consistently marked in FieldScene.
		public bool IsPointerOverRemotePawn(Vector2 screenPosition)
		{
			return TryGetRemotePawnAt(screenPosition, out _);
		}

		// Shared by the global cursor. This only indicates a client-side village
		// area; server validation still decides whether entry is allowed.
		public bool IsPointerOverVillage(Vector2 screenPosition)
		{
			if (_walkArea == null || _walkArea.IsInitialized == false)
				return false;

			Camera camera = Camera.main;
			if (camera == null || IsValidScreenPosition(camera, screenPosition) == false)
				return false;

			Ray ray = camera.ScreenPointToRay(screenPosition);
			Plane mapPlane = new Plane(Vector3.forward, _walkArea.PlaneTransform.position);
			if (mapPlane.Raycast(ray, out float enter) == false)
				return false;

			return _walkArea.TryGetVillageAt(ray.GetPoint(enter), out _, out _);
		}

		public bool TryGetMoveCursorWorldPosition(Vector2 screenPosition, out Vector3 worldPosition)
		{
			worldPosition = default;
			// Cursor feedback follows the tile under the pointer, not the pawn's
			// current movement state.  A click can start an in-flight move, but that
			// must not temporarily turn every valid destination back into the hand.
			if (_walkArea == null || _walkArea.IsInitialized == false || MyPawn == null)
				return false;

			Camera camera = Camera.main;
			if (camera == null || IsValidScreenPosition(camera, screenPosition) == false)
				return false;

			Ray ray = camera.ScreenPointToRay(screenPosition);
			Plane mapPlane = new Plane(Vector3.forward, _walkArea.PlaneTransform.position);
			if (mapPlane.Raycast(ray, out float enter) == false)
				return false;

			worldPosition = ray.GetPoint(enter);
			return _walkArea.IsWalkable(worldPosition);
		}

		static float GetClickRadius(FieldPawnController pawn)
		{
			Renderer renderer = pawn.GetComponentInChildren<Renderer>();
			if (renderer == null)
				return 0.55f;

			Vector3 extents = renderer.bounds.extents;
			return Mathf.Clamp(Mathf.Max(extents.x, extents.y) * 0.8f, 0.45f, 1.5f);
		}

		void SubscribeNetwork()
		{
			if (GameRoot.Instance == null)
				return;

			GameRoot.Instance.Network.EnterGameReceived += HandleEnterGame;
			GameRoot.Instance.Network.SpawnReceived += HandleSpawn;
			GameRoot.Instance.Network.DespawnReceived += HandleDespawn;
			GameRoot.Instance.Network.MoveReceived += HandleMove;
			GameRoot.Instance.Network.EnterVillageReceived += HandleEnterVillage;
		}

		void UnsubscribeNetwork()
		{
			if (GameRoot.Instance == null || _initialized == false)
				return;

			GameRoot.Instance.Network.EnterGameReceived -= HandleEnterGame;
			GameRoot.Instance.Network.SpawnReceived -= HandleSpawn;
			GameRoot.Instance.Network.DespawnReceived -= HandleDespawn;
			GameRoot.Instance.Network.MoveReceived -= HandleMove;
			GameRoot.Instance.Network.EnterVillageReceived -= HandleEnterVillage;
		}

		void HandleEnterVillage(Protocol.S_ENTER_VILLAGE packet)
		{
			_villageEnterRequested = false;
			if (packet == null || packet.Success == false)
			{
				string reason = packet == null || string.IsNullOrWhiteSpace(packet.Reason) ? "Server rejected village entry." : packet.Reason;
				Debug.LogWarning($"Village entry failed: {reason}");
				return;
			}

			if (string.IsNullOrWhiteSpace(packet.VillageId))
			{
				Debug.LogWarning("S_ENTER_VILLAGE succeeded without a village id.");
				return;
			}

			string artworkAddress = string.Empty;
			if (_villageRepository != null && _villageRepository.TryGetVillage(packet.VillageId, out FieldVillageDefinition localVillage))
				artworkAddress = localVillage.ArtworkAddress;

			FieldVillageDefinition village = new FieldVillageDefinition(
				packet.VillageId,
				packet.VillageName,
				packet.VillageDescription,
				artworkAddress);
			ShowFieldVillageUi(village);
		}

		void HandleBattleEnterDebugInput()
		{
			if (WasBattleEnterKeyPressed() == false)
				return;

			if (_battleEnterRequested)
			{
				Debug.Log("C_ENTER_BATTLE already requested.");
				return;
			}

			if (GameRoot.Instance == null)
			{
				Debug.LogWarning("Cannot send C_ENTER_BATTLE because GameRoot is not initialized.");
				return;
			}

			if (GameRoot.Instance.Network.EnterBattle() == false)
			{
				Debug.LogWarning($"Failed to send C_ENTER_BATTLE. {GameRoot.Instance.Network.LastError}");
				return;
			}

			_battleEnterRequested = true;
			Debug.Log("Sent C_ENTER_BATTLE.");
		}

		static bool WasBattleEnterKeyPressed()
		{
#if ENABLE_INPUT_SYSTEM
			Keyboard keyboard = Keyboard.current;
			return keyboard != null && keyboard.bKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
			return Input.GetKeyDown(KeyCode.B);
#else
			return false;
#endif
		}

		static bool WasFieldVillageUiToggleKeyPressed()
		{
#if ENABLE_INPUT_SYSTEM
			Keyboard keyboard = Keyboard.current;
			return keyboard != null && keyboard.f7Key.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
			return Input.GetKeyDown(KeyCode.F7);
#else
			return false;
#endif
		}

		static bool WasPlayerInventoryUiToggleKeyPressed()
		{
#if ENABLE_INPUT_SYSTEM
			Keyboard keyboard = Keyboard.current;
			return keyboard != null && keyboard.f8Key.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
			return Input.GetKeyDown(KeyCode.F8);
#else
			return false;
#endif
		}

		static bool TryGetPointerDown(out Vector2 screenPosition)
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

		static bool IsPointerOverUi()
		{
			return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
		}

		void HandleEnterGame(S_ENTER_GAME packet)
		{
			if (packet == null || packet.Success == false)
				return;

			ObjectInfo player = packet.Player;
			if (player == null)
			{
				SpawnFallbackLocalPawn();
				return;
			}

			ulong previousMyObjectId = _myObjectId;
			if (previousMyObjectId != 0 && player.ObjectId != 0 && previousMyObjectId != player.ObjectId)
				DespawnPawn(previousMyObjectId);

			_myObjectId = player.ObjectId;
			SpawnOrUpdatePawn(player, true);
		}

		void HandleSpawn(S_SPAWN packet)
		{
			if (packet == null)
				return;

			foreach (ObjectInfo player in packet.Players)
			{
				if (player.ObjectId != 0 && player.ObjectId == _myObjectId)
					continue;

				SpawnOrUpdatePawn(player, false);
			}
		}

		void SpawnKnownPlayers()
		{
			if (GameRoot.Instance == null)
				return;

			List<ObjectInfo> players = GameRoot.Instance.Network.GetKnownPlayersSnapshot();
			for (int i = 0; i < players.Count; i++)
			{
				ObjectInfo player = players[i];
				if (player == null || player.ObjectId == 0 || player.ObjectId == _myObjectId)
					continue;

				SpawnOrUpdatePawn(player, false);
			}
		}

		void HandleDespawn(S_DESPAWN packet)
		{
			if (packet == null)
				return;

			foreach (ulong objectId in packet.ObjectIds)
				DespawnPawn(objectId);
		}

		void HandleMove(S_MOVE packet)
		{
			if (packet == null || packet.ObjectId == 0 || packet.Target == null)
				return;

			float z = GetPawnZ(packet.ObjectId);
			Vector3 start = packet.Start != null
				? FieldPositionCodec.ToWorld(packet.Start, z)
				: GetPawnPositionOrDefault(packet.ObjectId, z);
			Vector3 target = FieldPositionCodec.ToWorld(packet.Target, z);
			List<Vector3> authoritativePath = BuildMovePath(packet, start, target, z, useVisualStart: false);
			float authoritativePathLength = GetPathLength(start, authoritativePath);

			if (_pawns.TryGetValue(packet.ObjectId, out FieldPawnController pawn) == false)
			{
				SpawnPawnForMove(packet, start, target, authoritativePath, authoritativePathLength);
				return;
			}

			// The server's start is its previously committed target. For a newly
			// received command, route from the pawn's displayed position instead so
			// changing direction replaces the in-flight route immediately.
			List<Vector3> visualPath = BuildMovePath(packet, pawn.transform.position, target, z, useVisualStart: true);

			// An already spawned remote pawn can still be interpolating toward the
			// previous server target. Snapping it to this packet's start would visibly
			// skip that remaining segment on every rapid C_MOVE. Both local and remote
			// pawns therefore continue from their displayed position at the server's
			// intended speed. A pawn first seen through S_MOVE is initialized at start
			// in SpawnPawnForMove below.
			pawn.ApplyServerMove(start, target, visualPath, packet.DurationMs, snapToStart: false, authoritativePathLength);
		}

		static float GetPathLength(Vector3 start, IList<Vector3> path)
		{
			float length = 0f;
			Vector3 previous = start;
			if (path == null)
				return length;

			for (int index = 0; index < path.Count; index++)
			{
				length += Vector3.Distance(previous, path[index]);
				previous = path[index];
			}

			return length;
		}

		List<Vector3> BuildMovePath(S_MOVE packet, Vector3 start, Vector3 target, float z, bool useVisualStart)
		{
			List<Vector3> path = new List<Vector3>();
			if (useVisualStart && _walkArea != null && _walkArea.TryFindPath(start, target, path))
				return path;

			if (packet.Path != null)
			{
				for (int i = 0; i < packet.Path.Count; i++)
				{
					Vec2Fixed waypoint = packet.Path[i];
					if (waypoint != null)
						path.Add(FieldPositionCodec.ToWorld(waypoint, z));
				}
			}

			if (path.Count == 0 || Vector3.Distance(path[path.Count - 1], target) > 0.01f)
				path.Add(target);
			return path;
		}

		async void SpawnOrUpdatePawn(ObjectInfo info, bool isMine)
		{
			if (info == null)
				return;

			ulong objectId = info.ObjectId;
			if (objectId == 0 && isMine)
				objectId = _myObjectId = 1;
			else if (objectId == 0)
				return;

			if (_pawns.TryGetValue(objectId, out FieldPawnController existing))
			{
				existing.Initialize(_walkArea, GetPosition(info, existing.transform.position.z), objectId, isMine);
				if (isMine)
					NotifyLocalPawnReady(existing.transform);
				return;
			}

			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(_pawnAddress);
			await handle.Task;

			if (_destroyed)
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				return;
			}

			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				Debug.LogError($"Failed to load addressable pawn: {_pawnAddress}");
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				return;
			}

			GameObject pawnObject = handle.Result;
			pawnObject.name = isMine ? "Field_Pawn_My" : $"Field_Pawn_{objectId}";
			SceneManager.MoveGameObjectToScene(pawnObject, gameObject.scene);

			FieldPawnController pawn = pawnObject.GetComponent<FieldPawnController>();
			if (pawn == null)
				pawn = pawnObject.AddComponent<FieldPawnController>();

			Vector3 startPosition = GetPosition(info, pawnObject.transform.position.z);
			pawn.Initialize(_walkArea, startPosition, objectId, isMine);

			_pawns[objectId] = pawn;
			_pawnHandles[objectId] = handle;

			if (isMine)
			{
				_myObjectId = objectId;
				NotifyLocalPawnReady(pawn.transform);
			}
		}

		async void SpawnPawnForMove(S_MOVE packet, Vector3 start, Vector3 target, List<Vector3> path, float authoritativePathLength)
		{
			bool isMine = packet.ObjectId == _myObjectId;
			ObjectInfo info = new ObjectInfo
			{
				ObjectId = packet.ObjectId,
				Position = packet.Start ?? packet.Target,
			};

			await SpawnOrUpdatePawnAsync(info, isMine);

			if (_destroyed || _pawns.TryGetValue(packet.ObjectId, out FieldPawnController pawn) == false)
				return;

			pawn.ApplyServerMove(start, target, path, packet.DurationMs, snapToStart: false, authoritativePathLength);
		}

		async System.Threading.Tasks.Task SpawnOrUpdatePawnAsync(ObjectInfo info, bool isMine)
		{
			if (info == null)
				return;

			ulong objectId = info.ObjectId;
			if (objectId == 0 && isMine)
				objectId = _myObjectId = 1;
			else if (objectId == 0)
				return;

			if (_pawns.TryGetValue(objectId, out FieldPawnController existing))
			{
				existing.Initialize(_walkArea, GetPosition(info, existing.transform.position.z), objectId, isMine);
				if (isMine)
					NotifyLocalPawnReady(existing.transform);
				return;
			}

			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(_pawnAddress);
			await handle.Task;

			if (_destroyed)
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				return;
			}

			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				Debug.LogError($"Failed to load addressable pawn: {_pawnAddress}");
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				return;
			}

			GameObject pawnObject = handle.Result;
			pawnObject.name = isMine ? "Field_Pawn_My" : $"Field_Pawn_{objectId}";
			SceneManager.MoveGameObjectToScene(pawnObject, gameObject.scene);

			FieldPawnController pawn = pawnObject.GetComponent<FieldPawnController>();
			if (pawn == null)
				pawn = pawnObject.AddComponent<FieldPawnController>();

			Vector3 startPosition = GetPosition(info, pawnObject.transform.position.z);
			pawn.Initialize(_walkArea, startPosition, objectId, isMine);

			_pawns[objectId] = pawn;
			_pawnHandles[objectId] = handle;

			if (isMine)
			{
				_myObjectId = objectId;
				NotifyLocalPawnReady(pawn.transform);
			}
		}

		void SpawnFallbackLocalPawn()
		{
			if (_myObjectId != 0 || _walkArea == null)
				return;

			_myObjectId = 1;
			ObjectInfo fallback = new ObjectInfo
			{
				ObjectId = _myObjectId,
				Position = FieldPositionCodec.ToFixed(_walkArea.GetDefaultSpawnPosition(0f)),
			};
			SpawnOrUpdatePawn(fallback, true);
		}

		void DespawnPawn(ulong objectId)
		{
			if (objectId == 0)
				return;

			_pawns.Remove(objectId);

			if (_pawnHandles.TryGetValue(objectId, out AsyncOperationHandle<GameObject> handle))
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);

				_pawnHandles.Remove(objectId);
			}

			if (_myObjectId == objectId)
				_myObjectId = 0;
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
			_myObjectId = 0;
		}

		Vector3 GetPosition(ObjectInfo info, float z)
		{
			if (info.Position != null)
				return FieldPositionCodec.ToWorld(info.Position, z);

			return _walkArea != null ? _walkArea.GetDefaultSpawnPosition(z) : new Vector3(0f, 0f, z);
		}

		float GetPawnZ(ulong objectId)
		{
			if (_pawns.TryGetValue(objectId, out FieldPawnController pawn))
				return pawn.transform.position.z;

			return 0f;
		}

		Vector3 GetPawnPositionOrDefault(ulong objectId, float z)
		{
			if (_pawns.TryGetValue(objectId, out FieldPawnController pawn))
				return pawn.transform.position;

			return _walkArea != null ? _walkArea.GetDefaultSpawnPosition(z) : new Vector3(0f, 0f, z);
		}

		void NotifyLocalPawnReady(Transform pawnTransform)
		{
			BindCameraToPawn(pawnTransform);
			LocalPawnReady?.Invoke(this);
		}

		static void BindCameraToPawn(Transform pawnTransform)
		{
			Camera camera = Camera.main;
			if (camera == null)
				return;

			CameraController controller = camera.GetComponent<CameraController>();
			if (controller == null)
				controller = camera.gameObject.AddComponent<CameraController>();

			controller.SetTarget(pawnTransform, true);
		}
	}
}
