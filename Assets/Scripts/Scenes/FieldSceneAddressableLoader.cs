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
		const string FieldMapAddress = "FieldMap";
		const string FieldPawnAddress = "Field_Pawn";
		static readonly AxialCoord FieldPawnSpawnAxial = new AxialCoord(0, 0);

		static FieldSceneAddressableLoader _instance;

		AsyncOperationHandle<GameObject> _fieldMapHandle;
		AsyncOperationHandle<GameObject> _fieldPawnHandle;
		bool _hasFieldMapHandle;
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
			FieldMapAxialCoordinates coordinates = fieldMap.GetComponent<FieldMapAxialCoordinates>();
			if (coordinates == null)
				coordinates = fieldMap.AddComponent<FieldMapAxialCoordinates>();

			coordinates.InitializeIfNeeded();
			SceneManager.MoveGameObjectToScene(fieldMap, SceneManager.GetActiveScene());
			Debug.Log($"Loaded addressable map: {FieldMapAddress}");

			LoadFieldPawn(coordinates, version);
		}

		async void LoadFieldPawn(FieldMapAxialCoordinates coordinates, int version)
		{
			if (_hasFieldPawnHandle)
				return;

			Debug.Log($"Loading addressable pawn: {FieldPawnAddress} at axial {FieldPawnSpawnAxial}");
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

			pawnController.Initialize(coordinates, FieldPawnSpawnAxial);
			SceneManager.MoveGameObjectToScene(fieldPawn, SceneManager.GetActiveScene());
			Debug.Log($"Loaded addressable pawn: {FieldPawnAddress} at axial {FieldPawnSpawnAxial}");
		}

		void ReleaseFieldSceneAddressables()
		{
			_loadVersion++;
			_isLoading = false;

			if (_hasFieldMapHandle && _fieldMapHandle.IsValid())
				Addressables.ReleaseInstance(_fieldMapHandle);

			if (_hasFieldPawnHandle && _fieldPawnHandle.IsValid())
				Addressables.ReleaseInstance(_fieldPawnHandle);

			_hasFieldMapHandle = false;
			_hasFieldPawnHandle = false;
			_fieldMapHandle = default;
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
	}
}
