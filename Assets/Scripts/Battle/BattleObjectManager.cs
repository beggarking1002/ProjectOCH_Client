using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
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
		readonly Dictionary<int, BattlePawnController> _pawns = new Dictionary<int, BattlePawnController>();
		readonly Dictionary<int, AsyncOperationHandle<GameObject>> _pawnHandles = new Dictionary<int, AsyncOperationHandle<GameObject>>();

		BattleMapGrid _mapGrid;
		string _pawnAddress;
		bool _destroyed;
		bool _missingCameraLogged;

		public IReadOnlyDictionary<int, BattlePawnController> Pawns => _pawns;

		public void Initialize(BattleMapGrid mapGrid, string pawnAddress)
		{
			_mapGrid = mapGrid;
			_pawnAddress = pawnAddress;
		}

		void Update()
		{
			HandleMouseInput();
		}

		void OnDestroy()
		{
			_destroyed = true;
			ReleasePawns();
		}

		public async void SpawnDebugPawns()
		{
			await SpawnPawnAsync(1, true, new AxialCoord(-2, -2));
			await SpawnPawnAsync(2, false, new AxialCoord(2, 2));
		}

		public async System.Threading.Tasks.Task<BattlePawnController> SpawnPawnAsync(int pawnId, bool isMine, AxialCoord axial)
		{
			if (_mapGrid == null)
			{
				Debug.LogError($"{nameof(BattleObjectManager)} requires a {nameof(BattleMapGrid)} before spawning pawns.");
				return null;
			}

			if (string.IsNullOrWhiteSpace(_pawnAddress))
			{
				Debug.LogError($"{nameof(BattleObjectManager)} requires a pawn address.");
				return null;
			}

			if (_pawns.TryGetValue(pawnId, out BattlePawnController existing))
			{
				existing.SetAxial(axial);
				return existing;
			}

			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(_pawnAddress);
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
				Debug.LogError($"Failed to load battle pawn addressable: {_pawnAddress}");
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				_pawnHandles.Remove(pawnId);
				return null;
			}

			GameObject pawnObject = handle.Result;
			pawnObject.name = isMine ? $"BattlePawn_My_{pawnId}" : $"BattlePawn_Enemy_{pawnId}";
			SceneManager.MoveGameObjectToScene(pawnObject, gameObject.scene);

			BattlePawnController pawn = pawnObject.GetComponent<BattlePawnController>();
			if (pawn == null)
				pawn = pawnObject.AddComponent<BattlePawnController>();

			Color tint = isMine ? Color.white : new Color(1f, 0.75f, 0.75f, 1f);
			pawn.Initialize(pawnId, isMine, _mapGrid, axial, tint);

			_pawns[pawnId] = pawn;
			Debug.Log($"Spawned battle pawn: id={pawnId}, mine={isMine}, axial={axial}, world={pawn.transform.position}");
			return pawn;
		}

		public void DespawnPawn(int pawnId)
		{
			_pawns.Remove(pawnId);

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
		}

		void HandleMouseInput()
		{
			if (_mapGrid == null || TryGetPointerDown(out Vector2 screenPosition) == false)
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
			if (_mapGrid.IsWalkable(axial) == false)
			{
				Debug.Log($"Clicked blocked battle tile. axial={axial}");
				return;
			}

			if (_pawns.TryGetValue(1, out BattlePawnController myPawn) == false)
				return;

			myPawn.SetAxial(axial);
			Debug.Log($"Move battle pawn to tile center. axial={axial}, world={myPawn.transform.position}");
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
	}
}
