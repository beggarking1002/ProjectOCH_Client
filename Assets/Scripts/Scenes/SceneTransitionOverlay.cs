using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace Scenes
{
	public sealed class SceneTransitionOverlay : MonoBehaviour
	{
		const string OverlayAddress = "SceneTransitionOverlay";
		const float DefaultFadeSeconds = 0.25f;
		const int SortingOrder = 32700;

		static SceneTransitionOverlay _instance;

		AsyncOperationHandle<GameObject> _overlayHandle;
		GameObject _overlayInstance;
		Graphic[] _graphics;
		bool _hasOverlayHandle;
		bool _isLoading;
		int _fadeVersion;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Bootstrap()
		{
			EnsureInstance();
		}

		public static Task ShowAsync()
		{
			return EnsureInstance().ShowInternalAsync();
		}

		public static void Hide()
		{
			EnsureInstance().HideInternal(DefaultFadeSeconds);
		}

		static SceneTransitionOverlay EnsureInstance()
		{
			if (_instance != null)
				return _instance;

			GameObject go = new GameObject("@SceneTransitionOverlay");
			DontDestroyOnLoad(go);
			_instance = go.AddComponent<SceneTransitionOverlay>();
			return _instance;
		}

		async Task ShowInternalAsync()
		{
			if (_overlayInstance == null)
				await LoadOverlayAsync();

			if (_overlayInstance == null)
				return;

			_fadeVersion++;
			_overlayInstance.SetActive(true);
			SetOverlayAlpha(1f);
		}

		async Task LoadOverlayAsync()
		{
			if (_isLoading)
			{
				while (_isLoading)
					await Task.Yield();

				return;
			}

			_isLoading = true;
			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(OverlayAddress);
			await handle.Task;
			_isLoading = false;

			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				Debug.LogError($"Failed to load addressable UI prefab: {OverlayAddress}");
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				return;
			}

			_overlayHandle = handle;
			_hasOverlayHandle = true;
			_overlayInstance = handle.Result;
			_overlayInstance.name = OverlayAddress;
			DontDestroyOnLoad(_overlayInstance);

			Canvas canvas = _overlayInstance.GetComponent<Canvas>();
			if (canvas == null)
			{
				Debug.LogError($"{OverlayAddress} prefab requires a Canvas component on the root.");
				ReleaseOverlay();
				return;
			}

			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = SortingOrder;
			_graphics = _overlayInstance.GetComponentsInChildren<Graphic>(true);
		}

		async void HideInternal(float duration)
		{
			if (_overlayInstance == null)
				return;

			int version = ++_fadeVersion;
			float elapsed = 0f;
			float startAlpha = GetCurrentAlpha();

			while (elapsed < duration)
			{
				if (version != _fadeVersion)
					return;

				elapsed += Time.unscaledDeltaTime;
				float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
				SetOverlayAlpha(Mathf.Lerp(startAlpha, 0f, t));
				await Task.Yield();
			}

			if (version != _fadeVersion || _overlayInstance == null)
				return;

			SetOverlayAlpha(0f);
			_overlayInstance.SetActive(false);
		}

		float GetCurrentAlpha()
		{
			if (_graphics == null || _graphics.Length == 0 || _graphics[0] == null)
				return 1f;

			return _graphics[0].color.a;
		}

		void SetOverlayAlpha(float alpha)
		{
			if (_graphics == null)
				return;

			for (int i = 0; i < _graphics.Length; i++)
			{
				Graphic graphic = _graphics[i];
				if (graphic == null)
					continue;

				Color color = graphic.color;
				color.a = alpha;
				graphic.color = color;
			}
		}

		void OnDestroy()
		{
			ReleaseOverlay();
			if (_instance == this)
				_instance = null;
		}

		void ReleaseOverlay()
		{
			if (_hasOverlayHandle && _overlayHandle.IsValid())
				Addressables.ReleaseInstance(_overlayHandle);

			_hasOverlayHandle = false;
			_overlayHandle = default;
			_overlayInstance = null;
			_graphics = null;
		}
	}
}
