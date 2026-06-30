using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace Scenes
{
	public sealed class BattleSceneAddressableLoader : MonoBehaviour
	{
		const string BattleSceneName = "BattleScene";
		const string BattleMapAddress = "BattleField_001";
		const string BattlePawnAddress = "Pawn_Beige_Ice";
		const int PawnSortingOrder = 20;

		static readonly Vector3Int MyPawnCell = new Vector3Int(-2, -2, 0);
		static readonly Vector3Int EnemyPawnCell = new Vector3Int(2, 2, 0);

		static BattleSceneAddressableLoader _instance;

		AsyncOperationHandle<GameObject> _mapHandle;
		readonly List<AsyncOperationHandle<GameObject>> _pawnHandles = new List<AsyncOperationHandle<GameObject>>();
		bool _hasMapHandle;
		bool _isLoading;
		int _loadVersion;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Bootstrap()
		{
			if (_instance != null)
				return;

			GameObject go = new GameObject("@BattleSceneAddressableLoader");
			DontDestroyOnLoad(go);
			_instance = go.AddComponent<BattleSceneAddressableLoader>();
		}

		void OnEnable()
		{
			SceneManager.sceneLoaded += OnSceneLoaded;
			SceneManager.sceneUnloaded += OnSceneUnloaded;

			Scene activeScene = SceneManager.GetActiveScene();
			if (activeScene.isLoaded && activeScene.name == BattleSceneName)
				LoadBattleSceneContent();
		}

		void OnDisable()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneUnloaded -= OnSceneUnloaded;
			ReleaseBattleSceneContent();
		}

		void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (scene.name == BattleSceneName)
			{
				LoadBattleSceneContent();
				return;
			}

			if (mode == LoadSceneMode.Single)
				ReleaseBattleSceneContent();
		}

		void OnSceneUnloaded(Scene scene)
		{
			if (scene.name == BattleSceneName)
				ReleaseBattleSceneContent();
		}

		async void LoadBattleSceneContent()
		{
			if (_isLoading || _hasMapHandle)
				return;

			_isLoading = true;
			int version = ++_loadVersion;

			AsyncOperationHandle<GameObject> mapHandle = Addressables.InstantiateAsync(BattleMapAddress);
			_mapHandle = mapHandle;
			_hasMapHandle = true;

			await mapHandle.Task;
			_isLoading = false;

			if (version != _loadVersion || SceneManager.GetActiveScene().name != BattleSceneName)
			{
				ReleaseHandle(mapHandle);
				return;
			}

			if (mapHandle.Status != AsyncOperationStatus.Succeeded)
			{
				Debug.LogError($"Failed to load battle map addressable: {BattleMapAddress}");
				ReleaseHandle(mapHandle);
				return;
			}

			GameObject battleMap = mapHandle.Result;
			battleMap.name = BattleMapAddress;
			SceneManager.MoveGameObjectToScene(battleMap, SceneManager.GetActiveScene());
			Debug.Log($"Loaded battle map: {BattleMapAddress}");

			Grid grid = battleMap.GetComponent<Grid>();
			if (grid == null)
			{
				Debug.LogError($"{BattleMapAddress} requires a Grid component.");
				return;
			}

			await SpawnDebugPawns(grid, version);
		}

		async System.Threading.Tasks.Task SpawnDebugPawns(Grid grid, int version)
		{
			await SpawnPawn("BattlePawn_My", MyPawnCell, grid, new Color(1f, 1f, 1f, 1f), version);
			await SpawnPawn("BattlePawn_Enemy", EnemyPawnCell, grid, new Color(1f, 0.75f, 0.75f, 1f), version);
		}

		async System.Threading.Tasks.Task SpawnPawn(string objectName, Vector3Int cell, Grid grid, Color color, int version)
		{
			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(BattlePawnAddress);
			_pawnHandles.Add(handle);

			await handle.Task;

			if (version != _loadVersion || SceneManager.GetActiveScene().name != BattleSceneName)
			{
				ReleaseHandle(handle);
				return;
			}

			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				Debug.LogError($"Failed to load battle pawn addressable: {BattlePawnAddress}");
				ReleaseHandle(handle);
				return;
			}

			GameObject pawn = handle.Result;
			pawn.name = objectName;
			Vector3 worldPosition = grid.GetCellCenterWorld(cell);
			worldPosition.z = 0f;
			pawn.transform.position = worldPosition;

			SpriteRenderer renderer = pawn.GetComponentInChildren<SpriteRenderer>();
			if (renderer != null)
			{
				renderer.sortingOrder = PawnSortingOrder;
				renderer.color = color;
			}

			SceneManager.MoveGameObjectToScene(pawn, SceneManager.GetActiveScene());
			Debug.Log($"Spawned battle pawn: {objectName} cell={cell} world={worldPosition}");
		}

		void ReleaseBattleSceneContent()
		{
			_loadVersion++;
			_isLoading = false;

			if (_hasMapHandle && _mapHandle.IsValid())
				Addressables.ReleaseInstance(_mapHandle);

			for (int i = 0; i < _pawnHandles.Count; i++)
			{
				AsyncOperationHandle<GameObject> handle = _pawnHandles[i];
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
			}

			_pawnHandles.Clear();
			_hasMapHandle = false;
			_mapHandle = default;
		}

		void ReleaseHandle(AsyncOperationHandle<GameObject> handle)
		{
			if (handle.IsValid())
				Addressables.ReleaseInstance(handle);

			if (_hasMapHandle && _mapHandle.Equals(handle))
			{
				_hasMapHandle = false;
				_mapHandle = default;
			}

			_pawnHandles.Remove(handle);
		}
	}
}
