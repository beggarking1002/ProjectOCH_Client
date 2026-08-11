using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using Battle;
using Field;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace App
{
	// Owns the custom screen-space cursor independently of app/network bootstrap.
	[DefaultExecutionOrder(-950)]
	[DisallowMultipleComponent]
	public sealed class GameCursorController : MonoBehaviour
	{
		const string HandCursorAddress = "Cursor_Hand";
		const string AttackCursorAddress = "Cursor_Attack";
		const string LootCursorAddress = "Cursor_Loot";
		// This is deliberately above every gameplay/UI canvas, including field invites.
		const int CursorSortingOrder = 32766;
		static readonly Vector2 HandCursorSize = new(54f, 65f);
		static readonly Vector2 HandCursorPivot = new(0.2f, 0.96f);
		static readonly Vector2 AttackCursorPivot = new(0.26f, 0.98f);

		AsyncOperationHandle<Sprite> _handCursorHandle;
		AsyncOperationHandle<Sprite> _attackCursorHandle;
		AsyncOperationHandle<Sprite> _lootCursorHandle;
		Canvas _cursorCanvas;
		Image _handCursorImage;
		bool _hasHandCursorHandle;
		bool _hasAttackCursorHandle;
		bool _hasLootCursorHandle;
		BattleObjectManager _battleObjectManager;
		FieldObjectManager _fieldObjectManager;

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
			CreateCursorOverlay();
			_handCursorHandle = Addressables.LoadAssetAsync<Sprite>(HandCursorAddress);
			_hasHandCursorHandle = true;
			_attackCursorHandle = Addressables.LoadAssetAsync<Sprite>(AttackCursorAddress);
			_hasAttackCursorHandle = true;
			_lootCursorHandle = Addressables.LoadAssetAsync<Sprite>(LootCursorAddress);
			_hasLootCursorHandle = true;
			await _handCursorHandle.Task;

			if (this == null)
			{
				ReleaseHandCursor();
				return;
			}

			if (_handCursorHandle.Status != AsyncOperationStatus.Succeeded || _handCursorHandle.Result == null)
			{
				Debug.LogWarning($"Failed to load hand cursor addressable: {HandCursorAddress}");
				ReleaseHandCursor();
				return;
			}

			_handCursorImage.sprite = _handCursorHandle.Result;
			_handCursorImage.enabled = true;
			SetSystemCursorVisible(false);
			UpdateCursorPosition();

			await _attackCursorHandle.Task;
			await _lootCursorHandle.Task;
		}

		void LateUpdate()
		{
			UpdateCursorPosition();
		}

		void OnApplicationFocus(bool hasFocus)
		{
			SetSystemCursorVisible(hasFocus == false || _handCursorImage == null || _handCursorImage.sprite == null);
		}

		void OnDestroy()
		{
			SetSystemCursorVisible(true);
			ReleaseHandCursor();
		}

		void UpdateCursorPosition()
		{
			if (_handCursorImage == null || _handCursorImage.sprite == null)
				return;

			if (Application.isFocused == false)
			{
				_handCursorImage.enabled = false;
				SetSystemCursorVisible(true);
				return;
			}

			if (!TryGetPointerPosition(out Vector2 screenPosition))
				return;

			UpdateCursorAppearance(screenPosition);
			_handCursorImage.enabled = true;
			SetSystemCursorVisible(false);
		}

		void UpdateCursorAppearance(Vector2 screenPosition)
		{
			BattleCursorHint battleHint = GetBattleCursorHint(screenPosition);

			bool useAttackCursor = battleHint == BattleCursorHint.Attack
				|| (battleHint == BattleCursorHint.Default && IsPointerOverUi() == false && IsPointerOverFieldEnemyPawn(screenPosition));
			bool useLootCursor = battleHint == BattleCursorHint.Assist;
			Sprite sprite = useAttackCursor && _attackCursorHandle.Status == AsyncOperationStatus.Succeeded
				? _attackCursorHandle.Result
				: useLootCursor && _lootCursorHandle.Status == AsyncOperationStatus.Succeeded
					? _lootCursorHandle.Result
					: _handCursorHandle.Result;
			if (sprite != null && _handCursorImage.sprite != sprite)
			{
				_handCursorImage.sprite = sprite;
				_handCursorImage.rectTransform.pivot = useAttackCursor
					? AttackCursorPivot
					: useLootCursor ? HandCursorPivot : HandCursorPivot;
			}

			_handCursorImage.rectTransform.anchoredPosition = screenPosition;
		}

		BattleCursorHint GetBattleCursorHint(Vector2 screenPosition)
		{
			if (_battleObjectManager == null)
				_battleObjectManager = FindFirstObjectByType<BattleObjectManager>();

			return _battleObjectManager != null && IsPointerOverUi() == false
				? _battleObjectManager.GetCursorHint(screenPosition)
				: BattleCursorHint.Default;
		}

		bool IsPointerOverFieldEnemyPawn(Vector2 screenPosition)
		{
			if (_fieldObjectManager == null)
				_fieldObjectManager = FindFirstObjectByType<FieldObjectManager>();

			return _fieldObjectManager != null && _fieldObjectManager.IsPointerOverRemotePawn(screenPosition);
		}

		static bool IsPointerOverUi()
		{
			return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
		}

		void CreateCursorOverlay()
		{
			GameObject canvasObject = new GameObject("@GameCursorCanvas", typeof(RectTransform), typeof(Canvas));
			canvasObject.transform.SetParent(transform, false);
			_cursorCanvas = canvasObject.GetComponent<Canvas>();
			_cursorCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
			_cursorCanvas.overrideSorting = true;
			_cursorCanvas.sortingOrder = CursorSortingOrder;

			GameObject imageObject = new GameObject("Hand", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			imageObject.transform.SetParent(canvasObject.transform, false);
			_handCursorImage = imageObject.GetComponent<Image>();
			_handCursorImage.raycastTarget = false;
			_handCursorImage.preserveAspect = true;
			_handCursorImage.enabled = false;

			RectTransform rect = _handCursorImage.rectTransform;
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.zero;
			rect.pivot = HandCursorPivot;
			rect.sizeDelta = HandCursorSize;
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

		void ReleaseHandCursor()
		{
			if (_hasHandCursorHandle && _handCursorHandle.IsValid())
				Addressables.Release(_handCursorHandle);

			_hasHandCursorHandle = false;
			_handCursorHandle = default;

			if (_hasAttackCursorHandle && _attackCursorHandle.IsValid())
				Addressables.Release(_attackCursorHandle);

			_hasAttackCursorHandle = false;
			_attackCursorHandle = default;

			if (_hasLootCursorHandle && _lootCursorHandle.IsValid())
				Addressables.Release(_lootCursorHandle);

			_hasLootCursorHandle = false;
			_lootCursorHandle = default;

		}

		static void SetSystemCursorVisible(bool visible)
		{
			Cursor.lockState = CursorLockMode.None;
			Cursor.visible = visible;
		}
	}
}
