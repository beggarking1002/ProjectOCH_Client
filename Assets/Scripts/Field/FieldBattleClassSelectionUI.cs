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

		readonly Dictionary<ClassGroup, PawnClass> _selected = new Dictionary<ClassGroup, PawnClass>();
		readonly List<OptionButton> _buttons = new List<OptionButton>();
		GameObject _overlayRoot;
		Transform _options;
		Text _status;
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
		S_BATTLE_CLASS_SELECTION_START _pendingStart;

		public static bool IsBlockingInput => _instance != null && _instance._overlayRoot != null && _instance._overlayRoot.activeSelf;

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
			_selected.Clear();
			_buttons.Clear();
			_waiting = false;
			_locked = false;
			_isDebugBattleSelection = packet.TargetPlayerId == 0;
			ClearOptions();
			CreateOptions(ClassGroup.Suen, "\uC2A4\uC5D4", packet.SuenOptions);
			CreateOptions(ClassGroup.Beige, "\uBCA0\uC774\uC9C0", packet.BeigeOptions);
			CreateOptions(ClassGroup.Alen, "\uC54C\uB80C", packet.AlenOptions);
			CreateOptions(ClassGroup.Zillian, "\uC9C8\uB9AC\uC5B8", packet.ZillianOptions);
			_overlayRoot.SetActive(true);
			_status.text = "\uAC01 \uC601\uC6C5\uC758 \uD074\uB798\uC2A4\uB97C \uD558\uB098\uC529 \uC120\uD0DD\uD558\uC138\uC694.";
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

			_selected[group] = pawnClass;
			_status.text = "\uAC01 \uC601\uC6C5\uC758 \uD074\uB798\uC2A4\uB97C \uD558\uB098\uC529 \uC120\uD0DD\uD558\uC138\uC694.";
			Refresh();
		}

		void Submit()
		{
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
			bool disabled = _waiting || _locked;
			for (int index = 0; index < _buttons.Count; index++)
			{
				OptionButton option = _buttons[index];
				bool selected = _selected.TryGetValue(option.Group, out PawnClass selectedClass) && selectedClass == option.PawnClass;
				option.Background.color = disabled ? DisabledColor : selected ? SelectedColor : NormalColor;
				option.Button.interactable = disabled == false;
			}
			_submit.interactable = disabled == false && HasAllSelections();
			_submitText.text = disabled ? "\uC120\uD0DD \uC644\uB8CC" : "\uC120\uD0DD \uD655\uC815";
		}

		void Hide()
		{
			_isDebugBattleSelection = false;
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
			_submit = view.Submit;
			_submitText = view.SubmitText;
			_submit.onClick.RemoveAllListeners();
			_submit.onClick.AddListener(Submit);
			GameRoot.ApplyUiFont(_overlayRoot);
			_bound = true;
			Hide();
			if (_pendingStart != null)
				ShowStart(_pendingStart);
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
	}
}
