using System.Collections;
using Field;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace Scenes
{
	public sealed class FieldSceneAddressableLoader : MonoBehaviour
	{
		const string FieldSceneName = "FieldScene";
		const string FieldWalkMapAddress = "Field_001_WalkMap";
		const string FieldLogicRootName = "@FieldLogic";
		const string WorldMapAddress = "WorldMapRoot";
		const string FieldSceneUiAddress = "FieldSceneHUD";
		const string FieldPawnAddress = "Pawn_Beige_Ice";
		const int WorldMapSortingOrderOffset = 1;
		const float FieldReadyTimeoutSeconds = 5f;

		static FieldSceneAddressableLoader _instance;

		AsyncOperationHandle<TextAsset> _walkMapHandle;
		AsyncOperationHandle<GameObject> _worldMapHandle;
		AsyncOperationHandle<GameObject> _fieldSceneUiHandle;
		GameObject _fieldLogicRoot;
		FieldObjectManager _objectManager;
		bool _hasWalkMapHandle;
		bool _hasWorldMapHandle;
		bool _hasFieldSceneUiHandle;
		bool _isLoading;
		bool _isLocalPawnReady;
		bool _isWorldMapReady;
		bool _fieldSceneTransitionCompleted;
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
				LoadFieldSceneContent();
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
				LoadFieldSceneContent();
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

		async void LoadFieldSceneContent()
		{
			if (_isLoading || _hasWalkMapHandle)
				return;

			_isLoading = true;
			int version = ++_loadVersion;
			_isLocalPawnReady = false;
			_isWorldMapReady = false;
			_fieldSceneTransitionCompleted = false;

			await SceneTransitionOverlay.ShowAsync();
			if (version != _loadVersion || SceneManager.GetActiveScene().name != FieldSceneName)
			{
				_isLoading = false;
				return;
			}
			StartCoroutine(FieldReadyTimeout(version));

			AsyncOperationHandle<TextAsset> handle = Addressables.LoadAssetAsync<TextAsset>(FieldWalkMapAddress);
			_walkMapHandle = handle;
			_hasWalkMapHandle = true;
			await handle.Task;
			_isLoading = false;

			if (version != _loadVersion || SceneManager.GetActiveScene().name != FieldSceneName)
			{
				ReleaseWalkMapHandle(handle);
				return;
			}

			if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
			{
				Debug.LogError($"Failed to load field walk map JSON: {FieldWalkMapAddress}");
				ReleaseWalkMapHandle(handle);
				SceneTransitionOverlay.Hide();
				return;
			}

			_fieldLogicRoot = new GameObject(FieldLogicRootName);
			SceneManager.MoveGameObjectToScene(_fieldLogicRoot, SceneManager.GetActiveScene());
			FieldMapWalkArea walkArea = _fieldLogicRoot.AddComponent<FieldMapWalkArea>();
			if (walkArea.Initialize(handle.Result.text) == false)
			{
				Debug.LogError($"Failed to initialize field walk map JSON: {FieldWalkMapAddress}");
				Destroy(_fieldLogicRoot);
				_fieldLogicRoot = null;
				ReleaseWalkMapHandle(handle);
				SceneTransitionOverlay.Hide();
				return;
			}

			Debug.Log($"Loaded field walk map JSON: {walkArea.MapId}. Tilemap prefab is not instantiated.");
			FieldObjectManager objectManager = _fieldLogicRoot.AddComponent<FieldObjectManager>();
			_objectManager = objectManager;
			_objectManager.LocalPawnReady -= OnLocalPawnReady;
			_objectManager.LocalPawnReady += OnLocalPawnReady;
			objectManager.Initialize(walkArea, FieldPawnAddress);
			_isLocalPawnReady = objectManager.IsLocalPawnReady;
			TryCompleteFieldSceneTransition();
			LoadWorldMap(_fieldLogicRoot.transform, version);
			LoadFieldSceneUi(version);
		}

		async void LoadFieldSceneUi(int version)
		{
			if (_hasFieldSceneUiHandle)
				return;

			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(FieldSceneUiAddress);
			_fieldSceneUiHandle = handle;
			_hasFieldSceneUiHandle = true;
			await handle.Task;

			if (version != _loadVersion || SceneManager.GetActiveScene().name != FieldSceneName)
			{
				ReleaseFieldSceneUiHandle(handle);
				return;
			}

			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				Debug.LogError($"Failed to load field scene UI: {FieldSceneUiAddress}");
				ReleaseFieldSceneUiHandle(handle);
				return;
			}

			SceneManager.MoveGameObjectToScene(handle.Result, SceneManager.GetActiveScene());
		}

		async void LoadWorldMap(Transform logicRoot, int version)
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
				_isWorldMapReady = true;
				TryCompleteFieldSceneTransition();
				return;
			}

			GameObject worldMap = handle.Result;
			worldMap.name = WorldMapAddress;
			worldMap.transform.position = logicRoot.position;
			worldMap.transform.rotation = logicRoot.rotation;
			ApplyWorldMapSorting(worldMap);
			SceneManager.MoveGameObjectToScene(worldMap, SceneManager.GetActiveScene());
			Debug.Log($"Loaded addressable world map: {WorldMapAddress}");
			_isWorldMapReady = true;
			TryCompleteFieldSceneTransition();
		}

		void ReleaseFieldSceneAddressables()
		{
			_loadVersion++;
			_isLoading = false;
			_isLocalPawnReady = false;
			_isWorldMapReady = false;
			_fieldSceneTransitionCompleted = false;
			UnsubscribeObjectManagerReady();

			if (_fieldLogicRoot != null)
				Destroy(_fieldLogicRoot);

			if (_hasWalkMapHandle && _walkMapHandle.IsValid())
				Addressables.Release(_walkMapHandle);

			if (_hasWorldMapHandle && _worldMapHandle.IsValid())
				Addressables.ReleaseInstance(_worldMapHandle);

			if (_hasFieldSceneUiHandle && _fieldSceneUiHandle.IsValid())
				Addressables.ReleaseInstance(_fieldSceneUiHandle);

			_fieldLogicRoot = null;
			_hasWalkMapHandle = false;
			_hasWorldMapHandle = false;
			_hasFieldSceneUiHandle = false;
			_walkMapHandle = default;
			_worldMapHandle = default;
			_fieldSceneUiHandle = default;
		}

		void OnLocalPawnReady(FieldObjectManager objectManager)
		{
			if (objectManager != _objectManager)
				return;

			_isLocalPawnReady = true;
			TryCompleteFieldSceneTransition();
		}

		void TryCompleteFieldSceneTransition()
		{
			if (_fieldSceneTransitionCompleted || _isLocalPawnReady == false || _isWorldMapReady == false)
				return;

			_fieldSceneTransitionCompleted = true;
			Debug.Log("FieldScene is ready. Hiding transition overlay.");
			SceneTransitionOverlay.Hide();
		}

		IEnumerator FieldReadyTimeout(int version)
		{
			yield return new WaitForSecondsRealtime(FieldReadyTimeoutSeconds);

			if (version != _loadVersion || _fieldSceneTransitionCompleted || SceneManager.GetActiveScene().name != FieldSceneName)
				yield break;

			_fieldSceneTransitionCompleted = true;
			Debug.LogWarning($"FieldScene readiness timed out after {FieldReadyTimeoutSeconds:0.#} seconds. Hiding transition overlay.");
			SceneTransitionOverlay.Hide();
		}

		void UnsubscribeObjectManagerReady()
		{
			if (_objectManager != null)
				_objectManager.LocalPawnReady -= OnLocalPawnReady;

			_objectManager = null;
		}

		void ReleaseWalkMapHandle(AsyncOperationHandle<TextAsset> handle)
		{
			if (handle.IsValid())
				Addressables.Release(handle);

			if (_hasWalkMapHandle && _walkMapHandle.Equals(handle))
			{
				_hasWalkMapHandle = false;
				_walkMapHandle = default;
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

		void ReleaseFieldSceneUiHandle(AsyncOperationHandle<GameObject> handle)
		{
			if (handle.IsValid())
				Addressables.ReleaseInstance(handle);

			if (_hasFieldSceneUiHandle && _fieldSceneUiHandle.Equals(handle))
			{
				_hasFieldSceneUiHandle = false;
				_fieldSceneUiHandle = default;
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
