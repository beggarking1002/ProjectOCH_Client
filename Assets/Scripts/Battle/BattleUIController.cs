using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Battle
{
	[DisallowMultipleComponent]
	public sealed class BattleUIController : MonoBehaviour
	{
		const string BattleUiName = "Canvas_BattleUI";
		const string BattleUiAddress = "BattleSceneUI";
		const string BattleUiEditorPath = "Assets/@Resources/Prefab/UI/Canvas_BattleUI.prefab";

		static readonly ActionSlotBinding[] SlotBindings =
		{
			new ActionSlotBinding("ActionSlot_01", BattleActionMode.Move, false),
			new ActionSlotBinding("ActionSlot_02", BattleActionMode.Skill1, false),
			new ActionSlotBinding("ActionSlot_03", BattleActionMode.Skill2, false),
			new ActionSlotBinding("ActionSlot_04", BattleActionMode.Skill3, false),
			new ActionSlotBinding("ActionSlot_05", BattleActionMode.Skill4, false),
			new ActionSlotBinding("ActionSlot_06", BattleActionMode.Ultimate, false),
			new ActionSlotBinding("ActionSlot_07", BattleActionMode.SubAction, false),
			new ActionSlotBinding("ActionSlot_08", BattleActionMode.Move, true),
		};

		readonly Button[] _actionButtons = new Button[SlotBindings.Length];
		readonly Image[] _actionImages = new Image[SlotBindings.Length];
		readonly Color[] _normalColors = new Color[SlotBindings.Length];

		BattleObjectManager _objectManager;
		GameObject _uiInstance;
		AsyncOperationHandle<GameObject> _uiHandle;
		bool _hasUiHandle;
		bool _isBinding;
		bool _bound;

		public void Initialize(BattleObjectManager objectManager)
		{
			_objectManager = objectManager;
			EnsureEventSystem();
			BindOrLoadUi();
		}

		void Update()
		{
			Refresh();
		}

		void OnDestroy()
		{
			if (_hasUiHandle && _uiHandle.IsValid())
			{
				Addressables.ReleaseInstance(_uiHandle);
				_hasUiHandle = false;
				_uiHandle = default;
				_uiInstance = null;
				return;
			}

			if (_uiInstance != null)
				Destroy(_uiInstance);
		}

		async void BindOrLoadUi()
		{
			if (_isBinding || _bound)
				return;

			_isBinding = true;

			GameObject existing = FindExistingBattleUi();
			if (existing != null)
			{
				BindUi(existing);
				_isBinding = false;
				return;
			}

			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(BattleUiAddress);
			await handle.Task;

			if (this == null)
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				return;
			}

			if (handle.Status == AsyncOperationStatus.Succeeded)
			{
				_uiHandle = handle;
				_hasUiHandle = true;
				BindUi(handle.Result);
				_isBinding = false;
				return;
			}

			if (handle.IsValid())
				Addressables.ReleaseInstance(handle);

			GameObject editorInstance = InstantiateFromEditorAsset();
			if (editorInstance != null)
				BindUi(editorInstance);
			else
				Debug.LogError($"Failed to load {BattleUiName}. Register it as Addressable address '{BattleUiAddress}' or place it in the BattleScene.");

			_isBinding = false;
		}

		void BindUi(GameObject uiObject)
		{
			if (uiObject == null)
				return;

			_uiInstance = uiObject;
			_uiInstance.name = BattleUiName;
			SceneManager.MoveGameObjectToScene(_uiInstance, gameObject.scene);

			Canvas canvas = _uiInstance.GetComponent<Canvas>();
			if (canvas != null)
			{
				canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				canvas.sortingOrder = 1000;
			}

			_uiInstance.transform.localScale = Vector3.one;
			BindActionSlots(_uiInstance.transform);
			_bound = true;
			Refresh();
		}

		void BindActionSlots(Transform root)
		{
			for (int i = 0; i < SlotBindings.Length; i++)
			{
				ActionSlotBinding binding = SlotBindings[i];
				Transform slot = FindDeepChild(root, binding.Name);
				if (slot == null)
				{
					Debug.LogWarning($"Missing battle action slot: {binding.Name}");
					continue;
				}

				Image image = slot.GetComponent<Image>();
				Button button = slot.GetComponent<Button>();
				if (button == null)
					button = slot.gameObject.AddComponent<Button>();

				if (image != null)
					button.targetGraphic = image;

				int slotIndex = i;
				button.onClick.RemoveAllListeners();
				button.onClick.AddListener(() => OnActionSlotClicked(slotIndex));

				_actionButtons[i] = button;
				_actionImages[i] = image;
				_normalColors[i] = image != null ? image.color : Color.white;
			}
		}

		void OnActionSlotClicked(int slotIndex)
		{
			if (_objectManager == null || slotIndex < 0 || slotIndex >= SlotBindings.Length)
				return;

			ActionSlotBinding binding = SlotBindings[slotIndex];
			if (binding.IsWaitCommand)
			{
				_objectManager.DebugEndTurn();
				Refresh();
				return;
			}

			_objectManager.SetActionMode(binding.Mode);
			Refresh();
		}

		void Refresh()
		{
			if (_objectManager == null || _bound == false)
				return;

			bool isWaiting = _objectManager.ActionMode == BattleActionMode.WaitingServer;
			bool canAct = isWaiting == false && (_objectManager.IsCurrentTurnLocal || _objectManager.BattleId == 0);

			for (int i = 0; i < SlotBindings.Length; i++)
			{
				Button button = _actionButtons[i];
				if (button != null)
					button.interactable = canAct;

				Image image = _actionImages[i];
				if (image == null)
					continue;

				bool selected = SlotBindings[i].IsWaitCommand == false && SlotBindings[i].Mode == _objectManager.ActionMode;
				Color color = selected ? new Color(1f, 0.88f, 0.35f, 1f) : _normalColors[i];
				if (canAct == false)
					color.a = 0.45f;

				image.color = color;
			}
		}

		static GameObject FindExistingBattleUi()
		{
			Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			foreach (Canvas canvas in canvases)
			{
				if (canvas != null && canvas.gameObject.name == BattleUiName)
					return canvas.gameObject;
			}

			return null;
		}

		static Transform FindDeepChild(Transform parent, string childName)
		{
			if (parent == null)
				return null;

			if (parent.name == childName)
				return parent;

			for (int i = 0; i < parent.childCount; i++)
			{
				Transform result = FindDeepChild(parent.GetChild(i), childName);
				if (result != null)
					return result;
			}

			return null;
		}

		static GameObject InstantiateFromEditorAsset()
		{
#if UNITY_EDITOR
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BattleUiEditorPath);
			return prefab != null ? Instantiate(prefab) : null;
#else
			return null;
#endif
		}

		static void EnsureEventSystem()
		{
			EventSystem existing = FindFirstObjectByType<EventSystem>();
			if (existing != null)
			{
#if ENABLE_INPUT_SYSTEM
				StandaloneInputModule standalone = existing.GetComponent<StandaloneInputModule>();
				if (standalone != null)
					Destroy(standalone);

				if (existing.GetComponent<InputSystemUIInputModule>() == null)
					existing.gameObject.AddComponent<InputSystemUIInputModule>();
#endif
				return;
			}

			GameObject eventSystem = new GameObject("EventSystem");
			eventSystem.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
			eventSystem.AddComponent<InputSystemUIInputModule>();
#else
			eventSystem.AddComponent<StandaloneInputModule>();
#endif
		}

		readonly struct ActionSlotBinding
		{
			public readonly string Name;
			public readonly BattleActionMode Mode;
			public readonly bool IsWaitCommand;

			public ActionSlotBinding(string name, BattleActionMode mode, bool isWaitCommand)
			{
				Name = name;
				Mode = mode;
				IsWaitCommand = isWaitCommand;
			}
		}
	}
}
