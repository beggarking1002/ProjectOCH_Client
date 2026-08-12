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
		const string StatIconAddress = "StatIcon";
		const string StatIconEditorPath = "Assets/@Resources/Art/UI/StatIcon.png";
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

		// StatIcon.png has intentional horizontal padding and non-uniform gaps. These
		// are the exact rects stored by its Unity sprite importer, in row-major order.
		static readonly Rect[] StatIconRects =
		{
			new Rect(152f, 861f, 193f, 205f), new Rect(394f, 861f, 194f, 205f), new Rect(634f, 861f, 189f, 205f), new Rect(867f, 861f, 189f, 206f), new Rect(1104f, 861f, 190f, 205f),
			new Rect(152f, 650f, 193f, 206f), new Rect(395f, 649f, 192f, 207f), new Rect(634f, 649f, 189f, 207f), new Rect(867f, 649f, 189f, 207f), new Rect(1104f, 649f, 190f, 207f),
			new Rect(152f, 438f, 193f, 206f), new Rect(395f, 438f, 192f, 206f), new Rect(634f, 438f, 189f, 206f), new Rect(867f, 438f, 189f, 206f), new Rect(1104f, 438f, 190f, 206f),
			new Rect(152f, 227f, 193f, 207f), new Rect(395f, 227f, 193f, 207f), new Rect(634f, 227f, 189f, 207f), new Rect(867f, 226f, 188f, 208f), new Rect(1104f, 226f, 190f, 207f),
			new Rect(152f, 15f, 193f, 206f), new Rect(395f, 15f, 192f, 207f), new Rect(634f, 15f, 189f, 207f), new Rect(867f, 15f, 188f, 206f), new Rect(1104f, 15f, 190f, 206f),
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
		readonly Queue<BattleActionLog> _pendingBattleActionLogs = new Queue<BattleActionLog>();
		readonly Sprite[] _statIconSprites = new Sprite[25];

		BattleObjectManager _objectManager;
		BattleGameDataRepository _gameData;
		Button _turnExitButton;
		Image _turnExitImage;
		Color _turnExitNormalColor = Color.white;
		Text _turnPanelText;
		Text _tileInfoText;
		HoverPawnInfoView _hoverPawnInfo;
		BattleTurnQueueView _turnQueueView;
		Image _currentTurnPortraitImage;
		Image _classMarkImage;
		GameObject _uiInstance;
		GameObject _resultOverlay;
		GameObject _optionalPositionSwapPrompt;
		Text _resultTitleText;
		System.Action<bool> _optionalPositionSwapChoice;
		Button _resultOkButton;
		AsyncOperationHandle<GameObject> _uiHandle;
		AsyncOperationHandle<GameObject> _resultUiHandle;
		AsyncOperationHandle<Texture2D> _statIconTextureHandle;
		Coroutine _resultCoroutine;
		Coroutine _battleActionLogPlayback;
		ulong _battleResultId;
		bool _hasUiHandle;
		bool _hasResultUiHandle;
		bool _hasStatIconTextureHandle;
		bool _isLoadingStatIcons;
		bool _isBinding;
		bool _isLoadingGameData;
		bool _isLoadingResultOverlay;
		bool _bound;
		bool _battleResultReceived;
		bool _battleResultAckSent;
		int _hoveredActionSlotIndex = -1;
		int _selectedActionSlotIndex = -1;
		ulong _currentPortraitPawnId;
		int _currentPortraitRequestVersion;

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
			_ = LoadStatIconAtlasAsync();
			await LoadGameDataAsync();
		}

		async Task LoadStatIconAtlasAsync()
		{
			if (_isLoadingStatIcons || _hasStatIconTextureHandle)
				return;

			_isLoadingStatIcons = true;
			Texture2D texture = null;
			AsyncOperationHandle<Texture2D> handle = Addressables.LoadAssetAsync<Texture2D>(StatIconAddress);
			await handle.Task;

			if (this == null)
			{
				if (handle.IsValid())
					Addressables.Release(handle);
				return;
			}

			if (handle.Status == AsyncOperationStatus.Succeeded)
			{
				_statIconTextureHandle = handle;
				_hasStatIconTextureHandle = true;
				texture = handle.Result;
			}
			else if (handle.IsValid())
			{
				Addressables.Release(handle);
			}

#if UNITY_EDITOR
			if (texture == null)
				texture = AssetDatabase.LoadAssetAtPath<Texture2D>(StatIconEditorPath);
#endif

			if (texture != null)
				CreateStatIconSprites(texture);
			else
				Debug.LogWarning($"Unable to load stat icon atlas '{StatIconAddress}'.");

			_isLoadingStatIcons = false;
			_hoverPawnInfo?.RefreshIcons();
		}

		void CreateStatIconSprites(Texture2D texture)
		{
			for (int index = 0; index < _statIconSprites.Length; index++)
			{
				if (_statIconSprites[index] != null)
					Destroy(_statIconSprites[index]);

				_statIconSprites[index] = Sprite.Create(texture, StatIconRects[index], new Vector2(0.5f, 0.5f), 100f);
			}
		}

		Sprite GetStatIconSprite(StatIcon icon)
		{
			int index = (int)icon;
			return index >= 0 && index < _statIconSprites.Length ? _statIconSprites[index] : null;
		}

		void ReleaseStatIconAtlas()
		{
			for (int index = 0; index < _statIconSprites.Length; index++)
			{
				if (_statIconSprites[index] != null)
					Destroy(_statIconSprites[index]);
				_statIconSprites[index] = null;
			}

			if (_hasStatIconTextureHandle && _statIconTextureHandle.IsValid())
				Addressables.Release(_statIconTextureHandle);

			_hasStatIconTextureHandle = false;
			_statIconTextureHandle = default;
		}

		void Update()
		{
			Refresh();
		}

		void OnDestroy()
		{
			_turnQueueView?.Dispose();
			UnbindObjectManager();
			UnsubscribeNetwork();
			if (_resultCoroutine != null)
				StopCoroutine(_resultCoroutine);
			if (_battleActionLogPlayback != null)
				StopCoroutine(_battleActionLogPlayback);

			ReleaseStatIconAtlas();
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
			{
				_objectManager.BattleActionLogApplied += OnBattleActionLogApplied;
				_objectManager.BattleStatusTickApplied += OnBattleStatusTickApplied;
				_objectManager.TurnQueueUpdated += OnTurnQueueUpdated;
				_objectManager.BattlePawnDied += OnBattlePawnDied;
				_objectManager.OptionalPositionSwapChoiceRequested += OnOptionalPositionSwapChoiceRequested;
			}
		}

		void UnbindObjectManager()
		{
			if (_objectManager != null)
			{
				_objectManager.BattleActionLogApplied -= OnBattleActionLogApplied;
				_objectManager.BattleStatusTickApplied -= OnBattleStatusTickApplied;
				_objectManager.TurnQueueUpdated -= OnTurnQueueUpdated;
				_objectManager.BattlePawnDied -= OnBattlePawnDied;
				_objectManager.OptionalPositionSwapChoiceRequested -= OnOptionalPositionSwapChoiceRequested;
			}

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
			BindActionBarDisplayFrames(_uiInstance.transform);
			BindTurnExit(_uiInstance.transform);
			BindStatePanels(_uiInstance.transform);
			EnsureOptionalPositionSwapPrompt(_uiInstance.transform);
			BindTurnQueue(_uiInstance.transform);
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
				// GothicUI uses the slot root as a permanent decorative frame. Skill art
				// is therefore rendered in a dedicated child instead of replacing it.
				Image iconImage = FindOrCreateActionIcon(slot);
				_actionIconImages[i] = iconImage;
				_defaultActionSprites[i] = null;
				_defaultActionPreserveAspects[i] = true;
				if (iconImage != null)
				{
					iconImage.sprite = null;
					iconImage.enabled = false;
				}
				HideLegacyActionDecoration(slot);
				HideActionSlotLabel(slot);
				_actionTexts[i] = null;
				_normalColors[i] = image != null ? image.color : Color.white;
				BindActionSlotTooltip(slot.gameObject, i);
			}
		}

		void BindActionBarDisplayFrames(Transform root)
		{
			Transform actionPanel = FindDeepChild(root, "ActionPanel");
			if (actionPanel == null)
				return;

			Sprite frameSprite = _actionImages.Length > 0 && _actionImages[0] != null
				? _actionImages[0].sprite
				: null;
			_currentTurnPortraitImage = CreateOrGetActionBarDisplayFrame(actionPanel, "CurrentTurnPortrait", frameSprite, true);
			_classMarkImage = CreateOrGetActionBarDisplayFrame(actionPanel, "ClassMark", frameSprite, false);
			_currentPortraitPawnId = 0;
			_currentPortraitRequestVersion++;
		}

		static Image CreateOrGetActionBarDisplayFrame(Transform actionPanel, string frameName, Sprite frameSprite, bool placeFirst)
		{
			Transform frame = actionPanel.Find(frameName);
			if (frame == null)
			{
				GameObject frameObject = new GameObject(frameName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
				frameObject.transform.SetParent(actionPanel, false);
				frame = frameObject.transform;
			}

			RectTransform frameRect = frame as RectTransform;
			frameRect.sizeDelta = new Vector2(60f, 68f);
			Image frameImage = frame.GetComponent<Image>();
			frameImage.sprite = frameSprite;
			frameImage.type = Image.Type.Simple;
			frameImage.raycastTarget = false;

			LayoutElement layout = frame.GetComponent<LayoutElement>();
			layout.ignoreLayout = false;
			layout.preferredWidth = 60f;
			layout.preferredHeight = 68f;

			Transform iconTransform = frame.Find("Icon");
			if (iconTransform == null)
			{
				GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
				iconObject.transform.SetParent(frame, false);
				iconTransform = iconObject.transform;
			}

			RectTransform iconRect = iconTransform as RectTransform;
			iconRect.anchorMin = new Vector2(0.15f, 0.12f);
			iconRect.anchorMax = new Vector2(0.85f, 0.88f);
			iconRect.offsetMin = Vector2.zero;
			iconRect.offsetMax = Vector2.zero;
			Image iconImage = iconTransform.GetComponent<Image>();
			iconImage.preserveAspect = true;
			iconImage.raycastTarget = false;
			iconImage.enabled = false;

			if (placeFirst)
				frame.SetSiblingIndex(0);
			else
				frame.SetAsLastSibling();

			return iconImage;
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

		void BindTurnQueue(Transform root)
		{
			Transform turnQueue = FindDeepChild(root, "TurnQueue");
			if (turnQueue == null)
			{
				Debug.LogWarning("Missing TurnQueue in BattleSceneUI.");
				return;
			}

			// The queue is presentation-only. A bad portrait asset must never prevent the
			// battle scene from finishing its transition (especially in the B-key debug flow).
			try
			{
				_turnQueueView?.Dispose();
				_turnQueueView = new BattleTurnQueueView(this, turnQueue, ResolvePawnClassForPortrait, ResolvePawnIsMineForPortrait);
				if (_objectManager != null && _objectManager.UpcomingTurnPawnIds.Count > 0)
				{
					_turnQueueView.Apply(new BattleTurnQueueUpdate(
						_objectManager.UpcomingTurnPawnIds,
						BattleTurnQueueUpdateKind.Initialize,
						new List<ulong>()));
				}
			}
			catch (System.Exception exception)
			{
				_turnQueueView = null;
				turnQueue.gameObject.SetActive(false);
				Debug.LogWarning($"Turn queue was disabled because it failed to initialize. Battle entry will continue. {exception.Message}");
			}
		}

		PawnClass ResolvePawnClassForPortrait(ulong pawnId)
		{
			if (_objectManager != null
				&& _objectManager.TryGetPawn(pawnId, out BattlePawn pawn)
				&& pawn.Info != null)
			{
				return pawn.Info.PawnClass;
			}

			return PawnClass.None;
		}

		bool? ResolvePawnIsMineForPortrait(ulong pawnId)
		{
			if (_objectManager != null && _objectManager.TryGetPawn(pawnId, out BattlePawn pawn))
				return pawn.IsMine;

			return null;
		}

		void OnTurnQueueUpdated(BattleTurnQueueUpdate update)
		{
			_turnQueueView?.Apply(update);
		}

		void OnBattlePawnDied(ulong pawnId)
		{
			_turnQueueView?.MarkPawnDead(pawnId);
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
			_hoverPawnInfo = CreateOrGetHoverPawnInfo(root);
		}

		void EnsureOptionalPositionSwapPrompt(Transform root)
		{
			if (_optionalPositionSwapPrompt != null || root == null)
				return;

			GameObject overlay = new GameObject("OptionalPositionSwapPrompt", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			overlay.transform.SetParent(root, false);
			RectTransform overlayRect = overlay.GetComponent<RectTransform>();
			overlayRect.anchorMin = Vector2.zero;
			overlayRect.anchorMax = Vector2.one;
			overlayRect.offsetMin = Vector2.zero;
			overlayRect.offsetMax = Vector2.zero;
			Image overlayImage = overlay.GetComponent<Image>();
			overlayImage.color = new Color(0f, 0f, 0f, 0.7f);

			GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			panel.transform.SetParent(overlay.transform, false);
			RectTransform panelRect = panel.GetComponent<RectTransform>();
			panelRect.anchorMin = new Vector2(0.5f, 0.5f);
			panelRect.anchorMax = new Vector2(0.5f, 0.5f);
			panelRect.pivot = new Vector2(0.5f, 0.5f);
			panelRect.sizeDelta = new Vector2(400f, 180f);
			panel.GetComponent<Image>().color = new Color(0.12f, 0.15f, 0.22f, 0.98f);

			CreateSwapPromptText(panel.transform);
			CreateSwapPromptButton(panel.transform, "SwapButton", "위치 교환", new Vector2(-92f, -52f), () => ResolveOptionalPositionSwapChoice(true));
			CreateSwapPromptButton(panel.transform, "StayButton", "회복만", new Vector2(92f, -52f), () => ResolveOptionalPositionSwapChoice(false));

			_optionalPositionSwapPrompt = overlay;
			_optionalPositionSwapPrompt.SetActive(false);
		}

		static void CreateSwapPromptText(Transform parent)
		{
			GameObject textObject = new GameObject("Message", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
			textObject.transform.SetParent(parent, false);
			RectTransform rect = textObject.GetComponent<RectTransform>();
			rect.anchorMin = new Vector2(0f, 0.45f);
			rect.anchorMax = new Vector2(1f, 1f);
			rect.offsetMin = new Vector2(20f, 8f);
			rect.offsetMax = new Vector2(-20f, -12f);
			Text text = textObject.GetComponent<Text>();
			text.font = GameRoot.UiFont;
			text.fontSize = 19;
			text.color = Color.white;
			text.alignment = TextAnchor.MiddleCenter;
			text.text = "나 돌아갈래\n회복·정화 후 대상 아군과 위치를 교환할까요?";
			text.raycastTarget = false;
		}

		static void CreateSwapPromptButton(Transform parent, string name, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction onClick)
		{
			GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
			buttonObject.transform.SetParent(parent, false);
			RectTransform rect = buttonObject.GetComponent<RectTransform>();
			rect.anchorMin = new Vector2(0.5f, 0.5f);
			rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.sizeDelta = new Vector2(155f, 42f);
			rect.anchoredPosition = anchoredPosition;
			Image image = buttonObject.GetComponent<Image>();
			image.color = new Color(0.28f, 0.48f, 0.78f, 1f);
			Button button = buttonObject.GetComponent<Button>();
			button.targetGraphic = image;
			button.onClick.AddListener(onClick);

			GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
			textObject.transform.SetParent(buttonObject.transform, false);
			RectTransform textRect = textObject.GetComponent<RectTransform>();
			textRect.anchorMin = Vector2.zero;
			textRect.anchorMax = Vector2.one;
			textRect.offsetMin = Vector2.zero;
			textRect.offsetMax = Vector2.zero;
			Text text = textObject.GetComponent<Text>();
			text.font = GameRoot.UiFont;
			text.fontSize = 16;
			text.color = Color.white;
			text.alignment = TextAnchor.MiddleCenter;
			text.text = label;
			text.raycastTarget = false;
		}

		void OnOptionalPositionSwapChoiceRequested(System.Action<bool> resolveChoice)
		{
			EnsureOptionalPositionSwapPrompt(_uiInstance != null ? _uiInstance.transform : null);
			if (_optionalPositionSwapPrompt == null)
			{
				resolveChoice?.Invoke(false);
				return;
			}

			_optionalPositionSwapChoice = resolveChoice;
			_optionalPositionSwapPrompt.SetActive(true);
		}

		void ResolveOptionalPositionSwapChoice(bool requestSwap)
		{
			System.Action<bool> resolveChoice = _optionalPositionSwapChoice;
			_optionalPositionSwapChoice = null;
			if (_optionalPositionSwapPrompt != null)
				_optionalPositionSwapPrompt.SetActive(false);
			resolveChoice?.Invoke(requestSwap);
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

			// The server emits normal hits, counters, and re-counters in their resolved
			// order. Queue them here so the client keeps that exact presentation order.
			_pendingBattleActionLogs.Enqueue(log);
			if (_battleActionLogPlayback == null)
				_battleActionLogPlayback = StartCoroutine(PlayBattleActionLogs());
		}

		void OnBattleStatusTickApplied(BattlePawn pawn, string statusKey, int amount)
		{
			if (pawn == null || amount <= 0 || _uiInstance == null || string.Equals(statusKey, "BLEED", System.StringComparison.OrdinalIgnoreCase) == false)
				return;

			Camera camera = Camera.main;
			if (camera == null)
				return;

			GameObject textObject = new GameObject("BattleStatusTickNumber");
			textObject.layer = _uiInstance.layer;
			textObject.transform.SetParent(_uiInstance.transform, false);
			RectTransform rect = textObject.AddComponent<RectTransform>();
			rect.sizeDelta = new Vector2(92f, 42f);
			rect.position = camera.WorldToScreenPoint(pawn.transform.position + Vector3.up * 0.82f);

			Text text = textObject.AddComponent<Text>();
			text.font = GameRoot.UiFont;
			text.fontSize = 16;
			text.fontStyle = FontStyle.Bold;
			text.color = new Color(0.72f, 0.16f, 0.18f, 1f);
			text.text = $"BLEED\n-{amount}";
			text.alignment = TextAnchor.MiddleCenter;
			text.horizontalOverflow = HorizontalWrapMode.Overflow;
			text.verticalOverflow = VerticalWrapMode.Overflow;
			text.raycastTarget = false;

			StartCoroutine(AnimateDamageNumber(rect, text, text.color));
		}

		IEnumerator PlayBattleActionLogs()
		{
			while (_pendingBattleActionLogs.Count > 0)
			{
				ShowBattleActionLog(_pendingBattleActionLogs.Dequeue());
				yield return new WaitForSecondsRealtime(0.5f);
			}

			_battleActionLogPlayback = null;
		}

		void ShowBattleActionLog(BattleActionLog log)
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

			bool showEvade = log.IsEvaded;
			bool showDamage = log.Damage != 0;
			if (showEvade == false && showDamage == false)
				return;

			bool isFireTileDamage = log.AttackerPawnId == 0
				&& string.Equals(log.ActionType, "fire_tile", System.StringComparison.OrdinalIgnoreCase);

			Camera camera = Camera.main;
			if (camera == null)
				return;

			string value = showEvade
				? "EVADE"
				: isFireTileDamage ? $"FIRE\n-{log.Damage}"
				: log.Damage > 0 ? $"-{log.Damage}" : $"+{-log.Damage}";
			if (log.IsCounter)
				value += "\nCOUNTER";
			if (log.IsBackAttack)
				value += "\nBACK";
			if (log.IsCritical)
				value += "\nCRIT";
			else if (log.IsPerfectGuarded)
				value += "\nPERFECT";
			else if (log.IsGuarded)
				value += "\nGUARD";

			Color color = showEvade
				? new Color(0.82f, 0.86f, 0.92f, 1f)
				: isFireTileDamage ? new Color(1f, 0.56f, 0.14f, 1f)
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
			BattlePawn hoveredPawn = null;
			if (hasHoveredTile)
				_objectManager.TryGetPawnAtAxial(hoveredAxial, out _, out hoveredPawn);

			if (_tileInfoText != null)
			{
				string actionTooltip = BuildActiveActionTooltip();
				_tileInfoText.text = string.IsNullOrWhiteSpace(actionTooltip)
					? BuildTileInfoText(hasHoveredTile, hoveredAxial)
					: actionTooltip;
			}

			if (_hoverPawnInfo != null)
				_hoverPawnInfo.SetPawn(IsPointerOverUi() ? null : hoveredPawn);
		}

		string BuildTurnText()
		{
			string ownership = _objectManager.IsCurrentTurnLocal ? "Mine" : "Enemy";
			if (_objectManager.BattleId == 0)
				ownership = "Debug";

			string canMove = "-";
			if (TryGetCurrentTurnPawn(out BattlePawn pawn))
			{
				canMove = pawn.CanMove ? "Yes" : "No";
			}

			return $"Turn\nPawn: {_objectManager.CurrentTurnPawnId}\nSide: {ownership}\nMode: {_objectManager.ActionMode}\nMoving: {(_objectManager.IsAnimatingMove ? "Yes" : "No")}\nMove: {canMove}\nLog:\n{_objectManager.BattleLogText}";
		}

		string BuildTileInfoText(bool hasHoveredTile, AxialCoord axial)
		{
			if (hasHoveredTile == false)
				return "Tile\nAxial: -\nState: -\nPawn: -\nEquipment: -";

			string state = _objectManager.IsTileWalkable(axial) ? "Walkable" : "Blocked";
			string pawn = "-";
			if (_objectManager.TryGetPawnAtAxial(axial, out ulong pawnId, out BattlePawn pawnController))
			{
				string side = pawnController.IsMine ? "Mine" : "Enemy";
				pawn = $"{pawnId} ({side})";
			}

			string equipment = _objectManager.MapGrid.TryGetEquipment(axial, out string equipmentKey, out ulong equipmentOwnerPawnId)
				? $"{equipmentKey} (Owner: {equipmentOwnerPawnId})"
				: "-";
			return $"Tile\nAxial: {axial}\nState: {state}\nPawn: {pawn}\nEquipment: {equipment}";
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
					iconKey = "icon_walk";
				}
				else if (TryGetUiSkillDefinition(currentTurnPawn, binding.ActionSlot, out BattleSkillDefinition skill))
				{
					label = BuildSkillDisplayName(skill);
					tooltip = BuildSkillTooltip(skill, label);

					if (_gameData.TryGetSkillView(skill.SkillKey, out BattleSkillViewDefinition view))
						iconKey = view.IconKey;

					if (currentTurnPawn is SuenAxe suenAxe
						&& suenAxe.TryGetSkillPresentation(skill.ActionSlot, out string suenName, out string suenIconKey))
					{
						label = suenName;
						tooltip = BuildSkillTooltip(skill, label);
						iconKey = suenIconKey;
					}
					else if (currentTurnPawn is AlenSpear alenSpear
						&& alenSpear.TryGetSkillPresentation(skill.ActionSlot, out string alenName, out string alenIconKey))
					{
						label = alenName;
						tooltip = BuildSkillTooltip(skill, label);
						iconKey = alenIconKey;
					}
					else if (currentTurnPawn is AlenSwordShield alenSwordShield
						&& alenSwordShield.TryGetSkillPresentation(skill.ActionSlot, out string shieldName, out string shieldIconKey))
					{
						label = shieldName;
						tooltip = BuildSkillTooltip(skill, label);
						iconKey = shieldIconKey;
					}
					else if (currentTurnPawn is ZillianLongbow zillianLongbow
						&& zillianLongbow.TryGetSkillPresentation(skill.ActionSlot, out string zillianName, out string zillianIconKey))
					{
						label = zillianName;
						tooltip = BuildSkillTooltip(skill, label);
						iconKey = zillianIconKey;
					}
					else if (currentTurnPawn is ZillianMace zillianMace
						&& zillianMace.TryGetSkillPresentation(skill.ActionSlot, out string maceName, out string maceIconKey))
					{
						label = maceName;
						tooltip = BuildSkillTooltip(skill, label);
						iconKey = maceIconKey;
					}
					else if (currentTurnPawn is SuenParvis suenParvis
						&& suenParvis.TryGetSkillPresentation(skill.ActionSlot, out string parvisName, out string parvisIconKey))
					{
						label = parvisName;
						tooltip = BuildSkillTooltip(skill, label);
						iconKey = parvisIconKey;
					}
				}

				_actionTooltips[i] = tooltip;
				SetActionText(i, label);
				SetActionIcon(i, iconKey);
			}

			RefreshCurrentTurnPortrait(currentTurnPawn);
		}

		void RefreshCurrentTurnPortrait(BattlePawn pawn)
		{
			if (_currentTurnPortraitImage == null)
				return;

			ulong pawnId = pawn != null ? pawn.PawnId : 0;
			if (_currentPortraitPawnId == pawnId)
				return;

			_currentPortraitPawnId = pawnId;
			int requestVersion = ++_currentPortraitRequestVersion;
			_currentTurnPortraitImage.sprite = null;
			_currentTurnPortraitImage.enabled = false;
			if (pawn == null || pawn.Info == null)
				return;

			string portraitKey = GetPortraitKey(pawn.Info.PawnClass);
			if (string.IsNullOrWhiteSpace(portraitKey) == false)
				_ = LoadCurrentTurnPortraitAsync(portraitKey, requestVersion);
		}

		async Task LoadCurrentTurnPortraitAsync(string portraitKey, int requestVersion)
		{
			Sprite portrait = await BattlePortraitSpriteCache.LoadAsync(portraitKey);
			if (_currentTurnPortraitImage == null || requestVersion != _currentPortraitRequestVersion)
				return;

			_currentTurnPortraitImage.sprite = portrait;
			_currentTurnPortraitImage.enabled = portrait != null;
		}

		static string GetPortraitKey(PawnClass pawnClass)
		{
			switch (pawnClass)
			{
				case PawnClass.SuenAxeSword:
				case PawnClass.SuenParvis: return "portrait_suen";
				case PawnClass.BeigeFire:
				case PawnClass.BeigeIce: return "portrait_beige";
				case PawnClass.ZillianLongbow:
				case PawnClass.ZillianMace: return "portrait_zillian";
				case PawnClass.AlenSpear:
				case PawnClass.AlenSwordShield: return "portrait_alen";
				case PawnClass.SeraNecromancer:
				case PawnClass.SeraWarlock: return "portrait_sera";
				case PawnClass.DarkhandSword: return "portrait_odo";
				default: return string.Empty;
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

		// The UI owns a separately loaded GameData repository. Do not ask the battle
		// manager for it here: its asynchronous load can complete after the UI and
		// would temporarily clear every icon key. Parvis is the one class whose
		// active skill is selected by a server status rather than by slot alone.
		bool TryGetUiSkillDefinition(BattlePawn pawn, int actionSlot, out BattleSkillDefinition skill)
		{
			skill = null;
			if (_gameData == null || pawn == null || pawn.Info == null)
				return false;

			if (pawn is SuenAxe suenAxe
				&& actionSlot == 7
				&& suenAxe.IsAxeOff == false)
				return false;

			if (pawn is SuenParvis parvis)
			{
				if (parvis.TryGetActiveSkillKey(actionSlot, out string activeSkillKey))
					return _gameData.TryGetSkill(activeSkillKey, out skill);

				return false;
			}

			return _gameData.TryGetSkill(pawn.Info.PawnClass, actionSlot, out skill);
		}

		string BuildSkillTooltip(BattleSkillDefinition skill, string displayName = null)
		{
			string name = string.IsNullOrWhiteSpace(displayName) ? BuildSkillDisplayName(skill) : displayName;
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
			string tooltip = $"{name}\nSlot: {skill.ActionSlot} / Range: {range}\nTarget: {skill.TargetType}";
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

		static void HideLegacyActionDecoration(Transform slot)
		{
			Transform legacyDecoration = slot != null ? slot.Find("Image") : null;
			if (legacyDecoration != null)
				legacyDecoration.gameObject.SetActive(false);
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

		static bool TryGetMoraleResource(BattlePawn pawn, out BattlePawn.ResourceState state)
		{
			state = default;
			return pawn != null && pawn.Resources.TryGetValue(Protocol.BattleResourceType.Morale, out state);
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
				string displayName = SuenAxe.GetStatusDisplayName(status.StatusKey);
					if (string.Equals(displayName, status.StatusKey, System.StringComparison.Ordinal))
						displayName = AlenSpear.GetStatusDisplayName(status.StatusKey);
					if (string.Equals(displayName, status.StatusKey, System.StringComparison.Ordinal))
						displayName = AlenSwordShield.GetStatusDisplayName(status.StatusKey);
					if (string.Equals(displayName, status.StatusKey, System.StringComparison.Ordinal))
						displayName = SuenParvis.GetStatusDisplayName(status.StatusKey);
				values.Add($"{displayName} x{status.Stacks} T{status.RemainingOwnerTurns}");
			}

			return string.Join(", ", values);
		}

		bool IsActionAvailable(ActionSlotBinding binding, BattlePawn pawn)
		{
			if (binding.IsWaitCommand || pawn == null)
				return true;

			if (pawn.IsActionBlocked)
				return false;

			if (binding.Mode == BattleActionMode.SubAction
				&& pawn is SuenAxe suenAxe
				&& suenAxe.IsAxeOff == false)
				return false;

			if (binding.Mode == BattleActionMode.SubAction
				&& pawn is SuenParvis parvis
				&& parvis.IsParvisOff == false)
				return false;

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
					return pawn.UsedNormalSkillThisTurn == false;
				default:
					return true;
			}
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

		static bool IsPointerOverUi()
		{
			return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
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

			rect.anchorMin = new Vector2(0.15f, 0.15f);
			rect.anchorMax = new Vector2(0.85f, 0.85f);
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

		HoverPawnInfoView CreateOrGetHoverPawnInfo(Transform root)
		{
			if (root == null)
				return null;

			Transform existing = root.Find("HoverPawnInfo");
			GameObject panelObject;
			RectTransform panelRect;
			Image background;
			if (existing != null)
			{
				panelObject = existing.gameObject;
				panelRect = panelObject.GetComponent<RectTransform>();
				background = panelObject.GetComponent<Image>();
			}
			else
			{
				panelObject = new GameObject("HoverPawnInfo", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
				panelObject.layer = root.gameObject.layer;
				panelObject.transform.SetParent(root, false);
				panelRect = panelObject.GetComponent<RectTransform>();
				background = panelObject.GetComponent<Image>();
				Outline outline = panelObject.GetComponent<Outline>();
				outline.effectColor = new Color(0.72f, 0.59f, 0.31f, 0.78f);
				outline.effectDistance = new Vector2(1f, -1f);
			}

			if (panelRect == null || background == null)
				return null;

			panelRect.anchorMin = new Vector2(0.5f, 0.5f);
			panelRect.anchorMax = new Vector2(0.5f, 0.5f);
			panelRect.pivot = Vector2.zero;
			panelRect.sizeDelta = new Vector2(286f, 250f);
			background.color = new Color(0.10f, 0.075f, 0.035f, 0.94f);
			background.raycastTarget = false;
			panelObject.transform.SetAsLastSibling();

			Text title = CreateOrGetHoverPawnText(panelObject.transform, "Title", 16, TextAnchor.MiddleLeft);
			RectTransform titleRect = title.rectTransform;
			titleRect.anchorMin = new Vector2(0f, 0.84f);
			titleRect.anchorMax = new Vector2(1f, 1f);
			titleRect.offsetMin = new Vector2(12f, 2f);
			titleRect.offsetMax = new Vector2(-12f, -6f);
			title.color = new Color(0.97f, 0.84f, 0.48f, 1f);

			Transform statsRoot = panelObject.transform.Find("Stats");
			if (statsRoot == null)
			{
				GameObject statsObject = new GameObject("Stats", typeof(RectTransform));
				statsObject.layer = panelObject.layer;
				statsObject.transform.SetParent(panelObject.transform, false);
				statsRoot = statsObject.transform;
			}

			RectTransform statsRect = statsRoot as RectTransform;
			statsRect.anchorMin = new Vector2(0f, 0.30f);
			statsRect.anchorMax = new Vector2(1f, 0.84f);
			statsRect.offsetMin = new Vector2(10f, 0f);
			statsRect.offsetMax = new Vector2(-10f, -2f);

			Text body = CreateOrGetHoverPawnText(panelObject.transform, "Body", 12, TextAnchor.UpperLeft);
			RectTransform bodyRect = body.rectTransform;
			bodyRect.anchorMin = Vector2.zero;
			bodyRect.anchorMax = new Vector2(1f, 0.30f);
			bodyRect.offsetMin = new Vector2(12f, 10f);
			bodyRect.offsetMax = new Vector2(-12f, -2f);
			body.color = new Color(0.92f, 0.90f, 0.82f, 1f);

			panelObject.SetActive(false);
			return new HoverPawnInfoView(panelObject, panelRect, root as RectTransform, title, body, statsRect, GetStatIconSprite);
		}

		static Text CreateOrGetHoverPawnText(Transform parent, string name, int fontSize, TextAnchor alignment)
		{
			Transform existing = parent.Find(name);
			Text text = existing != null ? existing.GetComponent<Text>() : null;
			if (text == null)
			{
				GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
				textObject.layer = parent.gameObject.layer;
				textObject.transform.SetParent(parent, false);
				text = textObject.GetComponent<Text>();
			}

			text.font = GameRoot.UiFont;
			text.fontSize = fontSize;
			text.alignment = alignment;
			text.horizontalOverflow = HorizontalWrapMode.Wrap;
			text.verticalOverflow = VerticalWrapMode.Truncate;
			text.raycastTarget = false;
			return text;
		}

		sealed class HoverPawnInfoView
		{
			readonly GameObject _root;
			readonly RectTransform _rect;
			readonly RectTransform _canvasRect;
			readonly Text _title;
			readonly Text _body;
			readonly HoverStatGridView _stats;
			BattlePawn _lastPawn;

			public HoverPawnInfoView(GameObject root, RectTransform rect, RectTransform canvasRect, Text title, Text body, RectTransform statsRoot, System.Func<StatIcon, Sprite> iconResolver)
			{
				_root = root;
				_rect = rect;
				_canvasRect = canvasRect;
				_title = title;
				_body = body;
				_stats = new HoverStatGridView(statsRoot, iconResolver);
			}

			public void SetPawn(BattlePawn pawn)
			{
				if (pawn == null || _root == null || _rect == null || _canvasRect == null)
				{
					SetVisible(false);
					return;
				}

				_lastPawn = pawn;

				Camera camera = Camera.main;
				if (camera == null)
				{
					SetVisible(false);
					return;
				}

				Vector3 screenPosition = camera.WorldToScreenPoint(pawn.transform.position + Vector3.up * 1.2f);
				if (screenPosition.z <= 0f
					|| RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPosition, null, out Vector2 localPosition) == false)
				{
					SetVisible(false);
					return;
				}

				if (_title != null)
				{
					string side = pawn.IsMine ? "ALLY" : "ENEMY";
					string pawnClass = pawn.Info != null ? pawn.Info.PawnClass.ToString() : "PAWN";
					_title.text = $"{side} · {FormatIdentifier(pawnClass)}";
				}

				if (_body != null)
					_body.text = BuildInfoText(pawn);
				_stats.SetPawn(pawn);

				PositionNearPawn(localPosition);
				SetVisible(true);
			}

			public void RefreshIcons()
			{
				if (_lastPawn != null)
					_stats.SetPawn(_lastPawn);
			}

			void PositionNearPawn(Vector2 pawnPosition)
			{
				Vector2 size = _rect.rect.size;
				Rect canvasBounds = _canvasRect.rect;
				const float margin = 10f;
				const float actionBarClearance = 100f;
				float minX = canvasBounds.xMin + margin;
				float maxX = canvasBounds.xMax - size.x - margin;
				float minY = canvasBounds.yMin + actionBarClearance;
				float maxY = canvasBounds.yMax - size.y - margin;
				_rect.anchoredPosition = new Vector2(
					Mathf.Clamp(pawnPosition.x + 24f, minX, maxX),
					Mathf.Clamp(pawnPosition.y + 18f, minY, maxY));
			}

			void SetVisible(bool visible)
			{
				if (_root != null && _root.activeSelf != visible)
					_root.SetActive(visible);
			}

			static string BuildInfoText(BattlePawn pawn)
			{
				string resource = TryGetPrimaryResource(pawn, out string resourceName, out BattlePawn.ResourceState resourceState, out _)
					? $"{resourceName}: {FormatValue(resourceState.Value, resourceState.MaxValue)}"
					: "Resource: -";
				string state = pawn.IsActionBlocked ? "Action blocked" : pawn.IsMine && pawn.CanMove == false ? "Movement used" : "Ready";
				return $"{pawn.Role}  ·  {pawn.Axial}\n{resource}  ·  {state}";
			}

			static string FormatIdentifier(string value)
			{
				return string.IsNullOrWhiteSpace(value) ? "PAWN" : value.Replace('_', ' ');
			}
		}

		enum StatIcon
		{
			Strength = 0, Dexterity = 1, SpellPower = 2, Defense = 3, Focus = 4,
			Curse = 5, Hp = 6, Damage = 7, Accuracy = 8, Critical = 9,
			Morale = 10, Evasion = 11, DamageReduction = 12, MoveRange = 13, AttackRange = 14,
			TurnOrder = 15, ArmorShield = 16, SkillShield = 17, Bleed = 18, Poison = 19,
			Burn = 20, Frostbite = 21, Stun = 22, Willpower = 23, Empty = 24,
		}

		sealed class HoverStatGridView
		{
			readonly RectTransform _root;
			readonly System.Func<StatIcon, Sprite> _iconResolver;
			readonly List<HoverStatEntry> _entries = new List<HoverStatEntry>();
			readonly List<HoverStatData> _values = new List<HoverStatData>();

			public HoverStatGridView(RectTransform root, System.Func<StatIcon, Sprite> iconResolver)
			{
				_root = root;
				_iconResolver = iconResolver;
				if (_root == null)
					return;

				// Stat slots are authored in HoverPawnInfo.prefab. Reuse those serialized
				// UI objects before creating any overflow entries for future status types.
				for (int index = 0; ; index++)
				{
					Transform slot = _root.Find($"Stat_{index}");
					if (slot == null)
						break;

					HoverStatEntry entry = TryGetPrefabEntry(slot);
					if (entry == null)
						break;

					_entries.Add(entry);
				}
			}

			public void SetPawn(BattlePawn pawn)
			{
				if (_root == null || pawn == null)
					return;

				_values.Clear();
				_values.Add(new HoverStatData(StatIcon.Hp, FormatValue(pawn.Hp, pawn.MaxHp)));
				_values.Add(new HoverStatData(StatIcon.ArmorShield, FormatValue(pawn.Armor, pawn.MaxArmor)));
				_values.Add(new HoverStatData(StatIcon.SkillShield, pawn.TotalBarrierValue.ToString()));
				if (TryGetMoraleResource(pawn, out BattlePawn.ResourceState morale))
					_values.Add(new HoverStatData(StatIcon.Morale, FormatValue(morale.Value, morale.MaxValue)));
				_values.Add(new HoverStatData(StatIcon.MoveRange, pawn.CanMove ? pawn.MoveRange.ToString() : "0"));

				if (pawn.Statuses != null)
				{
					List<string> keys = new List<string>(pawn.Statuses.Keys);
					keys.Sort(System.StringComparer.Ordinal);
					for (int index = 0; index < keys.Count; index++)
					{
						BattlePawn.StatusState status = pawn.Statuses[keys[index]];
						if (TryGetStatusIcon(status.StatusKey, out StatIcon icon))
							_values.Add(new HoverStatData(icon, $"x{status.Stacks} T{status.RemainingOwnerTurns}"));
					}
				}

				while (_entries.Count < _values.Count)
					_entries.Add(CreateEntry(_root, _entries.Count));

				for (int index = 0; index < _entries.Count; index++)
				{
					bool visible = index < _values.Count;
					_entries[index].SetVisible(visible);
					if (visible)
					{
						// The five core stat icons are authored and serialized in HoverPawnInfo.prefab.
						// Only variable status slots (and future overflow slots) need a runtime icon.
						Sprite sprite = index < 5 ? null : _iconResolver != null ? _iconResolver(_values[index].Icon) : null;
						_entries[index].Set(_values[index], sprite, index, _values.Count);
					}
				}
			}

			static bool TryGetStatusIcon(string statusKey, out StatIcon icon)
			{
				switch (statusKey?.ToUpperInvariant())
				{
					case "BLEED": icon = StatIcon.Bleed; return true;
					case "POISON": icon = StatIcon.Poison; return true;
					case "BURN": case "BURNING": icon = StatIcon.Burn; return true;
					case "FROST": case "FROSTBITE": icon = StatIcon.Frostbite; return true;
					case "STUN": icon = StatIcon.Stun; return true;
					default: icon = StatIcon.Empty; return false;
				}
			}

			static HoverStatEntry CreateEntry(RectTransform parent, int index)
			{
				GameObject entryObject = new GameObject($"Stat_{index}", typeof(RectTransform));
				entryObject.layer = parent.gameObject.layer;
				entryObject.transform.SetParent(parent, false);
				RectTransform entryRect = entryObject.GetComponent<RectTransform>();

				GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
				iconObject.layer = entryObject.layer;
				iconObject.transform.SetParent(entryObject.transform, false);
				Image icon = iconObject.GetComponent<Image>();
				icon.raycastTarget = false;
				icon.preserveAspect = true;
				icon.rectTransform.anchorMin = new Vector2(0f, 0.15f);
				icon.rectTransform.anchorMax = new Vector2(0f, 0.85f);
				icon.rectTransform.sizeDelta = new Vector2(25f, 25f);
				icon.rectTransform.anchoredPosition = new Vector2(13f, 0f);

				Text value = CreateOrGetHoverPawnText(entryObject.transform, "Value", 11, TextAnchor.MiddleLeft);
				value.rectTransform.anchorMin = new Vector2(0f, 0f);
				value.rectTransform.anchorMax = Vector2.one;
				value.rectTransform.offsetMin = new Vector2(29f, 0f);
				value.rectTransform.offsetMax = Vector2.zero;
				value.color = new Color(0.96f, 0.92f, 0.78f, 1f);
				return new HoverStatEntry(entryObject, entryRect, icon, value);
			}

			static HoverStatEntry TryGetPrefabEntry(Transform slot)
			{
				if (slot == null)
					return null;

				RectTransform rect = slot as RectTransform;
				Image icon = slot.Find("Icon")?.GetComponent<Image>();
				Text value = slot.Find("Value")?.GetComponent<Text>();
				return rect != null && icon != null && value != null
					? new HoverStatEntry(slot.gameObject, rect, icon, value)
					: null;
			}
		}

		readonly struct HoverStatData
		{
			public readonly StatIcon Icon;
			public readonly string Value;
			public HoverStatData(StatIcon icon, string value) { Icon = icon; Value = value; }
		}

		sealed class HoverStatEntry
		{
			readonly GameObject _root;
			readonly RectTransform _rect;
			readonly Image _icon;
			readonly Text _value;
			public HoverStatEntry(GameObject root, RectTransform rect, Image icon, Text value) { _root = root; _rect = rect; _icon = icon; _value = value; }

			public void Set(HoverStatData data, Sprite sprite, int index, int count)
			{
				const int columns = 3;
				int rows = Mathf.CeilToInt(count / (float)columns);
				int column = index % columns;
				int row = index / columns;
				float width = 1f / columns;
				float height = 1f / Mathf.Max(1, rows);
				_rect.anchorMin = new Vector2(column * width, 1f - ((row + 1) * height));
				_rect.anchorMax = new Vector2((column + 1) * width, 1f - (row * height));
				_rect.offsetMin = new Vector2(1f, 1f);
				_rect.offsetMax = new Vector2(-1f, -1f);
				if (sprite != null)
					_icon.sprite = sprite;
				_icon.enabled = _icon.sprite != null;
				_value.text = data.Value;
			}

			public void SetVisible(bool visible)
			{
				if (_root != null && _root.activeSelf != visible)
					_root.SetActive(visible);
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

				string suenLabel = SuenAxe.GetStatusIconLabel(statusKey);
				if (string.IsNullOrWhiteSpace(suenLabel) == false)
					return suenLabel;

				string alenLabel = AlenSpear.GetStatusIconLabel(statusKey);
				if (string.IsNullOrWhiteSpace(alenLabel) == false)
					return alenLabel;

				string alenShieldLabel = AlenSwordShield.GetStatusIconLabel(statusKey);
				if (string.IsNullOrWhiteSpace(alenShieldLabel) == false)
					return alenShieldLabel;

				string parvisLabel = SuenParvis.GetStatusIconLabel(statusKey);
				if (string.IsNullOrWhiteSpace(parvisLabel) == false)
					return parvisLabel;

				if (statusKey.IndexOf("COLD_HARD_WORKER_EMPOWERED", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return "EMP";
				if (statusKey.IndexOf("IGNORE_COLD_BACKLASH", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return "IMM";
				if (statusKey.IndexOf("THAWING_POTION_DAMAGE_DOWN", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return "DMG";
				if (statusKey.IndexOf("BLEED", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return "BLE";
				if (statusKey.IndexOf("STUN", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return "STN";
				if (statusKey.IndexOf("FROSTBITE", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return "FRB";
				if (statusKey.IndexOf("ACCURACY", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return "ACC";

				string compact = statusKey.Replace("_", string.Empty).ToUpperInvariant();
				return compact.Length <= 3 ? compact : compact.Substring(0, 3);
			}

			static Color GetStatusColor(string statusKey)
			{
				string key = statusKey ?? string.Empty;
				if (SuenAxe.GetStatusIconLabel(key) != null)
				{
					if (key.IndexOf("ACCURACY_DOWN", System.StringComparison.OrdinalIgnoreCase) >= 0)
						return new Color(0.68f, 0.36f, 0.25f, 0.96f);
					if (key.IndexOf("EVASION", System.StringComparison.OrdinalIgnoreCase) >= 0
						|| key.IndexOf("FIRST_HIT_EVADE", System.StringComparison.OrdinalIgnoreCase) >= 0)
						return new Color(0.35f, 0.75f, 0.88f, 0.96f);
					if (key.IndexOf("DAMAGE_REDUCTION", System.StringComparison.OrdinalIgnoreCase) >= 0
						|| key.IndexOf("INTERCEPT_GUARD", System.StringComparison.OrdinalIgnoreCase) >= 0)
						return new Color(0.4f, 0.6f, 0.94f, 0.96f);
					return new Color(0.86f, 0.63f, 0.24f, 0.96f);
				}

				if (SuenParvis.GetStatusIconLabel(key) != null)
				{
					if (key.IndexOf("YABAWI", System.StringComparison.OrdinalIgnoreCase) >= 0)
						return new Color(0.35f, 0.75f, 0.88f, 0.96f);
					return new Color(0.42f, 0.58f, 0.78f, 0.96f);
				}

				if (AlenSwordShield.GetStatusIconLabel(key) != null)
				{
					if (key.IndexOf("TAUNT", System.StringComparison.OrdinalIgnoreCase) >= 0)
						return new Color(0.83f, 0.36f, 0.24f, 0.96f);
					if (key.IndexOf("DUEL", System.StringComparison.OrdinalIgnoreCase) >= 0)
						return new Color(0.78f, 0.48f, 0.82f, 0.96f);
					if (key.IndexOf("RESPONSIBILITY", System.StringComparison.OrdinalIgnoreCase) >= 0)
						return new Color(0.32f, 0.64f, 0.9f, 0.96f);
					return new Color(0.42f, 0.62f, 0.88f, 0.96f);
				}

				if (key.IndexOf("COLD_HARD_WORKER_EMPOWERED", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return new Color(1f, 0.73f, 0.22f, 0.96f);
				if (key.IndexOf("IGNORE_COLD_BACKLASH", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return new Color(0.42f, 0.9f, 1f, 0.96f);
				if (key.IndexOf("THAWING_POTION_DAMAGE_DOWN", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return new Color(0.72f, 0.43f, 0.27f, 0.96f);
				if (key.IndexOf("ALEN_SPEAR_SENTINEL", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return new Color(0.38f, 0.74f, 0.45f, 0.96f);
				if (key.IndexOf("ALEN_SPEAR_CHARGE_COMMAND_MOVE", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return new Color(0.94f, 0.62f, 0.2f, 0.96f);
				if (key.IndexOf("STUN", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return new Color(0.96f, 0.68f, 0.2f, 0.96f);
				if (key.IndexOf("BLEED", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return new Color(0.72f, 0.16f, 0.18f, 0.96f);
				if (key.IndexOf("ACCURACY", System.StringComparison.OrdinalIgnoreCase) >= 0)
					return new Color(0.68f, 0.36f, 0.25f, 0.96f);
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
