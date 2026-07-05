using App;
using Protocol;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldBattleInviteUI : MonoBehaviour
	{
		const int CanvasSortingOrder = 1200;

		static FieldBattleInviteUI _instance;

		Canvas _canvas;
		GameObject _panel;
		Text _messageText;
		Button _primaryButton;
		Button _secondaryButton;
		Text _primaryButtonText;
		Text _secondaryButtonText;
		ulong _outgoingTargetPlayerId;
		ulong _incomingRequesterPlayerId;
		bool _subscribed;

		public static bool IsBlockingInput => _instance != null && _instance._panel != null && _instance._panel.activeSelf;

		void Awake()
		{
			_instance = this;
			EnsureEventSystem();
			BuildUi();
			Hide();
		}

		void OnEnable()
		{
			TrySubscribe();
		}

		void Update()
		{
			if (_subscribed == false)
				TrySubscribe();
		}

		void OnDisable()
		{
			Unsubscribe();
		}

		void OnDestroy()
		{
			if (_instance == this)
				_instance = null;
		}

		public void ShowInviteConfirm(ulong targetPlayerId)
		{
			if (targetPlayerId == 0)
				return;

			_outgoingTargetPlayerId = targetPlayerId;
			_incomingRequesterPlayerId = 0;
			ShowTwoButton("전투 신청하시겠습니까?", "Yes", "No", SendInvite, Hide);
		}

		void SendInvite()
		{
			if (_outgoingTargetPlayerId == 0)
			{
				Hide();
				return;
			}

			if (GameRoot.Instance == null || GameRoot.Instance.Network.SendBattleInvite(_outgoingTargetPlayerId) == false)
			{
				string reason = GameRoot.Instance != null ? GameRoot.Instance.Network.LastError : "Network is not initialized.";
				ShowOneButton($"전투 신청에 실패했습니다.\n{reason}", "OK", Hide);
				return;
			}

			ShowWaiting("전투 신청 대기중...");
		}

		void AcceptInvite()
		{
			SendInviteResponse(true);
		}

		void DeclineInvite()
		{
			SendInviteResponse(false);
			Hide();
		}

		void SendInviteResponse(bool accept)
		{
			if (_incomingRequesterPlayerId == 0)
			{
				Hide();
				return;
			}

			if (GameRoot.Instance == null || GameRoot.Instance.Network.SendBattleInviteResponse(_incomingRequesterPlayerId, accept) == false)
			{
				string reason = GameRoot.Instance != null ? GameRoot.Instance.Network.LastError : "Network is not initialized.";
				ShowOneButton($"응답 전송에 실패했습니다.\n{reason}", "OK", Hide);
			}
		}

		void OnBattleInviteRequestReceived(S_BATTLE_INVITE_REQUEST packet)
		{
			if (packet == null)
				return;

			_outgoingTargetPlayerId = packet.TargetPlayerId;
			if (packet.Success)
			{
				ShowWaiting("전투 신청 대기중...");
				return;
			}

			string reason = string.IsNullOrWhiteSpace(packet.Reason) ? "전투 신청이 거절되었습니다." : packet.Reason;
			ShowOneButton(reason, "OK", Hide);
		}

		void OnBattleInviteReceived(S_BATTLE_INVITE_RECEIVED packet)
		{
			if (packet == null || packet.RequesterPlayerId == 0)
				return;

			_incomingRequesterPlayerId = packet.RequesterPlayerId;
			_outgoingTargetPlayerId = 0;
			ShowTwoButton("전투 신청을 받았습니다.", "수락", "거절", AcceptInvite, DeclineInvite);
		}

		void OnBattleInviteResultReceived(S_BATTLE_INVITE_RESULT packet)
		{
			if (packet == null)
				return;

			if (packet.Accepted)
			{
				ShowWaiting("전투를 시작합니다...");
				return;
			}

			if (IsMyObjectId(packet.RequesterPlayerId))
				ShowOneButton("상대가 전투를 거절했습니다.", "OK", Hide);
			else
				Hide();
		}

		bool IsMyObjectId(ulong objectId)
		{
			return GameRoot.Instance != null
				&& GameRoot.Instance.Network.LastEnterGame != null
				&& GameRoot.Instance.Network.LastEnterGame.Player != null
				&& GameRoot.Instance.Network.LastEnterGame.Player.ObjectId == objectId;
		}

		void TrySubscribe()
		{
			if (_subscribed || GameRoot.Instance == null || GameRoot.Instance.Network == null)
				return;

			GameRoot.Instance.Network.BattleInviteRequestReceived += OnBattleInviteRequestReceived;
			GameRoot.Instance.Network.BattleInviteReceived += OnBattleInviteReceived;
			GameRoot.Instance.Network.BattleInviteResultReceived += OnBattleInviteResultReceived;
			_subscribed = true;
		}

		void Unsubscribe()
		{
			if (_subscribed && GameRoot.Instance != null && GameRoot.Instance.Network != null)
			{
				GameRoot.Instance.Network.BattleInviteRequestReceived -= OnBattleInviteRequestReceived;
				GameRoot.Instance.Network.BattleInviteReceived -= OnBattleInviteReceived;
				GameRoot.Instance.Network.BattleInviteResultReceived -= OnBattleInviteResultReceived;
			}

			_subscribed = false;
		}

		void ShowWaiting(string message)
		{
			SetPanelActive(true);
			_messageText.text = message;
			SetButtonVisible(_primaryButton, false);
			SetButtonVisible(_secondaryButton, false);
		}

		void ShowOneButton(string message, string buttonText, UnityEngine.Events.UnityAction onClick)
		{
			SetPanelActive(true);
			_messageText.text = message;
			ConfigureButton(_primaryButton, _primaryButtonText, buttonText, onClick);
			SetButtonVisible(_primaryButton, true);
			SetButtonVisible(_secondaryButton, false);
		}

		void ShowTwoButton(string message, string primaryText, string secondaryText, UnityEngine.Events.UnityAction primaryClick, UnityEngine.Events.UnityAction secondaryClick)
		{
			SetPanelActive(true);
			_messageText.text = message;
			ConfigureButton(_primaryButton, _primaryButtonText, primaryText, primaryClick);
			ConfigureButton(_secondaryButton, _secondaryButtonText, secondaryText, secondaryClick);
			SetButtonVisible(_primaryButton, true);
			SetButtonVisible(_secondaryButton, true);
		}

		void Hide()
		{
			_outgoingTargetPlayerId = 0;
			_incomingRequesterPlayerId = 0;
			SetPanelActive(false);
		}

		void SetPanelActive(bool active)
		{
			if (_panel != null)
				_panel.SetActive(active);
		}

		void ConfigureButton(Button button, Text label, string text, UnityEngine.Events.UnityAction onClick)
		{
			if (button == null)
				return;

			if (label != null)
				label.text = text;

			button.onClick.RemoveAllListeners();
			if (onClick != null)
				button.onClick.AddListener(onClick);
		}

		static void SetButtonVisible(Button button, bool visible)
		{
			if (button != null)
				button.gameObject.SetActive(visible);
		}

		void BuildUi()
		{
			GameObject canvasObject = new GameObject("Canvas_FieldBattleInviteUI");
			canvasObject.transform.SetParent(transform, false);
			_canvas = canvasObject.AddComponent<Canvas>();
			_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			_canvas.sortingOrder = CanvasSortingOrder;
			canvasObject.AddComponent<CanvasScaler>();
			canvasObject.AddComponent<GraphicRaycaster>();

			_panel = new GameObject("Panel");
			_panel.transform.SetParent(canvasObject.transform, false);
			Image panelImage = _panel.AddComponent<Image>();
			panelImage.color = new Color(0.05f, 0.06f, 0.08f, 0.92f);

			RectTransform panelRect = _panel.GetComponent<RectTransform>();
			panelRect.anchorMin = new Vector2(0.5f, 0.5f);
			panelRect.anchorMax = new Vector2(0.5f, 0.5f);
			panelRect.pivot = new Vector2(0.5f, 0.5f);
			panelRect.anchoredPosition = Vector2.zero;
			panelRect.sizeDelta = new Vector2(460f, 210f);

			_messageText = CreateText(_panel.transform, "Message", 22, TextAnchor.MiddleCenter);
			RectTransform messageRect = _messageText.GetComponent<RectTransform>();
			messageRect.anchorMin = new Vector2(0.08f, 0.44f);
			messageRect.anchorMax = new Vector2(0.92f, 0.86f);
			messageRect.offsetMin = Vector2.zero;
			messageRect.offsetMax = Vector2.zero;

			_primaryButton = CreateButton(_panel.transform, "PrimaryButton", "OK", out _primaryButtonText);
			RectTransform primaryRect = _primaryButton.GetComponent<RectTransform>();
			primaryRect.anchorMin = new Vector2(0.16f, 0.12f);
			primaryRect.anchorMax = new Vector2(0.46f, 0.32f);
			primaryRect.offsetMin = Vector2.zero;
			primaryRect.offsetMax = Vector2.zero;

			_secondaryButton = CreateButton(_panel.transform, "SecondaryButton", "No", out _secondaryButtonText);
			RectTransform secondaryRect = _secondaryButton.GetComponent<RectTransform>();
			secondaryRect.anchorMin = new Vector2(0.54f, 0.12f);
			secondaryRect.anchorMax = new Vector2(0.84f, 0.32f);
			secondaryRect.offsetMin = Vector2.zero;
			secondaryRect.offsetMax = Vector2.zero;
		}

		static Text CreateText(Transform parent, string name, int fontSize, TextAnchor alignment)
		{
			GameObject textObject = new GameObject(name);
			textObject.transform.SetParent(parent, false);
			Text text = textObject.AddComponent<Text>();
			text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
			text.fontSize = fontSize;
			text.alignment = alignment;
			text.color = Color.white;
			text.horizontalOverflow = HorizontalWrapMode.Wrap;
			text.verticalOverflow = VerticalWrapMode.Truncate;
			return text;
		}

		static Button CreateButton(Transform parent, string name, string text, out Text label)
		{
			GameObject buttonObject = new GameObject(name);
			buttonObject.transform.SetParent(parent, false);

			Image image = buttonObject.AddComponent<Image>();
			image.color = new Color(0.18f, 0.24f, 0.32f, 1f);

			Button button = buttonObject.AddComponent<Button>();
			button.targetGraphic = image;

			label = CreateText(buttonObject.transform, "Text", 18, TextAnchor.MiddleCenter);
			label.text = text;
			RectTransform labelRect = label.GetComponent<RectTransform>();
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = Vector2.zero;
			labelRect.offsetMax = Vector2.zero;

			return button;
		}

		static void EnsureEventSystem()
		{
			if (EventSystem.current != null)
				return;

			GameObject eventSystem = new GameObject("EventSystem");
			eventSystem.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
			eventSystem.AddComponent<InputSystemUIInputModule>();
#else
			eventSystem.AddComponent<StandaloneInputModule>();
#endif
		}
	}
}
