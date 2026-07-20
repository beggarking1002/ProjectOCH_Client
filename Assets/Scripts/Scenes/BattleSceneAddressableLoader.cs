using Battle;
using App;
using System.Collections;
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
		const string BattlePawnAddress = "Pawn_Beige_Fire";
		const float BattleReadyTimeoutSeconds = 5f;

		static BattleSceneAddressableLoader _instance;

		AsyncOperationHandle<GameObject> _mapHandle;
		bool _hasMapHandle;
		bool _isLoading;
		bool _battleSceneTransitionCompleted;
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
			_battleSceneTransitionCompleted = false;

			await SceneTransitionOverlay.ShowAsync();
			if (version != _loadVersion || SceneManager.GetActiveScene().name != BattleSceneName)
			{
				_isLoading = false;
				return;
			}

			StartCoroutine(BattleReadyTimeout(version));

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
				SceneTransitionOverlay.Hide();
				return;
			}

			GameObject battleMap = mapHandle.Result;
			battleMap.name = BattleMapAddress;
			SceneManager.MoveGameObjectToScene(battleMap, SceneManager.GetActiveScene());
			Debug.Log($"Loaded battle map: {BattleMapAddress}");

			BattleMapGrid mapGrid = battleMap.GetComponent<BattleMapGrid>();
			if (mapGrid == null)
				mapGrid = battleMap.AddComponent<BattleMapGrid>();

			mapGrid.InitializeIfNeeded();
			await mapGrid.LoadVisualTilesAsync();
			if (mapGrid.Grid == null)
			{
				Debug.LogError($"{BattleMapAddress} requires a Grid component.");
				SceneTransitionOverlay.Hide();
				return;
			}

			BattleObjectManager objectManager = battleMap.GetComponent<BattleObjectManager>();
			if (objectManager == null)
				objectManager = battleMap.AddComponent<BattleObjectManager>();

			objectManager.Initialize(mapGrid, BattlePawnAddress);
			Protocol.S_ENTER_BATTLE enterBattle = GameRoot.Instance != null ? GameRoot.Instance.Network.LastEnterBattle : null;
			if (enterBattle != null && enterBattle.Success)
				await objectManager.SpawnFromEnterBattleAsync(enterBattle);
			else
				await objectManager.SpawnDebugPawnsAsync();

			if (version != _loadVersion || SceneManager.GetActiveScene().name != BattleSceneName)
				return;

			BattleUIController uiController = battleMap.GetComponent<BattleUIController>();
			if (uiController == null)
				uiController = battleMap.AddComponent<BattleUIController>();

			await uiController.InitializeAsync(objectManager);

			if (version != _loadVersion || SceneManager.GetActiveScene().name != BattleSceneName)
				return;

			CompleteBattleSceneTransition();
		}

		void ReleaseBattleSceneContent()
		{
			_loadVersion++;
			_isLoading = false;
			_battleSceneTransitionCompleted = false;

			if (_hasMapHandle && _mapHandle.IsValid())
				Addressables.ReleaseInstance(_mapHandle);

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
		}

		void CompleteBattleSceneTransition()
		{
			if (_battleSceneTransitionCompleted)
				return;

			_battleSceneTransitionCompleted = true;
			Debug.Log("BattleScene is ready. Hiding transition overlay.");
			SceneTransitionOverlay.Hide();
		}

		IEnumerator BattleReadyTimeout(int version)
		{
			yield return new WaitForSecondsRealtime(BattleReadyTimeoutSeconds);

			if (version != _loadVersion || _battleSceneTransitionCompleted || SceneManager.GetActiveScene().name != BattleSceneName)
				yield break;

			_battleSceneTransitionCompleted = true;
			Debug.LogWarning($"BattleScene readiness timed out after {BattleReadyTimeoutSeconds:0.#} seconds. Hiding transition overlay.");
			SceneTransitionOverlay.Hide();
		}
	}
}
