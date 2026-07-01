using System.Collections.Generic;
using App;
using Protocol;
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
		readonly Dictionary<ulong, BattlePawnController> _pawns = new Dictionary<ulong, BattlePawnController>();
		readonly Dictionary<ulong, AsyncOperationHandle<GameObject>> _pawnHandles = new Dictionary<ulong, AsyncOperationHandle<GameObject>>();
		readonly HashSet<ulong> _localPawnIds = new HashSet<ulong>();

		BattleMapGrid _mapGrid;
		string _fallbackPawnAddress;
		ulong _battleId;
		ulong _currentTurnPawnId;
		bool _destroyed;
		bool _missingCameraLogged;

		public IReadOnlyDictionary<ulong, BattlePawnController> Pawns => _pawns;
		public ulong BattleId => _battleId;
		public ulong CurrentTurnPawnId => _currentTurnPawnId;

		public void Initialize(BattleMapGrid mapGrid, string pawnAddress)
		{
			_mapGrid = mapGrid;
			_fallbackPawnAddress = pawnAddress;

			PacketHandler.Instance.BattleMoveReceived -= OnBattleMoveReceived;
			PacketHandler.Instance.BattleMoveReceived += OnBattleMoveReceived;
		}

		void Update()
		{
			HandleMouseInput();
		}

		void OnDestroy()
		{
			_destroyed = true;
			PacketHandler.Instance.BattleMoveReceived -= OnBattleMoveReceived;
			ReleasePawns();
		}

		public async void SpawnDebugPawns()
		{
			_battleId = 0;
			_currentTurnPawnId = 1;
			_localPawnIds.Clear();

			await SpawnPawnAsync(1, true, new AxialCoord(-2, -2));
			await SpawnPawnAsync(2, false, new AxialCoord(2, 2));
		}

		public async void SpawnFromEnterBattle(S_ENTER_BATTLE packet)
		{
			if (packet == null || packet.Success == false)
				return;

			ReleasePawns();
			_localPawnIds.Clear();
			_battleId = packet.BattleId;
			_currentTurnPawnId = packet.CurrentTurnPawnId;

			foreach (BattlePawnInfo pawnInfo in packet.AlliedPawns)
				await SpawnPawnAsync(pawnInfo.PawnId, true, ToBattleAxial(pawnInfo.Axial), pawnInfo);

			foreach (BattlePawnInfo pawnInfo in packet.EnemyPawns)
				await SpawnPawnAsync(pawnInfo.PawnId, false, ToBattleAxial(pawnInfo.Axial), pawnInfo);

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

			ulong movingPawnId = GetControllablePawnId();
			if (_pawns.TryGetValue(movingPawnId, out BattlePawnController myPawn) == false)
				return;

			if (_battleId != 0 && GameRoot.Instance != null)
			{
				bool sent = GameRoot.Instance.Network.SendBattleMove(_battleId, movingPawnId, axial.Q, axial.R);
				if (sent)
					Debug.Log($"Sent C_BATTLE_MOVE. battleId={_battleId}, pawnId={movingPawnId}, axial={axial}");
				else
					Debug.LogWarning($"Failed to send C_BATTLE_MOVE. {GameRoot.Instance.Network.LastError}");

				return;
			}

			myPawn.SetAxial(axial);
			Debug.Log($"Move debug battle pawn to tile center. axial={axial}, world={myPawn.transform.position}");
		}

		ulong GetControllablePawnId()
		{
			if (_currentTurnPawnId != 0 && _localPawnIds.Contains(_currentTurnPawnId))
				return _currentTurnPawnId;

			foreach (ulong pawnId in _localPawnIds)
				return pawnId;

			return 1;
		}

		void OnBattleMoveReceived(S_BATTLE_MOVE packet)
		{
			if (packet == null || packet.BattleId != _battleId)
				return;

			if (packet.Success == false)
			{
				Debug.LogWarning($"Battle move rejected. pawnId={packet.PawnId}, result={packet.Result}, reason={packet.Reason}");
				return;
			}

			if (_pawns.TryGetValue(packet.PawnId, out BattlePawnController pawn) == false)
				return;

			if (packet.Target != null)
				pawn.SetAxial(ToBattleAxial(packet.Target));

			_currentTurnPawnId = packet.NextTurnPawnId;
			Debug.Log($"Applied S_BATTLE_MOVE. pawnId={packet.PawnId}, target={pawn.Axial}, nextTurnPawnId={_currentTurnPawnId}");
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

		static AxialCoord ToBattleAxial(Protocol.AxialCoord axial)
		{
			if (axial == null)
				return default;

			return new AxialCoord(axial.Q, axial.R);
		}
	}
}
