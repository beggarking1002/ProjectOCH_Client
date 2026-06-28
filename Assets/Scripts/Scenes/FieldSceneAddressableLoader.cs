using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using Field;

namespace Scenes
{
	public sealed class FieldSceneAddressableLoader : MonoBehaviour
	{
		const string FieldSceneName = "FieldScene";
		const string FieldMapAddress = "Field";
		const string WorldMapAddress = "WorldMapRoot";
		const string FieldPawnAddress = "Field_Pawn";
		const int WorldMapSortingOrderOffset = 1;

		static FieldSceneAddressableLoader _instance;

		AsyncOperationHandle<GameObject> _fieldMapHandle;
		AsyncOperationHandle<GameObject> _worldMapHandle;
		AsyncOperationHandle<GameObject> _fieldPawnHandle;
		bool _hasFieldMapHandle;
		bool _hasWorldMapHandle;
		bool _hasFieldPawnHandle;
		bool _isLoading;
		int _loadVersion;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Bootstrap()
		{
			if (_instance != null)
				return;

			GameObject go = new GameObject("@FieldSceneAddressableLoader");
			DontDestroyOnLoad(go);
			_instance = go.AddComponent<FieldSceneAddressableLoader>();
		}

		void OnEnable()
		{
			SceneManager.sceneLoaded += OnSceneLoaded;
			SceneManager.sceneUnloaded += OnSceneUnloaded;

			Scene activeScene = SceneManager.GetActiveScene();
			if (activeScene.isLoaded && activeScene.name == FieldSceneName)
				LoadFieldMap();
		}

		void OnDisable()
		{
			SceneManager.sceneLoaded -= OnSceneLoaded;
			SceneManager.sceneUnloaded -= OnSceneUnloaded;
			ReleaseFieldSceneAddressables();
		}

		void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (scene.name == FieldSceneName)
			{
				LoadFieldMap();
				return;
			}

			if (mode == LoadSceneMode.Single)
				ReleaseFieldSceneAddressables();
		}

		void OnSceneUnloaded(Scene scene)
		{
			if (scene.name == FieldSceneName)
				ReleaseFieldSceneAddressables();
		}

		async void LoadFieldMap()
		{
			if (_isLoading || _hasFieldMapHandle)
				return;

			_isLoading = true;
			int version = ++_loadVersion;

			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(FieldMapAddress);
			_fieldMapHandle = handle;
			_hasFieldMapHandle = true;

			await handle.Task;

			_isLoading = false;

			if (version != _loadVersion || SceneManager.GetActiveScene().name != FieldSceneName)
			{
				ReleaseHandle(handle);
				return;
			}

			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				Debug.LogError($"Failed to load addressable map: {FieldMapAddress}");
				ReleaseHandle(handle);
				return;
			}

			GameObject fieldMap = handle.Result;
			fieldMap.name = FieldMapAddress;
			FieldMapWalkArea walkArea = fieldMap.GetComponent<FieldMapWalkArea>();
			if (walkArea == null)
				walkArea = fieldMap.AddComponent<FieldMapWalkArea>();

			walkArea.InitializeIfNeeded();
			SceneManager.MoveGameObjectToScene(fieldMap, SceneManager.GetActiveScene());
			Debug.Log($"Loaded addressable map: {FieldMapAddress}");

			LoadWorldMap(fieldMap, version);
			LoadFieldPawn(walkArea, version);
		}

		async void LoadWorldMap(GameObject fieldMap, int version)
		{
			if (_hasWorldMapHandle)
				return;

			Debug.Log($"Loading addressable world map: {WorldMapAddress}");
			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(WorldMapAddress);
			_worldMapHandle = handle;
			_hasWorldMapHandle = true;

			await handle.Task;

			if (version != _loadVersion || SceneManager.GetActiveScene().name != FieldSceneName)
			{
				ReleaseWorldMapHandle(handle);
				return;
			}

			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				Debug.LogError($"Failed to load addressable world map: {WorldMapAddress}");
				ReleaseWorldMapHandle(handle);
				return;
			}

			GameObject worldMap = handle.Result;
			worldMap.name = WorldMapAddress;
			worldMap.transform.position = fieldMap.transform.position;
			worldMap.transform.rotation = fieldMap.transform.rotation;

			ApplyWorldMapSorting(worldMap);
			SceneManager.MoveGameObjectToScene(worldMap, SceneManager.GetActiveScene());
			Debug.Log($"Loaded addressable world map: {WorldMapAddress}");
		}

		async void LoadFieldPawn(FieldMapWalkArea walkArea, int version)
		{
			if (_hasFieldPawnHandle)
				return;

			Debug.Log($"Loading addressable pawn: {FieldPawnAddress}");
			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(FieldPawnAddress);
			_fieldPawnHandle = handle;
			_hasFieldPawnHandle = true;

			await handle.Task;

			if (version != _loadVersion || SceneManager.GetActiveScene().name != FieldSceneName)
			{
				ReleasePawnHandle(handle);
				return;
			}

			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				Debug.LogError($"Failed to load addressable pawn: {FieldPawnAddress}");
				ReleasePawnHandle(handle);
				return;
			}

			GameObject fieldPawn = handle.Result;
			fieldPawn.name = FieldPawnAddress;
			FieldPawnController pawnController = fieldPawn.GetComponent<FieldPawnController>();
			if (pawnController == null)
				pawnController = fieldPawn.AddComponent<FieldPawnController>();

			Vector3 spawnPosition = walkArea.GetDefaultSpawnPosition(fieldPawn.transform.position.z);
			pawnController.Initialize(walkArea, spawnPosition);
			SceneManager.MoveGameObjectToScene(fieldPawn, SceneManager.GetActiveScene());
			Debug.Log($"Loaded addressable pawn: {FieldPawnAddress} at world {spawnPosition}");
		}

		void ReleaseFieldSceneAddressables()
		{
			_loadVersion++;
			_isLoading = false;

			if (_hasFieldMapHandle && _fieldMapHandle.IsValid())
				Addressables.ReleaseInstance(_fieldMapHandle);

			if (_hasWorldMapHandle && _worldMapHandle.IsValid())
				Addressables.ReleaseInstance(_worldMapHandle);

			if (_hasFieldPawnHandle && _fieldPawnHandle.IsValid())
				Addressables.ReleaseInstance(_fieldPawnHandle);

			_hasFieldMapHandle = false;
			_hasWorldMapHandle = false;
			_hasFieldPawnHandle = false;
			_fieldMapHandle = default;
			_worldMapHandle = default;
			_fieldPawnHandle = default;
		}

		void ReleaseHandle(AsyncOperationHandle<GameObject> handle)
		{
			if (handle.IsValid())
				Addressables.ReleaseInstance(handle);

			if (_hasFieldMapHandle && _fieldMapHandle.Equals(handle))
			{
				_hasFieldMapHandle = false;
				_fieldMapHandle = default;
			}
		}

		void ReleaseWorldMapHandle(AsyncOperationHandle<GameObject> handle)
		{
			if (handle.IsValid())
				Addressables.ReleaseInstance(handle);

			if (_hasWorldMapHandle && _worldMapHandle.Equals(handle))
			{
				_hasWorldMapHandle = false;
				_worldMapHandle = default;
			}
		}

		void ReleasePawnHandle(AsyncOperationHandle<GameObject> handle)
		{
			if (handle.IsValid())
				Addressables.ReleaseInstance(handle);

			if (_hasFieldPawnHandle && _fieldPawnHandle.Equals(handle))
			{
				_hasFieldPawnHandle = false;
				_fieldPawnHandle = default;
			}
		}

		static void ApplyWorldMapSorting(GameObject worldMap)
		{
			SpriteRenderer[] renderers = worldMap.GetComponentsInChildren<SpriteRenderer>(true);
			for (int i = 0; i < renderers.Length; i++)
				renderers[i].sortingOrder += WorldMapSortingOrderOffset;
		}
	}
}
