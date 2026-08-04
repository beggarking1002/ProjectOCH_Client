using System.Collections.Generic;
using App;
using Protocol;
using UnityEngine;
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
			public Image Image;
			public Button Button;
		}

		const int CanvasSortingOrder = 1250;
		static readonly Color NormalColor = new Color(0.14f, 0.18f, 0.26f, 0.96f);
		static readonly Color SelectedColor = new Color(0.16f, 0.49f, 0.70f, 0.98f);
		static readonly Color DisabledColor = new Color(0.12f, 0.12f, 0.12f, 0.84f);
		static FieldBattleClassSelectionUI _instance;

		readonly Dictionary<ClassGroup, PawnClass> _selected = new Dictionary<ClassGroup, PawnClass>();
		readonly List<OptionButton> _buttons = new List<OptionButton>();
		GameObject _overlayRoot;
		GameObject _panel;
		Transform _options;
		Text _status;
		Button _submit;
		Text _submitText;
		bool _subscribed;
		bool _waiting;
		bool _locked;

		public static bool IsBlockingInput => _instance != null && _instance._overlayRoot != null && _instance._overlayRoot.activeSelf;

		void Awake()
		{
			_instance = this;
			FieldBattleInviteUI.EnsureEventSystem();
			BuildUi();
			Hide();
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

			_selected.Clear();
			_buttons.Clear();
			_waiting = false;
			_locked = false;
			ClearOptions();
			CreateOptions(ClassGroup.Suen, "수엔", packet.SuenOptions);
			CreateOptions(ClassGroup.Beige, "베이지", packet.BeigeOptions);
			CreateOptions(ClassGroup.Alen, "알렌", packet.AlenOptions);
			CreateOptions(ClassGroup.Zillian, "질리언", packet.ZillianOptions);
			_overlayRoot.SetActive(true);
			_status.text = "각 캐릭터군에서 클래스 하나씩을 선택하세요.";
			Refresh();
		}

		void OnResult(S_BATTLE_CLASS_SELECTION_RESULT packet)
		{
			if (packet == null || _overlayRoot.activeSelf == false)
				return;

			if (packet.Success == false)
			{
				_waiting = false;
				_locked = false;
				_status.text = string.IsNullOrWhiteSpace(packet.Reason) ? "선택이 거부되었습니다. 다시 시도하세요." : packet.Reason;
			}
			else
			{
				_waiting = packet.WaitingForOpponent;
				_locked = packet.WaitingForOpponent == false;
				_status.text = _waiting
					? "선택을 완료했습니다. 상대의 선택을 기다리는 중입니다..."
					: "양쪽 선택이 완료되었습니다. 전투를 준비하는 중입니다...";
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
			_status.text = "각 캐릭터군에서 클래스 하나씩을 선택하세요.";
			Refresh();
		}

		void Submit()
		{
			if (HasAllSelections() == false || GameRoot.Instance == null)
				return;

			List<PawnClass> classes = new List<PawnClass>(4)
			{
				_selected[ClassGroup.Suen],
				_selected[ClassGroup.Beige],
				_selected[ClassGroup.Alen],
				_selected[ClassGroup.Zillian],
			};
			if (GameRoot.Instance.Network.SendBattleClassSelection(classes) == false)
			{
				_status.text = string.IsNullOrWhiteSpace(GameRoot.Instance.Network.LastError) ? "선택 전송에 실패했습니다." : GameRoot.Instance.Network.LastError;
				Refresh();
				return;
			}

			_status.text = "선택을 전송했습니다...";
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
			for (int i = 0; i < _buttons.Count; i++)
			{
				OptionButton option = _buttons[i];
				bool selected = _selected.TryGetValue(option.Group, out PawnClass selectedClass) && selectedClass == option.PawnClass;
				option.Image.color = disabled ? DisabledColor : selected ? SelectedColor : NormalColor;
				option.Button.interactable = disabled == false;
			}
			_submit.interactable = disabled == false && HasAllSelections();
			_submitText.text = disabled ? "선택 완료" : "선택 확정";
		}

		void Hide()
		{
			if (_overlayRoot != null)
				_overlayRoot.SetActive(false);
		}

		void BuildUi()
		{
			_overlayRoot = Create("Canvas_FieldBattleClassSelectionUI", transform, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
			Canvas canvas = _overlayRoot.GetComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.sortingOrder = CanvasSortingOrder;
			CanvasScaler scaler = _overlayRoot.GetComponent<CanvasScaler>();
			scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
			scaler.referenceResolution = new Vector2(1280f, 720f);

			GameObject dimmer = Create("Dimmer", _overlayRoot.transform, typeof(Image));
			Stretch(dimmer.GetComponent<RectTransform>());
			dimmer.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.56f);
			_panel = Create("Panel", dimmer.transform, typeof(Image), typeof(VerticalLayoutGroup));
			RectTransform rect = _panel.GetComponent<RectTransform>();
			rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.sizeDelta = new Vector2(780f, 550f);
			_panel.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.98f);
			VerticalLayoutGroup layout = _panel.GetComponent<VerticalLayoutGroup>();
			layout.padding = new RectOffset(32, 32, 24, 24);
			layout.spacing = 12;
			layout.childControlWidth = true;
			layout.childControlHeight = true;
			layout.childForceExpandWidth = true;
			layout.childForceExpandHeight = false;

			Text title = Text("Title", _panel.transform, 30, TextAnchor.MiddleCenter);
			title.text = "PvP 클래스 선택";
			Layout(title.gameObject, 52f);
			_status = Text("Status", _panel.transform, 18, TextAnchor.MiddleCenter);
			_status.color = new Color(0.84f, 0.88f, 0.95f);
			Layout(_status.gameObject, 46f);
			GameObject optionsRoot = Create("Options", _panel.transform, typeof(HorizontalLayoutGroup));
			HorizontalLayoutGroup optionsLayout = optionsRoot.GetComponent<HorizontalLayoutGroup>();
			optionsLayout.spacing = 12;
			optionsLayout.childControlWidth = true;
			optionsLayout.childControlHeight = true;
			optionsLayout.childForceExpandWidth = true;
			optionsLayout.childForceExpandHeight = false;
			Layout(optionsRoot, 264f);
			_options = optionsRoot.transform;
			_submit = Button("Submit", _panel.transform, "선택 확정", Submit, out _submitText);
			Layout(_submit.gameObject, 54f);
			GameRoot.ApplyUiFont(_overlayRoot);
		}

		void ClearOptions()
		{
			for (int i = _options.childCount - 1; i >= 0; i--)
				Destroy(_options.GetChild(i).gameObject);
		}

		void CreateOptions(ClassGroup group, string groupName, IList<PawnClass> classes)
		{
			GameObject row = Create(groupName + "Column", _options, typeof(VerticalLayoutGroup));
			VerticalLayoutGroup layout = row.GetComponent<VerticalLayoutGroup>();
			layout.spacing = 8;
			layout.childAlignment = TextAnchor.UpperCenter;
			layout.childControlWidth = true;
			layout.childControlHeight = true;
			layout.childForceExpandWidth = true;
			layout.childForceExpandHeight = true;
			Layout(row, 264f, 160f);
			Text label = Text("Label", row.transform, 22, TextAnchor.MiddleCenter);
			label.text = groupName;
			Layout(label.gameObject, 44f);
			if (classes == null)
				return;

			for (int i = 0; i < classes.Count; i++)
			{
				PawnClass pawnClass = classes[i];
				Button button = Button(pawnClass.ToString(), row.transform, ClassName(pawnClass), () => Select(group, pawnClass), out _);
				Layout(button.gameObject, 82f);
				_buttons.Add(new OptionButton { Group = group, PawnClass = pawnClass, Image = button.GetComponent<Image>(), Button = button });
			}
		}

		static string ClassName(PawnClass pawnClass)
		{
			switch (pawnClass)
			{
				case PawnClass.SuenAxeSword: return "도끼검";
				case PawnClass.SuenParvis: return "파르비스";
				case PawnClass.BeigeIce: return "얼음";
				case PawnClass.BeigeFire: return "불";
				case PawnClass.AlenSpear: return "창";
				case PawnClass.AlenSwordShield: return "검방";
				case PawnClass.ZillianLongbow: return "롱보우";
				case PawnClass.ZillianMace: return "메이스";
				default: return pawnClass.ToString();
			}
		}

		static GameObject Create(string name, Transform parent, params System.Type[] components)
		{
			GameObject result = new GameObject(name, components);
			result.transform.SetParent(parent, false);
			return result;
		}

		static Text Text(string name, Transform parent, int fontSize, TextAnchor alignment)
		{
			GameObject go = Create(name, parent, typeof(Text));
			Text text = go.GetComponent<Text>();
			text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
			text.fontSize = fontSize;
			text.alignment = alignment;
			text.color = Color.white;
			return text;
		}

		static Button Button(string name, Transform parent, string label, UnityEngine.Events.UnityAction onClick, out Text labelText)
		{
			GameObject go = Create(name, parent, typeof(Image), typeof(Button));
			Image image = go.GetComponent<Image>();
			image.color = NormalColor;
			Button button = go.GetComponent<Button>();
			button.targetGraphic = image;
			button.onClick.AddListener(onClick);
			labelText = Text("Text", go.transform, 20, TextAnchor.MiddleCenter);
			labelText.text = label;
			Stretch(labelText.rectTransform);
			return button;
		}

		static void Layout(GameObject go, float height, float width = -1f)
		{
			LayoutElement element = go.AddComponent<LayoutElement>();
			element.preferredHeight = height;
			if (width >= 0f)
				element.preferredWidth = width;
		}

		static void Stretch(RectTransform rect)
		{
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
		}
	}
}
