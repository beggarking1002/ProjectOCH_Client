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

namespace Field
{
	[DefaultExecutionOrder(-50)]
	[DisallowMultipleComponent]
	public sealed class FieldObjectManager : MonoBehaviour
	{
		readonly Dictionary<ulong, FieldPawnController> _pawns = new Dictionary<ulong, FieldPawnController>();
		readonly Dictionary<ulong, AsyncOperationHandle<GameObject>> _pawnHandles = new Dictionary<ulong, AsyncOperationHandle<GameObject>>();

		FieldMapWalkArea _walkArea;
		string _pawnAddress;
		bool _initialized;
		bool _destroyed;
		bool _battleEnterRequested;
		ulong _myObjectId;
		FieldBattleInviteUI _battleInviteUi;

		public ulong MyObjectId => _myObjectId;
		public FieldPawnController MyPawn => _myObjectId != 0 && _pawns.TryGetValue(_myObjectId, out FieldPawnController pawn) ? pawn : null;

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
			SpawnKnownPlayers();
		}

		void OnDestroy()
		{
			_destroyed = true;
			UnsubscribeNetwork();
			ReleasePawns();
		}

		void Update()
		{
			HandleBattleInviteClickInput();
			HandleBattleEnterDebugInput();
		}

		void EnsureBattleInviteUi()
		{
			if (_battleInviteUi != null)
				return;

			_battleInviteUi = GetComponent<FieldBattleInviteUI>();
			if (_battleInviteUi == null)
				_battleInviteUi = gameObject.AddComponent<FieldBattleInviteUI>();
		}

		void HandleBattleInviteClickInput()
		{
			if (_battleInviteUi == null || FieldBattleInviteUI.IsBlockingInput || TryGetPointerDown(out Vector2 screenPosition) == false)
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
		}

		void UnsubscribeNetwork()
		{
			if (GameRoot.Instance == null || _initialized == false)
				return;

			GameRoot.Instance.Network.EnterGameReceived -= HandleEnterGame;
			GameRoot.Instance.Network.SpawnReceived -= HandleSpawn;
			GameRoot.Instance.Network.DespawnReceived -= HandleDespawn;
			GameRoot.Instance.Network.MoveReceived -= HandleMove;
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

			if (_pawns.TryGetValue(packet.ObjectId, out FieldPawnController pawn) == false)
			{
				SpawnPawnForMove(packet, start, target);
				return;
			}

			bool isMine = packet.ObjectId == _myObjectId;
			pawn.ApplyServerMove(start, target, packet.DurationMs, snapToStart: isMine == false);
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
					BindCameraToPawn(existing.transform);
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
				BindCameraToPawn(pawn.transform);
			}
		}

		async void SpawnPawnForMove(S_MOVE packet, Vector3 start, Vector3 target)
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

			pawn.ApplyServerMove(start, target, packet.DurationMs, snapToStart: isMine == false);
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
					BindCameraToPawn(existing.transform);
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
				BindCameraToPawn(pawn.transform);
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

		static void BindCameraToPawn(Transform pawnTransform)
		{
			Camera camera = Camera.main;
			if (camera == null)
				return;

			CameraController controller = camera.GetComponent<CameraController>();
			if (controller == null)
				controller = camera.gameObject.AddComponent<CameraController>();

			controller.SetTarget(pawnTransform);
		}
	}
}
