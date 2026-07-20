using System.Collections;
using System.Collections.Generic;
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
			new ActionSlotBinding("ActionSlot_01", BattleActionMode.Passive, 1, false),
			new ActionSlotBinding("ActionSlot_02", BattleActionMode.Skill1, 2, false),
			new ActionSlotBinding("ActionSlot_03", BattleActionMode.Skill2, 3, false),
			new ActionSlotBinding("ActionSlot_04", BattleActionMode.Skill3, 4, false),
			new ActionSlotBinding("ActionSlot_05", BattleActionMode.Skill4, 5, false),
			new ActionSlotBinding("ActionSlot_06", BattleActionMode.Ultimate, 6, false),
			new ActionSlotBinding("ActionSlot_07", BattleActionMode.SubAction, 7, false),
			new ActionSlotBinding("ActionSlot_08", BattleActionMode.Move, 8, false),
		};

		readonly Button[] _actionButtons = new Button[SlotBindings.Length];
		readonly Image[] _actionImages = new Image[SlotBindings.Length];
		readonly Image[] _actionIconImages = new Image[SlotBindings.Length];
		readonly Text[] _actionTexts = new Text[SlotBindings.Length];
		readonly Sprite[] _defaultActionSprites = new Sprite[SlotBindings.Length];
		readonly bool[] _defaultActionPreserveAspects = new bool[SlotBindings.Length];
		readonly string[] _actionIconKeys = new string[SlotBindings.Length];
		readonly string[] _actionTooltips = new string[SlotBindings.Length];
		readonly Color[] _normalColors = new Color[SlotBindings.Length];

		BattleObjectManager _objectManager;
		BattleGameDataRepository _gameData;
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
		bool _isLoadingGameData;
		bool _isLoadingResultOverlay;
		bool _bound;
		bool _battleResultReceived;
		bool _battleResultAckSent;
		int _hoveredActionSlotIndex = -1;
		int _selectedActionSlotIndex = -1;

		public async void Initialize(BattleObjectManager objectManager)
		{
			await InitializeAsync(objectManager);
		}

		public async Task InitializeAsync(BattleObjectManager objectManager)
		{
			BindObjectManager(objectManager);
			EnsureEventSystem();
			SubscribeNetwork();
			await BindOrLoadUiAsync();
			await LoadGameDataAsync();
		}

		void Update()
		{
			Refresh();
		}

		void OnDestroy()
		{
			UnbindObjectManager();
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

		void BindObjectManager(BattleObjectManager objectManager)
		{
			if (_objectManager == objectManager)
				return;

			UnbindObjectManager();
			_objectManager = objectManager;
			if (_objectManager != null)
				_objectManager.BattleActionLogApplied += OnBattleActionLogApplied;
		}

		void UnbindObjectManager()
		{
			if (_objectManager != null)
				_objectManager.BattleActionLogApplied -= OnBattleActionLogApplied;

			_objectManager = null;
		}

		async Task BindOrLoadUiAsync()
		{
			if (_isBinding || _bound)
				return;

			_isBinding = true;

			GameObject existing = FindExistingBattleUi(gameObject.scene);
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
			GameRoot.ApplyUiFont(_uiInstance);

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
				// The authored slot image is the icon itself.  Class-specific skill
				// sprites replace its dummy sprite instead of being layered on top.
				_actionIconImages[i] = image;
				_defaultActionSprites[i] = image != null ? image.sprite : null;
				_defaultActionPreserveAspects[i] = image != null && image.preserveAspect;
				HideLegacyActionIcon(slot);
				HideActionSlotLabel(slot);
				_actionTexts[i] = null;
				_normalColors[i] = image != null ? image.color : Color.white;
				BindActionSlotTooltip(slot.gameObject, i);
			}
		}

		async Task LoadGameDataAsync()
		{
			if (_isLoadingGameData)
				return;

			_isLoadingGameData = true;
			_gameData = await BattleGameDataRepository.LoadAsync();
			_isLoadingGameData = false;
			Refresh();
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
			_selectedActionSlotIndex = slotIndex;
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
			TryGetCurrentTurnPawn(out BattlePawn currentTurnPawn);
			RefreshActionSlotData(currentTurnPawn);

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

				Image iconImage = _actionIconImages[i];
				if (iconImage != null && iconImage != image)
				{
					Color iconColor = Color.white;
					if (isPassive == false && (canAct == false || isAvailable == false))
						iconColor.a = 0.45f;

					iconImage.color = iconColor;
				}
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

		void OnBattleActionLogApplied(BattleActionLog log)
		{
			if (log == null || _uiInstance == null || _objectManager == null)
				return;

			// A tile-targeted result has no defender Pawn. Do not turn it into a damage
			// number on the caster; Pawn presentation is only for actual defender IDs.
			if (log.DefenderPawnId == 0
				|| _objectManager.Pawns.TryGetValue(log.DefenderPawnId, out BattlePawn targetPawn) == false
				|| targetPawn == null)
			{
				return;
			}

			if (targetPawn == null)
				return;

			bool showMiss = log.IsEvaded;
			bool showDamage = log.Damage != 0;
			if (showMiss == false && showDamage == false)
				return;

			Camera camera = Camera.main;
			if (camera == null)
				return;

			string value = showMiss
				? "MISS"
				: log.Damage > 0 ? $"-{log.Damage}" : $"+{-log.Damage}";
			if (log.IsCritical)
				value += "\nCRIT";
			else if (log.IsPerfectGuarded)
				value += "\nPERFECT";
			else if (log.IsGuarded)
				value += "\nGUARD";

			Color color = showMiss
				? new Color(0.82f, 0.86f, 0.92f, 1f)
				: log.IsCritical ? new Color(1f, 0.83f, 0.2f, 1f)
				: log.IsGuarded || log.IsPerfectGuarded ? new Color(0.42f, 0.74f, 1f, 1f)
				: new Color(1f, 0.34f, 0.3f, 1f);

			GameObject textObject = new GameObject("BattleDamageNumber");
			textObject.layer = _uiInstance.layer;
			textObject.transform.SetParent(_uiInstance.transform, false);
			RectTransform rect = textObject.AddComponent<RectTransform>();
			rect.sizeDelta = new Vector2(92f, 38f);
			rect.position = camera.WorldToScreenPoint(targetPawn.transform.position + Vector3.up * 0.75f);

			Text text = textObject.AddComponent<Text>();
			text.font = GameRoot.UiFont;
			text.fontSize = log.IsCritical ? 20 : 17;
			text.fontStyle = FontStyle.Bold;
			text.color = color;
			text.alignment = TextAnchor.MiddleCenter;
			text.horizontalOverflow = HorizontalWrapMode.Overflow;
			text.verticalOverflow = VerticalWrapMode.Overflow;
			text.raycastTarget = false;

			StartCoroutine(AnimateDamageNumber(rect, text, color));
		}

		IEnumerator AnimateDamageNumber(RectTransform rect, Text text, Color color)
		{
			const float duration = 0.75f;
			Vector3 start = rect.position;
			float elapsed = 0f;
			while (elapsed < duration)
			{
				elapsed += Time.deltaTime;
				float ratio = Mathf.Clamp01(elapsed / duration);
				rect.position = start + Vector3.up * (42f * ratio);
				rect.localScale = Vector3.one * Mathf.Lerp(1.1f, 0.8f, ratio);
				text.color = new Color(color.r, color.g, color.b, 1f - ratio);
				yield return null;
			}

			if (rect != null)
				Destroy(rect.gameObject);
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
				GameRoot.ApplyUiFont(handle.Result);
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
			BattlePawn selectedPawn = TryGetCurrentTurnPawn(out BattlePawn currentTurnPawn) ? currentTurnPawn : null;
			BattlePawn hoveredPawn = null;

			if (_tileInfoText != null)
			{
				string actionTooltip = BuildActiveActionTooltip();
				_tileInfoText.text = string.IsNullOrWhiteSpace(actionTooltip)
					? BuildTileInfoText(hasHoveredTile, hoveredAxial)
					: actionTooltip;
			}

			if (_selectedPawnPanel != null)
				_selectedPawnPanel.SetPawn("Selected", selectedPawn);

			if (_enemyPawnPanel != null)
			{
				BattlePawn enemyPawn = null;
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
			if (TryGetCurrentTurnPawn(out BattlePawn pawn))
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
			if (_objectManager.TryGetPawnAtAxial(axial, out ulong pawnId, out BattlePawn pawnController))
			{
				string side = pawnController.IsMine ? "Mine" : "Enemy";
				pawn = $"{pawnId} ({side})";
			}

			return $"Tile\nAxial: {axial}\nState: {state}\nPawn: {pawn}";
		}

		string BuildActiveActionTooltip()
		{
			int slotIndex = _hoveredActionSlotIndex >= 0 ? _hoveredActionSlotIndex : _selectedActionSlotIndex;
			if (slotIndex < 0 || slotIndex >= _actionTooltips.Length)
				return string.Empty;

			return _actionTooltips[slotIndex];
		}

		void RefreshActionSlotData(BattlePawn currentTurnPawn)
		{
			Protocol.PawnClass pawnClass = currentTurnPawn != null && currentTurnPawn.Info != null
				? currentTurnPawn.Info.PawnClass
				: Protocol.PawnClass.None;

			for (int i = 0; i < SlotBindings.Length; i++)
			{
				ActionSlotBinding binding = SlotBindings[i];
				string label = BuildDefaultActionLabel(binding.Mode);
				string tooltip = label;
				string iconKey = string.Empty;

				if (binding.Mode == BattleActionMode.Move)
				{
					label = "Move";
					tooltip = "Move\nMove to a reachable tile.";
				}
				else if (_gameData != null
					&& pawnClass != Protocol.PawnClass.None
					&& _gameData.TryGetSkill(pawnClass, binding.ActionSlot, out BattleSkillDefinition skill))
				{
					label = BuildSkillDisplayName(skill);
					tooltip = BuildSkillTooltip(skill);

					if (_gameData.TryGetSkillView(skill.SkillKey, out BattleSkillViewDefinition view))
						iconKey = view.IconKey;
				}

				_actionTooltips[i] = tooltip;
				SetActionText(i, label);
				SetActionIcon(i, iconKey);
			}
		}

		string BuildSkillDisplayName(BattleSkillDefinition skill)
		{
			if (_gameData != null
				&& _gameData.TryGetDisplayText("SKILL", skill.SkillKey, out BattleDisplayTextSet textSet)
				&& textSet.TryGet("NAME", out BattleLocalizedText name)
				&& string.IsNullOrWhiteSpace(name.KoKr) == false)
			{
				return name.KoKr;
			}

			return HumanizeKey(skill.SkillKey);
		}

		string BuildSkillTooltip(BattleSkillDefinition skill)
		{
			string name = BuildSkillDisplayName(skill);
			string shortText = string.Empty;
			string descText = string.Empty;
			if (_gameData != null
				&& _gameData.TryGetDisplayText("SKILL", skill.SkillKey, out BattleDisplayTextSet textSet))
			{
				if (textSet.TryGet("SHORT", out BattleLocalizedText shortLocalized))
					shortText = shortLocalized.KoKr;

				if (textSet.TryGet("DESC", out BattleLocalizedText descLocalized))
					descText = descLocalized.KoKr;
			}

			string range = skill.RangeMin == skill.RangeMax
				? skill.RangeMax.ToString()
				: $"{skill.RangeMin}-{skill.RangeMax}";
			string tooltip = $"{name}\nSlot: {skill.ActionSlot} / AP: {skill.ApCost} / Range: {range}\nTarget: {skill.TargetType}";
			if (string.IsNullOrWhiteSpace(skill.TargetShape) == false)
				tooltip += $" / Shape: {skill.TargetShape}";
			if (string.IsNullOrWhiteSpace(skill.RequiredOverlayType) == false)
				tooltip += $" / Requires: {skill.RequiredOverlayType}";
			if (string.IsNullOrWhiteSpace(shortText) == false)
				tooltip += $"\n{shortText}";

			if (string.IsNullOrWhiteSpace(descText) == false)
				tooltip += $"\n\n{descText}";

			return tooltip;
		}

		void SetActionText(int slotIndex, string text)
		{
			if (slotIndex < 0 || slotIndex >= _actionTexts.Length)
				return;

			Text label = _actionTexts[slotIndex];
			if (label != null)
				label.text = text;
		}

		void SetActionIcon(int slotIndex, string iconKey)
		{
			if (slotIndex < 0 || slotIndex >= _actionIconImages.Length)
				return;

			Image iconImage = _actionIconImages[slotIndex];
			if (iconImage == null)
				return;

			if (string.Equals(_actionIconKeys[slotIndex], iconKey, System.StringComparison.OrdinalIgnoreCase))
				return;

			_actionIconKeys[slotIndex] = iconKey;
			if (string.IsNullOrWhiteSpace(iconKey))
			{
				iconImage.sprite = _defaultActionSprites[slotIndex];
				iconImage.preserveAspect = _defaultActionPreserveAspects[slotIndex];
				iconImage.enabled = iconImage.sprite != null;
				return;
			}

			LoadActionIconAsync(slotIndex, iconKey);
		}

		async void LoadActionIconAsync(int slotIndex, string iconKey)
		{
			Sprite sprite = await BattleSkillIconCache.LoadAsync(iconKey);
			if (this == null || slotIndex < 0 || slotIndex >= _actionIconImages.Length)
				return;

			if (string.Equals(_actionIconKeys[slotIndex], iconKey, System.StringComparison.OrdinalIgnoreCase) == false)
				return;

			Image iconImage = _actionIconImages[slotIndex];
			if (iconImage == null)
				return;

			iconImage.sprite = sprite;
			iconImage.enabled = sprite != null;
			iconImage.preserveAspect = true;
		}

		static void HideLegacyActionIcon(Transform slot)
		{
			Transform legacyIcon = slot != null ? slot.Find("Icon") : null;
			if (legacyIcon != null)
				legacyIcon.gameObject.SetActive(false);
		}

		static void HideActionSlotLabel(Transform slot)
		{
			Transform label = slot != null ? slot.Find("Label") : null;
			if (label != null)
				label.gameObject.SetActive(false);
		}

		void BindActionSlotTooltip(GameObject slotObject, int slotIndex)
		{
			if (slotObject == null)
				return;

			EventTrigger trigger = slotObject.GetComponent<EventTrigger>();
			if (trigger == null)
				trigger = slotObject.AddComponent<EventTrigger>();

			if (trigger.triggers == null)
				trigger.triggers = new System.Collections.Generic.List<EventTrigger.Entry>();

			EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
			enter.callback.AddListener(_ =>
			{
				_hoveredActionSlotIndex = slotIndex;
				RefreshStateTexts();
			});
			trigger.triggers.Add(enter);

			EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
			exit.callback.AddListener(_ =>
			{
				if (_hoveredActionSlotIndex == slotIndex)
					_hoveredActionSlotIndex = -1;

				RefreshStateTexts();
			});
			trigger.triggers.Add(exit);
		}

		static string BuildDefaultActionLabel(BattleActionMode mode)
		{
			switch (mode)
			{
				case BattleActionMode.Passive:
					return "Passive";
				case BattleActionMode.Move:
					return "Move";
				case BattleActionMode.Skill1:
					return "Skill 1";
				case BattleActionMode.Skill2:
					return "Skill 2";
				case BattleActionMode.Skill3:
					return "Skill 3";
				case BattleActionMode.Skill4:
					return "Skill 4";
				case BattleActionMode.Ultimate:
					return "Ultimate";
				case BattleActionMode.SubAction:
					return "SubAction";
				default:
					return mode.ToString();
			}
		}

		static string HumanizeKey(string key)
		{
			if (string.IsNullOrWhiteSpace(key))
				return string.Empty;

			string[] parts = key.Split('_');
			for (int i = 0; i < parts.Length; i++)
			{
				if (parts[i].Length == 0)
					continue;

				string lower = parts[i].ToLowerInvariant();
				parts[i] = char.ToUpperInvariant(lower[0]) + lower.Substring(1);
			}

			return string.Join(" ", parts);
		}

		static string BuildPawnText(string title, BattlePawn pawn)
		{
			if (pawn == null)
				return $"{title}\nPawn: -\nAxial: -\nHP: -\nArmor: -\nAP: -";

			string side = pawn.IsMine ? "Mine" : "Enemy";
			string hp = pawn.MaxHp > 0 ? $"{pawn.Hp}/{pawn.MaxHp}" : pawn.Hp.ToString();
			string armor = pawn.MaxArmor > 0 ? $"{pawn.Armor}/{pawn.MaxArmor}" : pawn.Armor.ToString();
			string shield = pawn.ShieldMax > 0 ? $"{pawn.ShieldCurrent}/{pawn.ShieldMax}" : pawn.ShieldCurrent.ToString();
			string resource = TryGetPrimaryResource(pawn, out string resourceName, out BattlePawn.ResourceState resourceState, out _)
				? $"{resourceName} {FormatValue(resourceState.Value, resourceState.MaxValue)}"
				: "-";
			string pawnClass = pawn.Info != null ? pawn.Info.PawnClass.ToString() : "Debug";
			string flags = $"{(pawn.CanMove ? "Move" : "NoMove")}, {(pawn.UsedSubActionThisTurn ? "SubUsed" : "SubReady")}, {(pawn.UsedUltimate ? "UltUsed" : "UltReady")}";
			return $"{title}\nPawn: {pawn.PawnId}\nSide: {side}\nClass: {pawnClass}\nRole: {pawn.Role}\nAxial: {pawn.Axial}\nFacing: {pawn.FacingDirection}\nHP: {hp}\nShield: {shield}\nArmor: {armor}\nResource: {resource}\nStatus: {FormatStatuses(pawn.Statuses)}\nAP: {pawn.CurrentAp}\nState: {flags}";
		}

		static bool TryGetPrimaryResource(BattlePawn pawn, out string name, out BattlePawn.ResourceState state, out Color color)
		{
			name = string.Empty;
			state = default;
			color = Color.white;
			if (pawn == null)
				return false;

			if (pawn.Resources.TryGetValue(Protocol.BattleResourceType.Heat, out state))
			{
				name = "HEAT";
				color = new Color(1f, 0.39f, 0.16f, 1f);
				return true;
			}

			if (pawn.Resources.TryGetValue(Protocol.BattleResourceType.Cold, out state))
			{
				name = "COLD";
				color = new Color(0.34f, 0.88f, 1f, 1f);
				return true;
			}

			return false;
		}

		static string FormatStatuses(IReadOnlyDictionary<string, BattlePawn.StatusState> statuses)
		{
			if (statuses == null || statuses.Count == 0)
				return "-";

			List<string> keys = new List<string>(statuses.Keys);
			keys.Sort(System.StringComparer.Ordinal);
			List<string> values = new List<string>(keys.Count);
			for (int i = 0; i < keys.Count; i++)
			{
				BattlePawn.StatusState status = statuses[keys[i]];
				values.Add($"{status.StatusKey} x{status.Stacks} T{status.RemainingOwnerTurns}");
			}

			return string.Join(", ", values);
		}

		bool IsActionAvailable(ActionSlotBinding binding, BattlePawn pawn)
		{
			if (binding.IsWaitCommand || pawn == null)
				return true;

			switch (binding.Mode)
			{
				case BattleActionMode.Move:
					return pawn.CanMove;
				case BattleActionMode.SubAction:
					return pawn.UsedSubActionThisTurn == false && HasEnoughAp(binding, pawn);
				case BattleActionMode.Ultimate:
					return pawn.UsedUltimate == false && HasEnoughAp(binding, pawn);
				case BattleActionMode.Passive:
					return false;
				case BattleActionMode.Skill1:
				case BattleActionMode.Skill2:
				case BattleActionMode.Skill3:
				case BattleActionMode.Skill4:
					return HasEnoughAp(binding, pawn);
				default:
					return true;
			}
		}

		bool HasEnoughAp(ActionSlotBinding binding, BattlePawn pawn)
		{
			if (pawn == null)
				return false;

			if (_gameData != null
				&& pawn.Info != null
				&& _gameData.TryGetSkill(pawn.Info.PawnClass, binding.ActionSlot, out BattleSkillDefinition skill))
			{
				return pawn.CurrentAp >= skill.ApCost;
			}

			return pawn.CurrentAp > 0;
		}

		bool TryGetCurrentTurnPawn(out BattlePawn pawn)
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
			text.font = GameRoot.UiFont;
			text.fontSize = fontSize;
			text.color = Color.white;
			text.alignment = TextAnchor.UpperLeft;
			text.horizontalOverflow = HorizontalWrapMode.Wrap;
			text.verticalOverflow = VerticalWrapMode.Truncate;
			text.raycastTarget = false;
			return text;
		}

		static Image FindOrCreateActionIcon(Transform slot)
		{
			if (slot == null)
				return null;

			Transform existing = slot.Find("Icon");
			RectTransform rect;
			Image image;
			if (existing != null)
			{
				rect = existing.GetComponent<RectTransform>();
				if (rect == null)
					rect = existing.gameObject.AddComponent<RectTransform>();

				image = existing.GetComponent<Image>();
				if (image == null)
					image = existing.gameObject.AddComponent<Image>();
			}
			else
			{
				GameObject iconObject = new GameObject("Icon");
				iconObject.layer = slot.gameObject.layer;
				iconObject.transform.SetParent(slot, false);
				rect = iconObject.AddComponent<RectTransform>();
				image = iconObject.AddComponent<Image>();
			}

			rect.anchorMin = new Vector2(0.14f, 0.34f);
			rect.anchorMax = new Vector2(0.86f, 0.92f);
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			rect.localScale = Vector3.one;

			image.raycastTarget = false;
			image.preserveAspect = true;
			image.enabled = image.sprite != null;
			return image;
		}

		static Text FindOrCreateActionLabel(Transform slot)
		{
			if (slot == null)
				return null;

			Transform existing = slot.Find("Label");
			Text text;
			RectTransform rect;
			if (existing != null)
			{
				text = existing.GetComponent<Text>();
				if (text == null)
					text = existing.gameObject.AddComponent<Text>();

				rect = existing.GetComponent<RectTransform>();
				if (rect == null)
					rect = existing.gameObject.AddComponent<RectTransform>();
			}
			else
			{
				GameObject labelObject = new GameObject("Label");
				labelObject.layer = slot.gameObject.layer;
				labelObject.transform.SetParent(slot, false);
				rect = labelObject.AddComponent<RectTransform>();
				text = labelObject.AddComponent<Text>();
			}

			rect.anchorMin = new Vector2(0.04f, 0.02f);
			rect.anchorMax = new Vector2(0.96f, 0.34f);
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			rect.localScale = Vector3.one;

			text.font = GameRoot.UiFont;
			text.fontSize = 9;
			text.color = Color.white;
			text.alignment = TextAnchor.MiddleCenter;
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

			Text text = CreateOrGetPanelText(panel, textName, fontSize, new Vector2(8f, 122f), new Vector2(-8f, -6f));
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
			barsRect.offsetMax = new Vector2(-8f, 116f);

			PanelBarView hpBar = CreateOrGetPanelBar(barsRect, "HpBar", 88f, new Color(0.82f, 0.18f, 0.16f, 1f), true);
			PanelBarView shieldBar = CreateOrGetPanelBar(barsRect, "ArmorBar", 68f, new Color(0.35f, 0.68f, 1f, 1f), true);
			PanelBarView resourceBar = CreateOrGetPanelBar(barsRect, "ResourceBar", 48f, new Color(0.34f, 0.88f, 1f, 1f), false);
			StatusIconStripView statusIcons = CreateOrGetStatusIconStrip(barsRect);
			return new PawnPanelView(text, hpBar, shieldBar, resourceBar, statusIcons);
		}

		static StatusIconStripView CreateOrGetStatusIconStrip(RectTransform parent)
		{
			Transform existing = parent.Find("StatusIcons");
			RectTransform rect;
			if (existing != null)
			{
				rect = existing.GetComponent<RectTransform>();
				if (rect == null)
					rect = existing.gameObject.AddComponent<RectTransform>();
			}
			else
			{
				GameObject iconsObject = new GameObject("StatusIcons");
				iconsObject.layer = parent.gameObject.layer;
				iconsObject.transform.SetParent(parent, false);
				rect = iconsObject.AddComponent<RectTransform>();
			}

			rect.anchorMin = new Vector2(0f, 0f);
			rect.anchorMax = new Vector2(1f, 0f);
			rect.pivot = new Vector2(0.5f, 0f);
			rect.offsetMin = new Vector2(0f, 2f);
			rect.offsetMax = new Vector2(0f, 24f);
			return new StatusIconStripView(rect);
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
			return new PanelBarView(trackRect.gameObject, fillImage, valueText);
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

			text.font = GameRoot.UiFont;
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

		static GameObject FindExistingBattleUi(Scene targetScene)
		{
			Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			foreach (Canvas canvas in canvases)
			{
				if (canvas != null
					&& canvas.gameObject.scene == targetScene
					&& canvas.gameObject.name == BattleUiName)
				{
					return canvas.gameObject;
				}
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
			readonly PanelBarView _shieldBar;
			readonly PanelBarView _resourceBar;
			readonly StatusIconStripView _statusIcons;

			public PawnPanelView(Text text, PanelBarView hpBar, PanelBarView shieldBar, PanelBarView resourceBar, StatusIconStripView statusIcons)
			{
				_text = text;
				_hpBar = hpBar;
				_shieldBar = shieldBar;
				_resourceBar = resourceBar;
				_statusIcons = statusIcons;
			}

			public void SetPawn(string title, BattlePawn pawn)
			{
				if (_text != null)
					_text.text = BuildPawnText(title, pawn);

				if (pawn == null)
				{
					_hpBar.Set(0f, "HP -");
					_shieldBar.SetVisible(false);
					_resourceBar.SetVisible(false);
					_statusIcons.SetStatuses(null);
					return;
				}

				_hpBar.Set(GetRatio(pawn.Hp, pawn.MaxHp), $"HP {FormatValue(pawn.Hp, pawn.MaxHp)}");
				bool hasShield = pawn.ShieldCurrent > 0 || pawn.ShieldMax > 0;
				_shieldBar.SetVisible(hasShield);
				if (hasShield)
					_shieldBar.Set(GetRatio(pawn.ShieldCurrent, pawn.ShieldMax), $"Shield {FormatValue(pawn.ShieldCurrent, pawn.ShieldMax)}");

				bool hasResource = TryGetPrimaryResource(pawn, out string resourceName, out BattlePawn.ResourceState resource, out Color resourceColor);
				_resourceBar.SetVisible(hasResource);
				if (hasResource)
					_resourceBar.Set(GetRatio(resource.Value, resource.MaxValue), $"{resourceName} {FormatValue(resource.Value, resource.MaxValue)}", resourceColor);

				_statusIcons.SetStatuses(pawn.Statuses);
			}
		}

		sealed class PanelBarView
		{
			readonly GameObject _root;
			readonly Image _fill;
			readonly Text _valueText;

			public PanelBarView(GameObject root, Image fill, Text valueText)
			{
				_root = root;
				_fill = fill;
				_valueText = valueText;
			}

			public void Set(float ratio, string text)
			{
				SetVisible(true);
				SetFill(_fill, ratio);
				if (_valueText != null)
					_valueText.text = text;
			}

			public void Set(float ratio, string text, Color color)
			{
				Set(ratio, text);
				if (_fill != null)
					_fill.color = color;
			}

			public void SetVisible(bool visible)
			{
				if (_root != null && _root.activeSelf != visible)
					_root.SetActive(visible);
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
			public readonly int ActionSlot;
			public readonly bool IsWaitCommand;

			public ActionSlotBinding(string name, BattleActionMode mode, int actionSlot, bool isWaitCommand)
			{
				Name = name;
				Mode = mode;
				ActionSlot = actionSlot;
				IsWaitCommand = isWaitCommand;
			}
		}

		sealed class StatusIconStripView
		{
			readonly RectTransform _root;
			readonly List<StatusIconView> _icons = new List<StatusIconView>();

			public StatusIconStripView(RectTransform root)
			{
				_root = root;
			}

			public void SetStatuses(IReadOnlyDictionary<string, BattlePawn.StatusState> statuses)
			{
				int count = statuses != null ? statuses.Count : 0;
				if (_root == null)
					return;

				_root.gameObject.SetActive(count > 0);
				if (count == 0)
				{
					for (int i = 0; i < _icons.Count; i++)
						_icons[i].SetVisible(false);

					return;
				}

				List<string> keys = new List<string>(statuses.Keys);
				keys.Sort(System.StringComparer.Ordinal);
				while (_icons.Count < keys.Count)
					_icons.Add(CreateIcon(_root, _icons.Count));

				for (int i = 0; i < _icons.Count; i++)
				{
					bool visible = i < keys.Count;
					_icons[i].SetVisible(visible);
					if (visible == false)
						continue;

					_icons[i].Set(statuses[keys[i]], i, keys.Count);
				}
			}

			static StatusIconView CreateIcon(RectTransform parent, int index)
			{
				GameObject iconObject = new GameObject($"StatusIcon_{index}");
				iconObject.layer = parent.gameObject.layer;
				iconObject.transform.SetParent(parent, false);
				RectTransform rect = iconObject.AddComponent<RectTransform>();
				Image background = iconObject.AddComponent<Image>();
				background.raycastTarget = false;

				GameObject labelObject = new GameObject("Label");
				labelObject.layer = iconObject.layer;
				labelObject.transform.SetParent(iconObject.transform, false);
				RectTransform labelRect = labelObject.AddComponent<RectTransform>();
				labelRect.anchorMin = Vector2.zero;
				labelRect.anchorMax = Vector2.one;
				labelRect.offsetMin = Vector2.zero;
				labelRect.offsetMax = Vector2.zero;
				Text label = labelObject.AddComponent<Text>();
				label.font = GameRoot.UiFont;
				label.fontSize = 7;
				label.color = Color.white;
				label.alignment = TextAnchor.MiddleCenter;
				label.horizontalOverflow = HorizontalWrapMode.Overflow;
				label.verticalOverflow = VerticalWrapMode.Overflow;
				label.raycastTarget = false;
				return new StatusIconView(iconObject, rect, background, label);
			}
		}

		sealed class StatusIconView
		{
			readonly GameObject _root;
			readonly RectTransform _rect;
			readonly Image _background;
			readonly Text _label;

			public StatusIconView(GameObject root, RectTransform rect, Image background, Text label)
			{
				_root = root;
				_rect = rect;
				_background = background;
				_label = label;
			}

			public void SetVisible(bool visible)
			{
				if (_root != null && _root.activeSelf != visible)
					_root.SetActive(visible);
			}

			public void Set(BattlePawn.StatusState status, int index, int count)
			{
				float width = 1f / Mathf.Max(1, count);
				_rect.anchorMin = new Vector2(index * width, 0f);
				_rect.anchorMax = new Vector2((index + 1) * width, 1f);
				_rect.offsetMin = new Vector2(1f, 0f);
				_rect.offsetMax = new Vector2(-1f, 0f);
				_root.name = $"StatusIcon_{status.StatusKey}";
				_background.color = GetStatusColor(status.StatusKey);
				_label.text = $"{GetStatusAbbreviation(status.StatusKey)}\nx{status.Stacks} T{status.RemainingOwnerTurns}";
			}

			static string GetStatusAbbreviation(string statusKey)
			{
				if (string.IsNullOrWhiteSpace(statusKey))
					return "?";

				if (statusKey.IndexOf("COLD_HARD_WORKER_EMPOWERED", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return "EMP";
				if (statusKey.IndexOf("IGNORE_COLD_BACKLASH", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return "IMM";
				if (statusKey.IndexOf("THAWING_POTION_DAMAGE_DOWN", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return "DMG";

				string compact = statusKey.Replace("_", string.Empty).ToUpperInvariant();
				return compact.Length <= 3 ? compact : compact.Substring(0, 3);
			}

			static Color GetStatusColor(string statusKey)
			{
				string key = statusKey ?? string.Empty;
				if (key.IndexOf("COLD_HARD_WORKER_EMPOWERED", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return new Color(1f, 0.73f, 0.22f, 0.96f);
				if (key.IndexOf("IGNORE_COLD_BACKLASH", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return new Color(0.42f, 0.9f, 1f, 0.96f);
				if (key.IndexOf("THAWING_POTION_DAMAGE_DOWN", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return new Color(0.72f, 0.43f, 0.27f, 0.96f);
				if (key.IndexOf("COLD", System.StringComparison.OrdinalIgnoreCase) >= 0
					|| key.IndexOf("FROST", System.StringComparison.OrdinalIgnoreCase) >= 0
					|| key.IndexOf("FREEZE", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return new Color(0.2f, 0.72f, 0.94f, 0.94f);
				if (key.IndexOf("BURN", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return new Color(0.92f, 0.34f, 0.2f, 0.94f);
				if (key.IndexOf("POISON", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return new Color(0.55f, 0.35f, 0.82f, 0.94f);

				return new Color(0.33f, 0.4f, 0.52f, 0.94f);
			}
		}
	}
}
