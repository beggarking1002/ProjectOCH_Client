using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace App
{
	// Owns the custom world-space cursor independently of app/network bootstrap.
	[DefaultExecutionOrder(-950)]
	[DisallowMultipleComponent]
	public sealed class GameCursorController : MonoBehaviour
	{
		const string CursorAddress = "Cursor";
		const int CursorSortingOrder = 1000;

		AsyncOperationHandle<GameObject> _cursorHandle;
		GameObject _cursorInstance;
		bool _hasCursorHandle;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Bootstrap()
		{
			if (FindFirstObjectByType<GameCursorController>() != null)
				return;

			GameObject controller = new GameObject("@GameCursorController");
			DontDestroyOnLoad(controller);
			controller.AddComponent<GameCursorController>();
		}

		async void Start()
		{
			_cursorHandle = Addressables.InstantiateAsync(CursorAddress);
			_hasCursorHandle = true;
			await _cursorHandle.Task;

			if (this == null)
			{
				ReleaseCursor();
				return;
			}

			if (_cursorHandle.Status != AsyncOperationStatus.Succeeded || _cursorHandle.Result == null)
			{
				Debug.LogWarning($"Failed to load game cursor addressable: {CursorAddress}");
				ReleaseCursor();
				return;
			}

			_cursorInstance = _cursorHandle.Result;
			_cursorInstance.name = "@GameCursor";
			_cursorInstance.transform.SetParent(transform, true);
			foreach (ParticleSystemRenderer renderer in _cursorInstance.GetComponentsInChildren<ParticleSystemRenderer>(true))
				renderer.sortingOrder = Mathf.Max(renderer.sortingOrder, CursorSortingOrder);

			SetSystemCursorVisible(false);
			UpdateCursorPosition();
		}

		void LateUpdate()
		{
			UpdateCursorPosition();
		}

		void OnApplicationFocus(bool hasFocus)
		{
			SetSystemCursorVisible(hasFocus == false || _cursorInstance == null);
		}

		void OnDestroy()
		{
			SetSystemCursorVisible(true);
			ReleaseCursor();
		}

		void UpdateCursorPosition()
		{
			if (_cursorInstance == null)
				return;

			Camera camera = Camera.main;
			if (camera == null || Application.isFocused == false)
			{
				_cursorInstance.SetActive(false);
				SetSystemCursorVisible(true);
				return;
			}

			if (_cursorInstance.activeSelf == false)
				_cursorInstance.SetActive(true);

			if (!TryGetPointerPosition(out Vector2 screenPosition))
				return;

			Ray ray = camera.ScreenPointToRay(screenPosition);
			Plane gameplayPlane = new Plane(Vector3.forward, Vector3.zero);
			if (gameplayPlane.Raycast(ray, out float distance))
				_cursorInstance.transform.position = ray.GetPoint(distance);

			SetSystemCursorVisible(false);
		}

		static bool TryGetPointerPosition(out Vector2 screenPosition)
		{
#if ENABLE_INPUT_SYSTEM
			Mouse mouse = Mouse.current;
			if (mouse != null)
			{
				screenPosition = mouse.position.ReadValue();
				return true;
			}
#elif ENABLE_LEGACY_INPUT_MANAGER
			screenPosition = Input.mousePosition;
			return true;
#endif

			screenPosition = default;
			return false;
		}

		void ReleaseCursor()
		{
			if (_hasCursorHandle && _cursorHandle.IsValid())
				Addressables.ReleaseInstance(_cursorHandle);

			_hasCursorHandle = false;
			_cursorHandle = default;
			_cursorInstance = null;
		}

		static void SetSystemCursorVisible(bool visible)
		{
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = visible;
		}
	}
}
