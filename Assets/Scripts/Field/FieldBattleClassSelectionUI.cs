using System;
using System.Collections.Generic;
using App;
using Protocol;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldBattleClassSelectionUI : MonoBehaviour
	{
		enum ClassGroup { Suen, Beige, Alen, Zillian }
		enum SelectionMode { Battle, FieldPawn }

		sealed class OptionButton
		{
			public ClassGroup Group;
			public PawnClass PawnClass;
			public Image Background;
			public Button Button;
		}

		const string BattleClassSelectionUiAddress = "FieldBattleClassSelectionUI";
		const int CanvasSortingOrder = 1250;
		static readonly Color NormalColor = new Color(0.18f, 0.125f, 0.055f, 0.96f);
		static readonly Color SelectedColor = new Color(0.42f, 0.29f, 0.10f, 0.98f);
		static readonly Color DisabledColor = new Color(0.08f, 0.06f, 0.03f, 0.84f);
		static FieldBattleClassSelectionUI _instance;
		public static event Action VisualCatalogReady;

		readonly Dictionary<ClassGroup, PawnClass> _selected = new Dictionary<ClassGroup, PawnClass>();
		readonly List<OptionButton> _buttons = new List<OptionButton>();
		GameObject _overlayRoot;
		Transform _options;
		Text _status;
		Text _title;
		Button _submit;
		Text _submitText;
		AsyncOperationHandle<GameObject> _uiHandle;
		bool _hasUiHandle;
		bool _isBinding;
		bool _bound;
		bool _subscribed;
		bool _waiting;
		bool _locked;
		bool _isDebugBattleSelection;
		bool _openFieldSelectionWhenBound;
		bool _fieldSelectionRequestPending;
		SelectionMode _mode;
		PawnClass _selectedFieldPawn = PawnClass.BeigeIce;
		S_BATTLE_CLASS_SELECTION_START _pendingStart;

		public static bool IsBlockingInput => _instance != null && _instance._overlayRoot != null && _instance._overlayRoot.activeSelf;

		public static bool OpenFieldPawnSelection()
		{
			if (_instance == null)
				return false;
			if (_instance._bound)
				_instance.ShowFieldPawnSelection();
			else
				_instance._openFieldSelectionWhenBound = true;
			return true;
		}

		public static bool TryGetPawnClassIcon(PawnClass pawnClass, out Sprite sprite)
		{
			sprite = null;
			if (_instance == null || _instance._options == null || TryGetClassGroup(pawnClass, out ClassGroup group) == false)
				return false;
			Transform buttonTransform = _instance._options.Find(group + "Column/" + pawnClass);
			if (buttonTransform == null)
				return false;
			Image[] images = buttonTransform.GetComponentsInChildren<Image>(true);
			for (int index = images.Length - 1; index >= 0; index--)
			{
				if (images[index].gameObject != buttonTransform.gameObject && images[index].sprite != null)
				{
					sprite = images[index].sprite;
					return true;
				}
			}
			return false;
		}

		void Awake()
		{
			_instance = this;
			FieldBattleInviteUI.EnsureEventSystem();
			BindOrLoadUi();
		}

		void OnEnable() => TrySubscribe();
		void Update()
		{
			if (_subscribed == false)
				TrySubscribe();
		}
		void OnDisable() => Unsubscribe();
		void OnDestroy()
		{
			if (_instance == this)
				_instance = null;
			if (_hasUiHandle && _uiHandle.IsValid())
				Addressables.ReleaseInstance(_uiHandle);
		}

		void TrySubscribe()
		{
			if (_subscribed || GameRoot.Instance == null || GameRoot.Instance.Network == null)
				return;

			GameRoot.Instance.Network.BattleClassSelectionStartReceived += OnStart;
			GameRoot.Instance.Network.BattleClassSelectionResultReceived += OnResult;
			GameRoot.Instance.Network.FieldPawnSelectionReceived += OnFieldPawnSelectionResult;
			GameRoot.Instance.Network.DespawnReceived += OnDespawn;
			GameRoot.Instance.Network.EnterBattleReceived += OnEnterBattle;
			_subscribed = true;
		}

		void Unsubscribe()
		{
			if (_subscribed && GameRoot.Instance != null && GameRoot.Instance.Network != null)
			{
				GameRoot.Instance.Network.BattleClassSelectionStartReceived -= OnStart;
				GameRoot.Instance.Network.BattleClassSelectionResultReceived -= OnResult;
				GameRoot.Instance.Network.FieldPawnSelectionReceived -= OnFieldPawnSelectionResult;
				GameRoot.Instance.Network.DespawnReceived -= OnDespawn;
				GameRoot.Instance.Network.EnterBattleReceived -= OnEnterBattle;
			}
			_subscribed = false;
		}

		void OnStart(S_BATTLE_CLASS_SELECTION_START packet)
		{
			if (packet == null)
				return;

			_pendingStart = packet;
			if (_bound)
				ShowStart(packet);
		}

		void ShowStart(S_BATTLE_CLASS_SELECTION_START packet)
		{
			_mode = SelectionMode.Battle;
			_selected.Clear();
			_buttons.Clear();
			_waiting = false;
			_locked = false;
			_isDebugBattleSelection = packet.TargetPlayerId == 0;
			_fieldSelectionRequestPending = false;
			ClearOptions();
			CreateOptions(ClassGroup.Suen, "\uC2A4\uC5D4", packet.SuenOptions);
			CreateOptions(ClassGroup.Beige, "\uBCA0\uC774\uC9C0", packet.BeigeOptions);
			CreateOptions(ClassGroup.Alen, "\uC54C\uB80C", packet.AlenOptions);
			CreateOptions(ClassGroup.Zillian, "\uC9C8\uB9AC\uC5B8", packet.ZillianOptions);
			_overlayRoot.SetActive(true);
			if (_title != null)
				_title.text = "전투 클래스 선택";
			_status.text = "\uAC01 \uC601\uC6C5\uC758 \uD074\uB798\uC2A4\uB97C \uD558\uB098\uC529 \uC120\uD0DD\uD558\uC138\uC694.";
			Refresh();
		}

		void ShowFieldPawnSelection()
		{
			_mode = SelectionMode.FieldPawn;
			_selected.Clear();
			_buttons.Clear();
			_waiting = false;
			_locked = false;
			_fieldSelectionRequestPending = false;
			_openFieldSelectionWhenBound = false;
			PawnClass currentClass = GameRoot.Instance?.Network.LastEnterGame?.Player?.FieldPawnClass ?? PawnClass.BeigeIce;
			_selectedFieldPawn = IsSupportedFieldPawn(currentClass) ? currentClass : PawnClass.BeigeIce;
			ClearOptions();
			CreateOptions(ClassGroup.Suen, "스엔", new[] { PawnClass.SuenAxeSword, PawnClass.SuenParvis });
			CreateOptions(ClassGroup.Beige, "베이지", new[] { PawnClass.BeigeFire, PawnClass.BeigeIce });
			CreateOptions(ClassGroup.Alen, "알렌", new[] { PawnClass.AlenSpear, PawnClass.AlenSwordShield });
			CreateOptions(ClassGroup.Zillian, "질리언", new[] { PawnClass.ZillianLongbow, PawnClass.ZillianMace });
			_overlayRoot.SetActive(true);
			if (_title != null)
				_title.text = "필드 캐릭터 선택";
			_status.text = "필드에서 사용할 캐릭터 엠블렘을 하나 선택하세요.";
			Refresh();
		}

		void OnResult(S_BATTLE_CLASS_SELECTION_RESULT packet)
		{
			if (packet == null || _bound == false || _overlayRoot.activeSelf == false)
				return;

			if (packet.Success == false)
			{
				_waiting = false;
				_locked = false;
				_status.text = string.IsNullOrWhiteSpace(packet.Reason) ? "\uC120\uD0DD\uC774 \uAC70\uBD80\uB418\uC5C8\uC2B5\uB2C8\uB2E4. \uB2E4\uC2DC \uC2DC\uB3C4\uD558\uC138\uC694." : packet.Reason;
			}
			else if (_isDebugBattleSelection)
			{
				Hide();
				return;
			}
			else
			{
				_waiting = packet.WaitingForOpponent;
				_locked = packet.WaitingForOpponent == false;
				_status.text = _waiting ? "\uC120\uD0DD\uC744 \uC804\uC1A1\uD588\uC2B5\uB2C8\uB2E4. \uC0C1\uB300\uC758 \uC120\uD0DD\uC744 \uAE30\uB2E4\uB9AC\uB294 \uC911\uC785\uB2C8\uB2E4..." : "\uC591\uCABD \uC120\uD0DD\uC774 \uC644\uB8CC\uB418\uC5C8\uC2B5\uB2C8\uB2E4. \uC804\uD22C\uB97C \uC900\uBE44\uD558\uB294 \uC911\uC785\uB2C8\uB2E4...";
			}
			Refresh();
		}

		void OnFieldPawnSelectionResult(S_FIELD_PAWN_SELECT packet)
		{
			if (packet == null || _bound == false || _mode != SelectionMode.FieldPawn || _fieldSelectionRequestPending == false)
				return;
			ulong myObjectId = GameRoot.Instance?.Network.LastEnterGame?.Player?.ObjectId ?? 0;
			if (packet.ObjectId != myObjectId)
				return;

			_fieldSelectionRequestPending = false;
			if (packet.Success)
			{
				_selectedFieldPawn = packet.PawnClass;
				Hide();
				return;
			}

			_status.text = string.IsNullOrWhiteSpace(packet.Reason) ? "필드 캐릭터 선택이 거부되었습니다." : packet.Reason;
			Refresh();
		}

		void OnDespawn(S_DESPAWN packet)
		{
			if (_locked)
				Hide();
		}

		void OnEnterBattle(S_ENTER_BATTLE packet)
		{
			if (packet != null && packet.Success)
				Hide();
		}

		void Select(ClassGroup group, PawnClass pawnClass)
		{
			if (_waiting || _locked)
				return;
			if (_mode == SelectionMode.FieldPawn)
			{
				_selectedFieldPawn = pawnClass;
				_status.text = "선택 적용을 누르면 같은 필드의 모든 플레이어에게 반영됩니다.";
				Refresh();
				return;
			}

			_selected[group] = pawnClass;
			_status.text = "\uAC01 \uC601\uC6C5\uC758 \uD074\uB798\uC2A4\uB97C \uD558\uB098\uC529 \uC120\uD0DD\uD558\uC138\uC694.";
			Refresh();
		}

		void Submit()
		{
			if (_mode == SelectionMode.FieldPawn)
			{
				if (_fieldSelectionRequestPending || IsSupportedFieldPawn(_selectedFieldPawn) == false || GameRoot.Instance == null)
					return;
				if (GameRoot.Instance.Network.SelectFieldPawn(_selectedFieldPawn) == false)
				{
					_status.text = string.IsNullOrWhiteSpace(GameRoot.Instance.Network.LastError) ? "필드 캐릭터 선택 전송에 실패했습니다." : GameRoot.Instance.Network.LastError;
					Refresh();
					return;
				}
				_fieldSelectionRequestPending = true;
				_status.text = "선택을 저장하는 중입니다...";
				Refresh();
				return;
			}

			if (HasAllSelections() == false || GameRoot.Instance == null)
				return;

			List<PawnClass> classes = new List<PawnClass>(4)
			{
				_selected[ClassGroup.Suen], _selected[ClassGroup.Beige],
				_selected[ClassGroup.Alen], _selected[ClassGroup.Zillian],
			};
			if (GameRoot.Instance.Network.SendBattleClassSelection(classes) == false)
			{
				_status.text = string.IsNullOrWhiteSpace(GameRoot.Instance.Network.LastError) ? "\uC120\uD0DD \uC804\uC1A1\uC5D0 \uC2E4\uD328\uD588\uC2B5\uB2C8\uB2E4." : GameRoot.Instance.Network.LastError;
				Refresh();
				return;
			}

			_status.text = "\uC120\uD0DD\uC744 \uC804\uC1A1\uD558\uB294 \uC911\uC785\uB2C8\uB2E4...";
			_submit.interactable = false;
		}

		bool HasAllSelections()
		{
			return _selected.ContainsKey(ClassGroup.Suen) && _selected.ContainsKey(ClassGroup.Beige)
				&& _selected.ContainsKey(ClassGroup.Alen) && _selected.ContainsKey(ClassGroup.Zillian);
		}

		void Refresh()
		{
			bool disabled = _waiting || _locked || _fieldSelectionRequestPending;
			for (int index = 0; index < _buttons.Count; index++)
			{
				OptionButton option = _buttons[index];
				bool selected = _mode == SelectionMode.FieldPawn
					? _selectedFieldPawn == option.PawnClass
					: _selected.TryGetValue(option.Group, out PawnClass selectedClass) && selectedClass == option.PawnClass;
				option.Background.color = disabled ? DisabledColor : selected ? SelectedColor : NormalColor;
				option.Button.interactable = disabled == false;
			}
			_submit.interactable = disabled == false && (_mode == SelectionMode.FieldPawn ? IsSupportedFieldPawn(_selectedFieldPawn) : HasAllSelections());
			_submitText.text = _fieldSelectionRequestPending ? "저장 중..." : _mode == SelectionMode.FieldPawn ? "선택 적용" : disabled ? "\uC120\uD0DD \uC644\uB8CC" : "\uC120\uD0DD \uD655\uC815";
		}

		void Hide()
		{
			_isDebugBattleSelection = false;
			_fieldSelectionRequestPending = false;
			if (_overlayRoot != null)
				_overlayRoot.SetActive(false);
		}

		async void BindOrLoadUi()
		{
			if (_isBinding || _bound)
				return;

			_isBinding = true;
			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(BattleClassSelectionUiAddress);
			await handle.Task;
			if (this == null || _bound)
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				return;
			}
			if (handle.Status != AsyncOperationStatus.Succeeded)
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				Debug.LogError($"Failed to load addressable UI prefab: {BattleClassSelectionUiAddress}");
				_isBinding = false;
				return;
			}

			_uiHandle = handle;
			_hasUiHandle = true;
			BindUi(handle.Result);
			_isBinding = false;
		}

		void BindUi(GameObject uiObject)
		{
			FieldBattleClassSelectionView view = uiObject != null ? uiObject.GetComponent<FieldBattleClassSelectionView>() : null;
			if (view == null || view.Canvas == null || view.Panel == null || view.Status == null || view.Options == null || view.Submit == null || view.SubmitText == null)
			{
				Debug.LogError($"Addressable UI prefab '{BattleClassSelectionUiAddress}' requires FieldBattleClassSelectionView with all prefab references assigned.");
				return;
			}

			_overlayRoot = uiObject;
			_overlayRoot.name = "Canvas_FieldBattleClassSelectionUI";
			SceneManager.MoveGameObjectToScene(_overlayRoot, gameObject.scene);
			view.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			view.Canvas.sortingOrder = CanvasSortingOrder;
			_options = view.Options;
			_status = view.Status;
			_title = view.Panel.transform.Find("Title")?.GetComponent<Text>();
			_submit = view.Submit;
			_submitText = view.SubmitText;
			_submit.onClick.RemoveAllListeners();
			_submit.onClick.AddListener(Submit);
			GameRoot.ApplyUiFont(_overlayRoot);
			_bound = true;
			Hide();
			VisualCatalogReady?.Invoke();
			if (_pendingStart != null)
				ShowStart(_pendingStart);
			else if (_openFieldSelectionWhenBound)
				ShowFieldPawnSelection();
		}

		void ClearOptions()
		{
			if (_options == null)
				return;

			Button[] buttons = _options.GetComponentsInChildren<Button>(true);
			for (int index = 0; index < buttons.Length; index++)
				buttons[index].gameObject.SetActive(false);
		}

		void CreateOptions(ClassGroup group, string groupName, IList<PawnClass> classes)
		{
			Transform column = _options != null ? _options.Find(group + "Column") : null;
			if (column == null)
			{
				Debug.LogError($"FieldBattleClassSelectionUI prefab requires an option column named '{group}Column'.");
				return;
			}

			Text label = column.Find("Label")?.GetComponent<Text>();
			if (label != null)
				label.text = groupName;
			if (classes == null)
				return;

			for (int index = 0; index < classes.Count; index++)
			{
				PawnClass pawnClass = classes[index];
				Button button = column.Find(pawnClass.ToString())?.GetComponent<Button>();
				if (button == null)
				{
					Debug.LogError($"FieldBattleClassSelectionUI prefab requires an option button named '{pawnClass}'.");
					continue;
				}

				button.gameObject.SetActive(true);
				button.onClick.RemoveAllListeners();
				button.onClick.AddListener(() => Select(group, pawnClass));
				_buttons.Add(new OptionButton { Group = group, PawnClass = pawnClass, Background = button.GetComponent<Image>(), Button = button });
			}
		}

		static bool TryGetClassGroup(PawnClass pawnClass, out ClassGroup group)
		{
			switch (pawnClass)
			{
				case PawnClass.SuenAxeSword:
				case PawnClass.SuenParvis: group = ClassGroup.Suen; return true;
				case PawnClass.BeigeFire:
				case PawnClass.BeigeIce: group = ClassGroup.Beige; return true;
				case PawnClass.AlenSpear:
				case PawnClass.AlenSwordShield: group = ClassGroup.Alen; return true;
				case PawnClass.ZillianLongbow:
				case PawnClass.ZillianMace: group = ClassGroup.Zillian; return true;
				default: group = default; return false;
			}
		}

		static bool IsSupportedFieldPawn(PawnClass pawnClass) => TryGetClassGroup(pawnClass, out _);
	}
}
