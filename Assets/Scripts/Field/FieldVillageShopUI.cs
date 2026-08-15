using System.Collections.Generic;
using App;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;

namespace Field
{
	[DisallowMultipleComponent]
	public sealed class FieldVillageShopUI : MonoBehaviour
	{
		[SerializeField] Button backButton;

		FieldVillageUI _villageUi;
		FieldEconomyRepository _repository;
		Protocol.S_VILLAGE_SHOP_STATE _pendingState;
		string _villageId;
		Text _headerText;
		Text _statusText;
		RectTransform _shopStockRoot;
		RectTransform _inventoryRoot;
		bool _subscribed;

		public async void Show(FieldVillageUI villageUi, string villageId)
		{
			_villageUi = villageUi;
			_villageId = villageId;
			BindView();
			Subscribe();
			backButton?.onClick.AddListener(ReturnToVillage);
			_repository = await FieldEconomyRepository.LoadAsync();
			if (this == null)
				return;
			if (_pendingState != null)
				Render(_pendingState);
			if (GameRoot.Instance == null || GameRoot.Instance.Network.OpenVillageShop(_villageId) == false)
				SetStatus("상점 정보를 요청할 수 없습니다.", true);
		}

		void OnDestroy()
		{
			Unsubscribe();
			backButton?.onClick.RemoveListener(ReturnToVillage);
		}

		void Subscribe()
		{
			if (_subscribed || GameRoot.Instance == null)
				return;
			GameRoot.Instance.Network.VillageShopStateReceived += OnShopStateReceived;
			_subscribed = true;
		}

		void Unsubscribe()
		{
			if (!_subscribed || GameRoot.Instance == null)
				return;
			GameRoot.Instance.Network.VillageShopStateReceived -= OnShopStateReceived;
			_subscribed = false;
		}

		void OnShopStateReceived(Protocol.S_VILLAGE_SHOP_STATE packet)
		{
			if (packet == null || packet.VillageId != _villageId)
				return;
			_pendingState = packet.Clone();
			if (_repository != null)
				Render(_pendingState);
		}

		void Render(Protocol.S_VILLAGE_SHOP_STATE packet)
		{
			if (_headerText != null && packet.Expedition != null)
			{
				int resetMinutes = Mathf.CeilToInt(packet.StockResetRemainingSeconds / 60f);
				_headerText.text = $"상점  ·  보유금 {packet.Expedition.Gold}G  ·  포만도 {packet.Expedition.Satiety}/{packet.Expedition.MaxSatiety}  ·  재입고 {resetMinutes}분";
			}

			SetStatus(packet.Success ? ActionMessage(packet.Action) : packet.Reason, packet.Success == false);
			RebuildShopRows(packet);
			RebuildInventoryRows(packet);
		}

		void RebuildShopRows(Protocol.S_VILLAGE_SHOP_STATE packet)
		{
			FieldItemGridUI.Clear(_shopStockRoot);
			if (_shopStockRoot == null)
				return;

			foreach (Protocol.VillageShopListingInfo listing in packet.Listings)
			{
				if (listing.Stock <= 0)
					continue;

				string itemId = listing.ItemId;
				Button button = FieldItemGridUI.CreateSlot(
					_shopStockRoot,
					ItemName(itemId),
					listing.Stock,
					$"{listing.UnitSellPrice}G",
					true);
				FieldItemGridUI.SetIconAsync(button, _repository, itemId);
				button.onClick.AddListener(() => Buy(itemId));
			}
		}

		void RebuildInventoryRows(Protocol.S_VILLAGE_SHOP_STATE packet)
		{
			FieldItemGridUI.Clear(_inventoryRoot);
			if (_inventoryRoot == null || packet.Expedition == null)
				return;

			Dictionary<ulong, int> sellPrices = new Dictionary<ulong, int>();
			foreach (Protocol.VillageTradeBuyOfferInfo offer in packet.TradeBuyOffers)
				sellPrices[offer.StackId] = offer.UnitBuyPrice;

			foreach (Protocol.ExpeditionItemStackInfo stack in packet.Expedition.Inventory)
			{
				bool canSell = sellPrices.TryGetValue(stack.StackId, out int unitBuyPrice);
				ulong stackId = stack.StackId;
				Button button = FieldItemGridUI.CreateSlot(
					_inventoryRoot,
					ItemName(stack.ItemId),
					stack.Quantity,
					canSell ? $"판매 {unitBuyPrice}G" : "판매 불가",
					canSell);
				FieldItemGridUI.SetIconAsync(button, _repository, stack.ItemId);
				if (canSell)
					button.onClick.AddListener(() => Sell(stackId));
			}
		}

		void Buy(string itemId)
		{
			if (GameRoot.Instance == null || GameRoot.Instance.Network.BuyVillageItem(_villageId, itemId) == false)
				SetStatus("구매 요청을 보내지 못했습니다.", true);
		}

		void Sell(ulong stackId)
		{
			if (GameRoot.Instance == null || GameRoot.Instance.Network.SellVillageItem(_villageId, stackId) == false)
				SetStatus("판매 요청을 보내지 못했습니다.", true);
		}

		string ItemName(string itemId)
		{
			return _repository != null && _repository.TryGetItem(itemId, out FieldItemDefinition item) && string.IsNullOrWhiteSpace(item.Name) == false
				? item.Name
				: itemId;
		}

		void BindView()
		{
			_headerText = transform.Find("ShopWindow/Header")?.GetComponent<Text>();
			_shopStockRoot = transform.Find("ShopWindow/ShopStockGrid") as RectTransform;
			_inventoryRoot = transform.Find("ShopWindow/PlayerInventoryGrid") as RectTransform;
			RectTransform tradePanel = transform.Find("ShopWindow/TradePanel") as RectTransform;
			FieldItemGridUI.Configure(_shopStockRoot);
			FieldItemGridUI.Configure(_inventoryRoot);
			if (tradePanel != null)
			{
				GameObject statusObject = CreateTextObject("Status", tradePanel);
				_statusText = statusObject.GetComponent<Text>();
				_statusText.alignment = TextAnchor.MiddleCenter;
				_statusText.fontSize = 18;
			}
		}

		static GameObject CreateTextObject(string name, RectTransform parent)
		{
			GameObject child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
			child.layer = parent.gameObject.layer;
			child.transform.SetParent(parent, false);
			RectTransform rect = child.GetComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = new Vector2(8, 4);
			rect.offsetMax = new Vector2(-8, -4);
			Text text = child.GetComponent<Text>();
			text.font = GameRoot.UiFont != null ? GameRoot.UiFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
			text.raycastTarget = false;
			text.horizontalOverflow = HorizontalWrapMode.Wrap;
			text.verticalOverflow = VerticalWrapMode.Truncate;
			return child;
		}

		void SetStatus(string message, bool isError)
		{
			if (_statusText == null)
				return;
			_statusText.text = string.IsNullOrWhiteSpace(message) ? "상품을 선택하세요" : message;
			_statusText.color = isError ? new Color(0.85f, 0.24f, 0.18f) : new Color(0.25f, 0.12f, 0.04f);
		}

		static string ActionMessage(string action)
		{
			switch (action)
			{
				case "buy": return "구매했습니다.";
				case "sell": return "판매했습니다.";
				default: return "상품을 선택하세요";
			}
		}

		void ReturnToVillage()
		{
			backButton?.onClick.RemoveListener(ReturnToVillage);
			Unsubscribe();
			Addressables.ReleaseInstance(gameObject);
		}
	}
}
