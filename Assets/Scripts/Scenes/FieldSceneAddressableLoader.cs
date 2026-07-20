using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using Field;
using System.Collections;

namespace Scenes
{
	public sealed class FieldSceneAddressableLoader : MonoBehaviour
	{
		const string FieldSceneName = "FieldScene";
		const string FieldMapAddress = "Field_001";
		const string WorldMapAddress = "WorldMapRoot";
		const string FieldPawnAddress = "Pawn_Beige_Fire";
		const int WorldMapSortingOrderOffset = 1;
		const float FieldReadyTimeoutSeconds = 5f;

		static FieldSceneAddressableLoader _instance;

		AsyncOperationHandle<GameObject> _fieldMapHandle;
		AsyncOperationHandle<GameObject> _worldMapHandle;
		FieldObjectManager _objectManager;
		bool _hasFieldMapHandle;
		bool _hasWorldMapHandle;
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
				SceneTransitionOverlay.Hide();
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

			FieldObjectManager objectManager = fieldMap.GetComponent<FieldObjectManager>();
			if (objectManager == null)
				objectManager = fieldMap.AddComponent<FieldObjectManager>();

			_objectManager = objectManager;
			_objectManager.LocalPawnReady -= OnLocalPawnReady;
			_objectManager.LocalPawnReady += OnLocalPawnReady;
			objectManager.Initialize(walkArea, FieldPawnAddress);
			_isLocalPawnReady = objectManager.IsLocalPawnReady;
			TryCompleteFieldSceneTransition();
			LoadWorldMap(fieldMap, version);
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
				_isWorldMapReady = true;
				TryCompleteFieldSceneTransition();
				return;
			}

			GameObject worldMap = handle.Result;
			worldMap.name = WorldMapAddress;
			worldMap.transform.position = fieldMap.transform.position;
			worldMap.transform.rotation = fieldMap.transform.rotation;

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

			if (_hasFieldMapHandle && _fieldMapHandle.IsValid())
				Addressables.ReleaseInstance(_fieldMapHandle);

			if (_hasWorldMapHandle && _worldMapHandle.IsValid())
				Addressables.ReleaseInstance(_worldMapHandle);

			_hasFieldMapHandle = false;
			_hasWorldMapHandle = false;
			_fieldMapHandle = default;
			_worldMapHandle = default;
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

		static void ApplyWorldMapSorting(GameObject worldMap)
		{
			SpriteRenderer[] renderers = worldMap.GetComponentsInChildren<SpriteRenderer>(true);
			for (int i = 0; i < renderers.Length; i++)
				renderers[i].sortingOrder += WorldMapSortingOrderOffset;
		}
	}
}
