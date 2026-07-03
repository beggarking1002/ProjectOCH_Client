using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem;
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
		const string BattleUiEditorPath = "Assets/@Resources/Prefab/UI/BattleSceneUI.prefab";

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
		Button _turnExitButton;
		Image _turnExitImage;
		Color _turnExitNormalColor = Color.white;
		Text _turnPanelText;
		Text _tileInfoText;
		Text _selectedPawnText;
		Text _enemyPawnText;
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
			BindTurnExit(_uiInstance.transform);
			BindStatePanels(_uiInstance.transform);
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

		void BindTurnExit(Transform root)
		{
			Transform turnExit = FindDeepChild(root, "TurnExit");
			if (turnExit == null)
			{
				Debug.LogWarning("Missing battle UI button: TurnExit");
				return;
			}

			_turnExitImage = turnExit.GetComponent<Image>();
			_turnExitButton = turnExit.GetComponent<Button>();
			if (_turnExitButton == null)
				_turnExitButton = turnExit.gameObject.AddComponent<Button>();

			if (_turnExitImage != null)
			{
				_turnExitButton.targetGraphic = _turnExitImage;
				_turnExitNormalColor = _turnExitImage.color;
			}

			_turnExitButton.onClick.RemoveAllListeners();
			_turnExitButton.onClick.AddListener(OnEndTurnClicked);
		}

		void BindStatePanels(Transform root)
		{
			_turnPanelText = CreateOrGetPanelText(root, "TurnPanel", "TurnPanel_StateText", 14);
			_tileInfoText = CreateOrGetPanelText(root, "TileInfo", "TileInfo_StateText", 13);
			_selectedPawnText = CreateOrGetPanelText(root, "Left_SelectedPawnPanel", "SelectedPawn_StateText", 13);
			_enemyPawnText = CreateOrGetPanelText(root, "Right_EnemyPawnPanel", "EnemyPawn_StateText", 13);
		}

		void OnActionSlotClicked(int slotIndex)
		{
			if (_objectManager == null || slotIndex < 0 || slotIndex >= SlotBindings.Length)
				return;

			ActionSlotBinding binding = SlotBindings[slotIndex];
			if (binding.IsWaitCommand)
			{
				OnEndTurnClicked();
				return;
			}

			_objectManager.SetActionMode(binding.Mode);
			Refresh();
		}

		void OnEndTurnClicked()
		{
			if (_objectManager == null)
				return;

			_objectManager.DebugEndTurn();
			Refresh();
		}

		void Refresh()
		{
			if (_objectManager == null || _bound == false)
				return;

			bool isWaiting = _objectManager.ActionMode == BattleActionMode.WaitingServer;
			bool canAct = isWaiting == false && (_objectManager.IsCurrentTurnLocal || _objectManager.BattleId == 0);

			if (_turnExitButton != null)
				_turnExitButton.interactable = canAct;

			if (_turnExitImage != null)
			{
				Color color = _turnExitNormalColor;
				if (canAct == false)
					color.a = 0.45f;

				_turnExitImage.color = color;
			}

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

			RefreshStateTexts();
		}

		void RefreshStateTexts()
		{
			if (_turnPanelText != null)
				_turnPanelText.text = BuildTurnText();

			AxialCoord hoveredAxial = default;
			bool hasHoveredTile = TryGetHoveredAxial(out hoveredAxial);

			if (_tileInfoText != null)
				_tileInfoText.text = BuildTileInfoText(hasHoveredTile, hoveredAxial);

			if (_selectedPawnText != null)
				_selectedPawnText.text = BuildPawnText("Selected", TryGetCurrentTurnPawn(out BattlePawnController selectedPawn) ? selectedPawn : null);

			if (_enemyPawnText != null)
			{
				BattlePawnController enemyPawn = null;
				if (hasHoveredTile && _objectManager.TryGetPawnAtAxial(hoveredAxial, out _, out BattlePawnController hoveredPawn) && hoveredPawn.IsMine == false)
					enemyPawn = hoveredPawn;

				_enemyPawnText.text = BuildPawnText("Target", enemyPawn);
			}
		}

		string BuildTurnText()
		{
			string ownership = _objectManager.IsCurrentTurnLocal ? "Mine" : "Enemy";
			if (_objectManager.BattleId == 0)
				ownership = "Debug";

			return $"Turn\nPawn: {_objectManager.CurrentTurnPawnId}\nSide: {ownership}\nMode: {_objectManager.ActionMode}";
		}

		string BuildTileInfoText(bool hasHoveredTile, AxialCoord axial)
		{
			if (hasHoveredTile == false)
				return "Tile\nAxial: -\nState: -\nPawn: -";

			string state = _objectManager.IsTileWalkable(axial) ? "Walkable" : "Blocked";
			string pawn = "-";
			if (_objectManager.TryGetPawnAtAxial(axial, out ulong pawnId, out BattlePawnController pawnController))
			{
				string side = pawnController.IsMine ? "Mine" : "Enemy";
				pawn = $"{pawnId} ({side})";
			}

			return $"Tile\nAxial: {axial}\nState: {state}\nPawn: {pawn}";
		}

		static string BuildPawnText(string title, BattlePawnController pawn)
		{
			if (pawn == null)
				return $"{title}\nPawn: -\nAxial: -\nHP: -";

			string side = pawn.IsMine ? "Mine" : "Enemy";
			string hp = pawn.Info != null ? pawn.Info.Hp.ToString() : "-";
			string pawnClass = pawn.Info != null ? pawn.Info.PawnClass.ToString() : "Debug";
			return $"{title}\nPawn: {pawn.PawnId}\nSide: {side}\nClass: {pawnClass}\nAxial: {pawn.Axial}\nHP: {hp}";
		}

		bool TryGetCurrentTurnPawn(out BattlePawnController pawn)
		{
			pawn = null;
			return _objectManager.CurrentTurnPawnId != 0
				&& _objectManager.TryGetPawn(_objectManager.CurrentTurnPawnId, out pawn);
		}

		bool TryGetHoveredAxial(out AxialCoord axial)
		{
			axial = default;

			if (_objectManager.MapGrid == null)
				return false;

			Camera camera = Camera.main;
			if (camera == null)
				return false;

			if (TryGetPointerPosition(out Vector2 screenPosition) == false)
				return false;

			if (IsValidScreenPosition(camera, screenPosition) == false)
				return false;

			Ray ray = camera.ScreenPointToRay(screenPosition);
			Plane mapPlane = new Plane(Vector3.forward, _objectManager.MapGrid.PlaneTransform.position);
			if (mapPlane.Raycast(ray, out float enter) == false)
				return false;

			Vector3 worldPosition = ray.GetPoint(enter);
			axial = _objectManager.MapGrid.WorldToAxial(worldPosition);
			return true;
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

		static bool IsValidScreenPosition(Camera camera, Vector2 screenPosition)
		{
			if (float.IsNaN(screenPosition.x) || float.IsNaN(screenPosition.y))
				return false;

			if (float.IsInfinity(screenPosition.x) || float.IsInfinity(screenPosition.y))
				return false;

			return screenPosition.x >= 0f
				&& screenPosition.y >= 0f
				&& screenPosition.x <= camera.pixelWidth
				&& screenPosition.y <= camera.pixelHeight;
		}

		static Text CreateOrGetPanelText(Transform root, string panelName, string textName, int fontSize)
		{
			Transform panel = FindDeepChild(root, panelName);
			if (panel == null)
			{
				Debug.LogWarning($"Missing battle UI panel: {panelName}");
				return null;
			}

			Transform existing = panel.Find(textName);
			Text text = existing != null ? existing.GetComponent<Text>() : null;
			if (text != null)
				return text;

			GameObject textObject = new GameObject(textName);
			textObject.transform.SetParent(panel, false);

			RectTransform rect = textObject.AddComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.pivot = new Vector2(0f, 1f);
			rect.offsetMin = new Vector2(8f, 6f);
			rect.offsetMax = new Vector2(-8f, -6f);

			text = textObject.AddComponent<Text>();
			text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			text.fontSize = fontSize;
			text.color = Color.white;
			text.alignment = TextAnchor.UpperLeft;
			text.horizontalOverflow = HorizontalWrapMode.Wrap;
			text.verticalOverflow = VerticalWrapMode.Truncate;
			text.raycastTarget = false;
			return text;
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
