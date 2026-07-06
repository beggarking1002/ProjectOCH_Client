using System.Collections;
using System.Threading.Tasks;
using App;
using Protocol;
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
		const string BattleResultUiAddress = "BattleResultUI";
		const float BattleResultDelaySeconds = 2f;

		static readonly ActionSlotBinding[] SlotBindings =
		{
			new ActionSlotBinding("ActionSlot_01", BattleActionMode.Move, false),
			new ActionSlotBinding("ActionSlot_02", BattleActionMode.Skill1, false),
			new ActionSlotBinding("ActionSlot_03", BattleActionMode.Skill2, false),
			new ActionSlotBinding("ActionSlot_04", BattleActionMode.Skill3, false),
			new ActionSlotBinding("ActionSlot_05", BattleActionMode.Skill4, false),
			new ActionSlotBinding("ActionSlot_06", BattleActionMode.Ultimate, false),
			new ActionSlotBinding("ActionSlot_07", BattleActionMode.SubAction, false),
			new ActionSlotBinding("ActionSlot_08", BattleActionMode.Passive, false),
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
		PawnPanelView _selectedPawnPanel;
		PawnPanelView _enemyPawnPanel;
		GameObject _uiInstance;
		GameObject _resultOverlay;
		Text _resultTitleText;
		Button _resultOkButton;
		AsyncOperationHandle<GameObject> _uiHandle;
		AsyncOperationHandle<GameObject> _resultUiHandle;
		Coroutine _resultCoroutine;
		ulong _battleResultId;
		bool _hasUiHandle;
		bool _hasResultUiHandle;
		bool _isBinding;
		bool _isLoadingResultOverlay;
		bool _bound;
		bool _battleResultReceived;
		bool _battleResultAckSent;

		public async void Initialize(BattleObjectManager objectManager)
		{
			await InitializeAsync(objectManager);
		}

		public async Task InitializeAsync(BattleObjectManager objectManager)
		{
			_objectManager = objectManager;
			EnsureEventSystem();
			SubscribeNetwork();
			await BindOrLoadUiAsync();
		}

		void Update()
		{
			Refresh();
		}

		void OnDestroy()
		{
			UnsubscribeNetwork();
			if (_resultCoroutine != null)
				StopCoroutine(_resultCoroutine);

			ReleaseResultOverlayHandle();

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

		void SubscribeNetwork()
		{
			if (GameRoot.Instance == null)
				return;

			GameRoot.Instance.Network.BattleResultReceived -= OnBattleResultReceived;
			GameRoot.Instance.Network.BattleResultReceived += OnBattleResultReceived;
		}

		void UnsubscribeNetwork()
		{
			if (GameRoot.Instance == null)
				return;

			GameRoot.Instance.Network.BattleResultReceived -= OnBattleResultReceived;
		}

		async Task BindOrLoadUiAsync()
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
			BindOrLoadResultOverlay();
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
				if (binding.Mode != BattleActionMode.Passive)
				{
					button.transition = Selectable.Transition.ColorTint;
					button.onClick.AddListener(() => OnActionSlotClicked(slotIndex));
				}
				else
				{
					button.transition = Selectable.Transition.None;
				}

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
			_selectedPawnPanel = CreateOrGetPawnPanelView(root, "Left_SelectedPawnPanel", "SelectedPawn_StateText", "SelectedPawn_StatusBars", 13);
			_enemyPawnPanel = CreateOrGetPawnPanelView(root, "Right_EnemyPawnPanel", "EnemyPawn_StateText", "EnemyPawn_StatusBars", 13);
		}

		void OnActionSlotClicked(int slotIndex)
		{
			if (_objectManager == null || slotIndex < 0 || slotIndex >= SlotBindings.Length)
				return;

			if (_battleResultReceived)
				return;

			if (_objectManager.IsInteractionLocked)
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

			if (_battleResultReceived)
				return;

			if (_objectManager.IsInteractionLocked)
				return;

			_objectManager.DebugEndTurn();
			Refresh();
		}

		void Refresh()
		{
			if (_objectManager == null || _bound == false)
				return;

			bool canAct = _battleResultReceived == false && _objectManager.IsInteractionLocked == false && (_objectManager.IsCurrentTurnLocal || _objectManager.BattleId == 0);
			TryGetCurrentTurnPawn(out BattlePawnController currentTurnPawn);

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
				bool isAvailable = IsActionAvailable(SlotBindings[i], currentTurnPawn);
				Button button = _actionButtons[i];
				if (button != null)
					button.interactable = canAct && isAvailable;

				Image image = _actionImages[i];
				if (image == null)
					continue;

				bool isPassive = SlotBindings[i].Mode == BattleActionMode.Passive;
				bool selected = isPassive == false && SlotBindings[i].IsWaitCommand == false && SlotBindings[i].Mode == _objectManager.ActionMode;
				Color color = selected ? new Color(1f, 0.88f, 0.35f, 1f) : _normalColors[i];
				if (isPassive == false && (canAct == false || isAvailable == false))
					color.a = 0.45f;

				image.color = color;
			}

			RefreshStateTexts();
		}

		void OnBattleResultReceived(S_BATTLE_RESULT packet)
		{
			if (packet == null || _objectManager == null)
				return;

			if (packet.BattleId != _objectManager.BattleId)
			{
				Debug.LogWarning($"Ignored S_BATTLE_RESULT because battleId mismatched. packetBattleId={packet.BattleId}, localBattleId={_objectManager.BattleId}, victory={packet.Victory}");
				return;
			}

			_battleResultReceived = true;
			_battleResultAckSent = false;
			_battleResultId = packet.BattleId;

			if (_resultCoroutine != null)
				StopCoroutine(_resultCoroutine);

			_resultCoroutine = StartCoroutine(ShowBattleResultAfterDelay(packet.Victory));
			Refresh();
		}

		IEnumerator ShowBattleResultAfterDelay(bool victory)
		{
			yield return new WaitForSeconds(BattleResultDelaySeconds);

			if (_resultOverlay == null)
				BindOrLoadResultOverlay();

			while (_isLoadingResultOverlay)
				yield return null;

			if (_resultOverlay == null)
			{
				Debug.LogError($"Battle result UI is not available. Register addressable prefab '{BattleResultUiAddress}'.");
				_resultCoroutine = null;
				yield break;
			}

			if (_resultTitleText != null)
				_resultTitleText.text = victory ? "You Win!" : "You Lose!";

			if (_resultOkButton != null)
				_resultOkButton.interactable = true;

			if (_resultOverlay != null)
				_resultOverlay.SetActive(true);

			_resultCoroutine = null;
		}

		void OnBattleResultOkClicked()
		{
			if (_battleResultAckSent)
				return;

			if (GameRoot.Instance == null)
			{
				Debug.LogWarning("Cannot send C_BATTLE_RESULT_ACK because GameRoot is missing.");
				return;
			}

			bool sent = GameRoot.Instance.Network.SendBattleResultAck(_battleResultId);
			if (sent == false)
			{
				Debug.LogWarning($"Failed to send C_BATTLE_RESULT_ACK. {GameRoot.Instance.Network.LastError}");
				return;
			}

			_battleResultAckSent = true;
			if (_resultOkButton != null)
				_resultOkButton.interactable = false;

			Debug.Log($"Sent C_BATTLE_RESULT_ACK. battleId={_battleResultId}");
		}

		async void BindOrLoadResultOverlay()
		{
			if (_uiInstance == null || _resultOverlay != null || _isLoadingResultOverlay)
				return;

			_isLoadingResultOverlay = true;
			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(BattleResultUiAddress);
			await handle.Task;
			_isLoadingResultOverlay = false;

			if (this == null)
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				return;
			}

			if (_resultOverlay != null)
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				return;
			}

			if (handle.Status == AsyncOperationStatus.Succeeded)
			{
				_resultUiHandle = handle;
				_hasResultUiHandle = true;
				handle.Result.transform.SetParent(_uiInstance.transform, false);
				BindResultOverlay(handle.Result.transform);
				return;
			}

			if (handle.IsValid())
				Addressables.ReleaseInstance(handle);

			Debug.LogError($"Failed to load addressable UI prefab: {BattleResultUiAddress}");
		}

		void ReleaseResultOverlayHandle()
		{
			if (_hasResultUiHandle && _resultUiHandle.IsValid())
			{
				Addressables.ReleaseInstance(_resultUiHandle);
				_hasResultUiHandle = false;
				_resultUiHandle = default;
				_resultOverlay = null;
				_resultTitleText = null;
				_resultOkButton = null;
			}
		}

		void BindResultOverlay(Transform overlay)
		{
			if (overlay == null)
				return;

			_resultOverlay = overlay.gameObject;
			_resultTitleText = FindDeepChild(overlay, "ResultTitle")?.GetComponent<Text>();
			_resultOkButton = FindDeepChild(overlay, "ResultOkButton")?.GetComponent<Button>();
			if (_resultOkButton != null)
			{
				_resultOkButton.onClick.RemoveAllListeners();
				_resultOkButton.onClick.AddListener(OnBattleResultOkClicked);
			}
			else
			{
				Debug.LogWarning("Battle result overlay requires a Button named ResultOkButton.");
			}

			if (_resultTitleText == null)
				Debug.LogWarning("Battle result overlay requires a Text named ResultTitle.");

			_resultOverlay.SetActive(false);
		}

		void RefreshStateTexts()
		{
			if (_turnPanelText != null)
				_turnPanelText.text = BuildTurnText();

			AxialCoord hoveredAxial = default;
			bool hasHoveredTile = TryGetHoveredAxial(out hoveredAxial);
			BattlePawnController selectedPawn = TryGetCurrentTurnPawn(out BattlePawnController currentTurnPawn) ? currentTurnPawn : null;
			BattlePawnController hoveredPawn = null;

			if (_tileInfoText != null)
				_tileInfoText.text = BuildTileInfoText(hasHoveredTile, hoveredAxial);

			if (_selectedPawnPanel != null)
				_selectedPawnPanel.SetPawn("Selected", selectedPawn);

			if (_enemyPawnPanel != null)
			{
				BattlePawnController enemyPawn = null;
				if (hasHoveredTile && _objectManager.TryGetPawnAtAxial(hoveredAxial, out _, out hoveredPawn) && hoveredPawn.IsMine == false)
					enemyPawn = hoveredPawn;

				_enemyPawnPanel.SetPawn("Target", enemyPawn);
			}
		}

		string BuildTurnText()
		{
			string ownership = _objectManager.IsCurrentTurnLocal ? "Mine" : "Enemy";
			if (_objectManager.BattleId == 0)
				ownership = "Debug";

			string ap = "-";
			string canMove = "-";
			if (TryGetCurrentTurnPawn(out BattlePawnController pawn))
			{
				ap = pawn.CurrentAp.ToString();
				canMove = pawn.CanMove ? "Yes" : "No";
			}

			return $"Turn\nPawn: {_objectManager.CurrentTurnPawnId}\nSide: {ownership}\nMode: {_objectManager.ActionMode}\nMoving: {(_objectManager.IsAnimatingMove ? "Yes" : "No")}\nAP: {ap}\nMove: {canMove}\nLog:\n{_objectManager.BattleLogText}";
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
				return $"{title}\nPawn: -\nAxial: -\nHP: -\nArmor: -\nAP: -";

			string side = pawn.IsMine ? "Mine" : "Enemy";
			string hp = pawn.MaxHp > 0 ? $"{pawn.Hp}/{pawn.MaxHp}" : pawn.Hp.ToString();
			string armor = pawn.MaxArmor > 0 ? $"{pawn.Armor}/{pawn.MaxArmor}" : pawn.Armor.ToString();
			string pawnClass = pawn.Info != null ? pawn.Info.PawnClass.ToString() : "Debug";
			string flags = $"{(pawn.CanMove ? "Move" : "NoMove")}, {(pawn.UsedSubActionThisTurn ? "SubUsed" : "SubReady")}, {(pawn.UsedUltimate ? "UltUsed" : "UltReady")}";
			return $"{title}\nPawn: {pawn.PawnId}\nSide: {side}\nClass: {pawnClass}\nRole: {pawn.Role}\nAxial: {pawn.Axial}\nFacing: {pawn.FacingDirection}\nHP: {hp}\nArmor: {armor}\nAP: {pawn.CurrentAp}\nState: {flags}";
		}

		static bool IsActionAvailable(ActionSlotBinding binding, BattlePawnController pawn)
		{
			if (binding.IsWaitCommand || pawn == null)
				return true;

			switch (binding.Mode)
			{
				case BattleActionMode.Move:
					return pawn.CanMove;
				case BattleActionMode.SubAction:
					return pawn.UsedSubActionThisTurn == false;
				case BattleActionMode.Ultimate:
					return pawn.UsedUltimate == false;
				case BattleActionMode.Passive:
					return false;
				case BattleActionMode.Skill1:
				case BattleActionMode.Skill2:
				case BattleActionMode.Skill3:
				case BattleActionMode.Skill4:
					return pawn.CurrentAp > 0;
				default:
					return true;
			}
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

			return CreateOrGetPanelText(panel, textName, fontSize, new Vector2(8f, 6f), new Vector2(-8f, -6f));
		}

		static Text CreateOrGetPanelText(Transform panel, string textName, int fontSize, Vector2 offsetMin, Vector2 offsetMax)
		{
			Transform existing = panel.Find(textName);
			Text text = existing != null ? existing.GetComponent<Text>() : null;
			if (text != null)
			{
				RectTransform existingRect = text.GetComponent<RectTransform>();
				if (existingRect != null)
				{
					existingRect.anchorMin = Vector2.zero;
					existingRect.anchorMax = Vector2.one;
					existingRect.offsetMin = offsetMin;
					existingRect.offsetMax = offsetMax;
				}

				return text;
			}

			GameObject textObject = new GameObject(textName);
			textObject.transform.SetParent(panel, false);

			RectTransform rect = textObject.AddComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.pivot = new Vector2(0f, 1f);
			rect.offsetMin = offsetMin;
			rect.offsetMax = offsetMax;

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

		static PawnPanelView CreateOrGetPawnPanelView(Transform root, string panelName, string textName, string barsName, int fontSize)
		{
			Transform panel = FindDeepChild(root, panelName);
			if (panel == null)
			{
				Debug.LogWarning($"Missing battle UI panel: {panelName}");
				return null;
			}

			Text text = CreateOrGetPanelText(panel, textName, fontSize, new Vector2(8f, 58f), new Vector2(-8f, -6f));
			Transform bars = panel.Find(barsName);
			RectTransform barsRect;
			if (bars != null)
			{
				barsRect = bars.GetComponent<RectTransform>();
				if (barsRect == null)
					barsRect = bars.gameObject.AddComponent<RectTransform>();
			}
			else
			{
				GameObject barsObject = new GameObject(barsName);
				barsObject.transform.SetParent(panel, false);
				barsRect = barsObject.AddComponent<RectTransform>();
			}

			barsRect.anchorMin = new Vector2(0f, 0f);
			barsRect.anchorMax = new Vector2(1f, 0f);
			barsRect.pivot = new Vector2(0.5f, 0f);
			barsRect.offsetMin = new Vector2(8f, 8f);
			barsRect.offsetMax = new Vector2(-8f, 52f);

			PanelBarView hpBar = CreateOrGetPanelBar(barsRect, "HpBar", 24f, new Color(0.82f, 0.18f, 0.16f, 1f), true);
			PanelBarView armorBar = CreateOrGetPanelBar(barsRect, "ArmorBar", 4f, new Color(0.35f, 0.68f, 1f, 1f), true);
			return new PawnPanelView(text, hpBar, armorBar);
		}

		static PanelBarView CreateOrGetPanelBar(RectTransform parent, string name, float bottom, Color fillColor, bool preserveExistingStyle)
		{
			Transform existing = parent.Find(name);
			RectTransform trackRect;
			Image trackImage;
			bool createdTrack = existing == null;
			bool createdTrackImage = false;
			if (existing != null)
			{
				trackRect = existing.GetComponent<RectTransform>();
				if (trackRect == null)
					trackRect = existing.gameObject.AddComponent<RectTransform>();

				trackImage = existing.GetComponent<Image>();
				if (trackImage == null)
				{
					trackImage = existing.gameObject.AddComponent<Image>();
					createdTrackImage = true;
				}
			}
			else
			{
				GameObject trackObject = new GameObject(name);
				trackObject.transform.SetParent(parent, false);
				trackRect = trackObject.AddComponent<RectTransform>();
				trackImage = trackObject.AddComponent<Image>();
			}

			trackRect.anchorMin = new Vector2(0f, 0f);
			trackRect.anchorMax = new Vector2(1f, 0f);
			trackRect.pivot = new Vector2(0.5f, 0f);
			trackRect.offsetMin = new Vector2(0f, bottom);
			trackRect.offsetMax = new Vector2(0f, bottom + 12f);
			if (createdTrack || createdTrackImage || preserveExistingStyle == false)
				trackImage.color = new Color(0.02f, 0.025f, 0.03f, 0.78f);

			trackImage.raycastTarget = false;

			Transform fill = existing != null ? existing.Find("Fill") : null;
			RectTransform fillRect;
			Image fillImage;
			bool createdFill = fill == null;
			bool createdFillImage = false;
			if (fill != null)
			{
				fillRect = fill.GetComponent<RectTransform>();
				if (fillRect == null)
					fillRect = fill.gameObject.AddComponent<RectTransform>();

				fillImage = fill.GetComponent<Image>();
				if (fillImage == null)
				{
					fillImage = fill.gameObject.AddComponent<Image>();
					createdFillImage = true;
				}
			}
			else
			{
				GameObject fillObject = new GameObject("Fill");
				fillObject.transform.SetParent(trackRect, false);
				fillRect = fillObject.AddComponent<RectTransform>();
				fillImage = fillObject.AddComponent<Image>();
			}

			fillRect.anchorMin = new Vector2(0f, 0f);
			fillRect.anchorMax = new Vector2(1f, 1f);
			fillRect.offsetMin = new Vector2(1f, 1f);
			fillRect.offsetMax = new Vector2(-1f, -1f);
			if (createdFill || createdFillImage || preserveExistingStyle == false)
				fillImage.color = fillColor;

			fillImage.raycastTarget = false;
			Text valueText = CreateOrGetBarText(trackRect, "ValueText");
			return new PanelBarView(fillImage, valueText);
		}

		static Text CreateOrGetBarText(RectTransform parent, string name)
		{
			Transform existing = parent.Find(name);
			Text text = existing != null ? existing.GetComponent<Text>() : null;
			RectTransform rect;
			if (text != null)
			{
				rect = text.GetComponent<RectTransform>();
				if (rect == null)
					rect = text.gameObject.AddComponent<RectTransform>();
			}
			else
			{
				GameObject textObject = existing != null ? existing.gameObject : new GameObject(name);
				textObject.transform.SetParent(parent, false);
				rect = textObject.GetComponent<RectTransform>();
				if (rect == null)
					rect = textObject.AddComponent<RectTransform>();

				text = textObject.GetComponent<Text>();
				if (text == null)
					text = textObject.AddComponent<Text>();
			}

			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;

			text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			text.fontSize = 10;
			text.color = Color.white;
			text.alignment = TextAnchor.MiddleCenter;
			text.horizontalOverflow = HorizontalWrapMode.Overflow;
			text.verticalOverflow = VerticalWrapMode.Truncate;
			text.raycastTarget = false;
			return text;
		}

		static float GetRatio(int value, int maxValue)
		{
			if (maxValue <= 0)
				return value > 0 ? 1f : 0f;

			return Mathf.Clamp01((float)value / maxValue);
		}

		static string FormatValue(int value, int maxValue)
		{
			return maxValue > 0 ? $"{value}/{maxValue}" : value.ToString();
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

		sealed class PawnPanelView
		{
			readonly Text _text;
			readonly PanelBarView _hpBar;
			readonly PanelBarView _armorBar;

			public PawnPanelView(Text text, PanelBarView hpBar, PanelBarView armorBar)
			{
				_text = text;
				_hpBar = hpBar;
				_armorBar = armorBar;
			}

			public void SetPawn(string title, BattlePawnController pawn)
			{
				if (_text != null)
					_text.text = BuildPawnText(title, pawn);

				if (pawn == null)
				{
					_hpBar.Set(0f, "HP -");
					_armorBar.Set(0f, "Armor -");
					return;
				}

				_hpBar.Set(GetRatio(pawn.Hp, pawn.MaxHp), $"HP {FormatValue(pawn.Hp, pawn.MaxHp)}");
				_armorBar.Set(GetRatio(pawn.Armor, pawn.MaxArmor), $"Armor {FormatValue(pawn.Armor, pawn.MaxArmor)}");
			}
		}

		sealed class PanelBarView
		{
			readonly Image _fill;
			readonly Text _valueText;

			public PanelBarView(Image fill, Text valueText)
			{
				_fill = fill;
				_valueText = valueText;
			}

			public void Set(float ratio, string text)
			{
				SetFill(_fill, ratio);
				if (_valueText != null)
					_valueText.text = text;
			}

			static void SetFill(Image fill, float ratio)
			{
				if (fill == null)
					return;

				RectTransform rect = fill.rectTransform;
				rect.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
			}
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
