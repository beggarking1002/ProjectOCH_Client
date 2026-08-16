using System.Threading.Tasks;
using App;
using UnityEngine;
using UnityEngine.UI;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldPlayerInventoryUI : MonoBehaviour
	{
		FieldEconomyRepository _repository;
		RectTransform _grid;
		Text _title;
		Text _capacityText;
		Button _resetButton;
		Text _resetButtonText;
		float _resetConfirmationDeadline;
		bool _resetRequestPending;
		bool _subscribed;

		async void OnEnable()
		{
			BindView();
			Subscribe();
			_repository = await FieldEconomyRepository.LoadAsync();
			if (this == null || isActiveAndEnabled == false)
				return;
			await _repository.PreloadAllItemIconsAsync();
			if (this == null || isActiveAndEnabled == false)
				return;
			Render(GameRoot.Instance?.Network.LastExpeditionState);
		}

		void OnDisable()
		{
			Unsubscribe();
		}

		void OnDestroy()
		{
			if (_resetButton != null)
				_resetButton.onClick.RemoveListener(OnResetClicked);
			Unsubscribe();
		}

		void Update()
		{
			if (_resetRequestPending || _resetConfirmationDeadline <= 0f || Time.unscaledTime <= _resetConfirmationDeadline)
				return;
			ResetResetButtonLabel();
		}

		void BindView()
		{
			_grid = transform.Find("Window/BagGrid") as RectTransform;
			_title = transform.Find("Window/TitleFrame/TitleText")?.GetComponent<Text>();
			_capacityText = transform.Find("Window/CapacityText")?.GetComponent<Text>();
			FieldItemGridUI.Configure(_grid, FieldItemGridUI.ArtworkColumnCount);
			CreateDevelopmentResetButton(transform.Find("Window") as RectTransform);
		}

		void Subscribe()
		{
			if (_subscribed || GameRoot.Instance == null)
				return;
			GameRoot.Instance.Network.ExpeditionStateReceived += Render;
			GameRoot.Instance.Network.PlayerDataResetReceived += OnPlayerDataResetReceived;
			_subscribed = true;
		}

		void Unsubscribe()
		{
			if (_subscribed == false || GameRoot.Instance == null)
				return;
			GameRoot.Instance.Network.ExpeditionStateReceived -= Render;
			GameRoot.Instance.Network.PlayerDataResetReceived -= OnPlayerDataResetReceived;
			_subscribed = false;
		}

		void Render(Protocol.S_EXPEDITION_STATE state)
		{
			if (_repository == null || _grid == null)
				return;
			if (_title != null)
			{
				int gold = state?.Gold ?? 0;
				_title.text = $"인벤토리  ·  보유금 {gold}G";
			}
			if (_capacityText != null)
			{
				int occupiedSlots = state == null ? 0 : FieldInventoryGrouping.Build(state.Inventory).Count;
				_capacityText.text = $"{occupiedSlots}종  ·  제한 없음";
			}

			FieldItemGridUI.Clear(_grid);
			if (state == null)
				return;
			foreach (FieldInventoryItemGroup group in FieldInventoryGrouping.Build(state.Inventory))
			{
				string itemName = ItemName(group.ItemId);
				Button slot = FieldItemGridUI.CreateSlot(_grid, itemName, group.TotalQuantity,
					FieldInventoryGrouping.EarliestExpiryLabel(group), true);
				FieldItemGridUI.SetIconAsync(slot, _repository, group.ItemId);
				FieldItemGridUI.BindTooltip(slot, FieldInventoryGrouping.BuildExpiryTooltip(group, itemName));
			}
		}

		void CreateDevelopmentResetButton(RectTransform window)
		{
			if (Debug.isDebugBuild == false || window == null || _resetButton != null)
				return;
			GameObject buttonObject = new GameObject("DevelopmentResetButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
			buttonObject.layer = window.gameObject.layer;
			buttonObject.transform.SetParent(window, false);
			RectTransform rect = buttonObject.GetComponent<RectTransform>();
			rect.anchorMin = new Vector2(0f, 0f);
			rect.anchorMax = new Vector2(0f, 0f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.anchoredPosition = new Vector2(120f, 52f);
			rect.sizeDelta = new Vector2(200f, 44f);
			Image image = buttonObject.GetComponent<Image>();
			image.color = new Color(0.42f, 0.10f, 0.08f, 0.95f);
			_resetButton = buttonObject.GetComponent<Button>();
			_resetButton.targetGraphic = image;
			_resetButton.onClick.AddListener(OnResetClicked);

			GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
			labelObject.layer = buttonObject.layer;
			labelObject.transform.SetParent(rect, false);
			RectTransform labelRect = labelObject.GetComponent<RectTransform>();
			labelRect.anchorMin = Vector2.zero;
			labelRect.anchorMax = Vector2.one;
			labelRect.offsetMin = new Vector2(6f, 3f);
			labelRect.offsetMax = new Vector2(-6f, -3f);
			_resetButtonText = labelObject.GetComponent<Text>();
			_resetButtonText.font = GameRoot.UiFont != null ? GameRoot.UiFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
			_resetButtonText.fontSize = 14;
			_resetButtonText.alignment = TextAnchor.MiddleCenter;
			_resetButtonText.color = new Color(1f, 0.82f, 0.72f, 1f);
			_resetButtonText.raycastTarget = false;
			ResetResetButtonLabel();
		}

		void OnResetClicked()
		{
			if (_resetRequestPending || GameRoot.Instance == null)
				return;
			if (_resetConfirmationDeadline <= 0f || Time.unscaledTime > _resetConfirmationDeadline)
			{
				_resetConfirmationDeadline = Time.unscaledTime + 5f;
				if (_resetButtonText != null)
					_resetButtonText.text = "정말 초기화? 다시 클릭";
				return;
			}

			_resetRequestPending = GameRoot.Instance.Network.ResetPlayerData();
			_resetConfirmationDeadline = 0f;
			if (_resetButtonText != null)
				_resetButtonText.text = _resetRequestPending ? "초기화 중..." : "요청 실패";
		}

		void OnPlayerDataResetReceived(Protocol.S_RESET_PLAYER_DATA packet)
		{
			_resetRequestPending = false;
			_resetConfirmationDeadline = Time.unscaledTime + 3f;
			if (_resetButtonText != null)
				_resetButtonText.text = packet != null && packet.Success ? "초기화 완료" : $"실패: {packet?.Reason}";
		}

		void ResetResetButtonLabel()
		{
			_resetConfirmationDeadline = 0f;
			if (_resetButtonText != null)
				_resetButtonText.text = "개발: 진행 데이터 초기화";
		}

		string ItemName(string itemId)
		{
			return _repository.TryGetItem(itemId, out FieldItemDefinition item) && string.IsNullOrWhiteSpace(item.Name) == false
				? item.Name
				: itemId;
		}
	}
}
