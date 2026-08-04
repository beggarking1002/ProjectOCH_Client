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
#endif

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldBattleInviteUI : MonoBehaviour
	{
		const string BattleInviteUiName = "Canvas_FieldBattleInviteUI";
		const string BattleInviteUiAddress = "FieldBattleInviteUI";
		const int CanvasSortingOrder = 1200;

		static FieldBattleInviteUI _instance;

		Canvas _canvas;
		GameObject _uiInstance;
		GameObject _panel;
		Text _messageText;
		Button _primaryButton;
		Button _secondaryButton;
		Text _primaryButtonText;
		Text _secondaryButtonText;
		AsyncOperationHandle<GameObject> _uiHandle;
		ulong _outgoingTargetPlayerId;
		ulong _incomingRequesterPlayerId;
		bool _hasUiHandle;
		bool _isBinding;
		bool _bound;
		bool _subscribed;

		public static bool IsBlockingInput => _instance != null && _instance._panel != null && _instance._panel.activeSelf;

		void Awake()
		{
			_instance = this;
			EnsureEventSystem();
			BindOrLoadUi();
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

			if (_hasUiHandle && _uiHandle.IsValid())
			{
				Addressables.ReleaseInstance(_uiHandle);
				_hasUiHandle = false;
				_uiHandle = default;
				_uiInstance = null;
			}
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

		void OnBattleClassSelectionStartReceived(S_BATTLE_CLASS_SELECTION_START packet)
		{
			// Invite acceptance now proceeds through class selection, not battle entry.
			if (packet != null)
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
			GameRoot.Instance.Network.BattleClassSelectionStartReceived += OnBattleClassSelectionStartReceived;
			_subscribed = true;
		}

		void Unsubscribe()
		{
			if (_subscribed && GameRoot.Instance != null && GameRoot.Instance.Network != null)
			{
				GameRoot.Instance.Network.BattleInviteRequestReceived -= OnBattleInviteRequestReceived;
				GameRoot.Instance.Network.BattleInviteReceived -= OnBattleInviteReceived;
				GameRoot.Instance.Network.BattleInviteResultReceived -= OnBattleInviteResultReceived;
				GameRoot.Instance.Network.BattleClassSelectionStartReceived -= OnBattleClassSelectionStartReceived;
			}

			_subscribed = false;
		}

		void ShowWaiting(string message)
		{
			if (EnsureUiReady() == false)
				return;

			SetPanelActive(true);
			_messageText.text = message;
			SetButtonVisible(_primaryButton, false);
			SetButtonVisible(_secondaryButton, false);
		}

		void ShowOneButton(string message, string buttonText, UnityEngine.Events.UnityAction onClick)
		{
			if (EnsureUiReady() == false)
				return;

			SetPanelActive(true);
			_messageText.text = message;
			ConfigureButton(_primaryButton, _primaryButtonText, buttonText, onClick);
			SetButtonVisible(_primaryButton, true);
			SetButtonVisible(_secondaryButton, false);
		}

		void ShowTwoButton(string message, string primaryText, string secondaryText, UnityEngine.Events.UnityAction primaryClick, UnityEngine.Events.UnityAction secondaryClick)
		{
			if (EnsureUiReady() == false)
				return;

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

		async void BindOrLoadUi()
		{
			if (_isBinding || _bound)
				return;

			_isBinding = true;

			AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(BattleInviteUiAddress);
			await handle.Task;

			if (this == null)
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				return;
			}

			if (_bound)
			{
				if (handle.IsValid())
					Addressables.ReleaseInstance(handle);
				_isBinding = false;
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

			Debug.LogError($"Failed to load addressable UI prefab: {BattleInviteUiAddress}");

			_isBinding = false;
		}

		bool EnsureUiReady()
		{
			if (_bound)
				return true;

			if (_isBinding)
				Debug.LogWarning($"Battle invite UI is still loading from Addressables. address={BattleInviteUiAddress}");
			else
				Debug.LogError($"Battle invite UI is not available. Register addressable prefab '{BattleInviteUiAddress}'.");

			return false;
		}

		void BindUi(GameObject uiObject)
		{
			if (uiObject == null)
				return;

			_uiInstance = uiObject;
			_uiInstance.name = BattleInviteUiName;
			SceneManager.MoveGameObjectToScene(_uiInstance, gameObject.scene);
			GameRoot.ApplyUiFont(_uiInstance);

			_canvas = _uiInstance.GetComponent<Canvas>();
			if (_canvas == null)
				_canvas = _uiInstance.GetComponentInChildren<Canvas>(true);

			if (_canvas == null)
			{
				Debug.LogError($"Addressable UI prefab '{BattleInviteUiAddress}' requires a Canvas.");
				return;
			}

			_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			_canvas.sortingOrder = CanvasSortingOrder;

			if (_canvas.GetComponent<CanvasScaler>() == null)
			{
				Debug.LogError($"Addressable UI prefab '{BattleInviteUiAddress}' requires a CanvasScaler on its Canvas.");
				return;
			}

			if (_canvas.GetComponent<GraphicRaycaster>() == null)
			{
				Debug.LogError($"Addressable UI prefab '{BattleInviteUiAddress}' requires a GraphicRaycaster on its Canvas.");
				return;
			}

			Transform panelTransform = FindDeepChild(_uiInstance.transform, "Panel");
			_panel = panelTransform != null ? panelTransform.gameObject : null;
			if (_panel == null)
			{
				Debug.LogError($"Addressable UI prefab '{BattleInviteUiAddress}' requires a child named Panel.");
				return;
			}

			_messageText = FindDeepChild(_panel.transform, "Message")?.GetComponent<Text>();
			_primaryButton = FindDeepChild(_panel.transform, "PrimaryButton")?.GetComponent<Button>();
			_secondaryButton = FindDeepChild(_panel.transform, "SecondaryButton")?.GetComponent<Button>();
			_primaryButtonText = _primaryButton != null ? FindDeepChild(_primaryButton.transform, "Text")?.GetComponent<Text>() : null;
			_secondaryButtonText = _secondaryButton != null ? FindDeepChild(_secondaryButton.transform, "Text")?.GetComponent<Text>() : null;
			if (_messageText == null || _primaryButton == null || _secondaryButton == null)
			{
				Debug.LogError($"Addressable UI prefab '{BattleInviteUiAddress}' requires Message(Text), PrimaryButton(Button), and SecondaryButton(Button).");
				return;
			}

			_bound = true;
			Hide();
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

		internal static void EnsureEventSystem()
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
